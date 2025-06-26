using Opc.Ua;

namespace S7UaLib.Events;

public class ConnectionEventArgs : EventArgs
{
    #region Constructors
    public ConnectionEventArgs(StatusCode? statusCode = null, Exception? exception = null)
    {
        StatusCode = statusCode;
        Exception = exception;
    }

    #endregion

    #region Public Properties

    public StatusCode? StatusCode { get; }
    public Exception? Exception { get; }

    #endregion

    #region Public Methods

    new public static ConnectionEventArgs Empty => new ConnectionEventArgs();

    #endregion
}