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
            var data = new MockedData (500000);

            var list = new RedisList (Guid.NewGuid ().ToString (), cache, data);
            // var set = new RedisSortedSet (Guid.NewGuid ().ToString (), cache, data);

            Console.WriteLine ("List Testing");
            PerformanceTesting.TimeOperation ("Insert all data into a list", list.AddAllRecords);
            PerformanceTesting.TimeOperation ("Lookup all records sequentially - one by one", list.LookupRecord);

            Console.WriteLine ("++++++++++============================================++++++++++");

            Console.WriteLine ($"Removing list id: {list.Identifier}");
            cache.KeyDelete (list.Identifier);

            //     Console.WriteLine ("Sorted Set Testing");
            //     PerformanceTesting.TimeOperation ("Insert all data into a sorted set", set.AddAllRecords);
            //     PerformanceTesting.TimeOperation ("Lookup all records sequentially - one by one", set.LookupRecordsSequentially);
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

            internal static TimeSpan TimeOperation (string message, Func < int, (LookupRecord, LookupRecord) > func) {
                var sw = Stopwatch.StartNew ();
                func (450000);
                sw.Stop ();

                Console.WriteLine ($"Testing {message} - Elapsed = {sw.Elapsed}");
                return sw.Elapsed;
            }
        }

        public abstract class Redis {
            public string Identifier { get; set; }
            protected long[] Records { get; set; }
            protected IDatabase Cache { get; set; }

            protected Redis (string identifier, IDatabase cache, MockedData data) {
                Identifier = identifier;
                Records = data.Records;
                Cache = cache;
            }

            public abstract void AddAllRecords ();

            // ensure first and prev portfolios work
            // test first - 1 && last + 1

            // 500000
            //     public void LookupRecordsSequentially () {
            //         LookupRecord (299997);
            //         LookupRecord (299999);
            //         LookupRecord (299999);
            //     }
        }

        public class LookupRecord {
            public int Index { get; set; }
            public RedisValue Value { get; set; }

            public LookupRecord (RedisValue value, int index) {
                Value = value;
                Index = index;
            }
        }

        public class RedisList : Redis {
            public RedisList (string identifier, IDatabase cache, MockedData data) : base (identifier, cache, data) { }

            public override void AddAllRecords () {
                var redisValues = Array.ConvertAll (Records, item => (RedisValue) item);
                Cache.ListRightPush (Identifier, redisValues);
            }

            public (LookupRecord prev, LookupRecord next) LookupRecord (int index) {
                var listLength = Cache.ListLength (Identifier);

                if (index < 0 || index > listLength)
                    throw new ArgumentException ("index out of range");

                // var items = new RedisValue[];
                // if (index == 0) {
                //     items = Cache.ListRange (Identifier, 0, index + 1);
                //     return (RedisValue.Null, items[1]);
                // } else if (index == listLength) {
                //     items = Cache.ListRange (Identifier, index - 1, -1);
                //     return (items[0], RedisValue.Null);
                // }

                // this function would actually have to return prev and next indexes also

                int startRange = index == 0 ? 0 : index - 1;
                int endRange = index == listLength ? -1 : index + 1;
                var items = Cache.ListRange (Identifier, startRange, endRange);
                RedisValue prev = index == 0 ? RedisValue.Null : items.First ();
                RedisValue next = index == listLength ? RedisValue.Null : items.Last ();
                Console.WriteLine ($"Current index: {index}");
                Console.WriteLine ($"Prev index: {index - 1}");
                Console.WriteLine ($"Prev value: {prev}");
                Console.WriteLine ($"Next index: {index + 1}");
                Console.WriteLine ($"Next value: {next}");
                return (new LookupRecord (prev, index - 1), new LookupRecord (next, index + 1));

                // if first
                // prev should be null & next should be good

                // if last
                // prev should be good & next should be null

                // else
                //     both prev and next should be good
            }
        }

        // public class RedisSortedSet : Redis {
        //     public RedisSortedSet (string identifier, IDatabase cache, MockedData data) : base (identifier, cache, data) { }

        //     public override void AddAllRecords () {
        //         SortedSetEntry[] sortedSetRecords = Records.Select ((item, index) => new SortedSetEntry (item, index)).ToArray ();
        //         Cache.SortedSetAdd (Identifier, sortedSetRecords);
        //     }

        //     public override void LookupRecord (int index) {
        //         Cache.SortedSetRangeByScore (Identifier, index, index);
        //     }
        // }

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

                Random rnd = new Random ();
                Records = Records.OrderBy (x => rnd.Next ()).ToArray ();
            }
        }
    }
}