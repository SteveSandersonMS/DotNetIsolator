using MessagePack;

namespace DotNetIsolator;

public class IsolatedMethod : IsolatedMember, IEquatable<IsolatedMethod>
{
    internal IsolatedMethod(IsolatedRuntime runtimeInstance, int monoMethodPtr) : base(runtimeInstance, monoMethodPtr)
    {

    }

    public bool Equals(IsolatedMethod other)
    {
        return base.Equals(other);
    }

    public override bool Equals(IsolatedMember other)
    {
        return other is IsolatedMethod && base.Equals(other);
    }

    public IsolatedMethod? MakeGenericMethod(params IsolatedClass[] genericArguments)
    {
        if(genericArguments.Length == 0)
        {
            return Runtime.MakeGenericMethod(_monoPtr, Array.Empty<int>());
        }
        Span<int> argsPtr = stackalloc int[genericArguments.Length];
        for(int i = 0; i < genericArguments.Length; i++)
        {
            var arg = genericArguments[i];
            if(arg.Runtime != Runtime)
            {
                throw new ArgumentException("Generic arguments must all come from the same runtime.", nameof(genericArguments));
            }
            argsPtr[i] = arg._monoPtr;
        }
        return Runtime.MakeGenericMethod(_monoPtr, argsPtr);
    }

    private int Serialize<T>(T value, IsolatedAllocator allocator)
    {
        if (value is IIsolatedGCHandle handle && handle.GetGCHandle(Runtime) is int gcHandle)
        {
            // Special size 0 case just with handle
            var memory = Runtime.Alloc(2 * sizeof(int));
            Runtime.WriteInt32(memory, 0);
            Runtime.WriteInt32(memory + sizeof(int), gcHandle);
            return memory;
        }
        // We might also want to special-case some basic known parameter types and skip MessagePack
        // for them, instead using ShadowStack and the raw bytes
        MessagePackSerializer.Typeless.Serialize(allocator, value);
        return allocator.Release();
    }

    protected override IsolatedObject GetReflectionObject()
    {
        return Runtime.GetReflectionMethod(_monoPtr);
    }

    #region Invoke overloads
    private TRes Invoke<TRes>(IsolatedObject? instance, Span<int> argAddresses)
    {
        return Runtime.InvokeMethod<TRes>(_monoPtr, instance, argAddresses);
    }

    private TRes Invoke<T0, TRes>(IsolatedObject? instance, T0 param0, Span<int> argAddresses, IsolatedAllocator allocator)
    {
        argAddresses[0] = Serialize(param0, allocator);

        try
        {
            return Runtime.InvokeMethod<TRes>(_monoPtr, instance, argAddresses);
        }
        finally
        {
            Runtime.Free(argAddresses[0]);
        }
    }

    private TRes Invoke<T0, T1, TRes>(IsolatedObject? instance, T0 param0, T1 param1, Span<int> argAddresses, IsolatedAllocator allocator)
    {
        argAddresses[1] = Serialize(param1, allocator);

        try
        {
            return Invoke<T0, TRes>(instance, param0, argAddresses, allocator);
        }
        finally
        {
            Runtime.Free(argAddresses[1]);
        }
    }

    private TRes Invoke<T0, T1, T2, TRes>(IsolatedObject? instance, T0 param0, T1 param1, T2 param2, Span<int> argAddresses, IsolatedAllocator allocator)
    {
        argAddresses[2] = Serialize(param2, allocator);

        try
        {
            return Invoke<T0, T1, TRes>(instance, param0, param1, argAddresses, allocator);
        }
        finally
        {
            Runtime.Free(argAddresses[2]);
        }
    }

    private TRes Invoke<T0, T1, T2, T3, TRes>(IsolatedObject? instance, T0 param0, T1 param1, T2 param2, T3 param3, Span<int> argAddresses, IsolatedAllocator allocator)
    {
        argAddresses[3] = Serialize(param3, allocator);

        try
        {
            return Invoke<T0, T1, T2, TRes>(instance, param0, param1, param2, argAddresses, allocator);
        }
        finally
        {
            Runtime.Free(argAddresses[3]);
        }
    }

    private TRes Invoke<T0, T1, T2, T3, T4, TRes>(IsolatedObject? instance, T0 param0, T1 param1, T2 param2, T3 param3, T4 param4, Span<int> argAddresses, IsolatedAllocator allocator)
    {
        argAddresses[4] = Serialize(param4, allocator);

        try
        {
            return Invoke<T0, T1, T2, T3, TRes>(instance, param0, param1, param2, param3, argAddresses, allocator);
        }
        finally
        {
            Runtime.Free(argAddresses[4]);
        }
    }

    public TRes Invoke<TRes>(IsolatedObject? instance)
    {
        return Runtime.InvokeMethod<TRes>(_monoPtr, instance, ReadOnlySpan<int>.Empty);
    }

    public TRes Invoke<T0, TRes>(IsolatedObject? instance, T0 param0)
    {
        Span<int> argAddresses = stackalloc int[1];
        using var allocator = Runtime.GetAllocator();
        return Invoke<T0, TRes>(instance, param0, argAddresses, allocator);
    }

    public TRes Invoke<T0, T1, TRes>(IsolatedObject? instance, T0 param0, T1 param1)
    {
        Span<int> argAddresses = stackalloc int[2];
        using var allocator = Runtime.GetAllocator();
        return Invoke<T0, T1, TRes>(instance, param0, param1, argAddresses, allocator);
    }

    public TRes Invoke<T0, T1, T2, TRes>(IsolatedObject? instance, T0 param0, T1 param1, T2 param2)
    {
        Span<int> argAddresses = stackalloc int[3];
        using var allocator = Runtime.GetAllocator();
        return Invoke<T0, T1, T2, TRes>(instance, param0, param1, param2, argAddresses, allocator);
    }

    public TRes Invoke<T0, T1, T2, T3, TRes>(IsolatedObject? instance, T0 param0, T1 param1, T2 param2, T3 param3)
    {
        Span<int> argAddresses = stackalloc int[4];
        using var allocator = Runtime.GetAllocator();
        return Invoke<T0, T1, T2, T3, TRes>(instance, param0, param1, param2, param3, argAddresses, allocator);
    }

    public TRes Invoke<T0, T1, T2, T3, T4, TRes>(IsolatedObject? instance, T0 param0, T1 param1, T2 param2, T3 param3, T4 param4)
    {
        Span<int> argAddresses = stackalloc int[5];
        using var allocator = Runtime.GetAllocator();
        return Invoke<T0, T1, T2, T3, T4, TRes>(instance, param0, param1, param2, param3, param4, argAddresses, allocator);
    }

    public TRes Invoke<TRes>(IsolatedObject? instance, params object[] args)
    {
        return Invoke<TRes>(instance, args.AsSpan());
    }

    public TRes Invoke<TRes>(IsolatedObject? instance, Span<object> args)
    {
        Span<int> argAddresses = stackalloc int[args.Length];
        using var allocator = Runtime.GetAllocator();
        int i = 0;
        try
        {
            for (; i < args.Length; i++)
            {
                argAddresses[i] = Serialize(args[i], allocator);
            }
            return Runtime.InvokeMethod<TRes>(_monoPtr, instance, argAddresses);
        }
        finally
        {
            for (int j = 0; j < i; j++)
            {
                Runtime.Free(argAddresses[j]);
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
    #endregion
}
