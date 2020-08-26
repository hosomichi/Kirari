using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Kirari.Diagnostics;

namespace Kirari
{
    /// <summary>
    /// Shared interface for <see cref="IDefaultConnectionStrategy"/> and <see cref="ITransactionConnectionStrategy"/>.
    /// </summary>
    public interface IConnectionStrategy : IDisposable
    {
        /// <summary>
        /// Create new command wrapped by <see cref="DbCommandProxy"/>.
        /// Detect command end by <see cref="DbCommandProxy.Dispose"/>.
        /// </summary>
        Task<DbCommandProxy> CreateCommandAsync(ConnectionFactoryParameters parameters,
            ICommandMetricsReportable? metricsReporter,
            CancellationToken cancellationToken);

        /// <summary>
        /// Change database to <paramref name="databaseName"/>.
        /// <paramref name="databaseName"/> is validated by <see cref="DbConnectionProxy{TConnection}"/>.
        /// </summary>
        Task ChangeDatabaseAsync(string databaseName, CancellationToken cancellationToken);

        DbConnection? GetConnectionOrNull(DbCommandProxy command);
    }
}
