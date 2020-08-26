using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Kirari.Diagnostics;
using MySql.Data.MySqlClient;

namespace Kirari.Samples.GlobalConnectionPoolStrategies
{
    public class GlobalConnectionPoolTransactionStrategy: IDefaultConnectionStrategy, ITransactionConnectionStrategy
    {
        /// <summary>
        /// 最大実行時間を取得します。
        /// この時間を越えて Pool から借り続けた場合、Pool によって強制的に回収されます。
        /// </summary>
        private static readonly TimeSpan _maxExecutionTime = TimeSpan.FromMinutes(30);

        private readonly object _commandQueueLock = new object();

        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);

        private readonly Queue<TaskCompletionSource<bool>> _commandPreparerQueue = new Queue<TaskCompletionSource<bool>>();

        private  GlobalConnectionPool _pool;

        private PooledConnection? _connection;

        private MySqlTransaction? _transaction;

        private string? _overriddenDatabaseName;

        private bool _isCommandExecuting;

        public DbConnection? TypicalConnection => this._connection?.ConnectionWithId.Connection;

        public GlobalConnectionPoolTransactionStrategy(GlobalConnectionPool pool)
        {
            this._pool = pool;
        }

        public async Task<DbCommandProxy> CreateCommandAsync(ConnectionFactoryParameters parameters,
            ICommandMetricsReportable? metricsReporter,
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
                this._connection.ConnectionWithId.Connection.ChangeDatabase(databaseName);
            }
            finally
            {
                this._connectionLock.Release();
            }
        }

        public DbConnection? GetConnectionOrNull(DbCommandProxy command)
            => this._connection?.ConnectionWithId.Connection;

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

        public DbTransaction? GetTransactionOrNull(DbCommandProxy command)
            => this._transaction;

        private async Task<IConnectionWithId<MySqlConnection>> GetConnectionAsync(ConnectionFactoryParameters parameters, CancellationToken cancellationToken)
        {
            //取得済ならそのまま使う
            if (this._connection != null) return this._connection.ConnectionWithId;

            await this._connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (this._connection != null) return this._connection.ConnectionWithId;

                //未取得の場合は Pool から払い出してもらう
                //Strategy が破棄されるまで返さないぞ！
                var connection = await this._pool.GetConnectionAsync(
                        parameters,
                        _maxExecutionTime,
                        nameof(GlobalConnectionPoolTransactionStrategy),
                        cancellationToken)
                    .ConfigureAwait(false);
                this._connection = connection;

                if (!string.IsNullOrWhiteSpace(this._overriddenDatabaseName) && connection.ConnectionWithId.Connection.Database != this._overriddenDatabaseName)
                {
                    await connection.ConnectionWithId.Connection.ChangeDatabaseAsync(this._overriddenDatabaseName, cancellationToken).ConfigureAwait(false);
                }

                return (this._connection = connection).ConnectionWithId;
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
            DisposeHelper.EnsureAllSteps(
                () => this.EndTransaction(), //Transaction が生き残っていたら終わらせる
                () =>
                {
                    //Connection を Pool に返す
                    if (this._connection != null)
                    {
                        this._pool.ReleaseConnection(this._connection.IndexInPool, this._connection.PayOutNumber);
                    }
                },
                () => this._connection = null, //借りてたものをもう使わないという意思表示
#pragma warning disable 8625
                () => this._pool = null); //Pool はお外で管理されてるからここでは参照切るだけ
#pragma warning restore 8625
        }
    }
}
