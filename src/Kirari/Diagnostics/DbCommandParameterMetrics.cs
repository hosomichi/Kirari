using System.Data;

namespace Kirari.Diagnostics
{
    /// <summary>
    /// Parameter information for command.
    /// </summary>
    public class DbCommandParameterMetrics
    {
        /// <summary>
        /// Get name of a parametr.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Get <see cref="DbType"/> of a parameter.
        /// </summary>
        public DbType Type { get; }

        /// <summary>
        /// Get value of a parameter
        /// </summary>
        public object Value { get; }

        public DbCommandParameterMetrics(string name, DbType type, object value)
        {
            this.Name = name;
            this.Type = type;
            this.Value = value;
        }
    }
}
