using Microsoft.Extensions.Logging;
using S7UaLib.S7.Converters.Contracts;

namespace S7UaLib.S7.Converters;

/// <summary>
/// A default pass-through converter that performs no conversion.
/// It simply returns the provided value as it is.
/// </summary>
public class DefaultConverter : IS7TypeConverter
{
    private readonly ILogger? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultConverter"/> class.
    /// </summary>
    /// <param name="targetType">The target type this converter will handle.</param>
    /// <param name="logger">An optional logger for diagnostics.</param>
    public DefaultConverter(Type targetType, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(targetType);

        _logger = logger;
        TargetType = targetType;
    }

    /// <inheritdoc cref="IS7TypeConverter.TargetType"/>
    public Type TargetType { get; }

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
}