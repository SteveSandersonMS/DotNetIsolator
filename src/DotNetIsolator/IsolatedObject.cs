namespace DotNetIsolator;

public class IsolatedObject : IDisposable
{
    private readonly IsolatedRuntime _runtimeInstance;
    private readonly string _assemblyName;
    private readonly string? _namespace;
    private readonly string? _declaringTypeName;
    private readonly string _typeName;

    internal IsolatedObject(IsolatedRuntime runtimeInstance, int gcHandle, string assemblyName, string? @namespace, string? declaringTypeName, string typeName)
    {
        _runtimeInstance = runtimeInstance;
        GuestGCHandle = gcHandle;
        _assemblyName = assemblyName;
        _namespace = @namespace;
        _declaringTypeName = declaringTypeName;
        _typeName = typeName;
    }

    internal int GuestGCHandle { get; private set; }

    public IsolatedMethod FindMethod(string methodName, int numArgs = -1)
    {
        if (GuestGCHandle == 0)
        {
            throw new InvalidOperationException("Cannot look up instance method because the object has already been released.");
        }

        return _runtimeInstance.GetMethod(_assemblyName, _namespace, _declaringTypeName, _typeName, methodName);
    }

    public TRes Invoke<TRes>(string methodName)
        => FindMethod(methodName, 0).Invoke<TRes>(this);

    public TRes Invoke<T0, TRes>(string methodName, T0 param0)
        => FindMethod(methodName, 1).Invoke<T0, TRes>(this, param0);

    public TRes Invoke<T0, T1, TRes>(string methodName, T0 param0, T1 param1)
        => FindMethod(methodName, 2).Invoke<T0, T1, TRes>(this, param0, param1);

    public TRes Invoke<T0, T1, T2, TRes>(string methodName, T0 param0, T1 param1, T2 param2)
        => FindMethod(methodName, 3).Invoke<T0, T1, T2, TRes>(this, param0, param1, param2);

    public TRes Invoke<T0, T1, T2, T3, TRes>(string methodName, T0 param0, T1 param1, T2 param2, T3 param3)
        => FindMethod(methodName, 4).Invoke<T0, T1, T2, T3, TRes>(this, param0, param1, param2, param3);

    public TRes Invoke<T0, T1, T2, T3, T4, TRes>(string methodName, T0 param0, T1 param1, T2 param2, T3 param3, T4 param4)
        => FindMethod(methodName, 5).Invoke<T0, T1, T2, T3, T4, TRes>(this, param0, param1, param2, param3, param4);

    public void InvokeVoid(string methodName)
        => FindMethod(methodName, 0).InvokeVoid(this);

    public void InvokeVoid<T0>(string methodName, T0 param0)
        => FindMethod(methodName, 1).InvokeVoid(this, param0);

    public void InvokeVoid<T0, T1>(string methodName, T0 param0, T1 param1)
        => FindMethod(methodName, 2).InvokeVoid(this, param0, param1);

    public void InvokeVoid<T0, T1, T2>(string methodName, T0 param0, T1 param1, T2 param2)
        => FindMethod(methodName, 3).InvokeVoid(this, param0, param1, param2);

    public void InvokeVoid<T0, T1, T2, T3>(string methodName, T0 param0, T1 param1, T2 param2, T3 param3)
        => FindMethod(methodName, 4).InvokeVoid(this, param0, param1, param2, param3);

    public void InvokeVoid<T0, T1, T2, T3, T4>(string methodName, T0 param0, T1 param1, T2 param2, T3 param3, T4 param4)
        => FindMethod(methodName, 5).InvokeVoid(this, param0, param1, param2, param3, param4);

    protected virtual void Dispose(bool disposing)
    {
        if (GuestGCHandle != 0)
        {
            _runtimeInstance.ReleaseGCHandle(GuestGCHandle);
            GuestGCHandle = 0;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~IsolatedObject()
    {
        Dispose(false);
    }
}
