using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Kirari.Diagnostics;

namespace Kirari.Samples.GlobalConnectionPoolStrategies
{
    public class GlobalConnectionPoolStrategy : IDefaultConnectionStrategy
    {
        private GlobalConnectionPool _pool;

        [CanBeNull]
        private string _overriddenDatabaseName;

        public GlobalConnectionPoolStrategy([NotNull] GlobalConnectionPool pool)
        {
            this._pool = pool;
        }

        public async Task<DbCommandProxy> CreateCommandAsync(ConnectionFactoryParameters parameters, ICommandMetricsReportable metricsReporter, CancellationToken cancellationToken)
        {
            var (connection, index) = await this._pool.GetConnectionAsync(parameters, cancellationToken).ConfigureAwait(false);

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
                _ => this._pool.ReleaseConnection(index));

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
            //Pool はお外で管理されてるからここでは参照切るだけ。
            this._pool = null;
        }
    }
}
