using System.Data.Common;
using JetBrains.Annotations;

namespace Kirari
{
    /// <summary>
    /// This provides connection strategy creation method.
    /// </summary>
    /// <typeparam name="TConnection">Type of connection for strategy.</typeparam>
    public interface IConnectionStrategyFactory<in TConnection>
        where TConnection : DbConnection
    {
        /// <summary>
        /// Create <see cref="IDefaultConnectionStrategy"/> and <see cref="ITransactionConnectionStrategy"/>.
        /// </summary>
        ConnectionStrategyPair CreateStrategyPair([NotNull] IConnectionFactory<TConnection> connectionFactory, ConnectionFactoryParameters parameters);
    }
}
