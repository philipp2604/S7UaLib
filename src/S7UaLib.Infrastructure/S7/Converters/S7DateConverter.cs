using S7UaLib.Core.S7.Converters;

namespace S7UaLib.Infrastructure.S7.Converters;

/// <summary>
/// Handles the conversion between the S7 DATE format and the standard .NET <see cref="DateTime"/>.
/// The S7 DATE type is a 16-bit unsigned integer representing the number of days since the epoch date of 1990-01-01.
/// The time component of a <see cref="DateTime"/> object is ignored during conversion to OPC and will be midnight (00:00:00) when converting from OPC.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="S7DateConverter"/> class.
/// </remarks>
public class S7DateConverter : IS7TypeConverter
{
    #region Private Fields

    private readonly DateTime _s7EpochDate = new(1990, 1, 1);
    private readonly DateTime _s7MaxDate = new(2099, 12, 31);

    #endregion Private Fields

    #region Public Properties

    /// <inheritdoc/>
    public Type TargetType => typeof(DateTime);

    #endregion Public Properties

    #region Public Methods

    /// <summary>
    /// Converts a ushort value from the OPC server (days since 1990-01-01) into a .NET <see cref="DateTime"/>.
    /// </summary>
    /// <param name="opcValue">The object from the OPC server, expected to be a <see cref="ushort"/>.</param>
    /// <returns>The corresponding <see cref="DateTime"/>, or <c>null</c> if the input is null.</returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        return opcValue is null ? null : opcValue is ushort daysSinceEpoch ? _s7EpochDate.AddDays(daysSinceEpoch) : (object?)null;
    }

    /// <summary>
    /// Converts a .NET <see cref="DateTime"/> back into a ushort (days since 1990-01-01) for the OPC server.
    /// </summary>
    /// <param name="userValue">The <see cref="DateTime"/> from the user application. The time component is ignored.</param>
    /// <returns>A <see cref="ushort"/> representing the number of days since the S7 epoch, or <c>null</c> if the input is null.</returns>
    public object? ConvertToOpc(object? userValue)
    {
        if (userValue is null)
        {
            return null;
        }

        if (userValue is DateTime dateTimeValue)
        {
            if (dateTimeValue.Date < _s7EpochDate || dateTimeValue.Date > _s7MaxDate)
            {
                return null;
            }

            TimeSpan difference = dateTimeValue.Date - _s7EpochDate;
            return (ushort)difference.TotalDays;
        }

        return null;
    }

    #endregion Public Methods
}