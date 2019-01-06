using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Kirari
{
    /// <summary>
    /// Dummy Command wraps actual command.
    /// Detect command end by <see cref="Dispose"/>.
    /// </summary>
    public class DbCommandProxy : DbCommand
    {
        [NotNull]
        private readonly Action<DbCommandProxy> _onDisposed;

        /// <summary>
        /// Get wrapped original command.
        /// </summary>
        [NotNull]
        public DbCommand SourceCommand { get; }

        public override string CommandText
        {
            get => this.SourceCommand.CommandText;
            set => this.SourceCommand.CommandText = value;
        }

        public override int CommandTimeout
        {
            get => this.SourceCommand.CommandTimeout;
            set => this.SourceCommand.CommandTimeout = value;
        }

        public override CommandType CommandType
        {
            get => this.SourceCommand.CommandType;
            set => this.SourceCommand.CommandType = value;
        }

        protected override DbConnection DbConnection
        {
            get => this.SourceCommand.Connection;
            set => this.SourceCommand.Connection = value is IDbConnectionProxy connectionProxy
                ? connectionProxy.GetConnectionOrNull(this) ?? throw new InvalidOperationException("There is no suitable connection for this commnd.")
                : value;
        }

        protected override DbParameterCollection DbParameterCollection
            => this.SourceCommand.Parameters;

        protected override DbTransaction DbTransaction
        {
            get => this.SourceCommand.Transaction;
            set => this.SourceCommand.Transaction = value is DbTransactionProxy transactionProxy
                ? transactionProxy.GetTransactionOrNull(this)
                : value;
        }

        public override bool DesignTimeVisible
        {
            get => this.SourceCommand.DesignTimeVisible;
            set => this.SourceCommand.DesignTimeVisible = value;
        }

        public override UpdateRowSource UpdatedRowSource
        {
            get => this.SourceCommand.UpdatedRowSource;
            set => this.SourceCommand.UpdatedRowSource = value;
        }

        public DbCommandProxy([NotNull] DbCommand sourceCommand,
            [NotNull] Action<DbCommandProxy> onDisposed)
        {
            this.SourceCommand = sourceCommand;
            this._onDisposed = onDisposed;
        }

        public override void Cancel()
            => this.SourceCommand.Cancel();

        protected override DbParameter CreateDbParameter()
            => this.SourceCommand.CreateParameter();

        public override int ExecuteNonQuery()
            => this.SourceCommand.ExecuteNonQuery();

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
            => this.SourceCommand.ExecuteNonQueryAsync(cancellationToken);

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => this.SourceCommand.ExecuteReader(behavior);

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
            => this.SourceCommand.ExecuteReaderAsync(behavior, cancellationToken);

        public override object ExecuteScalar()
            => this.SourceCommand.ExecuteScalar();

        public override Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
            => this.SourceCommand.ExecuteScalarAsync(cancellationToken);

        public override void Prepare()
            => this.SourceCommand.Prepare();

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            this.SourceCommand.Dispose();
            this._onDisposed(this);
        }
    }
}
