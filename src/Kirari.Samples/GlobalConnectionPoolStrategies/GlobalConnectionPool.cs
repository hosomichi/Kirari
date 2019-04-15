using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using MySql.Data.MySqlClient;

namespace Kirari.Samples.GlobalConnectionPoolStrategies
{
    public class GlobalConnectionPool
    {
        [NotNull]
        public string PoolName { get; }

        /// <summary>
        /// 準備中になってから実際に使われるまでの時間の上限。
        /// この時間をこえて使用中にならなかったら回収する。
        /// </summary>
        private static readonly TimeSpan _preparingExpiry = TimeSpan.FromSeconds(1);

        /// <summary>
        /// 最後に解放してから一定時間以上経過していたら、次回使う時には <see cref="MySqlConnection.Dispose"/> して <see cref="MySqlConnection"/> 作り直す。
        /// </summary>
        private static readonly TimeSpan _forceDisposeTimeFromLastUsed = TimeSpan.FromHours(1);

        [NotNull]
        private readonly IConnectionFactory<MySqlConnection> _factory;

        private long _payOutNumber;

        #region ConnectionPool

        /// <summary>
        /// プールするコネクションの数
        /// </summary>
        private readonly int _connectionPoolSize;

        /// <summary>
        /// 使用可能なコネクションのスロット。
        /// </summary>
        [NotNull]
        private readonly InternalPooledConnection[] _connectionPool;

        /// <summary>
        /// <see cref="_connectionPool"/> 内の貸し出し状況等を弄るためのロックオブジェクト。
        /// </summary>
        private readonly object _connectionPoolLock = new object();

        #endregion

        #region WaitQueue

        /// <summary>
        /// 定期的に <see cref="_waitQueue"/> で待っているやつを動かす処理を呼び出す間隔。
        /// </summary>
        private static readonly TimeSpan _waitQueueCheckInterval = TimeSpan.FromSeconds(1);

        /// <summary>
        /// <see cref="_waitQueue"/> を操作するためのロックオブジェクト。
        /// </summary>
        [NotNull]
        private readonly object _waitQueueLock = new object();

        /// <summary>
        /// プールの空きを待っている処理の一覧。
        /// </summary>
        [NotNull]
        private readonly Queue<TaskCompletionSource<PayOut>> _waitQueue = new Queue<TaskCompletionSource<PayOut>>();

        #endregion

        public GlobalConnectionPool([NotNull] string poolName, [NotNull] IConnectionFactory<MySqlConnection> factory, int connectionPoolSize)
        {
            this.PoolName = poolName;
            this._factory = factory;
            this._connectionPoolSize = connectionPoolSize;

            //固定長配列いず大正義
            this._connectionPool = new InternalPooledConnection[this._connectionPoolSize];

            //全部利用可能状態で初期化
            var now = DateTime.Now;
            for (var i = 0; i < connectionPoolSize; i++)
            {
                this._connectionPool[i] = new InternalPooledConnection()
                {
                    ConnectionWithId = null,
                    Status = PoolStatus.Assignable,
                    StatusChangedAt = now,
                    ExpiredAt = now,
                    PayOutNumber = null,
                    CallerName = null,
                };
            }

            //定期的にキューをチェックするやつを回す
            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        await Task.Delay(_waitQueueCheckInterval);

                        this.TryDequeueWaitQueue();
                    }
                    catch (Exception)
                    {
                        //無限ループ奴なので握り潰し
                    }
                }
            });
        }

        /// <summary>
        /// 利用可能な Connection と、その Connection の <see cref="_connectionPool"/> 内での Index を取得する。
        /// 利用可能な Connection が無い場合は、利用可能になるまで待機する。
        /// </summary>
        public async ValueTask<PooledConnection> GetConnectionAsync(ConnectionFactoryParameters parameters,
            TimeSpan expiry,
            string callerName,
            CancellationToken cancellationToken)
        {
            var usablePayOut = this.GetUsableConnection();
            PayOut payOut;
            if (usablePayOut.HasValue)
            {
                payOut = usablePayOut.Value;
            }
            else
            {
                //利用可能なコネクションが無かったら待機列へどうぞ。

                var preparer = new TaskCompletionSource<PayOut>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (this._waitQueueLock)
                {
                    this._waitQueue.Enqueue(preparer);
                }

                //利用可能なコネクションの Index を受け取って待機列を抜けるのだ。
                payOut = await preparer.Task.ConfigureAwait(false);
            }

            var pooledConnection = this._connectionPool[payOut.Index];

            lock (this._connectionPoolLock)
            {
                //準備が間に合わず再利用されてしまった場合はしょうがないので例外
                if (pooledConnection.Status != PoolStatus.Preparing
                    || pooledConnection.PayOutNumber.HasValue == false
                    || pooledConnection.PayOutNumber.Value != payOut.PayOutNumber)
                {
                    throw new InvalidOperationException("時間切れのため他の処理にコネクションが再利用されました。");
                }

                pooledConnection.Status = PoolStatus.Using;
                pooledConnection.StatusChangedAt = DateTime.Now;
                pooledConnection.CallerName = callerName;
                pooledConnection.ExpiredAt = pooledConnection.StatusChangedAt.Add(expiry);
            }

            try
            {
                //実際のコネクションが無かったら作る。
                if (pooledConnection.ConnectionWithId == null)
                {
                    pooledConnection.ConnectionWithId = this._factory.CreateConnection(parameters);
                }

                //作ったやつは当然開いてないから開く必要がある。
                //あるいは時間経過で勝手に閉じられることもあるかもね。
                if (pooledConnection.ConnectionWithId.Connection.State != ConnectionState.Open)
                {
                    await pooledConnection.ConnectionWithId.Connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                lock (this._connectionPoolLock)
                {
                    pooledConnection.Status = PoolStatus.Assignable;
                    pooledConnection.StatusChangedAt = DateTime.Now;
                    pooledConnection.PayOutNumber = null;
                    pooledConnection.CallerName = null;
                    pooledConnection.ExpiredAt = pooledConnection.StatusChangedAt;
                    pooledConnection.ConnectionWithId?.Dispose();
                    pooledConnection.ConnectionWithId = null;
                }
                throw;
            }

            return new PooledConnection(pooledConnection.ConnectionWithId, payOut.Index, payOut.PayOutNumber);
        }


        public void ReleaseConnection(int index, long payOutNumber)
        {
            var pooledConnection = this._connectionPool[index];

            lock (this._connectionPoolLock)
            {
                //別の誰かが既に回収済みなら何もしない
                if (pooledConnection.Status != PoolStatus.Using) return;
                if (pooledConnection.PayOutNumber.HasValue == false) return;

                //既に違う相手に貸し出し済なら何もしない
                if (pooledConnection.PayOutNumber.Value != payOutNumber) return;

                //回収して使用可能になったことをマーク
                pooledConnection.Status = PoolStatus.Assignable;
                pooledConnection.StatusChangedAt = DateTime.Now;
                pooledConnection.PayOutNumber = null;
                pooledConnection.CallerName = null;
                pooledConnection.ExpiredAt = pooledConnection.StatusChangedAt;
            }

            //空きができたら待機列からお呼び出し
            this.TryDequeueWaitQueue();
        }

        private void TryDequeueWaitQueue()
        {
            lock (this._waitQueueLock)
            {
                //キューが空なら何もしない
                var queueCount = this._waitQueue.Count;
                if(queueCount <= 0) return;

                //およそ使用可能な Connection 数を取得
                var estimatedUsableCount = this.GetUsableConnectionCount();

                //キュー内の数と使用可能な数のうち小さい方を試行回数とする
                var tryCount = Math.Min(queueCount, estimatedUsableCount);

                for (var i = 0; i < tryCount; i++)
                {
                    var payOut = this.GetUsableConnection();

                    //もう使える Connection が無くなっていたら終了
                    //数をチェックしてからここまでの間にロックかけてないからそういうこともある
                    if(payOut.HasValue == false) break;

                    //この処理内でキューの数は変わらないから Dequeue は成功する
                    var waitSource = this._waitQueue.Dequeue();
                    waitSource.SetResult(payOut.Value);
                }
            }
        }

        private int GetUsableConnectionCount()
        {
            var count = 0;
            lock (this._connectionPoolLock)
            {
                for (var index = 0; index < this._connectionPoolSize; index++)
                {
                    var pooledConnection = this._connectionPool[index];
                    switch (pooledConnection.Status)
                    {
                        case PoolStatus.Assignable:
                            count += 1;
                            break;
                        case PoolStatus.Preparing:
                        case PoolStatus.Using:
                            //期限の切れてないやつは使えない
                            if(pooledConnection.ExpiredAt > DateTime.Now) continue;

                            count += 1;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                return count;
            }
        }

        private PayOut? GetUsableConnection()
        {
            lock (this._connectionPoolLock)
            {
                //回収しやすいやつから探索するのだ

                //まずは使用可能なやつを探索
                var assignable = Enumerable.Range(0, this._connectionPoolSize)
                    .Select(index => (index, connection : this._connectionPool[index]))
                    .Where(x => x.connection.Status == PoolStatus.Assignable)
                    .OrderBy(x => x.connection.StatusChangedAt) //StatusChangedAt の昇順で最も長く Assignable を維持しているやつを選ぶ。つまり使われていない期間の長いやつから順に使う。
                    .ToArray();
                if (assignable.Length > 0)
                {
                    var (index, pooledConnection) = assignable[0];
                    var payOutNumber = ChangeToPreparing(pooledConnection);
                    return new PayOut(index, payOutNumber);
                }

                //準備中で期限切れたやつを探索
                for (var index = 0; index < this._connectionPoolSize; index++)
                {
                    var pooledConnection = this._connectionPool[index];
                    if (pooledConnection.Status != PoolStatus.Preparing) continue;

                    //期限の切れてないやつは使えない
                    if(pooledConnection.ExpiredAt > DateTime.Now) continue;

                    var payOutNumber = ChangeToPreparing(pooledConnection);
                    return new PayOut(index, payOutNumber);
                }

                //使用中で期限切れたやつを探索
                for (var index = 0; index < this._connectionPoolSize; index++)
                {
                    var pooledConnection = this._connectionPool[index];
                    if (pooledConnection.Status != PoolStatus.Using) continue;

                    //期限の切れてないやつは使えない
                    if(pooledConnection.ExpiredAt > DateTime.Now) continue;

                    //一定時間返ってきていないやつは何かによって回収漏れな可能性が高いので回収しちゃう
                    pooledConnection.ConnectionWithId?.Dispose();
                    pooledConnection.ConnectionWithId = null;

                    var payOutNumber = ChangeToPreparing(pooledConnection);
                    return new PayOut(index, payOutNumber);
                }

                //見つからなかった
                return null;

                long ChangeToPreparing(InternalPooledConnection connection)
                {
                    if (connection.ConnectionWithId != null)
                    {
                        if (connection.ConnectionWithId.Connection.State != ConnectionState.Open)
                        {
                            //切断済コネクションは捨てて作り直すぞ！
                            connection.ConnectionWithId.Dispose();
                            connection.ConnectionWithId = null;
                        }
                        else if (connection.StatusChangedAt.Add(_forceDisposeTimeFromLastUsed) < DateTime.Now)
                        {
                            //最後に解放してから一定時間以上経過してても捨てて作り直すぞ！
                            connection.ConnectionWithId.Dispose();
                            connection.ConnectionWithId = null;
                        }
                    }

                    connection.Status = PoolStatus.Preparing;
                    connection.StatusChangedAt = DateTime.Now;
                    connection.ExpiredAt = connection.StatusChangedAt.Add(_preparingExpiry);
                    connection.PayOutNumber = Interlocked.Increment(ref this._payOutNumber);
                    connection.CallerName = null;

                    return connection.PayOutNumber.Value;
                }
            }
        }

        private class InternalPooledConnection : IDisposable
        {
            [CanBeNull]
            public IConnectionWithId<MySqlConnection> ConnectionWithId { get; set; }

            /// <summary>
            /// 現在の状態。
            /// </summary>
            public PoolStatus Status { get; set; }

            /// <summary>
            /// 現在の状態に変化した日時。
            /// </summary>
            public DateTime StatusChangedAt { get; set; }

            /// <summary>
            /// 有効期限。
            /// この日時を過ぎても状態が変化していなかったら回収して使用可能にする。
            /// </summary>
            public DateTime ExpiredAt { get; set; }

            /// <summary>
            /// 貸し出し番号。
            /// 貸し出した相手からの処理要求かどうかを識別するのに使う。
            /// </summary>
            public long? PayOutNumber { get; set; }

            /// <summary>
            /// この接続を借りた主体を識別する名前。
            /// </summary>
            public string CallerName { get; set; }

            public void Dispose()
            {
                this.ConnectionWithId?.Dispose();
                this.ConnectionWithId = null;
            }
        }

        private struct PayOut : IEquatable<PayOut>
        {
            public int Index { get; }

            public long PayOutNumber{ get; }

            public PayOut(int index, long payOutNumber)
            {
                this.Index = index;
                this.PayOutNumber = payOutNumber;
            }

            public bool Equals(PayOut other)
            {
                return this.Index == other.Index && this.PayOutNumber == other.PayOutNumber;
            }

            public override bool Equals(object obj)
            {
                return obj is PayOut other && this.Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (this.Index * 397) ^ this.PayOutNumber.GetHashCode();
                }
            }
        }

        private enum PoolStatus
        {
            /// <summary>
            /// 誰にも使用されておらず、使用可能な状態を示します。
            /// </summary>
            Assignable,

            /// <summary>
            /// 貸し出しの予約が行われ、貸し出し処理に移っていることを示します。
            /// </summary>
            Preparing,

            /// <summary>
            /// 貸し出され、使用中であることを示します。
            /// </summary>
            Using,
        }
    }
}
