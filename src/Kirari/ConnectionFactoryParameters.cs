namespace Kirari
{
    /// <summary>
    /// Parameters for <see cref="IConnectionFactory{TConnection}"/>.
    /// </summary>
    public struct ConnectionFactoryParameters
    {
        public string ConnectionString { get; }

        public ConnectionFactoryParameters(string connectionString)
        {
            this.ConnectionString = connectionString;
        }
    }
}
