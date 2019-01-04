using System;
using Kirari.ConnectionStrategies;
using MySql.Data.MySqlClient;

namespace Kirari.Runner
{
    public class SampleConnection : DbConnectionProxy<MySqlConnection>
    {
        private class ConnectionFactory : IConnectionFactory<MySqlConnection>
        {
            public static IConnectionFactory<MySqlConnection> Instance { get; } = new ConnectionFactory();

            public MySqlConnection CreateConnection(ConnectionFactoryParameters parameters)
            {
                return new MySqlConnection(parameters.ConnectionString);
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

        public SampleConnection(string connectionString, bool forceSingleConnection)
            : base(
                connectionString,
                ConnectionFactory.Instance,
                forceSingleConnection
                    ? SingleConnectionStrategyFactory.Instance
                    : StandardConnectionStrategyFactory.Default)
        {
        }
    }
}
