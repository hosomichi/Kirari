﻿using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Kirari.Diagnostics;

namespace Kirari
{
    /// <summary>
    /// Connection virtualizer for source connection type '<typeparamref name="TConnection"/>'.
    /// Main component for this library.
    /// </summary>
    /// <typeparam name="TConnection">Connection type to virtualize.</typeparam>
    public class DbConnectionProxy<TConnection> : DbConnection, IDbConnectionProxy
        where TConnection : DbConnection
    {
        private readonly IConnectionFactory<TConnection> _factory;

        private readonly ICommandMetricsReportable? _commandMetricsReporter;

        private readonly IDefaultConnectionStrategy _defaultStrategy;

        private readonly ITransactionConnectionStrategy _transactionStrategy;

        private bool _disposed;

        private IConnectionStrategy _currentStrategy;

        private IConnectionWithId<TConnection>? _adminConnection;

        private IConnectionWithId<TConnection> AdminConnection
            => this._adminConnection ??= this._factory.CreateConnection(this.CreateFactoryParameters());

        private readonly string _connectionString;

        /// <summary>
        /// Gets the string used to open connection.
        /// Setter is not supported.
        /// Must be set in constructor.
        /// </summary>
        public override string ConnectionString
        {
            get => this._connectionString;
            set => throw new NotSupportedException();
        }

        public override string Database
            => this.AdminConnection.Connection.Database;

        public override ConnectionState State
            => ConnectionState.Open;

        public override string DataSource
            => this.AdminConnection.Connection.DataSource;

        public override string ServerVersion
            => this.AdminConnection.Connection.ServerVersion;

        public override int ConnectionTimeout
            => this.AdminConnection.Connection.ConnectionTimeout;

        public DbConnectionProxy(string connectionString,
            IConnectionFactory<TConnection> connectionFactory,
            IConnectionStrategyFactory<TConnection> strategyFactory)
            : this(connectionString, connectionFactory, strategyFactory, null)
        {
        }

        public DbConnectionProxy(string connectionString,
            IConnectionFactory<TConnection> connectionFactory,
            IConnectionStrategyFactory<TConnection> strategyFactory,
            ICommandMetricsReportable? commandMetricsReporter)
        {
            this._connectionString = connectionString;
            this._factory = connectionFactory;
            this._commandMetricsReporter = commandMetricsReporter;
            var (defaultStrategy, transactionStrategy) = strategyFactory.CreateStrategyPair(connectionFactory, this.CreateFactoryParameters());
            this._defaultStrategy = defaultStrategy;
            this._transactionStrategy = transactionStrategy;
            this._currentStrategy = defaultStrategy;
        }

        // ReSharper disable once RedundantNameQualifier
        protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel)
            => this.BeginTransactionAsync(isolationLevel).GetAwaiter().GetResult();

        /// <inheritdoc cref="BeginDbTransaction"/>
        // ReSharper disable once RedundantNameQualifier
        public Task<DbTransactionProxy> BeginTransactionAsync(System.Data.IsolationLevel isolationLevel)
            => this.BeginTransactionAsync(isolationLevel, CancellationToken.None);

        /// <inheritdoc cref="BeginDbTransaction"/>
        // ReSharper disable once RedundantNameQualifier
        public async Task<DbTransactionProxy> BeginTransactionAsync(System.Data.IsolationLevel isolationLevel, CancellationToken cancellationToken)
        {
            this._currentStrategy = this._transactionStrategy;

            await this._transactionStrategy.BeginTransactionAsync(
                    isolationLevel,
                    this.CreateFactoryParameters(),
                    cancellationToken)
                .ConfigureAwait(false);
            return new DbTransactionProxy(this._transactionStrategy, isolationLevel);
        }

        public override void ChangeDatabase(string databaseName)
            => this.ChangeDatabaseAsync(databaseName).GetAwaiter().GetResult();

        /// <inheritdoc cref="ChangeDatabase"/>
        public Task ChangeDatabaseAsync(string databaseName)
            => this.ChangeDatabaseAsync(databaseName, CancellationToken.None);

        /// <inheritdoc cref="ChangeDatabase"/>
        public async Task ChangeDatabaseAsync(string databaseName, CancellationToken cancellationToken)
        {
            await this.AdminConnection.Connection.OpenAsync(cancellationToken);
            this.AdminConnection.Connection.ChangeDatabase(databaseName);
            this.AdminConnection.Connection.Close();

            await this._defaultStrategy.ChangeDatabaseAsync(databaseName, cancellationToken).ConfigureAwait(false);
            await this._transactionStrategy.ChangeDatabaseAsync(databaseName, cancellationToken).ConfigureAwait(false);
        }

        public override void Close()
        {
            //do nothing.
        }

        /// <summary>
        /// Create and returns a DbCommand object associated with current the connection.
        /// This method is thread-safe.
        /// </summary>
        protected override DbCommand CreateDbCommand()
            => this.CreateDbCommandAsync().GetAwaiter().GetResult();

        /// <inheritdoc cref="CreateDbCommand"/>
        public Task<DbCommandProxy> CreateDbCommandAsync()
            => this.CreateDbCommandAsync(CancellationToken.None);

        /// <inheritdoc cref="CreateDbCommand"/>
        public Task<DbCommandProxy> CreateDbCommandAsync(CancellationToken cancellationToken)
            => this._currentStrategy.CreateCommandAsync(this.CreateFactoryParameters(), this._commandMetricsReporter, cancellationToken);

        /// <summary>
        /// Not supported.
        /// </summary>
        public override void EnlistTransaction(Transaction transaction)
            => throw new NotSupportedException();

        public override void Open()
            => this.OpenAsync(CancellationToken.None).GetAwaiter().GetResult();

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            //do nothing.
            return Task.CompletedTask;
        }

        public override DataTable GetSchema()
            => this.AdminConnection.Connection.GetSchema();

        public override DataTable GetSchema(string collectionName)
            => this.AdminConnection.Connection.GetSchema(collectionName);

        public override DataTable GetSchema(string collectionName, string[] restrictionValues)
            => this.AdminConnection.Connection.GetSchema(collectionName, restrictionValues);

        private ConnectionFactoryParameters CreateFactoryParameters()
            => new ConnectionFactoryParameters(this.ConnectionString);

        protected override void Dispose(bool disposing)
        {
            if(this._disposed) return;
            this._disposed = true;

            DisposeHelper.EnsureAllSteps(
                () => base.Dispose(disposing),
                () => this._defaultStrategy.Dispose(),
                () => this._transactionStrategy.Dispose(),
                () => this._adminConnection?.Dispose(),
                () => this._adminConnection = null);
        }

        DbConnection? IDbConnectionProxy.GetConnectionOrNull(DbCommandProxy command)
            => this._currentStrategy.GetConnectionOrNull(command);
    }
}
