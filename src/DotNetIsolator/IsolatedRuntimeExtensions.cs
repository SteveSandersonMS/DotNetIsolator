namespace DotNetIsolator;

public static class IsolatedRuntimeExtensions
{
    public static IsolatedClass GetClass(this IsolatedRuntime runtime, Type type)
    {
        return runtime.GetClass(type.Assembly.GetName().Name!, type.Namespace, type.DeclaringType?.Name, type.Name);
    }

    public static IsolatedClass GetClass<T>(this IsolatedRuntime runtime)
    {
        return runtime.GetClass(typeof(T));
    }

    public static IsolatedMethod GetMethod(this IsolatedRuntime runtime, Type type, string methodName)
    {
        return runtime.GetClass(type).GetMethod(methodName);
    }

    public static IsolatedMethod GetMethod(this IsolatedRuntime runtime, Type type, string methodName, int numArgs)
    {
        return runtime.GetClass(type).GetMethod(methodName, numArgs);
    }

    public static IsolatedMethod GetMethod(this IsolatedRuntime runtime, string assemblyName, string? @namespace, string? declaringTypeName, string typeName, string methodName, int numArgs = -1)
    {
        return runtime.GetClass(assemblyName, @namespace, declaringTypeName, typeName).GetMethod(methodName, numArgs);
    }

    public static IsolatedObject CreateObject(this IsolatedRuntime runtime, string assemblyName, string? @namespace, string className)
    {
        return runtime.GetClass(assemblyName, @namespace, declaringTypeName: null, className).CreateInstance();
    }

    public static IsolatedObject CreateObject(this IsolatedRuntime runtime, string assemblyName, string? @namespace, string? declaringTypeName, string className)
    {
        return runtime.GetClass(assemblyName, @namespace, declaringTypeName, className).CreateInstance();
    }

    public static IsolatedObject CreateObject(this IsolatedRuntime runtime, Type type)
    {
        return runtime.GetClass(type).CreateInstance();
    }

    public static IsolatedObject CreateObject<T>(this IsolatedRuntime runtime)
    {
        return runtime.GetClass<T>().CreateInstance();
    }
}
