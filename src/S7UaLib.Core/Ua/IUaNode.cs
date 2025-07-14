namespace S7UaLib.Core.Ua;

/// <summary>
/// Defines a common interface for UA elements that have a NodeId and DisplayName.
/// </summary>
public interface IUaNode
{
    #region Public Properties

    /// <summary>
    /// Gets the unique identifier of the node.
    /// </summary>
    public string? NodeId { get; }

    /// <summary>
    /// Gets the display name of the node.
    /// </summary>
    public string? DisplayName { get; }

    #endregion Public Properties
}