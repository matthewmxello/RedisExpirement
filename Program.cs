using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace RedisExpirement {
    class Program {
        public static void Main (string[] args) {
            var builder = new ConfigurationBuilder ()
                .SetBasePath (Directory.GetCurrentDirectory ())
                .AddJsonFile ("appsettings.json");
            var configuration = builder.Build ();
            var redisConnection = configuration["RedisConnection"];

            var redis = ConnectionMultiplexer.Connect (redisConnection);
            FlushAllDbs (redisConnection);

            var cache = redis.GetDatabase ();
            var data = new MockedData (300000);

            var list = new RedisList (Guid.NewGuid ().ToString (), cache, data);
            var set = new RedisSortedSet (Guid.NewGuid ().ToString (), cache, data);

            Console.WriteLine ("List Testing");
            PerformanceTesting.TimeOperation ("Insert all data into a list", list.AddAllRecords);
            // PerformanceTesting.TimeOperation ("Lookup all records sequentially - one by one", list.LookupRecordsSequentially);

            Console.WriteLine ("++++++++++============================================++++++++++");

            Console.WriteLine ("Sorted Set Testing");
            // PerformanceTesting.TimeOperation ("Insert all data into a sorted set", set.AddAllRecords);
            // PerformanceTesting.TimeOperation ("Lookup all records sequentially - one by one", set.LookupRecordsSequentially);
        }

        public static void FlushAllDbs (string redisConnection) {
            var options = ConfigurationOptions.Parse (redisConnection);
            options.AllowAdmin = true;
            var redis = ConnectionMultiplexer.Connect (options);

            var endpoints = redis.GetEndPoints ();
            var server = redis.GetServer (endpoints[0]);
            server.FlushAllDatabases ();
        }

        public static class PerformanceTesting {
            public static TimeSpan TimeOperation (string message, Action action) {
                var sw = Stopwatch.StartNew ();
                action ();
                sw.Stop ();

                Console.WriteLine ($"Testing {message} - Elapsed = {sw.Elapsed}");
                return sw.Elapsed;
            }
        }

        public abstract class Redis {
            protected string Identifier { get; set; }
            protected long[] Records { get; set; }
            protected IDatabase Cache { get; set; }

            protected Redis (string identifier, IDatabase cache, MockedData data) {
                Identifier = identifier;
                Records = data.Records;
                Cache = cache;
            }

            public abstract void AddAllRecords ();
            public abstract void LookupRecord (int index);

            public void LookupRecordsSequentially () {
                for (int i = 0; i < Records.Length; i++)
                    LookupRecord (i);
            }
        }

        public class RedisList : Redis {
            public RedisList (string identifier, IDatabase cache, MockedData data) : base (identifier, cache, data) { }

            public override void AddAllRecords () {
                for (var i = 0; i < Records.Length; i++)
                    Cache.ListRightPush (Identifier, Records[i]);
            }

            public override void LookupRecord (int index) {
                Cache.ListGetByIndex (Identifier, index);
            }
        }

        public class RedisSortedSet : Redis {
            public RedisSortedSet (string identifier, IDatabase cache, MockedData data) : base (identifier, cache, data) { }

            public override void AddAllRecords () {
                for (var i = 0; i < Records.Length; i++)
                    Cache.SortedSetAdd (Identifier, Records[i], i);
            }

            public override void LookupRecord (int index) {
                Cache.SortedSetRangeByScore (Identifier, index, index);
            }
        }

        public class MockedData {
            public long[] Records { get; set; }
            public MockedData (long numOfRecords) {
                CreateRecords (numOfRecords);
            }

            private void CreateRecords (long numOfRecords) {
                Records = new long[numOfRecords];
                for (var i = 0; i < Records.Length; i++) {
                    Records[i] = i * 100;
                }
            }
        }
    }
}