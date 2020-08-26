using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Kirari
{
    /// <summary>
    /// Dummy transaction wraps actual transaction controled by <see cref="ITransactionConnectionStrategy"/>.
    /// </summary>
    public class DbTransactionProxy : DbTransaction
    {
        private bool _disposed;

        private readonly ITransactionConnectionStrategy _strategy;

        protected override DbConnection DbConnection
            => this._strategy.TypicalConnection!; //Do not call DbConnection before TypicalConnection is determined.

        public override IsolationLevel IsolationLevel { get; }

        public DbTransactionProxy(ITransactionConnectionStrategy strategy,
            IsolationLevel isolationLevel)
        {
            this._strategy = strategy;
            this.IsolationLevel = isolationLevel;
        }

        /// <summary>
        /// Get current <see cref="DbTransaction"/> for command if transaction enabled.
        /// Return null if transaction disabled.
        /// </summary>
        public DbTransaction? GetTransactionOrNull(DbCommandProxy command)
            => this._strategy.GetTransactionOrNull(command);

        public override void Commit()
            => this.CommitAsync().GetAwaiter().GetResult();

        /// <inheritdoc cref="Commit"/>
        public Task CommitAsync()
            => this.CommitAsync(CancellationToken.None);

        /// <inheritdoc cref="Commit"/>
        public Task CommitAsync(CancellationToken cancellationToken)
            => this._strategy.CommitAsync(cancellationToken);

        public override void Rollback()
            => this.RollbackAsync().GetAwaiter().GetResult();

        /// <inheritdoc cref="Rollback"/>
        public Task RollbackAsync()
            => this.RollbackAsync(CancellationToken.None);

        /// <inheritdoc cref="Rollback"/>
        public Task RollbackAsync(CancellationToken cancellationToken)
            => this._strategy.RollbackAsync(cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if(this._disposed) return;
            this._disposed = true;

            DisposeHelper.EnsureAllSteps(
                () => base.Dispose(disposing),
                () => this._strategy.EndTransaction());
        }
    }
}
