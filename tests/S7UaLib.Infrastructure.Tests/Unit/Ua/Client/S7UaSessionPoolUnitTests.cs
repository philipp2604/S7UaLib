using Microsoft.Extensions.Logging;
using Moq;
using S7UaLib.Core.Ua;
using S7UaLib.Infrastructure.Ua.Client;
using S7UaLib.TestHelpers;
using System.Collections;
using System.Collections.Concurrent;

namespace S7UaLib.Infrastructure.Tests.Unit.Ua.Client;

[Trait("Category", "Unit")]
public class S7UaSessionPoolUnitTests
{
    private readonly Mock<ILogger<S7UaSessionPool>> _mockLogger;
    private readonly UserIdentity _userIdentity;
    private readonly Action<IList, IList> _validateResponse;
    private readonly Opc.Ua.ApplicationConfiguration _opcAppConfig;
    private readonly Opc.Ua.ConfiguredEndpoint _endpoint;
    private class TestableS7UaSessionPool : S7UaSessionPool
    {
        public Queue<Opc.Ua.Client.ISession> MockSessionsToCreate { get; } = new();
        public Exception? ExceptionToThrowOnCreate { get; set; }

        public TestableS7UaSessionPool(UserIdentity userIdentity, int maxPoolSize, Action<IList, IList> validateResponse, ILogger<S7UaSessionPool>? logger = null)
            : base(userIdentity, maxPoolSize, validateResponse, logger) { }

        internal override Task<Opc.Ua.Client.ISession> CreateNewSessionAsync(CancellationToken cancellationToken)
        {
            if (MockSessionsToCreate.TryDequeue(out var session))
            {
                return Task.FromResult(session);
            }

            if (ExceptionToThrowOnCreate != null)
            {
                return Task.FromException<Opc.Ua.Client.ISession>(ExceptionToThrowOnCreate);
            }

            throw new InvalidOperationException("Test setup error: Not enough mock sessions or exceptions were provided.");
        }
    }

    public S7UaSessionPoolUnitTests()
    {
        _mockLogger = new Mock<ILogger<S7UaSessionPool>>();
        _userIdentity = new UserIdentity("test", "pass");
        _validateResponse = (_, _) => { };
        _opcAppConfig = new Opc.Ua.ApplicationConfiguration();
        _endpoint = new Opc.Ua.ConfiguredEndpoint();
    }

    private Mock<Opc.Ua.Client.ISession> CreateMockSession(bool isConnected = true)
    {
        var mockSession = new Mock<Opc.Ua.Client.ISession>();
        mockSession.Setup(s => s.Connected).Returns(isConnected);
        mockSession.Setup(s => s.SessionId).Returns(new Opc.Ua.NodeId(Guid.NewGuid()));
        return mockSession;
    }

    #region Constructor and Pre-condition Tests

    [Fact]
    public void Constructor_WithMaxPoolSize_InitializesSemaphoreWithCorrectCount()
    {
        const int maxPoolSize = 7;
        using var sut = new S7UaSessionPool(_userIdentity, maxPoolSize, _validateResponse, _mockLogger.Object);
        var semaphore = (SemaphoreSlim)PrivateFieldHelpers.GetPrivateField(sut, "_poolSemaphore")!;
        Assert.Equal(maxPoolSize, semaphore.CurrentCount);
    }

    [Fact]
    public async Task ExecuteWithSessionAsync_BeforeInitialize_ThrowsInvalidOperationException()
    {
        using var sut = new S7UaSessionPool(_userIdentity, 1, _validateResponse);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteWithSessionAsync(session => Task.FromResult(true)));
        Assert.Contains("must be initialized", ex.Message);
    }

    #endregion Constructor and Pre-condition Tests

    #region Initialization Tests

    [Fact]
    public async Task InitializeAsync_Success_CreatesAllSessionsAndPopulatesQueue()
    {
        // Arrange
        const int maxPoolSize = 3;
        var sut = new TestableS7UaSessionPool(_userIdentity, maxPoolSize, _validateResponse, _mockLogger.Object);
        sut.MockSessionsToCreate.Enqueue(CreateMockSession().Object);
        sut.MockSessionsToCreate.Enqueue(CreateMockSession().Object);
        sut.MockSessionsToCreate.Enqueue(CreateMockSession().Object);

        // Act
        await sut.InitializeAsync(_opcAppConfig, _endpoint);

        // Assert
        var availableSessions = (ConcurrentQueue<Opc.Ua.Client.ISession>)PrivateFieldHelpers.GetPrivateField(sut, "_availableSessions")!;
        Assert.Equal(maxPoolSize, availableSessions.Count);
        Assert.Empty(sut.MockSessionsToCreate); // All queued sessions were used.
    }

    [Fact]
    public async Task InitializeAsync_PartialFailure_ThrowsAndDisposesSuccessfullyCreatedSessions()
    {
        // Arrange
        const int maxPoolSize = 3;
        var sut = new TestableS7UaSessionPool(_userIdentity, maxPoolSize, _validateResponse, _mockLogger.Object);

        var successfulSession = CreateMockSession();
        sut.MockSessionsToCreate.Enqueue(successfulSession.Object);
        sut.ExceptionToThrowOnCreate = new Exception("Creation failed");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.InitializeAsync(_opcAppConfig, _endpoint));
        Assert.Contains("Could only create 1 out of 3", ex.Message);

        successfulSession.Verify(s => s.Dispose(), Times.Once);
    }

    #endregion Initialization Tests

    #region Execution Logic Tests

    [Fact]
    public async Task ExecuteWithSessionAsync_WhenOperationSucceeds_ReturnsSessionToPool()
    {
        const int maxPoolSize = 1;
        var sut = new TestableS7UaSessionPool(_userIdentity, maxPoolSize, _validateResponse, _mockLogger.Object);
        sut.MockSessionsToCreate.Enqueue(CreateMockSession().Object);
        await sut.InitializeAsync(_opcAppConfig, _endpoint);

        var semaphore = (SemaphoreSlim)PrivateFieldHelpers.GetPrivateField(sut, "_poolSemaphore")!;
        Assert.Equal(1, semaphore.CurrentCount);

        var result = await sut.ExecuteWithSessionAsync(session => Task.FromResult("Success"));

        Assert.Equal("Success", result);
        Assert.Equal(1, semaphore.CurrentCount);
        var availableSessions = (ConcurrentQueue<Opc.Ua.Client.ISession>)PrivateFieldHelpers.GetPrivateField(sut, "_availableSessions")!;
        Assert.Single(availableSessions);
    }

    [Fact]
    public async Task ExecuteWithSessionAsync_WhenOperationThrows_StillReturnsSessionToPool()
    {
        const int maxPoolSize = 1;
        var sut = new TestableS7UaSessionPool(_userIdentity, maxPoolSize, _validateResponse, _mockLogger.Object);
        sut.MockSessionsToCreate.Enqueue(CreateMockSession().Object);
        await sut.InitializeAsync(_opcAppConfig, _endpoint);

        var semaphore = (SemaphoreSlim)PrivateFieldHelpers.GetPrivateField(sut, "_poolSemaphore")!;

        await Assert.ThrowsAsync<ArgumentException>(() => sut.ExecuteWithSessionAsync<bool>(session => throw new ArgumentException("Test Failure")));

        Assert.Equal(1, semaphore.CurrentCount);
        var availableSessions = (ConcurrentQueue<Opc.Ua.Client.ISession>)PrivateFieldHelpers.GetPrivateField(sut, "_availableSessions")!;
        Assert.Single(availableSessions);
    }

    [Fact]
    public async Task ExecuteWithSessionAsync_WithDisconnectedSession_RecreatesSessionAndExecutes()
    {
        var sut = new TestableS7UaSessionPool(_userIdentity, 1, _validateResponse, _mockLogger.Object);
        var oldSession = CreateMockSession(isConnected: false);
        var newSession = CreateMockSession(isConnected: true);
        sut.MockSessionsToCreate.Enqueue(oldSession.Object); // For InitializeAsync
        sut.MockSessionsToCreate.Enqueue(newSession.Object); // For recreation
        await sut.InitializeAsync(_opcAppConfig, _endpoint);

        var result = await sut.ExecuteWithSessionAsync(session =>
        {
            Assert.Same(newSession.Object, session);
            return Task.FromResult(true);
        });

        Assert.True(result);
        oldSession.Verify(s => s.Dispose(), Times.Once);
    }

    #endregion Execution Logic Tests

    #region Concurrency and Dispose Tests

    [Fact]
    public async Task ExecuteWithSessionAsync_WhenPoolExhausted_WaitsForSession()
    {
        const int maxPoolSize = 1;
        var sut = new TestableS7UaSessionPool(_userIdentity, maxPoolSize, _validateResponse);
        sut.MockSessionsToCreate.Enqueue(CreateMockSession().Object);
        await sut.InitializeAsync(_opcAppConfig, _endpoint);

        var tcs = new TaskCompletionSource<bool>();

        var op1 = sut.ExecuteWithSessionAsync(async session => { await tcs.Task; return true; });
        await Task.Delay(50);
        var op2 = sut.ExecuteWithSessionAsync(session => Task.FromResult(true));

        Assert.False(op2.IsCompleted, "Second operation should be waiting for the semaphore.");

        tcs.SetResult(true);
        await Task.WhenAll(op1, op2);

        Assert.True(op1.IsCompletedSuccessfully);
        Assert.True(op2.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Dispose_DisposesAllCreatedSessions()
    {
        // Arrange
        const int maxPoolSize = 3;
        var sut = new TestableS7UaSessionPool(_userIdentity, maxPoolSize, _validateResponse);
        var sessions = new[] { CreateMockSession(), CreateMockSession(), CreateMockSession() };
        sut.MockSessionsToCreate.Enqueue(sessions[0].Object);
        sut.MockSessionsToCreate.Enqueue(sessions[1].Object);
        sut.MockSessionsToCreate.Enqueue(sessions[2].Object);
        await sut.InitializeAsync(_opcAppConfig, _endpoint);

        // Take one session out to simulate it being "in use"
        _ = await sut.ExecuteWithSessionAsync(session => Task.FromResult(session));

        // Act
        sut.Dispose();

        // Assert
        foreach (var mockSession in sessions)
        {
            mockSession.Verify(s => s.Close(), Times.Once);
        }
    }

    #endregion Concurrency and Dispose Tests
}