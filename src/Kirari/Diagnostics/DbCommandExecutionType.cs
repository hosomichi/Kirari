using System.Data.Common;

namespace Kirari.Diagnostics
{
    /// <summary>
    /// What kind of <see cref="DbCommand"/> method is called.
    /// </summary>
    public enum DbCommandExecutionType
    {
        /// <summary>
        /// <see cref="DbCommand.ExecuteNonQuery"/> or variant is called.
        /// </summary>
        ExecuteNonQuery,

        /// <summary>
        /// <see cref="DbCommand.ExecuteReader"/> or variant is called.
        /// </summary>
        ExecuteReader,

        /// <summary>
        /// <see cref="DbCommand.ExecuteScalar"/> or variant is called.
        /// </summary>
        ExecuteScalar
    }
}
