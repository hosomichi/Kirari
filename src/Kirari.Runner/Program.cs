using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using MySql.Data.MySqlClient;

namespace Kirari.Runner
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Run().GetAwaiter().GetResult();
        }

        private static async Task Run()
        {
            IConfigurationSource source;
            IConfiguration config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", true, true)
                .Build();

            var connectionString = config.GetConnectionString("Default");

            Console.WriteLine($"Setup");
            const string databaseName = "KirariTest";
            using (var conn = new SampleConnection(connectionString, false))
            {
                await conn.ExecuteAsync($"CREATE DATABASE {databaseName};");
                conn.ChangeDatabase(databaseName);
                await conn.ExecuteAsync(@"
CREATE TABLE TestData
(
  Id BIGINT NOT NULL PRIMARY KEY,
  Name TEXT NOT NULL
);
");
            }

            try
            {
                Console.WriteLine($"Multiple connection");
                var stopWatch = Stopwatch.StartNew();
                using (var conn = new SampleConnection(connectionString, false))
                {
                    await conn.ChangeDatabaseAsync(databaseName);

                    Console.WriteLine($"Preapare Data (Elaplsed: {stopWatch.ElapsedMilliseconds}ms)");
                    using (var transaction = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted))
                    {
                        const string insertTemplate = "INSERT INTO TestData (Id, Name) VALUES (@id, @name)";
                        await Task.WhenAll(
                            conn.ExecuteAsync(insertTemplate, new { id = 1, name = "TestData1" }),
                            conn.ExecuteAsync(insertTemplate, new { id = 2, name = "TestData2" }),
                            conn.ExecuteAsync(insertTemplate, new { id = 3, name = "TestData3" }),
                            conn.ExecuteAsync(insertTemplate, new { id = 4, name = "TestData4" })
                        );

                        transaction.Commit();
                    }

                    Console.WriteLine($"Multiple Await (Elaplsed: {stopWatch.ElapsedMilliseconds}ms)");
                    foreach (var count in Enumerable.Range(0, 10))
                    {
                        var (val1, val2, val3, val4) = await (
                                conn.QueryAsync<TestData>("SELECT * FROM TestData "),
                                conn.QueryAsync<TestData>("SELECT * FROM TestData ORDER BY Id DESC"),
                                conn.QueryAsync<TestData>("SELECT * FROM TestData WHERE Id = 1"),
                                conn.QueryAsync<TestData>("SELECT * FROM TestData WHERE Id In (2,4)"))
                            .WhenAll();
                        Console.WriteLine($"Result{count}-1 " + string.Join(", ", val1.Select(x => x.ToString())));
                        Console.WriteLine($"Result{count}-2 " + string.Join(", ", val2.Select(x => x.ToString())));
                        Console.WriteLine($"Result{count}-3 " + string.Join(", ", val3.Select(x => x.ToString())));
                        Console.WriteLine($"Result{count}-4 " + string.Join(", ", val4.Select(x => x.ToString())));
                    }

                    Console.WriteLine($"Parallel (Elaplsed: {stopWatch.ElapsedMilliseconds}ms)");
                    Parallel.For(0, 40, new ParallelOptions() { MaxDegreeOfParallelism = 4 }, count =>
                    {
                        IEnumerable<TestData> val;
                        switch (count % 4)
                        {
                            case 0:
                                val = conn.Query<TestData>("SELECT * FROM TestData ");
                                break;
                            case 1:
                                val = conn.Query<TestData>("SELECT * FROM TestData ORDER BY Id DESC");
                                break;
                            case 2:
                                val = conn.Query<TestData>("SELECT * FROM TestData WHERE Id = 1");
                                break;
                            case 3:
                                val = conn.Query<TestData>("SELECT * FROM TestData WHERE Id In (2,4)");
                                break;
                            default:
                                val = Array.Empty<TestData>();
                                break;
                        }

                        Console.WriteLine($"Result{count} " + string.Join(", ", val.Select(x => x.ToString())));
                    });

                    Console.WriteLine($"Cleanup Data (Elaplsed: {stopWatch.ElapsedMilliseconds}ms)");
                    await conn.ExecuteAsync("TRUNCATE TestData");
                }

                Console.WriteLine($"Single Connection (Elaplsed: {stopWatch.ElapsedMilliseconds}ms)");
                using (var conn = new SampleConnection(connectionString, true))
                {
                    await conn.ChangeDatabaseAsync(databaseName);

                    Console.WriteLine($"Preapare Data (Elaplsed: {stopWatch.ElapsedMilliseconds}ms)");
                    using (var transaction = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted))
                    {
                        const string insertTemplate = "INSERT INTO TestData (Id, Name) VALUES (@id, @name)";
                        await Task.WhenAll(
                            conn.ExecuteAsync(insertTemplate, new { id = 1, name = "TestData1" }),
                            conn.ExecuteAsync(insertTemplate, new { id = 2, name = "TestData2" }),
                            conn.ExecuteAsync(insertTemplate, new { id = 3, name = "TestData3" }),
                            conn.ExecuteAsync(insertTemplate, new { id = 4, name = "TestData4" })
                        );

                        transaction.Commit();
                    }

                    Console.WriteLine($"Multiple Await (Elaplsed: {stopWatch.ElapsedMilliseconds}ms)");
                    foreach (var count in Enumerable.Range(0, 10))
                    {
                        var (val1, val2, val3, val4) = await (
                                conn.QueryAsync<TestData>("SELECT * FROM TestData "),
                                conn.QueryAsync<TestData>("SELECT * FROM TestData ORDER BY Id DESC"),
                                conn.QueryAsync<TestData>("SELECT * FROM TestData WHERE Id = 1"),
                                conn.QueryAsync<TestData>("SELECT * FROM TestData WHERE Id In (2,4)"))
                            .WhenAll();
                        Console.WriteLine($"Result{count}-1 " + string.Join(", ", val1.Select(x => x.ToString())));
                        Console.WriteLine($"Result{count}-2 " + string.Join(", ", val2.Select(x => x.ToString())));
                        Console.WriteLine($"Result{count}-3 " + string.Join(", ", val3.Select(x => x.ToString())));
                        Console.WriteLine($"Result{count}-4 " + string.Join(", ", val4.Select(x => x.ToString())));
                    }

                    Console.WriteLine($"Parallel (Elaplsed: {stopWatch.ElapsedMilliseconds}ms)");
                    Parallel.For(0, 40, new ParallelOptions() { MaxDegreeOfParallelism = 4 }, count =>
                    {
                        IEnumerable<TestData> val;
                        switch (count % 4)
                        {
                            case 0:
                                val = conn.Query<TestData>("SELECT * FROM TestData ");
                                break;
                            case 1:
                                val = conn.Query<TestData>("SELECT * FROM TestData ORDER BY Id DESC");
                                break;
                            case 2:
                                val = conn.Query<TestData>("SELECT * FROM TestData WHERE Id = 1");
                                break;
                            case 3:
                                val = conn.Query<TestData>("SELECT * FROM TestData WHERE Id In (2,4)");
                                break;
                            default:
                                val = Array.Empty<TestData>();
                                break;
                        }

                        Console.WriteLine($"Result{count} " + string.Join(", ", val.Select(x => x.ToString())));
                    });

                    Console.WriteLine($"Cleanup Data (Elaplsed: {stopWatch.ElapsedMilliseconds}ms)");
                    await conn.ExecuteAsync("TRUNCATE TestData");
                }
            }
            finally
            {
                Console.WriteLine($"Cleanup");
                using (var conn = new SampleConnection(connectionString, false))
                {
                    await conn.ExecuteAsync($"DROP DATABASE {databaseName};");
                }
            }
        }

        private class TestData
        {
            public long Id { get; set; }

            public string Name { get; set; }

            public override string ToString()
            {
                return $"{{Id = {this.Id}, Name = {this.Name}}}";
            }
        }
    }
}
