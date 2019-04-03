using System;
using System.Collections.Concurrent;
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
        /// 最後に解放してから一定時間以上経過していたら、次回使う時には <see cref="MySqlConnection.Dispose"/> して <see cref="MySqlConnection"/> 作り直す。
        /// </summary>
        private static readonly TimeSpan _forceDisposeTimeFromLastUsed = TimeSpan.FromHours(1);

        /// <summary>
        /// 貸し出してから一定時間返却がなかったら強制的に Pool に戻す。
        /// </summary>
        private static readonly TimeSpan _maxExecutionTime = TimeSpan.FromHours(2);

        /// <summary>
        /// 未解放のコネクションが無いか一定周期でチェックを行う。
        /// </summary>
        private static readonly TimeSpan _groomingInterval = TimeSpan.FromMinutes(30);

        [NotNull]
        private readonly IConnectionFactory<MySqlConnection> _factory;

        private readonly int _connectionPoolSize;

        [NotNull]
        private readonly object _commandQueueLock = new object();

        [NotNull]
        private readonly object _groomingLock = new object();

        [NotNull]
        private readonly PooledConnection[] _connectionPool;

        [NotNull]
        private readonly ConcurrentQueue<int> _usableConnections = new ConcurrentQueue<int>();

        [NotNull]
        private readonly Queue<TaskCompletionSource<int>> _commandPreparerQueue = new Queue<TaskCompletionSource<int>>();

        private DateTime _nextGroomingTime;

        public GlobalConnectionPool([NotNull] string poolName, [NotNull] IConnectionFactory<MySqlConnection> factory, int connectionPoolSize)
        {
            this.PoolName = poolName;
            this._factory = factory;
            this._connectionPoolSize = connectionPoolSize;
            this._nextGroomingTime = DateTime.Now.Add(_groomingInterval);

            //固定長配列いず大正義。
            this._connectionPool = new PooledConnection[this._connectionPoolSize];

            //初期状態では全ての Index が利用可能。
            for (var i = 0; i < connectionPoolSize; i++)
            {
                this._usableConnections.Enqueue(i);
            }
        }

        /// <summary>
        /// 利用可能な Connection と、その Connection の <see cref="_connectionPool"/> 内での Index を取得する。
        /// 利用可能な Connection が無い場合は、利用可能になるまで待機する。
        /// </summary>
        public async ValueTask<(IConnectionWithId<MySqlConnection> connection, int index)> GetConnectionAsync(ConnectionFactoryParameters parameters,
            CancellationToken cancellationToken)
        {
            this.Grooming();

            //利用可能なコネクションが無かったら待機列へどうぞ。
            if (!this._usableConnections.TryDequeue(out var index))
            {
                var preparer = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (this._commandQueueLock)
                {
                    this._commandPreparerQueue.Enqueue(preparer);
                }

                //利用可能なコネクションの Index を受け取って待機列を抜けるのだ。
                index = await preparer.Task.ConfigureAwait(false);
            }

            var connection = this._connectionPool[index];

            if (connection != null)
            {
                if (connection.Connection.State != ConnectionState.Open)
                {
                    //切断済コネクションは捨てて作り直すぞ！
                    connection.Connection.Dispose();
                    connection = null;
                }
                else if (connection.LastReleased.HasValue && connection.LastReleased.Value.Add(_forceDisposeTimeFromLastUsed) < DateTime.Now)
                {
                    //最後に解放してから一定時間以上経過してても捨てて作り直すぞ！
                    connection.Connection.Dispose();
                    connection = null;
                }
            }

            //最初に全部のコネクション作るわけじゃないから無かったら作るんだよ。
            if (connection == null)
            {
                this._connectionPool[index] = connection = new PooledConnection(this._factory.CreateConnection(parameters));
            }

            connection.LastPayOut = DateTime.Now;

            //作ったやつは当然開いてないから開く必要がある。
            //あるいは時間経過で勝手に閉じられることもあるかもね。
            if (connection.Connection.State != ConnectionState.Open)
            {
                await connection.Connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            return (connection.ConnectionWithId, index);
        }


        public void ReleaseConnection(int index)
        {
            //解放したタイミングで最後に使った時間を更新。
            var connection = this._connectionPool[index];
            if (connection != null)
            {
                connection.LastReleased = DateTime.Now;
            }

            //待機中のコマンドがあればそちらに直接利用可能な Index を流す。
            lock (this._commandQueueLock)
            {
                if (this._commandPreparerQueue.Count > 0)
                {
                    var preparer = this._commandPreparerQueue.Dequeue();
                    preparer.SetResult(index);
                    return;
                }
            }

            //待機中のコマンドがない場合は Pool に返す。
            this._usableConnections.Enqueue(index);
        }

        private void Grooming()
        {
            var now = DateTime.Now;
            if (this._nextGroomingTime > now) return;

            lock (this._groomingLock)
            {
                //多重実行防止のためロックの中で再チェック。
                if (this._nextGroomingTime > now) return;

                //次回実行日時を更新。
                this._nextGroomingTime = now.Add(_groomingInterval);

                var maxPayoutToRelease = now.Add(-_maxExecutionTime); //この時刻以前に払い出して返ってきていないやつは解放する。
                for (var index = 0; index < this._connectionPool.Length; index++)
                {
                    var connection = this._connectionPool[index];

                    //まだ作っていないやつは当然何もしない。
                    if (connection == null) continue;

                    //規定時間経過していないやつは触らなくてよし。
                    if (connection.LastPayOut > maxPayoutToRelease) continue;

                    //払い出した後にちゃんと返ってきてるやつも問題無し。
                    if (connection.LastReleased.HasValue && connection.LastReleased >= connection.LastPayOut) continue;

                    //一定時間返ってきていないやつは何かによって回収漏れな可能性が高いので回収しちゃう。
                    connection.Dispose();
                    this._connectionPool[index] = null;
                    var usableConnections = this._usableConnections.ToHashSet();
                    if (!usableConnections.Contains(index))
                    {
                        this._usableConnections.Enqueue(index);
                    }
                }
            }
        }

        private class PooledConnection : IDisposable
        {
            public IConnectionWithId<MySqlConnection> ConnectionWithId { get; private set; }

            public MySqlConnection Connection => this.ConnectionWithId.Connection;

            /// <summary>
            /// 最後に Pool から払い出した日時
            /// </summary>
            public DateTime LastPayOut { get; set; }

            /// <summary>
            /// 最後に Pool に戻ってきた日時
            /// </summary>
            public DateTime? LastReleased { get; set; }

            public PooledConnection(IConnectionWithId<MySqlConnection> connectionWithId)
            {
                this.ConnectionWithId = connectionWithId;
            }

            public void Dispose()
            {
                this.ConnectionWithId?.Dispose();
                this.ConnectionWithId = null;
            }
        }
    }
}
