﻿using System;
using System.Collections.Generic;
using System.Linq;
using Raven.Client.Data.Indexes;
using Raven.Server.Documents.Indexes.MapReduce;
using Raven.Server.Documents.Indexes.MapReduce.Auto;
using Raven.Server.Documents.Indexes.MapReduce.Static;
using Raven.Server.ServerWide;
using Raven.Server.ServerWide.Context;
using Raven.Server.Utils;
using Sparrow.Json;
using Voron;
using Voron.Data;
using Voron.Data.BTrees;
using Voron.Data.Fixed;
using Voron.Impl;

namespace Raven.Server.Documents.Indexes.Debugging
{
    public static class IndexDebugExtensions
    {
        public static IDisposable GetIdentifiersOfMappedDocuments(this Index self, string startsWith, int start, int take, out IEnumerable<string> docIds)
        {
            if (self.Type.IsMapReduce() == false)
                throw new NotSupportedException("Getting doc ids for map indexes is not supported");

            using (var scope = new DisposeableScope())
            {
                TransactionOperationContext indexContext;
                scope.EnsureDispose(self._contextPool.AllocateOperationContext(out indexContext));

                RavenTransaction tx;
                scope.EnsureDispose(tx = indexContext.OpenReadTransaction());

                var tree = tx.InnerTransaction.ReadTree(MapReduceIndexBase<MapReduceIndexDefinition>.MapPhaseTreeName);

                if (tree == null)
                {
                    docIds = Enumerable.Empty<string>();
                    return scope;
                }

                TreeIterator it;
                scope.EnsureDispose(it = tree.Iterate(false));
                    
                docIds = IterateKeys(it, startsWith, start, take, indexContext).Select(x => x.ToString());
                    
                return scope.Delay();
            }
        }

        private static IEnumerable<Slice> IterateKeys(IIterator it, string prefix, int start, int take, TransactionOperationContext context)
        {
            if (it.Seek(Slices.BeforeAllKeys) == false)
                yield break;

            if (string.IsNullOrEmpty(prefix) == false)
            {
                Slice prefixSlice;
                Slice.From(context.Transaction.InnerTransaction.Allocator, prefix, out prefixSlice);

                it.RequiredPrefix = prefixSlice;
                
                if (it.Seek(prefixSlice) == false)
                    yield break;
            }
            else if (it.Seek(MapReduceIndexingContext.LastMapResultIdKey))
                it.MoveNext();

            do
            {
                if (start > 0)
                {
                    start--;
                    continue;
                }
 
                if (--take < 0)
                    yield break;

                yield return it.CurrentKey;
            } while (it.MoveNext());
        }

        public static IDisposable GetReduceTree(this Index self, string docId, out IEnumerable<ReduceTree> trees)
        {
            using (var scope = new DisposeableScope())
            {
                TransactionOperationContext indexContext;
                scope.EnsureDispose(self._contextPool.AllocateOperationContext(out indexContext));

                RavenTransaction tx;
                scope.EnsureDispose(tx = indexContext.OpenReadTransaction());

                var mapPhaseTree = tx.InnerTransaction.ReadTree(MapReduceIndexBase<MapReduceIndexDefinition>.MapPhaseTreeName);

                if (mapPhaseTree == null)
                    throw new Exception("TODO arek");

                var reducePhaseTree = tx.InnerTransaction.ReadTree(MapReduceIndexBase<MapReduceIndexDefinition>.ReducePhaseTreeName);

                if (reducePhaseTree == null)
                    throw new Exception("TODO arek");
                
                Slice docIdAsSlice;
                scope.EnsureDispose(Slice.From(indexContext.Allocator, docId, out docIdAsSlice));

                FixedSizeTree mapEntries;
                scope.EnsureDispose(mapEntries = mapPhaseTree.FixedTreeFor(docId, sizeof(long)));

                trees = IterateTrees(self, mapEntries, mapPhaseTree, reducePhaseTree, indexContext);

                return scope.Delay();
            }
        }

        private static IEnumerable<ReduceTree> IterateTrees(Index self, FixedSizeTree mapEntries, Tree mapPhaseTree, Tree reducePhaseTree, TransactionOperationContext indexContext)
        {
            HashSet<ulong> rendered = new HashSet<ulong>();

            var typePerHash = reducePhaseTree.FixedTreeFor(MapReduceIndexBase<MapReduceIndexDefinition>.ResultsStoreTypesTreeName, sizeof(byte));

            foreach (var mapEntry in MapReduceIndexBase<MapReduceIndexDefinition>.GetMapEntries(mapEntries))
            {
                if (rendered.Add(mapEntry.ReduceKeyHash) == false)
                    continue;

                MapReduceResultsStore store;

                var mapReduceIndex = self as MapReduceIndex;

                if (mapReduceIndex != null)
                    store = mapReduceIndex.CreateResultsStore(typePerHash,
                        mapEntry.ReduceKeyHash, indexContext, false);
                else
                    store = ((AutoMapReduceIndex) self).CreateResultsStore(typePerHash,
                        mapEntry.ReduceKeyHash, indexContext, false);

                using (store)
                {
                    switch (store.Type)
                    {
                        case MapResultsStorageType.Tree:
                            yield return RenderTree(store.Tree, mapPhaseTree, indexContext);
                            break;
                        case MapResultsStorageType.Nested:
                            yield return RenderNestedSection(store.GetNestedResultsSection(reducePhaseTree), mapPhaseTree, indexContext);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(store.Type.ToString());
                    }
                }
            }
        }

        private static unsafe ReduceTree RenderTree(Tree tree, Tree mapPhaseTree, TransactionOperationContext context)
        {
            var stack = new Stack<ReduceTreePage>();
            var rootPage = tree.GetReadOnlyTreePage(tree.State.RootPageNumber);

            var root = new ReduceTreePage(rootPage);

            stack.Push(root);

            var needToAssignSource = new Dictionary<long, MapResultInLeaf>();

            var tx = tree.Llt;
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                var page = node.Page;

                if (page.NumberOfEntries == 0 && page != rootPage)
                    throw new InvalidOperationException($"The page {page.PageNumber} is empty");
                
                for (var i = 0; i < page.NumberOfEntries; i++)
                {
                    if (page.IsBranch)
                    {
                        var p = page.GetNode(i)->PageNumber;

                        var childNode = new ReduceTreePage(tree.GetReadOnlyTreePage(p));

                        node.Children.Add(childNode);

                        stack.Push(childNode);
                    }
                    else
                    {
                        var entry = new MapResultInLeaf();

                        Slice s;
                        using (page.GetNodeKey(tx, i, out s))
                        {
                            var mapEntryId = *(long*) s.Content.Ptr;
                            needToAssignSource[mapEntryId] = entry;
                        }

                        var valueReader = TreeNodeHeader.Reader(tx, page.GetNode(i));

                        entry.Data = new BlittableJsonReaderObject(valueReader.Base, valueReader.Length, context);

                        node.Entries.Add(entry);
                    }
                }
            }

            FindAndAssignSourcesOfMapResults_Inefficiently(mapPhaseTree, context, tx, needToAssignSource);

            return new ReduceTree
            {
                Name = tree.Name.ToString(),
                Root = root
            };
        }

        private static ReduceTree RenderNestedSection(NestedMapResultsSection section, Tree mapPhaseTree, TransactionOperationContext context)
        {
            var entries = new Dictionary<long, BlittableJsonReaderObject>();
            var root = new ReduceTreePage(section.RelevantPage);

            section.GetResultsForDebug(context, entries);

            var needToAssignSource = new Dictionary<long, MapResultInLeaf>();

            foreach (var mapEntry in entries)
            {
                var entry = new MapResultInLeaf
                {
                    Data = mapEntry.Value
                };

                root.Entries.Add(entry);
                needToAssignSource[mapEntry.Key] = entry;
            }

            FindAndAssignSourcesOfMapResults_Inefficiently(mapPhaseTree, context, mapPhaseTree.Llt, needToAssignSource);

            return new ReduceTree
            {
                Name = section.Name.ToString(),
                Root = root
            };
        }

        private static void FindAndAssignSourcesOfMapResults_Inefficiently(Tree mapPhaseTree, TransactionOperationContext context,
                                    LowLevelTransaction tx, Dictionary<long, MapResultInLeaf> needToAssignSource)
        {
            // TODO arek - some cache at least?

            var mapEntriesFst = new FixedSizeTree(tx, mapPhaseTree, Slices.Empty, sizeof(ulong), clone: false);

            using (var it = mapPhaseTree.Iterate(false))
            {
                foreach (var docId in IterateKeys(it, null, 0, int.MaxValue, context))
                {
                    mapEntriesFst.RepurposeInstance(docId, false);

                    using (var fstIt = mapEntriesFst.Iterate())
                    {
                        do
                        {
                            MapResultInLeaf entry;
                            if (needToAssignSource.TryGetValue(fstIt.CurrentKey, out entry))
                            {
                                entry.Source = docId.ToString();
                                needToAssignSource.Remove(fstIt.CurrentKey);
                            }
                        } while (fstIt.MoveNext());
                    }

                    if (needToAssignSource.Count == 0)
                        break;
                }
            }
        }
    }
}