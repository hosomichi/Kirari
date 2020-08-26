using MySql.Data.MySqlClient;

namespace Kirari.Samples.GlobalConnectionPoolStrategies
{
    public class PooledConnection
    {
        public IConnectionWithId<MySqlConnection> ConnectionWithId { get; }

        public int IndexInPool { get; }

        public long PayOutNumber { get; }

        public PooledConnection(IConnectionWithId<MySqlConnection> connectionWithId,
            int indexInPool,
            long payOutNumber)
        {
            this.ConnectionWithId = connectionWithId;
            this.IndexInPool = indexInPool;
            this.PayOutNumber = payOutNumber;
        }

        public void Deconstruct(out IConnectionWithId<MySqlConnection> connectionWithId, out int indexInPool, out long payOutNumber)
        {
            connectionWithId = this.ConnectionWithId;
            indexInPool = this.IndexInPool;
            payOutNumber = this.PayOutNumber;
        }
    }
}
