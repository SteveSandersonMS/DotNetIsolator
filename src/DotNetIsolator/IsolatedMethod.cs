using MessagePack;
using System.Buffers;

namespace DotNetIsolator;

public class IsolatedMethod : IEquatable<IsolatedMethod>
{
    private readonly IsolatedRuntime _runtimeInstance;
    private readonly int _monoMethodPtr;

    internal IsolatedMethod(IsolatedRuntime runtimeInstance, int monoMethodPtr)
    {
        _runtimeInstance = runtimeInstance;
        _monoMethodPtr = monoMethodPtr;
    }

    public bool Equals(IsolatedMethod other)
    {
        return
            _runtimeInstance == other._runtimeInstance &&
            _monoMethodPtr == other._monoMethodPtr;
    }

    public override bool Equals(object obj)
    {
        return obj is IsolatedMethod method && Equals(method);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(_runtimeInstance, _monoMethodPtr);
    }

    private int Serialize<T>(T value, IsolatedAllocator allocator)
    {
        // We might also want to special-case some basic known parameter types and skip MessagePack
        // for them, instead using ShadowStack and the raw bytes
        MessagePackSerializer.Typeless.Serialize(allocator, value);
        return allocator.Release();
    }

    public TRes Invoke<TRes>(IsolatedObject? instance)
        => _runtimeInstance.InvokeMethod<TRes>(_monoMethodPtr, instance, Span<int>.Empty);

    public TRes Invoke<T0, TRes>(IsolatedObject? instance, T0 param0)
    {
        Span<int> argAddresses = stackalloc int[1];
        using var allocator = _runtimeInstance.GetAllocator();
        argAddresses[0] = Serialize(param0, allocator);

        try
        {
            return _runtimeInstance.InvokeMethod<TRes>(_monoMethodPtr, instance, argAddresses);
        }
        finally
        {
            _runtimeInstance.Free(argAddresses[0]);
        }
    }

    public TRes Invoke<T0, T1, TRes>(IsolatedObject? instance, T0 param0, T1 param1)
    {
        Span<int> argAddresses = stackalloc int[2];
        using var allocator = _runtimeInstance.GetAllocator();
        argAddresses[0] = Serialize(param0, allocator);
        argAddresses[1] = Serialize(param1, allocator);

        try
        {
            return _runtimeInstance.InvokeMethod<TRes>(_monoMethodPtr, instance, argAddresses);
        }
        finally
        {
            _runtimeInstance.Free(argAddresses[0]);
            _runtimeInstance.Free(argAddresses[1]);
        }
    }

    public TRes Invoke<T0, T1, T2, TRes>(IsolatedObject? instance, T0 param0, T1 param1, T2 param2)
    {
        Span<int> argAddresses = stackalloc int[3];
        using var allocator = _runtimeInstance.GetAllocator();
        argAddresses[0] = Serialize(param0, allocator);
        argAddresses[1] = Serialize(param1, allocator);
        argAddresses[2] = Serialize(param2, allocator);

        try
        {
            return _runtimeInstance.InvokeMethod<TRes>(_monoMethodPtr, instance, argAddresses);
        }
        finally
        {
            _runtimeInstance.Free(argAddresses[0]);
            _runtimeInstance.Free(argAddresses[1]);
            _runtimeInstance.Free(argAddresses[2]);
        }
    }

    public TRes Invoke<T0, T1, T2, T3, TRes>(IsolatedObject? instance, T0 param0, T1 param1, T2 param2, T3 param3)
    {
        Span<int> argAddresses = stackalloc int[4];
        using var allocator = _runtimeInstance.GetAllocator();
        argAddresses[0] = Serialize(param0, allocator);
        argAddresses[1] = Serialize(param1, allocator);
        argAddresses[2] = Serialize(param2, allocator);
        argAddresses[3] = Serialize(param3, allocator);

        try
        {
            return _runtimeInstance.InvokeMethod<TRes>(_monoMethodPtr, instance, argAddresses);
        }
        finally
        {
            _runtimeInstance.Free(argAddresses[0]);
            _runtimeInstance.Free(argAddresses[1]);
            _runtimeInstance.Free(argAddresses[2]);
            _runtimeInstance.Free(argAddresses[3]);
        }
    }

    public TRes Invoke<T0, T1, T2, T3, T4, TRes>(IsolatedObject? instance, T0 param0, T1 param1, T2 param2, T3 param3, T4 param4)
    {
        Span<int> argAddresses = stackalloc int[5];
        using var allocator = _runtimeInstance.GetAllocator();
        argAddresses[0] = Serialize(param0, allocator);
        argAddresses[1] = Serialize(param1, allocator);
        argAddresses[2] = Serialize(param2, allocator);
        argAddresses[3] = Serialize(param3, allocator);
        argAddresses[4] = Serialize(param4, allocator);

        try
        {
            return _runtimeInstance.InvokeMethod<TRes>(_monoMethodPtr, instance, argAddresses);
        }
        finally
        {
            _runtimeInstance.Free(argAddresses[0]);
            _runtimeInstance.Free(argAddresses[1]);
            _runtimeInstance.Free(argAddresses[2]);
            _runtimeInstance.Free(argAddresses[3]);
            _runtimeInstance.Free(argAddresses[4]);
        }
    }

    public TRes Invoke<TRes>(IsolatedObject? instance, params object[] args)
    {
        return Invoke<TRes>(instance, args.AsSpan());
    }

    public TRes Invoke<TRes>(IsolatedObject? instance, Span<object> args)
    {
        Span<int> argAddresses = stackalloc int[args.Length];
        using var allocator = _runtimeInstance.GetAllocator();
        for (int i = 0; i < args.Length; i++)
        {
            argAddresses[i] = Serialize(args[i], allocator);
        }
        try
        {
            return _runtimeInstance.InvokeMethod<TRes>(_monoMethodPtr, instance, argAddresses);
        }
        finally
        {
            for (int i = 0; i < args.Length; i++)
            {
                _runtimeInstance.Free(argAddresses[i]);
            }
        }
    }

    public void InvokeVoid(IsolatedObject? instance)
        => Invoke<object>(instance);

    public void InvokeVoid<T0>(IsolatedObject? instance, T0 param0)
        => Invoke<T0, object>(instance, param0);

    public void InvokeVoid<T0, T1>(IsolatedObject? instance, T0 param0, T1 param1)
        => Invoke<T0, T1, object>(instance, param0, param1);

    public void InvokeVoid<T0, T1, T2>(IsolatedObject? instance, T0 param0, T1 param1, T2 param2)
        => Invoke<T0, T1, T2, object>(instance, param0, param1, param2);

    public void InvokeVoid<T0, T1, T2, T3>(IsolatedObject? instance, T0 param0, T1 param1, T2 param2, T3 param3)
        => Invoke<T0, T1, T2, T3, object>(instance, param0, param1, param2, param3);

    public void InvokeVoid<T0, T1, T2, T3, T4>(IsolatedObject? instance, T0 param0, T1 param1, T2 param2, T3 param3, T4 param4)
        => Invoke<T0, T1, T2, T3, T4, object>(instance, param0, param1, param2, param3, param4);
}
