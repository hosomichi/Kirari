using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Kirari.Diagnostics;
using MySql.Data.MySqlClient;

namespace Kirari.Samples.GlobalConnectionPoolStrategies
{
    public class GlobalConnectionPoolTransactionStrategy: IDefaultConnectionStrategy, ITransactionConnectionStrategy
    {
        [NotNull]
        private readonly object _commandQueueLock = new object();

        [NotNull]
        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);

        [NotNull]
        private readonly Queue<TaskCompletionSource<bool>> _commandPreparerQueue = new Queue<TaskCompletionSource<bool>>();

        private  GlobalConnectionPool _pool;

        [CanBeNull]
        private IConnectionWithId<MySqlConnection> _connection;

        private int? _connectionIndex;

        [CanBeNull]
        private MySqlTransaction _transaction;

        [CanBeNull]
        private string _overriddenDatabaseName;

        private bool _isCommandExecuting;

        public DbConnection TypicalConnection => this._connection?.Connection;

        public GlobalConnectionPoolTransactionStrategy([NotNull] GlobalConnectionPool pool)
        {
            this._pool = pool;
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

            var connection = await this.GetConnectionAsync(parameters, cancellationToken).ConfigureAwait(false);

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
            var connection = await this.GetConnectionAsync(parameters, cancellationToken).ConfigureAwait(false);
            this._transaction = await connection.Connection.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
        }

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            if (this._transaction == null) return;
            await this._transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task RollbackAsync(CancellationToken cancellationToken)
        {
            if (this._transaction == null) return;
            await this._transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }

        public void EndTransaction()
        {
            this._transaction?.Dispose();
            this._transaction = null;
        }

        public DbTransaction GetTransactionOrNull(DbCommandProxy command)
            => this._transaction;

        [ItemNotNull]
        private async Task<IConnectionWithId<MySqlConnection>> GetConnectionAsync(ConnectionFactoryParameters parameters, CancellationToken cancellationToken)
        {
            //取得済ならそのまま使う。
            if (this._connection != null) return this._connection;

            await this._connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (this._connection != null) return this._connection;

                //未取得の場合は Pool から払い出してもらう。
                //Strategy が破棄されるまで返さないぞ！
                var (connection, index) = await this._pool.GetConnectionAsync( parameters, cancellationToken).ConfigureAwait(false);
                this._connection = connection;
                this._connectionIndex = index;

                if (!string.IsNullOrWhiteSpace(this._overriddenDatabaseName) && connection.Connection.Database != this._overriddenDatabaseName)
                {
                    await connection.Connection.ChangeDatabaseAsync(this._overriddenDatabaseName, cancellationToken).ConfigureAwait(false);
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
            //Transaction が生き残っていたら終わらせる。
            this.EndTransaction();

            //Connection を Pool に返す。
            if (this._connectionIndex.HasValue)
            {
                this._pool.ReleaseConnection(this._connectionIndex.Value);
            }

            //借りてたものをもう使わないという意思表示。
            this._connection = null;
            this._connectionIndex = null;

            //Pool はお外で管理されてるからここでは参照切るだけ。
            this._pool = null;
        }
    }
}
