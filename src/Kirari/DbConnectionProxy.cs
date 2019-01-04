using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using JetBrains.Annotations;

namespace Kirari
{
    /// <summary>
    /// Connection virtualizer for source connection type '<typeparamref name="TConnection"/>'.
    /// Main component for this library.
    /// </summary>
    /// <typeparam name="TConnection">Connection type to virtualize.</typeparam>
    public class DbConnectionProxy<TConnection> : DbConnection
      where TConnection : DbConnection
    {
        [NotNull]
        private readonly IConnectionFactory<TConnection> _factory;

        [NotNull]
        private readonly IDefaultConnectionStrategy _defaultStrategy;

        [NotNull]
        private readonly ITransactionConnectionStrategy _transactionStrategy;

        [NotNull]
        private IConnectionStrategy _currentStrategy;

        [CanBeNull]
        private TConnection _adminConnection;

        [NotNull]
        private TConnection AdminConnection
            => this._adminConnection ?? (this._adminConnection = this._factory.CreateConnection(this.CreateFactoryParameters()));

        [NotNull]
        private readonly string _connectionString;

        /// <summary>
        /// Gets the string used to open connection.
        /// Setter is not supported.
        /// Must sets in constructor.
        /// </summary>
        public override string ConnectionString
        {
            get => this._connectionString;
            set => throw new NotSupportedException();
        }

        public override string Database
            => this.AdminConnection.Database;

        public override ConnectionState State
            => ConnectionState.Open;

        public override string DataSource
            => this.AdminConnection.DataSource;

        public override string ServerVersion
            => this.AdminConnection.ServerVersion;

        public override int ConnectionTimeout
            => this.AdminConnection.ConnectionTimeout;

        public DbConnectionProxy([NotNull] string connectionString,
            [NotNull] IConnectionFactory<TConnection> connectionFactory,
            [NotNull] IConnectionStrategyFactory<TConnection> strategyFactory)
        {
            this._connectionString = connectionString;
            this._factory = connectionFactory;
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
            await this.AdminConnection.OpenAsync(cancellationToken);
            this.AdminConnection.ChangeDatabase(databaseName);
            this.AdminConnection.Close();

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
            => this._currentStrategy.CreateCommandAsync(this.CreateFactoryParameters(), cancellationToken);

        /// <summary>
        /// Not supported.
        /// </summary>
        public override void EnlistTransaction(Transaction transaction)
            => throw new NotSupportedException();

        public override void Open()
            => this.OpenAsync(CancellationToken.None).GetAwaiter().GetResult();

#pragma warning disable 1998
        public override async Task OpenAsync(CancellationToken cancellationToken)
#pragma warning restore 1998
        {
            //do nothing.
        }

        public override DataTable GetSchema()
            => this.AdminConnection.GetSchema();

        public override DataTable GetSchema(string collectionName)
            => this.AdminConnection.GetSchema(collectionName);

        public override DataTable GetSchema(string collectionName, string[] restrictionValues)
            => this.AdminConnection.GetSchema(collectionName, restrictionValues);

        private ConnectionFactoryParameters CreateFactoryParameters()
            => new ConnectionFactoryParameters(this.ConnectionString);

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            this._defaultStrategy.Dispose();
            this._transactionStrategy.Dispose();

            this._adminConnection?.Dispose();
            this._adminConnection = null;
        }
    }
}
