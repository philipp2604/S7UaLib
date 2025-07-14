using System.Reflection;

namespace S7UaLib.TestHelpers;

public static class PrivateFieldHelpers
{
    public static void SetPrivateField(object obj, string fieldName, object value)
    {
        var type = obj.GetType();
        FieldInfo? field = null;

        while (type != null)
        {
            field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
                break;
            type = type.BaseType;
        }

        if (field == null)
            throw new ArgumentException($"Field '{fieldName}' not found in type '{obj.GetType().Name}' or its base classes.");

        field.SetValue(obj, value);
    }

    public static object? GetPrivateField(object obj, string fieldName)
    {
        var type = obj.GetType();
        FieldInfo? field = null;

        while (type != null)
        {
            field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
                break;
            type = type.BaseType;
        }

        return field == null
            ? throw new ArgumentException($"Field '{fieldName}' not found in type '{obj.GetType().Name}' or its base classes.")
            : field.GetValue(obj);
    }

    public static object? InvokePrivateMethod(object obj, string methodName, object[] parameters)
    {
        var type = obj.GetType();
        MethodInfo? method = null;

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