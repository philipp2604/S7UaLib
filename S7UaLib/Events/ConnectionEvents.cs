using Opc.Ua;

namespace S7UaLib.Events;

public class ConnectionEventArgs(StatusCode? statusCode = null, Exception? exception = null) : EventArgs
{
    #region Public Properties

    public StatusCode? StatusCode { get; } = statusCode;
    public Exception? Exception { get; } = exception;

    #endregion

    #region Public Methods

    new public static ConnectionEventArgs Empty => new();

    #endregion
}