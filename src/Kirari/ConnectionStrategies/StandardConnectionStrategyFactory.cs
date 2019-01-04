using System;
using System.Data.Common;

namespace Kirari.ConnectionStrategies
{
    /// <summary>
    /// Provides <see cref="TermBasedReuseStrategy"/> and <see cref="QueuedSingleConnectionStrategy"/> pair.
    /// </summary>
    public class StandardConnectionStrategyFactory : IConnectionStrategyFactory<DbConnection>
    {
        public static StandardConnectionStrategyFactory Default { get; } = new StandardConnectionStrategyFactory(TimeSpan.FromMilliseconds(500));

        private readonly TimeSpan _reusableTime;

        public StandardConnectionStrategyFactory(TimeSpan reusableTime)
        {
            this._reusableTime = reusableTime;
        }

        public ConnectionStrategyPair CreateStrategyPair(IConnectionFactory<DbConnection> connectionFactory, ConnectionFactoryParameters parameters)
        {
            return new ConnectionStrategyPair(
                new TermBasedReuseStrategy(connectionFactory, this._reusableTime),
                new QueuedSingleConnectionStrategy(connectionFactory));
        }
    }
}
