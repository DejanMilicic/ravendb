﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FastTests;
using Raven.Server.Documents;
using Sparrow.Threading;
using Xunit;
using Xunit.Abstractions;

namespace SlowTests.Issues;

public class RavenDB_19002 : NoDisposalNeeded
{
    public RavenDB_19002(ITestOutputHelper output) : base(output)
    {
    }

    private class MyDb : IDisposable
    {
        public string Name { get; }

        private readonly DisposeOnce<SingleAttempt> _disposeOnce;

        public MyDb(string name)
        {
            Name = name;
            _disposeOnce = new DisposeOnce<SingleAttempt>(() => { });
        }

        public bool IsDisposed => _disposeOnce.Disposed;

        public void Dispose()
        {
            _disposeOnce.Dispose();
        }
    }

    [Fact]
    public void ResourceCacheMustNotAllowToLeakDatabaseInstance()
    {
        var dbsCache = new ResourceCache<MyDb>();

        var dbName = "foo";

        var createdDbs = new List<MyDb>();

        var task1 = new Task<MyDb>(() =>
        {
            var myDb = new MyDb(dbName);

            createdDbs.Add(myDb);

            return myDb;
        }, TaskCreationOptions.RunContinuationsAsynchronously);

        var database1 = dbsCache.GetOrAdd(dbName, task1);

        if (database1 == task1)
        {
            task1.Start();
            task1.Wait();

            dbsCache.ForTestingPurposesOnly().OnRemoveLockAndReturnDispose = cache =>
            {
                cache.ForTestingPurposesOnly().OnRemoveLockAndReturnDispose = null;

                cache.TryGetValue(dbName, out var current);
                cache.TryRemove(dbName, current); // might be run from different thread

                var task2 = new Task<MyDb>(() =>
                {
                    var myDb = new MyDb(dbName);

                    createdDbs.Add(myDb);

                    return myDb;
                }, TaskCreationOptions.RunContinuationsAsynchronously);

                var database2 = dbsCache.GetOrAdd(dbName, task2); // might be run from different thread

                if (database2 == task2)
                {
                    task2.Start();
                    task2.Wait();
                }
            };

            using (dbsCache.RemoveLockAndReturn(dbName, x => x.Dispose(), out _))
            {

            }
        }

        var task3 = new Task<MyDb>(() =>
        {
            var myDb = new MyDb(dbName);

            createdDbs.Add(myDb);

            return myDb;
        }, TaskCreationOptions.RunContinuationsAsynchronously);

        var database3 = dbsCache.GetOrAdd(dbName, task3);
        if (database3 == task3)
        {
            task3.Start();
            task3.Wait();
        }

        using (dbsCache.RemoveLockAndReturn(dbName, x => x.Dispose(), out var db))
        {

        }

        Assert.Equal(2, createdDbs.Count);

        foreach (var db in createdDbs)
        {
            Assert.True(db.IsDisposed);
        }
    }
}