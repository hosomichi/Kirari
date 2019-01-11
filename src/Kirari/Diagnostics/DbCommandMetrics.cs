using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace Kirari.Diagnostics
{
    public class DbCommandMetrics
    {
        /// <summary>
        /// Get unique identifier for command.
        /// </summary>
        public long CommandId { get; }

        /// <summary>
        /// Get unique identifier for connection.
        /// </summary>
        public long ConnectionId { get; }

        /// <summary>
        /// Get what kind of method is captured.
        /// </summary>
        public DbCommandExecutionType ExecutionType { get; }

        /// <summary>
        /// Get query body to execute.
        /// </summary>
        public string CommandText { get; }

        /// <summary>
        /// Get parameters used by command.
        /// </summary>
        public IReadOnlyList<DbCommandParameterMetrics> Parameters { get; }

        /// <summary>
        /// Get time of execution started.
        /// </summary>
        public DateTimeOffset StartTime { get; }

        /// <summary>
        /// Get duration for command execution.
        /// </summary>
        public TimeSpan ExecutionElapsedTime { get; }

        /// <summary>
        /// Get <see cref="Exception"/> if occured or null.
        /// </summary>
        [CanBeNull]
        public Exception Exception { get; }

        public DbCommandMetrics(long commandId,
            long connectionId,
            DbCommandExecutionType executionType,
            string capturedCommandText,
            IReadOnlyList<DbCommandParameterMetrics> parameters,
            DateTimeOffset startTime,
            TimeSpan executionElapsedTime,
            [CanBeNull] Exception exception)
        {
            this.ExecutionType = executionType;
            this.CommandText = capturedCommandText;
            this.Parameters = parameters;
            this.StartTime = startTime;
            this.ExecutionElapsedTime = executionElapsedTime;
            this.Exception = exception;
            this.CommandId = commandId;
            this.ConnectionId = connectionId;
        }
    }
}
