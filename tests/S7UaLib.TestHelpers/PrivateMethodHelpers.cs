using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.TestHelpers;

/// <summary>
/// Provides helper methods for invoking private methods on objects for testing purposes.
/// </summary>
public static class PrivateMethodHelpers
{
    /// <summary>
    /// Invokes a private instance method on an object.
    /// </summary>
    /// <param name="obj">The object instance.</param>
    /// <param name="methodName">The name of the private method to invoke.</param>
    /// <param name="parameters">An array of arguments to pass to the method.</param>
    /// <returns>The return value of the invoked method, or null if the method has no return value.</returns>
    /// <exception cref="ArgumentException">Thrown if the specified method is not found in the object's type or its base classes.</exception>
    public static object? InvokePrivateMethod(object obj, string methodName, object[] parameters)
    {
        var type = obj.GetType();
        MethodInfo? method = null;

        // Search the current type and all base types for the private method.
        while (type != null)
        {
            method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null)
                break;
            type = type.BaseType;
        }

        return method == null
            ? throw new ArgumentException($"Method '{methodName}' not found in type '{obj.GetType().Name}' or its base classes.")
            : method.Invoke(obj, parameters);
    }
}