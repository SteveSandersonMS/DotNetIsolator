namespace DotNetIsolator;

public class IsolatedObject : IDisposable, IIsolatedGCHandle, IEquatable<IsolatedObject>
{
    private readonly IsolatedRuntime _runtimeInstance;
    private readonly int _monoClassPtr;

    IsolatedClass? _classCached;

    public IsolatedClass Class => _classCached ??= new(_runtimeInstance, _monoClassPtr);

    internal IsolatedObject(IsolatedRuntime runtimeInstance, int gcHandle, int monoClassPtr)
    {
        _runtimeInstance = runtimeInstance;
        GuestGCHandle = gcHandle;
        _monoClassPtr = monoClassPtr;
    }

    internal int GuestGCHandle { get; private set; }

    public static explicit operator IsolatedMember(IsolatedObject obj)
    {
        return obj._runtimeInstance.GetReflectedMember(obj.GuestGCHandle);
    }

    public override string ToString()
    {
        return _runtimeInstance.ToStringMethod.Invoke<string>(this);
    }

    public int? GetGCHandle(IsolatedRuntime runtime)
    {
        return runtime == _runtimeInstance ? GuestGCHandle : null;
    }

    private IsolatedMethod FindMethod(string methodName, int numArgs = -1)
    {
        if (GuestGCHandle == 0)
        {
            throw new InvalidOperationException("Cannot look up instance method because the object has already been released.");
        }

        return _runtimeInstance.GetMethod(_monoClassPtr, methodName, numArgs)
            ?? throw new ArgumentException($"Cannot find method {methodName} on the class.");
    }

    public bool ValueEquals(IsolatedObject other)
    {
        return _runtimeInstance == other._runtimeInstance
            && _runtimeInstance.EqualsMethod.Invoke<IsolatedObject, IsolatedObject, bool>(null, this, other);
    }

    public bool Equals(IsolatedObject other)
    {
        return _runtimeInstance == other._runtimeInstance
            && _runtimeInstance.ReferenceEqualsMethod.Invoke<IsolatedObject, IsolatedObject, bool>(null, this, other);
    }

    public override bool Equals(object obj)
    {
        return obj is IsolatedObject other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_runtimeInstance, _runtimeInstance.GetHashCode(GuestGCHandle));
    }

    #region Invoke overloads

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

    #endregion

    public T Deserialize<T>()
    {
        return _runtimeInstance.InvokeMethod<T>(0, this, default);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (GuestGCHandle != 0)
        {
            _runtimeInstance.ReleaseGCHandle(GuestGCHandle);
            GuestGCHandle = 0;
        }
        if (disposing)
        {
            if (_classCached != null)
            {
                _classCached.Dispose();
                _classCached = null;
            }
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
