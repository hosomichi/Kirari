using System.Data.Common;
using System.Windows.Input;
using JetBrains.Annotations;

namespace Kirari
{
    /// <summary>
    /// This provides connection creation method.
    /// </summary>
    /// <typeparam name="TConnection">Type of created connection.</typeparam>
    public interface IConnectionFactory<out TConnection>
        where TConnection : DbConnection
    {
        /// <summary>
        /// Create new connection for parameter.
        /// </summary>
        [NotNull]
        IConnectionWithId<TConnection> CreateConnection(ConnectionFactoryParameters parameters);
    }
}
