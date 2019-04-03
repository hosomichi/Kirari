using MySql.Data.MySqlClient;

namespace Kirari.Samples.GlobalConnectionPoolStrategies
{
    public class GlobalConnectionPoolStrategyFactory : IConnectionStrategyFactory<MySqlConnection>
    {
        private readonly GlobalConnectionPool _pool;

        public GlobalConnectionPoolStrategyFactory(GlobalConnectionPool pool)
        {
            this._pool = pool;
        }

        public ConnectionStrategyPair CreateStrategyPair(IConnectionFactory<MySqlConnection> connectionFactory, ConnectionFactoryParameters parameters)
        {
            return new ConnectionStrategyPair(
                new GlobalConnectionPoolStrategy(this._pool),
                new GlobalConnectionPoolTransactionStrategy(this._pool));
        }
    }
}
