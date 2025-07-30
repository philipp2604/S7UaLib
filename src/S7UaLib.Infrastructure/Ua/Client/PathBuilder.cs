using S7UaLib.Core.Ua;

namespace S7UaLib.Infrastructure.Ua.Client;

/// <summary>
/// Helper class for building hierarchical paths in the OPC UA address space.
/// </summary>
internal class PathBuilder
{
    private readonly string _rootContext;
    private readonly string _currentPath;

    public PathBuilder(string? rootContext = null)
    {
        _rootContext = rootContext ?? string.Empty;
        _currentPath = _rootContext;
    }

    private PathBuilder(string rootContext, string currentPath)
    {
        _rootContext = rootContext;
        _currentPath = currentPath;
    }

    /// <summary>
    /// Creates a child path builder for the specified segment.
    /// </summary>
    /// <param name="segment">The path segment to append.</param>
    /// <returns>A new PathBuilder with the appended segment.</returns>
    public PathBuilder Child(string? segment)
    {
        if (string.IsNullOrEmpty(segment))
            return this;

        var newPath = string.IsNullOrEmpty(_currentPath)
            ? segment
            : $"{_currentPath}.{segment}";

        return new PathBuilder(_rootContext, newPath);
    }

    /// <summary>
    /// Builds the initial path for an element, considering the root context.
    /// </summary>
    /// <param name="element">The element to build the path for.</param>
    /// <returns>The initial path string.</returns>
    public string BuildInitialPath(IUaNode element)
    {
        return string.IsNullOrEmpty(_rootContext)
            ? element.DisplayName ?? string.Empty
            : _rootContext.Equals(element.DisplayName, StringComparison.OrdinalIgnoreCase)
            ? _rootContext
            : $"{_rootContext}.{element.DisplayName}";
    }

    /// <summary>
    /// Gets the current path.
    /// </summary>
    public string CurrentPath => _currentPath;

    /// <summary>
    /// Implicit conversion to string for convenience.
    /// </summary>
    public static implicit operator string(PathBuilder builder) => builder._currentPath;
}