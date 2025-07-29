namespace S7UaLib.Infrastructure.Ua.Client;

internal interface IS7UaSessionPool : IDisposable
{
    #region Public Methods

    /// <summary>
    /// Initializes the session pool with the main client's configuration and endpoint.
    /// This creates a fixed pool of sessions that are reused for all operations.
    /// </summary>
    /// <param name="opcApplicationConfiguration">The OPC UA application configuration from the main client.</param>
    /// <param name="endpoint">The configured endpoint from the main client.</param>
    public Task InitializeAsync(Opc.Ua.ApplicationConfiguration opcApplicationConfiguration, Opc.Ua.ConfiguredEndpoint endpoint);

    /// <summary>
    /// Executes an operation with a session from the pool.
    /// </summary>
    /// <typeparam name="T">The return type of the operation.</typeparam>
    /// <param name="operation">The operation to execute with the session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    public Task<T> ExecuteWithSessionAsync<T>(
        Func<Opc.Ua.Client.ISession, Task<T>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an operation with a session from the pool (synchronous version).
    /// </summary>
    /// <typeparam name="T">The return type of the operation.</typeparam>
    /// <param name="operation">The operation to execute with the session.</param>
    /// <returns>The result of the operation.</returns>
    public T ExecuteWithSession<T>(Func<Opc.Ua.Client.ISession, T> operation);

    #endregion Public Methods
}