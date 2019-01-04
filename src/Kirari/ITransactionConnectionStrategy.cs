using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Kirari
{
    /// <summary>
    /// Connection strategy for transaction scope.
    /// </summary>
    public interface ITransactionConnectionStrategy : IConnectionStrategy
    {
        /// <summary>
        /// Connection used for <see cref="DbTransaction.Connection"/> property.
        /// </summary>
        DbConnection TypicalConnection { get; }

        /// <summary>
        /// Enable transaction until <see cref="EndTransaction"/> is called.
        /// </summary>
        Task BeginTransactionAsync(IsolationLevel isolationLevel, ConnectionFactoryParameters parameters, CancellationToken cancellationToken);

        /// <summary>
        /// Commit transaction if transaction enabled.
        /// </summary>
        Task CommitAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Rollback transaction if transaction enabled.
        /// </summary>
        Task RollbackAsync(CancellationToken cancellationToken);

        /// <summary>
        /// End transaction if transaction enabled.
        /// </summary>
        void EndTransaction();

        /// <summary>
        /// Get current <see cref="DbTransaction"/> for command if transaction enabled.
        /// Return null if transaction disabled.
        /// </summary>
        [CanBeNull]
        DbTransaction GetTransactionOrNull(DbCommandProxy command);
    }
}
