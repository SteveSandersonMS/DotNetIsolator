namespace DotNetIsolator;

public class IsolatedObject : IDisposable
{
    private readonly IsolatedRuntime _runtimeInstance;
    private readonly int _monoClass;

    internal IsolatedObject(IsolatedRuntime runtimeInstance, int gcHandle, int monoClass)
    {
        _runtimeInstance = runtimeInstance;
        GuestGCHandle = gcHandle;
        _monoClass = monoClass;
    }

    internal int GuestGCHandle { get; private set; }

    public IsolatedMethod FindMethod(string methodName, int numArgs = -1)
    {
        if (GuestGCHandle == 0)
        {
            throw new InvalidOperationException("Cannot look up instance method because the object has already been released.");
        }

        return _runtimeInstance.GetMethod(_monoClass, methodName);
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

    public TRes Invoke<TRes>(string methodName, params object[] args)
        => FindMethod(methodName, args.Length).Invoke<TRes>(this, args);

    public TRes Invoke<TRes>(string methodName, Span<object> args)
        => FindMethod(methodName, args.Length).Invoke<TRes>(this, args);

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

    public T Deserialize<T>()
    {
        return _runtimeInstance.InvokeDotNetMethod<T>(0, this, default);
    }

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
