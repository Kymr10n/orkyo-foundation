using System.Collections.Concurrent;
using System.Net;
using Api.Configuration;
using Api.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkyo.Shared;

namespace Orkyo.Foundation.Tests.Middleware;

/// <summary>
/// The order <c>UseFoundationMiddleware</c> composes. Request logging used to sit inside the
/// exception handler, so every domain 4xx reached it as an exception and was logged at Error with
/// a stack trace — the level the runbook's error query counts.
/// </summary>
public class FoundationMiddlewareOrderTests
{
    private sealed class CapturingProvider : ILoggerProvider
    {
        public ConcurrentQueue<(string Category, LogLevel Level, string Message)> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Capturing(categoryName, Entries);
        public void Dispose() { }

        private sealed class Capturing(string category, ConcurrentQueue<(string, LogLevel, string)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => entries.Enqueue((category, logLevel, formatter(state, exception)));
        }
    }

    private static async Task<(HttpStatusCode Status, CapturingProvider Logs)> GetAsync(Func<IResult> handler)
    {
        var logs = new CapturingProvider();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = EnvironmentNames.Test });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders().AddProvider(logs).SetMinimumLevel(LogLevel.Trace);
        builder.Services.AddExceptionHandler<AppExceptionHandler>();
        builder.Services.AddProblemDetails();

        var app = builder.Build();
        app.UseFoundationMiddleware();
        app.MapGet("/thing", handler);

        await app.StartAsync();
        try
        {
            var response = await app.GetTestClient().GetAsync("/thing");
            return (response.StatusCode, logs);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task ANotFoundException_IsLoggedAsTheFinal404_WithNoError()
    {
        var (status, logs) = await GetAsync(() => throw new NotFoundException("Request", Guid.NewGuid()));

        status.Should().Be(HttpStatusCode.NotFound);
        logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Error);
        logs.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("responded 404"));
    }

    [Fact]
    public async Task AnUnhandledException_IsStillAnErrorAndA500()
    {
        var (status, logs) = await GetAsync(() => throw new InvalidOperationException("boom"));

        status.Should().Be(HttpStatusCode.InternalServerError);
        logs.Entries.Should().Contain(e => e.Level >= LogLevel.Error);
        logs.Entries.Should().Contain(e => e.Message.Contains("responded 500"));
    }
}
