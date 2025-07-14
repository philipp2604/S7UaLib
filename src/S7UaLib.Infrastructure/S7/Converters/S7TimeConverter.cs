using S7UaLib.Core.S7.Converters;

namespace S7UaLib.Infrastructure.S7.Converters;

/// <summary>
/// Handles the conversion between the S7 TIME format and the standard .NET <see cref="TimeSpan"/>.
/// The S7 TIME type is a 32-bit signed integer representing a duration in milliseconds.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="S7TimeConverter"/> class.
/// </remarks>
public class S7TimeConverter : IS7TypeConverter
{
    #region Public Properties

    /// <inheritdoc/>
    public Type TargetType => typeof(TimeSpan);

    #endregion Public Properties

    #region Public Methods

    /// <summary>
    /// Converts a 32-bit integer (milliseconds) from the OPC server into a .NET <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="opcValue">The object from the OPC server, expected to be a <see cref="int"/>.</param>
    /// <returns>The corresponding <see cref="TimeSpan"/>, or <c>null</c> if the input is null.</returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        return opcValue is null ? null : opcValue is int milliseconds ? TimeSpan.FromMilliseconds(milliseconds) : (object?)null;
    }

    /// <summary>
    /// Converts a .NET <see cref="TimeSpan"/> back into a 32-bit integer (milliseconds) for the OPC server.
    /// </summary>
    /// <param name="userValue">The <see cref="TimeSpan"/> from the user application.</param>
    /// <returns>An <see cref="int"/> representing the total duration in milliseconds, or <c>null</c> if the input is null.</returns>
    public object? ConvertToOpc(object? userValue)
    {
        if (userValue is null)
        {
            return null;
        }

        if (userValue is TimeSpan timeSpanValue)
        {
            try
            {
                return Convert.ToInt32(timeSpanValue.TotalMilliseconds);
            }
            catch (OverflowException)
            {
                return null;
            }
        }

        return null;
    }

    #endregion Public Methods
}