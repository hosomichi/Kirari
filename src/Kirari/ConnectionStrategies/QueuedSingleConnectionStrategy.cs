using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Kirari.Diagnostics;

namespace Kirari.ConnectionStrategies
{
    /// <summary>
    /// This strategy uses only single connection.
    /// Queue commands and dequeue next when previous command is disposed.
    /// </summary>
    public sealed class QueuedSingleConnectionStrategy : IDefaultConnectionStrategy, ITransactionConnectionStrategy
    {
        [NotNull]
        private readonly IConnectionFactory<DbConnection> _factory;

        [NotNull]
        private readonly object _commandQueueLock = new object();

        [NotNull]
        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);

        [NotNull]
        private readonly Queue<TaskCompletionSource<bool>> _commandPreparerQueue = new Queue<TaskCompletionSource<bool>>();

        [CanBeNull]
        private IConnectionWithId<DbConnection> _connection;

        [CanBeNull]
        private DbTransaction _transaction;

        [CanBeNull]
        private string _overriddenDatabaseName;

        private bool _isCommandExecuting;

        public DbConnection TypicalConnection => this._connection?.Connection;

        public QueuedSingleConnectionStrategy([NotNull] IConnectionFactory<DbConnection> factory)
        {
            this._factory = factory;
        }

        public async Task<DbCommandProxy> CreateCommandAsync(ConnectionFactoryParameters parameters,
            ICommandMetricsReportable metricsReporter,
            CancellationToken cancellationToken)
        {
            var preparer = new TaskCompletionSource<bool>();
            lock (this._commandQueueLock)
            {
                if (!this._isCommandExecuting)
                {
                    this._isCommandExecuting = true;
                    preparer.SetResult(true);
                }
                else this._commandPreparerQueue.Enqueue(preparer);
            }

            await preparer.Task.ConfigureAwait(false);

            var connection = await this.GetOrCreateConnectionAsync(parameters, cancellationToken).ConfigureAwait(false);

            var sourceCommand = connection.Connection.CreateCommand();

            if (this._transaction != null)
            {
                sourceCommand.Transaction = this._transaction;
            }

            var command = new DbCommandProxy(
                connection.Id,
                sourceCommand,
                metricsReporter,
                this.OnCommandCompleted);

            return command;
        }

        public async Task ChangeDatabaseAsync(string databaseName, CancellationToken cancellationToken)
        {
            await this._connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                this._overriddenDatabaseName = databaseName;
                if (this._connection == null) return;
                this._connection.Connection.ChangeDatabase(databaseName);
            }
            finally
            {
                this._connectionLock.Release();
            }
        }

        public DbConnection GetConnectionOrNull(DbCommandProxy command)
            => this._connection?.Connection;

        public async Task BeginTransactionAsync(IsolationLevel isolationLevel, ConnectionFactoryParameters parameters, CancellationToken cancellationToken)
        {
            var connection = await this.GetOrCreateConnectionAsync(parameters, cancellationToken).ConfigureAwait(false);
            this._transaction = connection.Connection.BeginTransaction(isolationLevel);
        }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            this._transaction?.Commit();
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken)
        {
            this._transaction?.Rollback();
            return Task.CompletedTask;
        }

        public void EndTransaction()
        {
            this._transaction?.Dispose();
            this._transaction = null;
        }

        public DbTransaction GetTransactionOrNull(DbCommandProxy command)
            => this._transaction;

        [ItemNotNull]
        private async Task<IConnectionWithId<DbConnection>> GetOrCreateConnectionAsync(ConnectionFactoryParameters parameters, CancellationToken cancellationToken)
        {
            if (this._connection != null) return this._connection;

            await this._connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (this._connection != null) return this._connection;
                var connection = this._factory.CreateConnection(parameters);
                await connection.Connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(this._overriddenDatabaseName))
                {
                    connection.Connection.ChangeDatabase(this._overriddenDatabaseName);
                }

                return this._connection = connection;
            }
            finally
            {
                this._connectionLock.Release();
            }
        }

        private void OnCommandCompleted(DbCommandProxy command)
        {
            lock (this._commandQueueLock)
            {
                if (this._commandPreparerQueue.Count > 0)
                {
                    var preparer = this._commandPreparerQueue.Dequeue();
                    preparer.SetResult(true);
                }
                else this._isCommandExecuting = false;
            }
        }

        public void Dispose()
        {
            this.ReleaseUnmanagedResources();
            GC.SuppressFinalize(this);
        }

        ~QueuedSingleConnectionStrategy()
        {
            this.ReleaseUnmanagedResources();
        }

        private void ReleaseUnmanagedResources()
        {
            this._connection?.Dispose();
            this._connection = null;
        }
    }
}
