// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CoreWCF.NetTcp.Tests
{
    /// <summary>
    /// Regression coverage for the generic recursion that used to make CoreWCF.NetTcp impossible to
    /// compile ahead of time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ConnectionIdWrappingLogger.Log&lt;TState&gt;</c> used to forward a
    /// <c>(TState, string, Func&lt;TState, Exception, string&gt;)</c> tuple as the inner logger's state.
    /// That makes <c>Log&lt;TState&gt;</c> call <c>Log&lt;(TState, ...)&gt;</c>, which calls
    /// <c>Log&lt;((TState, ...), ...)&gt;</c>, with nothing to bound it. Under the JIT each level is
    /// instantiated lazily and a chain only ever one or two loggers deep never notices; ILC has to decide
    /// the whole set before the program runs and failed the publish outright.
    /// </para>
    /// <para>
    /// The assertion that guards it is <see cref="ForwardsTheCallersOwnState"/>: the inner logger has to
    /// see the state it was given, at the type it was given. Anything that wraps the state reintroduces
    /// the recursion, and no build on a JIT runtime would report it.
    /// </para>
    /// <para>
    /// The logger is internal and this repository deliberately does not add InternalsVisibleTo for tests,
    /// so it is constructed by reflection - the same approach as KafkaTombstoneRegressionTests.
    /// </para>
    /// </remarks>
    public class ConnectionIdWrappingLoggerTests
    {
        private const string ConnectionId = "0HN1ABCDEF";

        private static ILogger CreateWrappingLogger(ILogger inner, string connectionId)
        {
            Type type = typeof(CoreWCF.NetTcpBinding).Assembly
                .GetType("CoreWCF.Channels.Framing.ConnectionIdWrappingLogger", throwOnError: true);

            return (ILogger)Activator.CreateInstance(
                type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new object[] { inner, connectionId },
                culture: null);
        }

        [Fact]
        public void PrependsTheConnectionIdToTheFormattedMessage()
        {
            var inner = new CapturingLogger();
            ILogger logger = CreateWrappingLogger(inner, ConnectionId);

            logger.Log(LogLevel.Information, new EventId(7), "connection accepted", null, static (s, e) => s);

            Assert.Equal($"[{ConnectionId}] connection accepted", inner.Single().Message);
        }

        [Fact]
        public void PassesTheExceptionToTheOriginalFormatter()
        {
            var inner = new CapturingLogger();
            ILogger logger = CreateWrappingLogger(inner, ConnectionId);
            var failure = new InvalidOperationException("boom");

            logger.Log(LogLevel.Error, new EventId(8), "read failed", failure,
                static (s, e) => $"{s}: {e.Message}");

            Assert.Equal($"[{ConnectionId}] read failed: boom", inner.Single().Message);
        }

        /// <summary>
        /// The state reaches the inner logger unchanged, at the type the caller used.
        /// </summary>
        /// <remarks>
        /// This is the assertion that stops the generic recursion coming back. It is also what lets a
        /// structured logging provider see the caller's state at all - while it was wrapped in a tuple,
        /// every provider downstream of this logger saw a ValueTuple and could extract nothing.
        /// </remarks>
        [Fact]
        public void ForwardsTheCallersOwnState()
        {
            var inner = new CapturingLogger();
            ILogger logger = CreateWrappingLogger(inner, ConnectionId);

            var state = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("endpoint", "net.tcp://localhost:8089/echo"),
            };

            logger.Log(LogLevel.Debug, new EventId(9), state, null, static (s, e) => s[0].Value.ToString());

            Entry entry = inner.Single();
            Assert.Same(state, entry.State);
            Assert.Equal(typeof(List<KeyValuePair<string, object>>), entry.StateType);
        }

        [Fact]
        public void ForwardsLevelEventIdAndException()
        {
            var inner = new CapturingLogger();
            ILogger logger = CreateWrappingLogger(inner, ConnectionId);
            var failure = new InvalidOperationException("boom");

            logger.Log(LogLevel.Warning, new EventId(11, "Framing"), "state", failure, static (s, e) => s);

            Entry entry = inner.Single();
            Assert.Equal(LogLevel.Warning, entry.LogLevel);
            Assert.Equal(11, entry.EventId.Id);
            Assert.Equal("Framing", entry.EventId.Name);
            Assert.Same(failure, entry.Exception);
        }

        [Fact]
        public void DelegatesIsEnabledAndBeginScopeToTheInnerLogger()
        {
            var inner = new CapturingLogger { Enabled = false };
            ILogger logger = CreateWrappingLogger(inner, ConnectionId);

            Assert.False(logger.IsEnabled(LogLevel.Information));

            using (logger.BeginScope("scope"))
            {
                Assert.Equal("scope", inner.LastScope);
            }
        }

        private sealed class Entry
        {
            public LogLevel LogLevel { get; set; }
            public EventId EventId { get; set; }
            public object State { get; set; }
            public Type StateType { get; set; }
            public Exception Exception { get; set; }
            public string Message { get; set; }
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly List<Entry> _entries = new List<Entry>();

            public bool Enabled { get; set; } = true;

            public object LastScope { get; private set; }

            public Entry Single() => _entries.Single();

            public IDisposable BeginScope<TState>(TState state)
            {
                LastScope = state;
                return new NoopScope();
            }

            public bool IsEnabled(LogLevel logLevel) => Enabled;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
                Func<TState, Exception, string> formatter)
            {
                _entries.Add(new Entry
                {
                    LogLevel = logLevel,
                    EventId = eventId,
                    State = state,
                    // Captured from the generic parameter rather than from the value, so a null state or a
                    // wrapper struct is still reported as the type the inner logger was instantiated at.
                    StateType = typeof(TState),
                    Exception = exception,
                    Message = formatter(state, exception),
                });
            }

            private sealed class NoopScope : IDisposable
            {
                public void Dispose() { }
            }
        }
    }
}
