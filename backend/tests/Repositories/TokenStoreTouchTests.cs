using Api.Repositories;
using Api.Services;
using Api.Services.Reporting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Orkyo.Foundation.Tests.Repositories;

/// <summary>
/// The last-used touch is best-effort, so a failure is a warning — but a cancelled touch is not a
/// failure, and used to be logged as one.
/// </summary>
public class TokenStoreTouchTests
{
    private static (TokenStore<ReportingTokenRecord, ReportingTokenSummary> Store, Mock<ILogger> Logger) Create(string connectionString)
    {
        var connections = new Mock<IDbConnectionFactory>();
        connections.Setup(c => c.CreateControlPlaneConnection()).Returns(() => new NpgsqlConnection(connectionString));
        var logger = new Mock<ILogger>();
        var store = new TokenStore<ReportingTokenRecord, ReportingTokenSummary>(
            connections.Object, TimeProvider.System, logger.Object, "reporting_api_tokens", "orkyo_rpt", new byte[32]);
        return (store, logger);
    }

    [Fact]
    public async Task ACancelledTouch_IsNotLoggedAsAFailure()
    {
        var (store, logger) = Create("Host=localhost;Port=1;Timeout=1");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => store.TouchLastUsedAsync(Guid.NewGuid(), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
    }

    [Fact]
    public async Task AFailedTouch_IsAWarning_NotAThrow()
    {
        var (store, logger) = Create("Host=localhost;Port=1;Timeout=1");

        await store.TouchLastUsedAsync(Guid.NewGuid(), CancellationToken.None);

        logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
