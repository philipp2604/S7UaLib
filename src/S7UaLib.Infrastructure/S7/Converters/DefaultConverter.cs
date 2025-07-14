using S7UaLib.Core.S7.Converters;

namespace S7UaLib.Infrastructure.S7.Converters;

/// <summary>
/// A default pass-through converter that performs no conversion.
/// It simply returns the provided value as it is.
/// </summary>
public class DefaultConverter : IS7TypeConverter
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultConverter"/> class.
    /// </summary>
    /// <param name="targetType">The target type this converter will handle.</param>
    public DefaultConverter(Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        TargetType = targetType;
    }

    #endregion Constructors

    #region Public Properties

    /// <inheritdoc cref="IS7TypeConverter.TargetType"/>
    public Type TargetType { get; }

    #endregion Public Properties

    #region Public Methods

    /// <inheritdoc cref="IS7TypeConverter.ConvertFromOpc"/>
    public object? ConvertFromOpc(object? opcValue)
    {
        return opcValue;
    }

    /// <inheritdoc cref="IS7TypeConverter.ConvertToOpc(object?)"/>
    public object? ConvertToOpc(object? userValue)
    {
        return userValue;
    }

    #endregion Public Methods
}