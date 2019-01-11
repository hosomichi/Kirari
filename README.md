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
            return new ConnectionWithId<MySqlConnection>(new MySqlConnection(parameters.ConnectionString));
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

# Advanced
You can implement your own connection strategy.

Let's walk through with implementing simply create connection per command strategy using `MySqlConnection`.

## Create strategy class
First step, please create strategy class implements `IDefaultConnectionStrategy` interface.

`IDefaultConnectionStrategy` means the class can be used for non-transactional query execution.

```csharp
public class PerCommandConnectionStrategy : IDefaultConnectionStrategy
{
    public Task<DbCommandProxy> CreateCommandAsync(ConnectionFactoryParameters parameters, 
        ICommandMetricsReportable commandMetricsReporter,
        CancellationToken cancellationToken)
    {
    }

    public Task ChangeDatabaseAsync(string databaseName,
        CancellationToken cancellationToken)
    {
    }

    public DbConnection GetConnectionOrNull(DbCommandProxy command)
    {
    }

    public void Dispose()
    {
    }
}
```

## Determine how to recieve `IConnectionFactory`.
You need to use `IConnectionFactory`, because how to create *raw connection* is wrapped in `IConnectionFactory`.

I recommend simply receive in constuctor.

```csharp
    private readonly IConnectionFactory<MySqlConnection> _connectionFactory;

    public PerCommandConnectionStrategy(IConnectionFactory<MySqlConnection> connectionFactory)
    {
        this._connectionFactory = connectionFactory;
    }
```

## Implements `CreateCommandAsync`
This is the most important method in `IDefaultConnectionStrategy`.


You must implement witch connection to use, and what to do when command ends.
And also, this method must be thread-safe.

In this case, simply create connection per command.

```csharp
    public async Task<DbCommandProxy> CreateCommandAsync(ConnectionFactoryParameters parameters,
        ICommandMetricsReportable commandMetricsReporter,
        CancellationToken cancellationToken)
    {
        var connection = this._connectionFactory.CreateConnection(parameters);
        //Library code is recommended to call ConfigureAwait(false)
        await connection.Connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return new DbCommandProxy(
            connection.Id,
            connection.CreateCommand(), //Create actual command linked with connection.
            commandMetricsReporter,
            command => connection.Dispose()); //Dispose connection when command ends.
    }
```

## Implements `ChangeDatabaseAsync` if needed.
If you have the potential to call `DbConnection.ChangeDatabase`, you must implement this, or not, you can throw `NotImplementedException`.

```csharp
    private string _changedDatabase;

    public async Task<DbCommandProxy> CreateCommandAsync(ConnectionFactoryParameters parameters,
        ICommandMetricsReportable commandMetricsReporter,
        CancellationToken cancellationToken)
    {
        var connection = this._connectionFactory.CreateConnection(parameters);
        //Library code is recommended to ConfigureAwait(false)
        await connection.Connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        //Change database if needed.
        if (!string.IsNullOrEmpty(this._changedDatabase))
        {
            connection.Connection.ChangeDatabaseAsync(this._changedDatabase, cancellationToken).ConfigureAwait(false)
        }
        return new DbCommandProxy(
            connection.Id,
            connection.CreateCommand(), //Create actual command linked with connection.
            commandMetricsReporter,
            command => connection.Dispose()); //Dispose connection when command ends.
    }

    public Task ChangeDatabaseAsync(string databaseName,
        CancellationToken cancellationToken)
    {
        //Keep changed database name to apply created connection.
        this._changedDatabase = databaseName;
        return Task.CompletedTask;
    }
```

## Implements `GetConnectionOrNull` if needed.
If you have the potential to set `DbConnectionProxy` to `DbCommandProxy.Connection`, you must implement this, or not, you can throw `NotImplementedException`.

In most case, this is not required.

If you want to implement this method, you must track all connection for command.

```csharp
    private readonly ConcurrentDictionary<DbCommandProxy, IConnectionWithId<MySqlConnection>> _connectionCache 
        = new ConcurrentDictionary<DbCommandProxy, IConnectionWithId<MySqlConnection>>();

    public async Task<DbCommandProxy> CreateCommandAsync(ConnectionFactoryParameters parameters,
        CancellationToken cancellationToken)
    {
        var connection = this._connectionFactory.CreateConnection(parameters);
        //Library code is recommended to ConfigureAwait(false)
        await connection.Connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        //Change database if needed.
        if (!string.IsNullOrEmpty(this._changedDatabase))
        {
            connection.Connection.ChangeDatabaseAsync(this._changedDatabase, cancellationToken).ConfigureAwait(false);
        }
        var commandProxy = new DbCommandProxy(
            connection.Id,
            connection.Connection.CreateCommand(), //Create actual command linked with connection.
            commandMetricsReporter,
            command =>
            {
                this._connectionCache.TryRemove(command, out _); //Remove from managed connections.
                connection.Dispose(); //Dispose connection when command ends.
            });
        this._connectionCache.TryAdd(commandProxy, connection); //Track connection for GetConnectionOrNull method.
        return commandProxy;
    }

    public DbConnection GetConnectionOrNull(DbCommandProxy command)
        => this._connectionCache.TryGetValue(command, out var connection) ? connection : null;
```

## Implements `Disposed`
Release unmanaged resources.

In this case, if all commands is ensured to disposed, you don't need additional operation.

Or if you ensure to dispose all connections you created, you must track all connection to dispose.

```csharp
    public void Dispose()
    {
        foreach (var connection in this._connectionCache.Values)
        {
            connection.Dispose();
        }
        this._connectionCache.Clear();
    }
```

Now, whole strategy class is implemented.

Class definition is here.

```csharp
using System.Collections.Concurrent;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Kirari;
using Kirari.ConnectionStrategies;
using Kirari.Diagnostics;
using MySql.Data.MySqlClient;

public class PerCommandConnectionStrategy : IDefaultConnectionStrategy
{
    private readonly IConnectionFactory<MySqlConnection> _connectionFactory;

    private readonly ConcurrentDictionary<DbCommandProxy, IConnectionWithId<MySqlConnection>> _connectionCache
        = new ConcurrentDictionary<DbCommandProxy, IConnectionWithId<MySqlConnection>>();

    private string _changedDatabase;

    public PerCommandConnectionStrategy(IConnectionFactory<MySqlConnection> connectionFactory)
    {
        this._connectionFactory = connectionFactory;
    }

    public async Task<DbCommandProxy> CreateCommandAsync(ConnectionFactoryParameters parameters,
        ICommandMetricsReportable commandMetricsReporter,
        CancellationToken cancellationToken)
    {
        var connection = this._connectionFactory.CreateConnection(parameters);
        //Library code is recommended to ConfigureAwait(false)
        await connection.Connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        //Change database if needed.
        if (!string.IsNullOrEmpty(this._changedDatabase))
        {
            connection.Connection.ChangeDatabaseAsync(this._changedDatabase, cancellationToken).ConfigureAwait(false);
        }
        var commandProxy = new DbCommandProxy(
            connection.Id,
            connection.Connection.CreateCommand(), //Create actual command linked with connection.
            commandMetricsReporter,
            command =>
            {
                this._connectionCache.TryRemove(command, out _); //Remove from managed connections.
                connection.Dispose(); //Dispose connection when command ends.
            });
        this._connectionCache.TryAdd(commandProxy, connection); //Track connection for GetConnectionOrNull method.
        return commandProxy;
    }

    public Task ChangeDatabaseAsync(string databaseName,
        CancellationToken cancellationToken)
    {
        //Keep changed database name to apply created connection.
        this._changedDatabase = databaseName;
        return Task.CompletedTask;
    }

    public DbConnection GetConnectionOrNull(DbCommandProxy command)
        => this._connectionCache.TryGetValue(command, out var connection) ? connection.Connection : null;

    public void Dispose()
    {
        foreach (var connection in this._connectionCache.Values)
        {
            connection.Dispose();
        }
        this._connectionCache.Clear();
    }
}
```

## Create strategy factory to use
`DbConnectionProxy` class creates strategy by using 'IConnectionStrategyFactory'.

So, implement this interface to use your own strategy.

```csharp
public class StrategyFactory : IConnectionStrategyFactory<MySqlConnection>
{
     //Singleton pattern.
     //Factory class is not required to create every time.
     public static StrategyFactory Instance { get; } = new StrategyFactory();

     public ConnectionStrategyPair CreateStrategyPair(IConnectionFactory<MySqlConnection> connectionFactory, ConnectionFactoryParameters parameters)
     {
         return new ConnectionStrategyPair(
             new PerCommandConnectionStrategy(connectionFactory), //Use implemented strategy.
             new QueuedSingleConnectionStrategy(connectionFactory)); //Use built-in strategy for transaction.
     }
}
```
## Create your connection class to use
I Reccomend to create your own wrapper class include configuration.

```csharp
public class MyConnection : DbConnectionProxy<MySqlConnection>
{
    private class ConnectionFactory : IConnectionFactory<MySqlConnection>
    {
        public static IConnectionFactory<MySqlConnection> Instance { get; } = new ConnectionFactory();

        public IConnectionWithId<MySqlConnection> CreateConnection(ConnectionFactoryParameters parameters)
        {
            return new ConnectionWithId<MySqlConnection>(new MySqlConnection(parameters.ConnectionString));
        }
    }

    public MyConnection(string connectionString)
        : base(
            connectionString,
            ConnectionFactory.Instance,
            StrategyFactory.Instance)
    {
    }
}
```

# License
[MIT License](LICENSE)