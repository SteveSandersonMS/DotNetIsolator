namespace DotNetIsolator;

public class IsolatedClass : IsolatedMember, IEquatable<IsolatedClass>
{
    private readonly IsolatedRuntime _runtimeInstance;
    private readonly int _monoClassPtr;

    internal IsolatedClass(IsolatedRuntime runtimeInstance, int monoClassPtr)
    {
        _runtimeInstance = runtimeInstance;
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

    protected override IsolatedObject GetReflectionObject()
    {
        return _runtimeInstance.GetReflectionClass(_monoClassPtr);
    }
}
