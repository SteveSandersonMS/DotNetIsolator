namespace DotNetIsolator;

public class IsolatedClass : IsolatedMember, IEquatable<IsolatedClass>
{
    internal IsolatedClass(IsolatedRuntime runtimeInstance, int monoClassPtr) : base(runtimeInstance, monoClassPtr)
    {

    }

    public bool Equals(IsolatedClass other)
    {
        return base.Equals(other);
    }

    public override bool Equals(IsolatedMember other)
    {
        return other is IsolatedClass && base.Equals(other);
    }

    public IsolatedObject CreateInstance()
    {
        return Runtime.CreateObject(_monoPtr);
    }

    public IsolatedMethod? GetMethod(string methodName, int numArgs = -1)
    {
        return Runtime.GetMethod(_monoPtr, methodName, numArgs);
    }

    public IsolatedMethod? GetMethod(string methodDesc, bool matchNamespace)
    {
        return Runtime.GetMethod(_monoPtr, methodDesc, matchNamespace);
    }

    public IsolatedClass? MakeGenericClass(params IsolatedClass[] genericArguments)
    {
        if (genericArguments.Length == 0)
        {
            return Runtime.MakeGenericClass(_monoPtr, Array.Empty<int>());
        }
        Span<int> argsPtr = stackalloc int[genericArguments.Length];
        for (int i = 0; i < genericArguments.Length; i++)
        {
            var arg = genericArguments[i];
            if (arg.Runtime != Runtime)
            {
                throw new ArgumentException("Generic arguments must all come from the same runtime.", nameof(genericArguments));
            }
            argsPtr[i] = arg._monoPtr;
        }
        return Runtime.MakeGenericClass(_monoPtr, argsPtr);
    }

    protected override IsolatedObject GetReflectionObject()
    {
        return Runtime.GetReflectionClass(_monoPtr);
    }
}
