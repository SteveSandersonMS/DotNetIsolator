namespace DotNetIsolator;

public class IsolatedClass
{
    private readonly IsolatedRuntime _runtimeInstance;
    private readonly int _monoClassPtr;

    internal IsolatedClass(IsolatedRuntime runtimeInstance, int monoClassPtr)
    {
        _runtimeInstance = runtimeInstance;
        _monoClassPtr = monoClassPtr;
    }

    public IsolatedObject CreateInstance()
    {
        return _runtimeInstance.CreateObject(_monoClassPtr);
    }

    public IsolatedMethod? GetMethod(string methodName, int numArgs = -1)
    {
        return _runtimeInstance.GetMethod(_monoClassPtr, methodName, numArgs);
    }
}
