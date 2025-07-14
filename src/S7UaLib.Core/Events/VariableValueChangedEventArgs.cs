using S7UaLib.Core.S7.Structure;

namespace S7UaLib.Core.Events;

/// <summary>
/// Provides data for the VariableValueChanged event.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="VariableValueChangedEventArgs"/> class.
/// </remarks>
/// <param name="oldVariable">The variable state before the change.</param>
/// <param name="newVariable">The variable state after the change.</param>
public class VariableValueChangedEventArgs(IS7Variable oldVariable, IS7Variable newVariable) : EventArgs
{
    #region Public Properties

    /// <summary>
    /// Gets the variable state before the change.
    /// </summary>
    public IS7Variable OldVariable { get; } = oldVariable;

    /// <summary>
    /// Gets the variable state after the change.
    /// </summary>
    public IS7Variable NewVariable { get; } = newVariable;

    #endregion Public Properties
}