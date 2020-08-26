using System;
using System.Data.Common;

namespace Kirari
{
    /// <summary>
    /// Target connection instance with unique identifier.
    /// </summary>
    public interface IConnectionWithId<out TConnection> : IDisposable
        where TConnection : DbConnection
    {
        /// <summary>
        /// Get unique identifier for connection.
        /// </summary>
        long Id { get; }

        /// <summary>
        /// Get created connection by factory.
        /// </summary>
        TConnection Connection { get; }
    }
}
