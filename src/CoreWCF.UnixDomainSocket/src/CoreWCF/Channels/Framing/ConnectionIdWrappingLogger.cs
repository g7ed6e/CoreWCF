// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Extensions.Logging;

namespace CoreWCF.Channels.Framing
{
    internal class ConnectionIdWrappingLogger : ILogger
    {
        private ILogger _innerLogger;
        private string _connectionId;

        public ConnectionIdWrappingLogger(ILogger innerLogger, string connectionId)
        {
            _innerLogger = innerLogger;
            _connectionId = connectionId;
        }
        public IDisposable BeginScope<TState>(TState state) => _innerLogger.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => _innerLogger.IsEnabled(logLevel);

        /// <summary>
        /// Forwards the entry with the connection id prepended to the formatted message.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The state is passed through unchanged and only the formatter is wrapped. That is not a
        /// stylistic preference. Forwarding a state that embeds <typeparamref name="TState"/> - which a
        /// <c>(TState, string, Func&lt;TState, Exception, string&gt;)</c> tuple did - makes
        /// <c>Log&lt;TState&gt;</c> call <c>Log&lt;(TState, ...)&gt;</c>, which calls
        /// <c>Log&lt;((TState, ...), ...)&gt;</c>, with nothing to bound it. The JIT instantiates those
        /// lazily and a chain only ever one or two loggers deep never notices, but an ahead of time
        /// compiler has to decide the whole set before the program runs: ILC refused to compile
        /// CoreWCF.NetTcp at all, failing the publish rather than warning.
        /// </para>
        /// <para>
        /// Passing the state through also means a structured logging provider sees the state the caller
        /// supplied. A tuple wrapping it hid that.
        /// </para>
        /// </remarks>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            string connectionId = _connectionId;
            _innerLogger.Log(logLevel, eventId, state, exception,
                (s, e) => $"[{connectionId}] {formatter(s, e)}");
        }
    }

}
