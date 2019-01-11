using System;
using Kirari.ConnectionStrategies;
using Kirari.Diagnostics;
using MySql.Data.MySqlClient;

namespace Kirari.Runner
{
    public class SampleConnection : DbConnectionProxy<MySqlConnection>
    {
        private class ConnectionFactory : IConnectionFactory<MySqlConnection>
        {
            public static IConnectionFactory<MySqlConnection> Instance { get; } = new ConnectionFactory();

            public IConnectionWithId<MySqlConnection> CreateConnection(ConnectionFactoryParameters parameters)
            {
                return new ConnectionWithId<MySqlConnection>(new MySqlConnection(parameters.ConnectionString));
            }
        }


        private class SingleConnectionStrategyFactory : IConnectionStrategyFactory<MySqlConnection>
        {
            public static IConnectionStrategyFactory<MySqlConnection> Instance { get; } = new SingleConnectionStrategyFactory();

            public ConnectionStrategyPair CreateStrategyPair(IConnectionFactory<MySqlConnection> connectionFactory, ConnectionFactoryParameters parameters)
            {
                var strategy = new QueuedSingleConnectionStrategy(connectionFactory);
                return new ConnectionStrategyPair(strategy,strategy);
            }
        }

        private class ConsoleMetricsReporter : ICommandMetricsReportable
        {
            public static ConsoleMetricsReporter Instance { get; } = new ConsoleMetricsReporter();

            public void Report(DbCommandMetrics commandMetrics)
            {
                Console.WriteLine($"{commandMetrics.ExecutionType} Duration:{commandMetrics.ExecutionElapsedTime}ms Command:{commandMetrics.CommandText}");
            }
        }

        public SampleConnection(string connectionString, bool forceSingleConnection)
            : base(
                connectionString,
                ConnectionFactory.Instance,
                forceSingleConnection
                    ? SingleConnectionStrategyFactory.Instance
                    : StandardConnectionStrategyFactory.Default,
                ConsoleMetricsReporter.Instance)
        {
        }
    }
}
