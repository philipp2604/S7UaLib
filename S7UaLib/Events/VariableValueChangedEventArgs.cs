using Opc.Ua;
using S7UaLib.S7.Structure.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.Events;

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
    /// <summary>
    /// Gets the variable state before the change.
    /// </summary>
    public IS7Variable OldVariable { get; } = oldVariable;

    /// <summary>
    /// Gets the variable state after the change.
    /// </summary>
    public IS7Variable NewVariable { get; } = newVariable;
}