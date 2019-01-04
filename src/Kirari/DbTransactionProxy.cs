using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Kirari
{
    /// <summary>
    /// Dummy transaction wraps actual transaction controled by <see cref="ITransactionConnectionStrategy"/>.
    /// </summary>
    public class DbTransactionProxy : DbTransaction
    {
        [NotNull]
        private readonly ITransactionConnectionStrategy _strategy;

        protected override DbConnection DbConnection
            => this._strategy.TypicalConnection;

        public override IsolationLevel IsolationLevel { get; }

        public DbTransactionProxy([NotNull]ITransactionConnectionStrategy strategy,
            IsolationLevel isolationLevel)
        {
            this._strategy = strategy;
            this.IsolationLevel = isolationLevel;
        }

        /// <summary>
        /// Get current <see cref="DbTransaction"/> for command if transaction enabled.
        /// Return null if transaction disabled.
        /// </summary>
        [CanBeNull]
        public DbTransaction GetTransactionOrNull(DbCommandProxy command)
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
            base.Dispose(disposing);
            this._strategy.EndTransaction();
        }
    }
}
