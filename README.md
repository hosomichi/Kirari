# Kirari
**K**irari **i**s **r**econfigurable **a**synchronous **R**DB **i**nterface.

[![NuGet version](https://badge.fury.io/nu/Kirari.svg)](https://www.nuget.org/packages/Kirari/)
[![Build Status](https://dev.azure.com/hosomichi/Kirari/_apis/build/status/hosomichi.Kirari?branchName=master)](https://dev.azure.com/hosomichi/Kirari/_build/latest?definitionId=1?branchName=master)

# Features
- `DbConnection` wrapper supports multi-thread query execution.
- Can reconfigure how to supports multi-thread query execution.
- Act like `DbConnection`, and supports other libraries for `DbConnection`.

# Usage
Sample code uses [Async MySQL Connector for .NET and .NET Core](https://github.com/mysql-net/MySqlConnector) and [Dapper](https://github.com/StackExchange/Dapper).

```csharp
using System.Threading.Tasks;
using Dapper;
using Kirari;
using Kirari.ConnectionStrategies;
using MySql.Data.MySqlClient;

public class Program
{
    public async void Main(string connectionString)
    {
        using (var connection = new MyConnection(connectionString))
        {
            //Can await WhenAll multiple query
            var (result1, result2) = await (
                    connection.QueryAsync<int>("SELECT DepartmentCode FROM Department"),
                    connection.QueryAsync<string>("SELECT FirstName From Employee"))
                .WhenAll();
        }
    }
}

//This library requires a little bit complex configuration.
//I Reccomend to create your own wrapper class include configuration.
public class MyConnection : DbConnectionProxy<MySqlConnection>
{
    private class ConnectionFactory : IConnectionFactory<MySqlConnection>
    {
        public static ConnectionFactory Instance { get; } = new ConnectionFactory();
        public MySqlConnection CreateConnection(ConnectionFactoryParameters parameters)
        {
            return new MySqlConnection(parameters.ConnectionString);
        }
    }
    public MyConnection(string connectionString)
        : base(connectionString,
            ConnectionFactory.Instance,
            StandardConnectionStrategyFactory.Default)
    {
    }
}

public static class Extensions
{
    public static async Task<(T1, T2)> WhenAll<T1, T2>(this (Task<T1>, Task<T2>) tasks)
    {
        await Task.WhenAll(tasks.Item1, tasks.Item2).ConfigureAwait(false);
        return (tasks.Item1.Result, tasks.Item2.Result);
    }
}
```

# License
[MIT License](LICENSE)