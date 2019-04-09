using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Kirari.Diagnostics;

namespace Kirari
{
    /// <summary>
    /// Dummy Command wraps actual command.
    /// Detect command end by <see cref="Dispose"/>.
    /// </summary>
    public class DbCommandProxy : DbCommand
    {
        private static long _id;

        [CanBeNull]
        private readonly ICommandMetricsReportable _commandReporter;

        [NotNull]
        private readonly Action<DbCommandProxy> _onDisposed;

        private bool _disposed;

        /// <summary>
        /// Get unique identifier for this command.
        /// </summary>
        public long Id { get; }

        /// <summary>
        /// Get unique identifier for connection linked with this command.
        /// </summary>
        public long ConnectionId { get; }

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

        public DbCommandProxy(long connectionId,
            [NotNull] DbCommand sourceCommand,
            [CanBeNull] ICommandMetricsReportable commandReporter,
            [NotNull] Action<DbCommandProxy> onDisposed)
        {
            this.Id = Interlocked.Increment(ref _id);
            this.ConnectionId = connectionId;
            this.SourceCommand = sourceCommand;
            this._commandReporter = commandReporter;
            this._onDisposed = onDisposed;
        }

        public override void Cancel()
            => this.SourceCommand.Cancel();

        protected override DbParameter CreateDbParameter()
            => this.SourceCommand.CreateParameter();

        public override int ExecuteNonQuery()
            => this.ExecuteWithReporting(DbCommandExecutionType.ExecuteNonQuery,
                this.SourceCommand.ExecuteNonQuery);

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
            => this.ExecuteWithReportingAsync(DbCommandExecutionType.ExecuteNonQuery,
                () => this.SourceCommand.ExecuteNonQueryAsync(cancellationToken));

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => this.ExecuteWithReporting(DbCommandExecutionType.ExecuteReader,
                () => this.SourceCommand.ExecuteReader(behavior));

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
            => this.ExecuteWithReportingAsync(DbCommandExecutionType.ExecuteReader,
                () => this.SourceCommand.ExecuteReaderAsync(behavior, cancellationToken));

        public override object ExecuteScalar()
            => this.ExecuteWithReporting(DbCommandExecutionType.ExecuteScalar,
                this.SourceCommand.ExecuteScalar);

        public override Task<object> ExecuteScalarAsync(CancellationToken cancellationToken)
            => this.ExecuteWithReportingAsync(DbCommandExecutionType.ExecuteScalar,
                () => this.SourceCommand.ExecuteScalarAsync(cancellationToken));

        public override void Prepare()
            => this.SourceCommand.Prepare();

        private T ExecuteWithReporting<T>(DbCommandExecutionType executionType,
            Func<T> execute)
        {
            if (this._commandReporter == null) return execute();

            var startTime = DateTimeOffset.Now;
            var stopwatch = Stopwatch.StartNew();

            T result;
            Exception exception = null;
            try
            {
                result = execute();
            }
            catch (Exception ex)
            {
                exception = ex;
                throw;
            }
            finally
            {
                stopwatch.Stop();

                var parameters = this.Parameters.OfType<IDataParameter>()
                    .Select(x => new DbCommandParameterMetrics(x.ParameterName, x.DbType, x.Value))
                    .ToArray();
                this._commandReporter.Report(new DbCommandMetrics(this.Id,
                    this.ConnectionId,
                    executionType,
                    this.SourceCommand.CommandText,
                    parameters,
                    startTime,
                    stopwatch.Elapsed,
                    exception));
            }

            return result;
        }

        private async Task<T> ExecuteWithReportingAsync<T>(DbCommandExecutionType executionType,
            Func<Task<T>> execute)
        {
            if (this._commandReporter == null) return await execute().ConfigureAwait(false);

            var startTime = DateTimeOffset.Now;
            var stopwatch = Stopwatch.StartNew();

            T result;
            Exception exception = null;
            try
            {
                result = await execute().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw;
            }
            finally
            {
                stopwatch.Stop();

                var parameters = this.Parameters.OfType<IDataParameter>()
                    .Select(x => new DbCommandParameterMetrics(x.ParameterName, x.DbType, x.Value))
                    .ToArray();
                this._commandReporter.Report(new DbCommandMetrics(this.Id,
                    this.ConnectionId,
                    executionType,
                    this.SourceCommand.CommandText,
                    parameters,
                    startTime,
                    stopwatch.Elapsed,
                    exception));
            }

            return result;
        }

        protected override void Dispose(bool disposing)
        {
            if(this._disposed) return;
            this._disposed = true;

            DisposeHelper.EnsureAllSteps(
                () => base.Dispose(disposing),
                () => this.SourceCommand.Dispose(),
                () => this._onDisposed(this));
        }
    }
}
