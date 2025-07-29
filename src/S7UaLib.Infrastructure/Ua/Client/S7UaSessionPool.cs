using Microsoft.Extensions.Logging;
using S7UaLib.Core.Ua;
using System.Collections;
using System.Collections.Concurrent;

namespace S7UaLib.Infrastructure.Ua.Client;

/// <summary>
/// Manages a pool of OPC UA sessions for stateless operations like discovery, reading, and writing.
/// Sessions are created on-demand and reused to improve performance while avoiding the overhead
/// of maintaining persistent connections for short-lived operations.
/// </summary>
internal class S7UaSessionPool : IS7UaSessionPool
{
    #region Private Fields

    private readonly ILogger<S7UaSessionPool>? _logger;
    private readonly UserIdentity _userIdentity;
    private readonly Action<IList, IList> _validateResponse;
    private readonly ConcurrentQueue<Opc.Ua.Client.ISession> _availableSessions = new();
    private readonly ConcurrentBag<Opc.Ua.Client.ISession> _allSessions = []; // Track all created sessions for disposal
    private readonly SemaphoreSlim _poolSemaphore;

    private Opc.Ua.ApplicationConfiguration? _opcApplicationConfiguration;
    private Opc.Ua.ConfiguredEndpoint? _endpoint;
    private bool _disposed;

    // Configuration
    private readonly int _maxPoolSize;

    #endregion Private Fields

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="S7UaSessionPool"/> class.
    /// </summary>
    /// <param name="userIdentity">The user identity for session authentication.</param>
    /// <param name="maxPoolSize">Maximum number of sessions to maintain in the pool.</param>
    /// <param name="validateResponse">Response validation callback.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public S7UaSessionPool(
        UserIdentity userIdentity,
        int maxPoolSize,
        Action<IList, IList> validateResponse,
        ILogger<S7UaSessionPool>? logger = null)
    {
        _userIdentity = userIdentity ?? throw new ArgumentNullException(nameof(userIdentity));
        _validateResponse = validateResponse ?? throw new ArgumentNullException(nameof(validateResponse));
        _logger = logger;
        _maxPoolSize = maxPoolSize;

        // Initialize semaphore with the pool size - this controls how many concurrent operations can get sessions
        _poolSemaphore = new SemaphoreSlim(_maxPoolSize, _maxPoolSize);
    }

    #endregion Constructors

    #region Public Methods

    /// <inheritdoc/>
    public async Task InitializeAsync(Opc.Ua.ApplicationConfiguration opcApplicationConfiguration, Opc.Ua.ConfiguredEndpoint endpoint)
    {
        ThrowIfDisposed();

        _opcApplicationConfiguration = opcApplicationConfiguration ?? throw new ArgumentNullException(nameof(opcApplicationConfiguration));
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));

        _logger?.LogDebug("Initializing session pool with {MaxPoolSize} sessions for endpoint: {EndpointUrl}",
            _maxPoolSize, endpoint.EndpointUrl);

        // Pre-create all sessions for the pool with delay to avoid overwhelming the server
        var createdSessions = new List<Opc.Ua.Client.ISession>();
        var successfulSessions = 0;

        for (int i = 0; i < _maxPoolSize; i++)
        {
            try
            {
                var session = await CreateNewSessionAsync(CancellationToken.None).ConfigureAwait(false);
                createdSessions.Add(session);
                _allSessions.Add(session); // Track for disposal
                successfulSessions++;
                _logger?.LogDebug("Pre-created session {Index}/{MaxPoolSize}: {SessionId}",
                    i + 1, _maxPoolSize, session.SessionId);

                // Add small delay to avoid overwhelming the server
                if (i < _maxPoolSize - 1)
                {
                    await Task.Delay(50).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to pre-create session {Index}/{MaxPoolSize}", i + 1, _maxPoolSize);

                // Clean up any sessions that were successfully created
                foreach (var session in createdSessions)
                {
                    try { session.Dispose(); } catch { }
                }

                throw new InvalidOperationException($"Failed to initialize session pool. Could only create {successfulSessions} out of {_maxPoolSize} required sessions.", ex);
            }
        }

        // Update semaphore to match actual number of created sessions
        if (successfulSessions != _maxPoolSize)
        {
            _logger?.LogWarning("Only created {ActualSessions} out of {RequestedSessions} sessions. Adjusting semaphore.", successfulSessions, _maxPoolSize);

            // Release excess semaphore permits
            var excessPermits = _maxPoolSize - successfulSessions;
            _poolSemaphore.Release(excessPermits);
        }

        // Atomically add all created sessions to the queue
        foreach (var session in createdSessions)
        {
            _availableSessions.Enqueue(session);
            _logger?.LogDebug("Added session {SessionId} to available queue", session.SessionId);
        }

        _logger?.LogInformation("Session pool initialized successfully with {SessionCount} sessions (Queue count: {QueueCount}, Semaphore count: {SemaphoreCount})",
            createdSessions.Count, _availableSessions.Count, _poolSemaphore.CurrentCount);
    }

    /// <inheritdoc/>
    public async Task<T> ExecuteWithSessionAsync<T>(
        Func<Opc.Ua.Client.ISession, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ThrowIfNotInitialized();

        var session = await GetSessionAsync(cancellationToken).ConfigureAwait(false);
        _logger?.LogTrace("Acquired session {SessionId} from pool for operation", session.SessionId);

        try
        {
            var result = await operation(session).ConfigureAwait(false);
            _logger?.LogTrace("Operation completed successfully with session {SessionId}", session.SessionId);
            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Operation failed with session {SessionId}: {Exception}", session.SessionId, ex.Message);
            throw;
        }
        finally
        {
            _logger?.LogTrace("Returning session {SessionId} to pool", session.SessionId);
            ReturnSession(session);
        }
    }

    /// <inheritdoc/>
    public T ExecuteWithSession<T>(Func<Opc.Ua.Client.ISession, T> operation)
    {
        ThrowIfDisposed();
        ThrowIfNotInitialized();

        var session = GetSessionAsync(CancellationToken.None).GetAwaiter().GetResult();
        try
        {
            return operation(session);
        }
        finally
        {
            ReturnSession(session);
        }
    }

    #endregion Public Methods

    #region Private Methods

    /// <summary>
    /// Gets a session from the fixed pool, waiting if all sessions are currently in use.
    /// Automatically recreates invalid sessions.
    /// </summary>
    private async Task<Opc.Ua.Client.ISession> GetSessionAsync(CancellationToken cancellationToken)
    {
        // Wait for a session to become available (blocking if all sessions are in use)
        await _poolSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        // At this point, we have exclusive access to get a session
        if (!_availableSessions.TryDequeue(out var session))
        {
            // This should never happen if the semaphore count matches the pool size
            var availableCount = _availableSessions.Count;
            var semaphoreCount = _poolSemaphore.CurrentCount;

            _logger?.LogError("Session pool inconsistent state detected! Available sessions: {AvailableCount}, Semaphore current count: {SemaphoreCount}, Max pool size: {MaxPoolSize}",
                availableCount, semaphoreCount, _maxPoolSize);

            _poolSemaphore.Release();
            throw new InvalidOperationException($"Session pool is in an inconsistent state - no sessions available despite semaphore acquisition. Available: {availableCount}, Semaphore: {semaphoreCount}, MaxPool: {_maxPoolSize}");
        }

        // Check if session is still valid
        if (session.Connected)
        {
            _logger?.LogTrace("Retrieved valid session from pool: {SessionId}", session.SessionId);
            return session;
        }
        else
        {
            // Session is invalid, recreate it
            _logger?.LogWarning("Session {SessionId} is disconnected, recreating...", session.SessionId);
            session.Dispose();

            try
            {
                var newSession = await CreateNewSessionAsync(cancellationToken).ConfigureAwait(false);
                _allSessions.Add(newSession); // Track for disposal
                _logger?.LogInformation("Successfully recreated session: {SessionId}", newSession.SessionId);
                return newSession;
            }
            catch (Exception ex)
            {
                // Failed to recreate session, put semaphore back and rethrow
                _logger?.LogError(ex, "Failed to recreate invalid session");
                _poolSemaphore.Release();
                throw;
            }
        }
    }

    /// <summary>
    /// Creates a new OPC UA session.
    /// </summary>
    internal virtual async Task<Opc.Ua.Client.ISession> CreateNewSessionAsync(CancellationToken cancellationToken)
    {
        _logger?.LogDebug("Creating new session for pool");

        try
        {
            var identity = _userIdentity.Username == null && _userIdentity.Password == null
                ? new Opc.Ua.UserIdentity()
                : new Opc.Ua.UserIdentity(_userIdentity.Username, _userIdentity.Password);

            var sessionFactory = Opc.Ua.Client.TraceableSessionFactory.Instance;
            var session = await sessionFactory.CreateAsync(
                _opcApplicationConfiguration!,
                _endpoint!,
                true,
                false,
                $"{_opcApplicationConfiguration!.ApplicationName}_Pool",
                (uint)_opcApplicationConfiguration!.ClientConfiguration.DefaultSessionTimeout,
                identity,
                null,
                cancellationToken
            ).ConfigureAwait(false);

            if (session?.Connected != true)
            {
                throw new InvalidOperationException("Failed to create session for pool");
            }

            _logger?.LogDebug("Created new session for pool: {SessionId}", session.SessionId);

            return session;
        }
        catch (Opc.Ua.ServiceResultException ex) when (ex.StatusCode == Opc.Ua.StatusCodes.BadTooManySessions)
        {
            _logger?.LogError("Server rejected session creation: too many sessions (max pool size: {MaxPoolSize}). " +
                             "Consider reducing maxSessions parameter or checking server session limits.",
                             _maxPoolSize);
            throw new InvalidOperationException(
                "Server has too many active sessions. " +
                $"Reduce the maxSessions parameter (currently {_maxPoolSize}) or increase the server's session limit.", ex);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create new session for pool");
            throw;
        }
    }

    /// <summary>
    /// Returns a session to the fixed pool and releases the semaphore to allow the next operation.
    /// </summary>
    private void ReturnSession(Opc.Ua.Client.ISession session)
    {
        if (session != null && !_disposed)
        {
            // Always return the session to the pool (even if disconnected - it will be recreated on next use)
            _availableSessions.Enqueue(session);
            _logger?.LogTrace("Returned session to pool: {SessionId} (Connected: {Connected})",
                session.SessionId, session.Connected);
        }
        else
        {
            _logger?.LogWarning("Attempted to return null session or pool is disposed");
        }

        // Always release the semaphore to allow the next operation
        _poolSemaphore.Release();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void ThrowIfNotInitialized()
    {
        if (_opcApplicationConfiguration == null || _endpoint == null)
        {
            throw new InvalidOperationException("Session pool must be initialized before use. Call InitializeAsync() first.");
        }
    }

    #endregion Private Methods

    #region Dispose

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            // Dispose ALL sessions (including those currently in use)
            var sessionCount = 0;
            foreach (var session in _allSessions)
            {
                sessionCount++;
                try
                {
                    if (!session.Disposed)
                    {
                        try
                        {
                            // Always close the session without parameters
                            session.Close();
                        }
                        catch (Exception closeEx)
                        {
                            _logger?.LogError(closeEx, "DISPOSE: Error closing session {SessionId}", session.SessionId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "DISPOSE: Error disposing session {Index}/{Total}: {SessionId}",
                        sessionCount, _allSessions.Count, session.SessionId);
                }
            }

            // Clear collections
            var queueCount = 0;
            while (_availableSessions.TryDequeue(out _)) { queueCount++; } // Clear available queue

            _allSessions.Clear();

            _poolSemaphore?.Dispose();
        }

        _disposed = true;
    }

    ~S7UaSessionPool()
    {
        Dispose(false);
    }

    #endregion Dispose
}