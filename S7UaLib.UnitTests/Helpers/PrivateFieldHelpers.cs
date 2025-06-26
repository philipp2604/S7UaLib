using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.UnitTests.Helpers;
internal static class PrivateFieldHelpers
{
    public static void SetPrivateField(object obj, string fieldName, object value)
    {
        var field = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new ArgumentException($"Field '{fieldName}' not found in type '{obj.GetType().Name}'.");
        field.SetValue(obj, value);
    }

    public static void InvokePrivateMethod(object obj, string methodName, object[] parameters)
    {
        var method = obj.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new ArgumentException($"Method '{methodName}' not found in type '{obj.GetType().Name}'.");
        method.Invoke(obj, parameters);
    }

    public static object? GetPrivateField(object obj, string fieldName)
    {
        var field = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field == null
            ? throw new ArgumentException($"Field '{fieldName}' not found in type '{obj.GetType().Name}'.")
            : field.GetValue(obj);
    }
}
