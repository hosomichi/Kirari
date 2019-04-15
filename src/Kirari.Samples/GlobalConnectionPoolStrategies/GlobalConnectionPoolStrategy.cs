using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Kirari.Diagnostics;

namespace Kirari.Samples.GlobalConnectionPoolStrategies
{
    public class GlobalConnectionPoolStrategy : IDefaultConnectionStrategy
    {
        /// <summary>
        /// 最大実行時間を取得します。
        /// この時間を越えて Pool から借り続けた場合、Pool によって強制的に回収されます。
        /// </summary>
        private static readonly TimeSpan _maxExecutionTime = TimeSpan.FromMinutes(5);

        private readonly ConcurrentDictionary<DbCommandProxy, bool> _workingCommands = new ConcurrentDictionary<DbCommandProxy, bool>();

        private GlobalConnectionPool _pool;

        [CanBeNull]
        private string _overriddenDatabaseName;

        public GlobalConnectionPoolStrategy([NotNull] GlobalConnectionPool pool)
        {
            this._pool = pool;
        }

        public async Task<DbCommandProxy> CreateCommandAsync(ConnectionFactoryParameters parameters, ICommandMetricsReportable metricsReporter, CancellationToken cancellationToken)
        {
            var (connection, index, payOutNumber) = await this._pool.GetConnectionAsync(
                    parameters,
                    _maxExecutionTime,
                    nameof(GlobalConnectionPoolStrategy),
                    cancellationToken)
                .ConfigureAwait(false);

            //ChangeDatabase はあまり対応したくない感じだけど、一応指定されてたらここで反映しよう。
            if (!string.IsNullOrWhiteSpace(this._overriddenDatabaseName) && connection.Connection.Database != this._overriddenDatabaseName)
            {
                await connection.Connection.ChangeDatabaseAsync(this._overriddenDatabaseName, cancellationToken).ConfigureAwait(false);
            }

            var sourceCommand = connection.Connection.CreateCommand();

            var command = new DbCommandProxy(
                connection.Id,
                sourceCommand,
                metricsReporter,
                x =>
                {
                    this._workingCommands.TryRemove(x, out _);
                    this._pool.ReleaseConnection(index, payOutNumber);
                });
            this._workingCommands.TryAdd(command, true);

            return command;
        }

        public Task ChangeDatabaseAsync(string databaseName, CancellationToken cancellationToken)
        {
            this._overriddenDatabaseName = databaseName;

            return Task.CompletedTask;
        }

        public DbConnection GetConnectionOrNull(DbCommandProxy command)
        {
            //Command.Connection の差し替えには対応しない。
            throw new NotSupportedException();
        }

        public void Dispose()
        {
            DisposeHelper.EnsureAllSteps(
                () =>
                {
                    //実行中のものがあれば全て解放する
                    var workingCommands = this._workingCommands.Keys.ToArray();
                    foreach (var workingCommand in workingCommands)
                    {
                        workingCommand.Dispose();
                    }
                },
                () => this._workingCommands.Clear(),
                () => this._pool = null); //Pool はお外で管理されてるからここでは参照切るだけ
        }
    }
}
