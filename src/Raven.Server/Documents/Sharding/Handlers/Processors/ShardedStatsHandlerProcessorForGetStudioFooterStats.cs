﻿using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Raven.Client.Http;
using Raven.Server.Documents.Handlers.Processors;
using Raven.Server.Documents.Operations;
using Raven.Server.Documents.Sharding.Operations;
using Raven.Server.Documents.Studio;
using Raven.Server.ServerWide.Context;

namespace Raven.Server.Documents.Sharding.Handlers.Processors
{
    internal class ShardedStatsHandlerProcessorForGetStudioFooterStats : AbstractStatsHandlerProcessorForGetStudioFooterStats<ShardedRequestHandler, TransactionOperationContext>
    {
        public ShardedStatsHandlerProcessorForGetStudioFooterStats([NotNull] ShardedRequestHandler requestHandler) : base(requestHandler, requestHandler.ContextPool)
        {
        }
        
        protected override async ValueTask<FooterStatistics> GetFooterStatisticsAsync()
        {
            var op = new ShardedGetStudioFooterStatsOperation();
            var stats = await RequestHandler.ShardExecutor.ExecuteParallelForAllAsync(op);
            stats.CountOfIndexes = RequestHandler.ShardedContext.DatabaseRecord.Indexes.Count;
            //missing CountOfStaleIndexes and CountOfIndexingErrors fields
            return stats;
        }

        private readonly struct ShardedGetStudioFooterStatsOperation : IShardedOperation<FooterStatistics>
        {
            public FooterStatistics Combine(Memory<FooterStatistics> results)
            {
                var span = results.Span;

                var combined = new FooterStatistics();

                foreach (var stats in span)
                {
                    combined.CountOfDocuments += stats.CountOfDocuments;
                }

                return combined;
            }

            public RavenCommand<FooterStatistics> CreateCommandForShard(int shard) => new GetStudioFooterStatisticsOperation.GetStudioFooterStatisticsCommand();
        }
    }
}