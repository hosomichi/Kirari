using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Kirari.ConnectionStrategies
{
    /// <summary>
    /// This strategy create connection per command, but can reuse connection after command disposed until intended reusable time elapsed.
    /// Expired reusable connection automatically disposed.
    /// </summary>
    public sealed class TermBasedReuseStrategy : IDefaultConnectionStrategy
    {
        [NotNull]
        private readonly IConnectionFactory<DbConnection> _factory;

        private readonly TimeSpan _reusableTime;

        [NotNull]
        private readonly object _reusableConnectionsLock = new object();

        [NotNull]
        private readonly HashSet<DbConnection> _reusableConnections = new HashSet<DbConnection>();

        [NotNull]
        private readonly ConcurrentDictionary<DbCommandProxy, DbConnection> _workingCommands = new ConcurrentDictionary<DbCommandProxy, DbConnection>();

        [CanBeNull]
        private string _overriddenDatabaseName;

        public TermBasedReuseStrategy([NotNull] IConnectionFactory<DbConnection> factory,
            TimeSpan reusableTime)
        {
            this._factory = factory;
            this._reusableTime = reusableTime;
        }

        public async Task<DbCommandProxy> CreateCommandAsync(ConnectionFactoryParameters parameters,
            CancellationToken cancellationToken)
        {
            var connection = this.TryReuse();
            if (connection == null)
            {
                connection = this._factory.CreateConnection(parameters);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(this._overriddenDatabaseName))
                {
                    connection.ChangeDatabase(this._overriddenDatabaseName);
                }
            }

            var command = new DbCommandProxy(
                connection.CreateCommand(),
                this.OnCommandCompleted);

            this._workingCommands.TryAdd(command, connection);

            return command;
        }

#pragma warning disable 1998
        public async Task ChangeDatabaseAsync(string databaseName, CancellationToken cancellationToken)
#pragma warning restore 1998
        {
            this._overriddenDatabaseName = databaseName;
        }

        private void OnCommandCompleted([NotNull]DbCommandProxy command)
        {
            //Get initial connection because DbCommandProxy.Connection is mutable.
            if (!this._workingCommands.TryRemove(command, out var connection)) return;

            lock (this._reusableConnectionsLock)
            {
                this._reusableConnections.Add(connection);
            }

            //fire and forget.
            Task.Run(async () =>
            {
                await Task.Delay(this._reusableTime).ConfigureAwait(false);
                lock (this._reusableConnectionsLock)
                {
                    if (this._reusableConnections.Remove(connection))
                    {
                        connection.Dispose();
                    }
                }
            });
        }

        [CanBeNull]
        private DbConnection TryReuse()
        {
            lock (this._reusableConnectionsLock)
            {
                if (this._reusableConnections.Count <= 0) return null;

                var connection = this._reusableConnections.First();
                this._reusableConnections.Remove(connection);
                if (!string.IsNullOrWhiteSpace(this._overriddenDatabaseName) && connection.Database != this._overriddenDatabaseName)
                {
                    connection.ChangeDatabase(this._overriddenDatabaseName);
                }
                return connection;
            }
        }

        public void Dispose()
        {
            this.ReleaseUnmanagedResources();
            GC.SuppressFinalize(this);
        }

        ~TermBasedReuseStrategy()
        {
            this.ReleaseUnmanagedResources();
        }

        private void ReleaseUnmanagedResources()
        {
            lock (this._reusableConnectionsLock)
            {
                foreach (var connection in this._reusableConnections)
                {
                    connection.Dispose();
                }

                this._reusableConnections.Clear();
            }

            foreach (var kvp in this._workingCommands)
            {
                kvp.Key.Dispose();
                kvp.Value.Dispose();
            }

            this._workingCommands.Clear();
        }
    }
}
