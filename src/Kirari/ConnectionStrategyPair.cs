using JetBrains.Annotations;

namespace Kirari
{
    /// <summary>
    /// A pair of <see cref="IDefaultConnectionStrategy"/> and <see cref="ITransactionConnectionStrategy"/>.
    /// Can extract strategies by <see cref="Deconstruct"/>.
    /// </summary>
    public struct ConnectionStrategyPair
    {
        [NotNull]
        public IDefaultConnectionStrategy DefaultConnectionStrategy { get; }

        [NotNull]
        public ITransactionConnectionStrategy TransactionConnectionStrategy { get; }

        public ConnectionStrategyPair([NotNull] IDefaultConnectionStrategy defaultConnectionStrategy,
            [NotNull] ITransactionConnectionStrategy transactionConnectionStrategy)
        {
            this.DefaultConnectionStrategy = defaultConnectionStrategy;
            this.TransactionConnectionStrategy = transactionConnectionStrategy;
        }

        public void Deconstruct(out IDefaultConnectionStrategy defaultConnectionStrategy, out ITransactionConnectionStrategy transactionConnectionStrategy)
        {
            defaultConnectionStrategy = this.DefaultConnectionStrategy;
            transactionConnectionStrategy = this.TransactionConnectionStrategy;
        }
    }
}
