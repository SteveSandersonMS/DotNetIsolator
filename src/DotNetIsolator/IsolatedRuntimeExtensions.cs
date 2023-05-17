using System.Reflection;

namespace DotNetIsolator;

public static class IsolatedRuntimeExtensions
{
    public static IsolatedClass? GetClass(this IsolatedRuntime runtime, Type type)
    {
        if(type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            var typeArgs = type.GetGenericArguments().Select(arg => runtime.GetClass(arg)).ToArray();
            return runtime.GetClass(type.GetGenericTypeDefinition())?.MakeGenericClass(typeArgs!);
        }
        return runtime.GetClass(type.Assembly.GetName().Name!, type.Namespace, type.DeclaringType?.Name, type.Name);
    }

    public static IsolatedClass? GetClass<T>(this IsolatedRuntime runtime)
    {
        return runtime.GetClass(typeof(T));
    }

    public static IsolatedMethod? GetMethod(this IsolatedRuntime runtime, MethodInfo method)
    {
        if(method.IsGenericMethod && !method.IsGenericMethodDefinition)
        {
            var typeArgs = method.GetGenericArguments().Select(arg => runtime.GetClass(arg)).ToArray();
            return runtime.GetMethod(method.GetGenericMethodDefinition())?.MakeGenericMethod(typeArgs!);
        }
        return runtime.GetClass(method.DeclaringType)?.GetMethod(method.Name, method.GetParameters().Length);
    }

    public static IsolatedMethod? GetMethod(this IsolatedRuntime runtime, Type type, string methodName)
    {
        return runtime.GetClass(type)?.GetMethod(methodName);
    }

    public static IsolatedMethod? GetMethod(this IsolatedRuntime runtime, Type type, string methodName, int numArgs)
    {
        return runtime.GetClass(type)?.GetMethod(methodName, numArgs);
    }

    public static IsolatedMethod? GetMethod(this IsolatedRuntime runtime, string assemblyName, string? @namespace, string? declaringTypeName, string typeName, string methodName, int numArgs = -1)
    {
        return runtime.GetClass(assemblyName, @namespace, declaringTypeName, typeName)?.GetMethod(methodName, numArgs);
    }

    public static IsolatedObject? CreateObject(this IsolatedRuntime runtime, string assemblyName, string? @namespace, string className)
    {
        return runtime.GetClass(assemblyName, @namespace, declaringTypeName: null, className)?.CreateInstance();
    }

    public static IsolatedObject? CreateObject(this IsolatedRuntime runtime, string assemblyName, string? @namespace, string? declaringTypeName, string className)
    {
        return runtime.GetClass(assemblyName, @namespace, declaringTypeName, className)?.CreateInstance();
    }

    public static IsolatedObject? CreateObject(this IsolatedRuntime runtime, Type type)
    {
        return runtime.GetClass(type)?.CreateInstance();
    }

    public static IsolatedObject? CreateObject<T>(this IsolatedRuntime runtime)
    {
        return runtime.GetClass(typeof(T))?.CreateInstance();
    }
}
