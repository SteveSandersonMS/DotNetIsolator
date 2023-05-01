namespace DotNetIsolator;

public class IsolatedClass : IsolatedMember, IEquatable<IsolatedClass>
{
    internal readonly int _monoClassPtr;

    internal IsolatedClass(IsolatedRuntime runtimeInstance, int monoClassPtr) : base(runtimeInstance)
    {
        _monoClassPtr = monoClassPtr;
    }

    public bool Equals(IsolatedClass other)
    {
        return
            _runtimeInstance == other._runtimeInstance &&
            _monoClassPtr == other._monoClassPtr;
    }

    public override bool Equals(object obj)
    {
        return obj is IsolatedClass cls && Equals(cls);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_runtimeInstance, _monoClassPtr);
    }

    public IsolatedObject CreateInstance()
    {
        return _runtimeInstance.CreateObject(_monoClassPtr);
    }

    public IsolatedMethod? GetMethod(string methodName, int numArgs = -1)
    {
        return _runtimeInstance.GetMethod(_monoClassPtr, methodName, numArgs);
    }

    public IsolatedMethod? GetMethod(string methodDesc, bool matchNamespace)
    {
        return _runtimeInstance.GetMethod(_monoClassPtr, methodDesc, matchNamespace);
    }

    public IsolatedClass? MakeGenericClass(params IsolatedClass[] genericArguments)
    {
        if (genericArguments.Length == 0)
        {
            return _runtimeInstance.MakeGenericClass(_monoClassPtr, Array.Empty<int>());
        }
        Span<int> argsPtr = stackalloc int[genericArguments.Length];
        for (int i = 0; i < genericArguments.Length; i++)
        {
            var arg = genericArguments[i];
            if (arg._runtimeInstance != _runtimeInstance)
            {
                throw new ArgumentException("Generic arguments must all come from the same runtime.", nameof(genericArguments));
            }
            argsPtr[i] = arg._monoClassPtr;
        }
        return _runtimeInstance.MakeGenericClass(_monoClassPtr, argsPtr);
    }

    protected override IsolatedObject GetReflectionObject()
    {
        return _runtimeInstance.GetReflectionClass(_monoClassPtr);
    }
}
