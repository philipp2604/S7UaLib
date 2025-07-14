using S7UaLib.Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.Core.Events;

/// <summary>
/// Provides data for connection-related events, including the status code and any associated exception.
/// </summary>
/// <remarks>This class is typically used to convey information about the outcome of a connection operation, such
/// as whether it succeeded, failed, or encountered an error. It includes optional details about the status code and any
/// exception that occurred.</remarks>
/// <param name="statusCode">The optional status code that was returned by the method that fired this event.</param>
/// <param name="exception">The optional exception that was thrown by the method that fired this event.</param>
public class ConnectionEventArgs(StatusCode? statusCode = null, Exception? exception = null) : EventArgs
{
    #region Public Properties

    /// <summary>
    /// Gets the optional status code that was returned by the event firing connection method.
    /// </summary>
    public StatusCode? StatusCode { get; } = statusCode;

    /// <summary>
    /// Gets the optional exception that was thrown by the event firing connection method.
    /// </summary>
    public Exception? Exception { get; } = exception;

    #endregion Public Properties

    #region Public Methods

    /// <summary>
    /// Creates a new, empty instance of <c>ConnectionEventArgs</c>.
    /// </summary>
    public new static ConnectionEventArgs Empty => new();

    #endregion Public Methods
}