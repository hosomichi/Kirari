using System.Data.Common;
using System.Threading;

namespace Kirari
{
    /// <summary>
    /// Default implementation for <see cref="IConnectionWithId{TConnection}"/>.
    /// Automatically generate connection identifier when construct.
    /// </summary>
    /// <typeparam name="TConnection"></typeparam>
    public sealed class ConnectionWithId<TConnection> : IConnectionWithId<TConnection>
        where TConnection : DbConnection
    {
        private static long _id;

        public long Id { get; }

        public TConnection Connection { get; }

        public ConnectionWithId(TConnection connection)
        {
            this.Id = Interlocked.Increment(ref _id);
            this.Connection = connection;
        }

        public void Dispose()
        {
            this.Connection.Dispose();
        }
    }
}
