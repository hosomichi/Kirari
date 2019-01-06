using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Kirari
{
    /// <summary>
    /// Provides non generic access for <see cref="DbConnectionProxy{TConnection}"/>.
    /// </summary>
    public interface IDbConnectionProxy
    {
        /// <summary>
        /// Get the <see cref="DbConnection"/> for <see cref="DbCommandProxy"/>.
        /// Retrun null if <see cref="DbCommandProxy"/> is not managed by current <see cref="IConnectionStrategy"/>.
        /// </summary>
        [CanBeNull]
        DbConnection GetConnectionOrNull(DbCommandProxy command);
    }
}
