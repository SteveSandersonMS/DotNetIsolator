using DotNetIsolator.Internal;
using MessagePack;
using MessagePack.Resolvers;
using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Wasmtime;

namespace DotNetIsolator;

public class IsolatedRuntime : IDisposable
{
    static readonly MessagePackSerializerOptions CallFromGuestResolverOptions =
        MessagePackSerializerOptions.Standard.WithResolver(
            CompositeResolver.Create(
                GeneratedResolver.Instance,
                ContractlessStandardResolverAllowPrivate.Instance));

    private readonly Store _store;
    private readonly Instance _instance;
    private readonly Memory _memory;
    private readonly Func<int, int> _malloc;
    private readonly Func<int, int, int> _realloc;
    private readonly Action<int> _free;
    private readonly Func<int, int, int, int> _lookupDotNetClass;
    private readonly Func<int, int> _instantiateDotNetClass;
    private readonly Func<int, int, int, int> _lookupDotNetMethod;
    private readonly Func<int, int, int, int> _deserializeAsDotNetObject;
    private readonly Action<int> _invokeDotNetMethod;
    private readonly Action<int> _releaseObject;
    private readonly ConcurrentDictionary<(string AssemblyName, string? Namespace, string TypeName), IsolatedClass> _classLookupCache = new();
    private readonly ConcurrentDictionary<(int MonoClass, string MethodName, int NumArgs), IsolatedMethod> _methodLookupCache = new();
    private readonly ShadowStack _shadowStack;
    private readonly Dictionary<string, Delegate> _registeredCallbacks = new();
    private bool _isDisposed;

    public IsolatedRuntime(IsolatedRuntimeHost host)
    {
        var store = new Store(host.Engine);
        store.SetWasiConfiguration(host.WasiConfigurationOrDefault);
        store.SetData(this);

        _store = store;
        _instance = host.Linker.Instantiate(store, host.Module);
        _memory = _instance.GetMemory("memory") ?? throw new InvalidOperationException("Couldn't find memory 'memory'");
        _malloc = _instance.GetFunction<int, int>("malloc")
            ?? throw new InvalidOperationException("Missing required export 'malloc'");
        _free = _instance.GetAction<int>("free")
            ?? throw new InvalidOperationException("Missing required export 'free'");
        _realloc = _instance.GetFunction<int, int, int>("dotnetisolator_realloc")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_realloc'");
        _lookupDotNetClass = _instance.GetFunction<int, int, int, int>("dotnetisolator_lookup_class")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_lookup_class'");
        _instantiateDotNetClass = _instance.GetFunction<int, int>("dotnetisolator_instantiate_class")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_instantiate_class'");
        _lookupDotNetMethod = _instance.GetFunction<int, int, int, int>("dotnetisolator_lookup_method")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_lookup_method'");
        _deserializeAsDotNetObject = _instance.GetFunction<int, int, int, int>("dotnetisolator_deserialize_object")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_deserialize_object'");
        _invokeDotNetMethod = _instance.GetAction<int>("dotnetisolator_invoke_method")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_invoke_method'");
        _releaseObject = _instance.GetAction<int>("dotnetisolator_release_object")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_release_object'");

        _shadowStack = new ShadowStack(_memory, _malloc, _free);

        // _start is already called in preinitialization, so we can skip it now
        // var startExport = _instance.GetAction("_start") ?? throw new InvalidOperationException("Couldn't find export '_start'");
        // startExport.Invoke();
    }

    internal static IsolatedRuntime FromStore(Store store)
    {
        var runtime = (IsolatedRuntime?)store.GetData();
        if (runtime is null)
        {
            throw new InvalidOperationException("Runtime was not set on the store");
        }

        return runtime;
    }

    public IsolatedAllocator GetAllocator()
    {
        return new(this);
    }

    public IsolatedClass? GetClass(string assemblyName, string? @namespace, string? declaringTypeName, string className)
    {
        try
        {
            return _classLookupCache.GetOrAdd((assemblyName, @namespace, className), info => {
                var monoClassName = declaringTypeName is null ? className : $"{declaringTypeName}/{className}";
                // All these CopyValue strings are freed inside the C code
                var monoClassPtr = _lookupDotNetClass(
                    CopyValue(info.AssemblyName),
                    CopyValue(info.Namespace),
                    CopyValue(monoClassName));

                if (monoClassPtr == 0)
                {
                    throw new IsolatedException();
                }

                return new IsolatedClass(this, monoClassPtr);
            });
        }
        catch (IsolatedException)
        {
            return null;
        }
    }

    internal IsolatedObject CreateObject(int monoClassPtr)
    {
        var gcHandle = _instantiateDotNetClass(monoClassPtr);
        return new IsolatedObject(this, gcHandle, monoClassPtr);
    }

    public IsolatedObject CopyObject<T>(T value)
    {
        using var allocator = GetLengthPrefixedAllocator();
        MessagePackSerializer.Typeless.Serialize(allocator, value);
        var serializedBytesAddress = ReleaseLengthPrefixedAllocator(allocator);
        try
        {
            using var monoClassPtrBuf = _shadowStack.Push<int>();
            using var errorMessageBuf = _shadowStack.Push<int>();
            var gcHandle = _deserializeAsDotNetObject(serializedBytesAddress, monoClassPtrBuf.Address, errorMessageBuf.Address);

            if (errorMessageBuf.Value != 0)
            {
                var errorMessage = ReadDotNetString(errorMessageBuf.Value);
                throw new IsolatedException(errorMessage, new IsolatedObject(this, gcHandle, monoClassPtrBuf.Value));
            }

            return new IsolatedObject(this, gcHandle, monoClassPtrBuf.Value);
        }
        finally
        {
            Free(serializedBytesAddress);
        }
    }

    internal int CopyValue(string? value)
    {
        if (value is null)
        {
            return 0;
        }

        var valueUtf8Length = Encoding.UTF8.GetByteCount(value);
        var resultPtr = Alloc(valueUtf8Length + 1);

        var destinationSpan = _memory.GetSpan(resultPtr, valueUtf8Length);
        Encoding.UTF8.GetBytes(value, destinationSpan);
        _memory.WriteByte(resultPtr + valueUtf8Length, 0); // Null-terminated string
        return resultPtr;
    }

    internal int CopyValue<T>(ReadOnlySpan<T> value, bool addLengthPrefix) where T: unmanaged
    {
        var lengthPrefixSize = addLengthPrefix ? 4 : 0;
        var valueAsBytes = MemoryMarshal.AsBytes(value);
        var length = valueAsBytes.Length;
        var resultPtr = Alloc(length + lengthPrefixSize);

        if (addLengthPrefix)
        {
            _memory.WriteInt32(resultPtr, length);
        }

        var destinationSpan = _memory.GetSpan(resultPtr + lengthPrefixSize, length);
        valueAsBytes.CopyTo(destinationSpan);
        return resultPtr;
    }

    internal int CopyValueLengthPrefixed(ReadOnlySpan<byte> value)
        => CopyValue(value, addLengthPrefix: true);

    internal IsolatedAllocator GetLengthPrefixedAllocator()
    {
        var allocator = GetAllocator();
        allocator.Advance(4);
        return allocator;
    }

    internal int ReleaseLengthPrefixedAllocator(IsolatedAllocator allocator)
    {
        var length = allocator.WrittenBytes;
        var address = allocator.Release();
        _memory.WriteInt32(address, length);
        return address;
    }

    internal IsolatedMethod? GetMethod(int monoClassPtr, string methodName, int numArgs = -1)
    {
        try
        {
            return _methodLookupCache.GetOrAdd((monoClassPtr, methodName, numArgs), info =>
            {
                // All these CopyValue strings are freed inside the C code
                var methodPtr = _lookupDotNetMethod(
                    info.MonoClass,
                    CopyValue(info.MethodName),
                    info.NumArgs);

                if (methodPtr == 0)
                {
                    throw new IsolatedException();
                }

                return new IsolatedMethod(this, methodPtr);
            });
        }
        catch (IsolatedException)
        {
            return null;
        }
    }

    // Internal because you only need to call it via IsolatedMethod
    internal TRes InvokeMethod<TRes>(int monoMethodPtr, IsolatedObject? instance, ReadOnlySpan<int> argAddresses)
    {
        // Prepare an Invocation struct within guest memory
        var len = Unsafe.SizeOf<Invocation>();
        var wasmPtr = _malloc(len); // Freed below
        try
        {
            bool asHandle = typeof(TRes).Equals(typeof(IsolatedObject));

            var invocationStruct = _memory.GetSpan(wasmPtr, len);
            ref var invocation = ref MemoryMarshal.Cast<byte, Invocation>(invocationStruct)[0];
            invocation = new()
            {
                TargetGCHandle = instance is IsolatedObject o ? o.GuestGCHandle : 0,
                MethodPtr = monoMethodPtr,
                ResultType = asHandle ? Invocation.ResultTypeHandle : Invocation.ResultTypeSerialize,
                ArgsLengthPrefixedBuffers = CopyValue(argAddresses, addLengthPrefix: false), // Freed in C code
                ArgsLengthPrefixedBuffersLength = argAddresses.Length,
            };

            _invokeDotNetMethod(wasmPtr);
            if (invocation.ResultException != 0)
            {
                var exceptionString = ReadDotNetString(invocation.ResultException);
                throw new IsolatedException(exceptionString, new IsolatedObject(this, invocation.ResultGCHandle, invocation.ResultPtr));
            }

            if (invocation.ResultPtr == 0)
            {
                return default!;
            }
            else
            {
                if (asHandle)
                {
                    return (TRes)(object)new IsolatedObject(this, invocation.ResultGCHandle, invocation.ResultPtr);
                }
                else
                {
                    var resultBytes = _memory
                        .GetSpan(invocation.ResultPtr, invocation.ResultLength)
                        .ToArray();

                    // Note that we don't deserialize using MessagePackSerializer.Typeless because we don't want the guest code
                    // to be able to make the host instantiate arbitrary types. The host will only instantiate the types statically
                    // defined by the type graph of TRes.
                    var result = MessagePackSerializer.Deserialize<TRes>(resultBytes, ContractlessStandardResolverAllowPrivate.Options)!;

                    ReleaseGCHandle(invocation.ResultGCHandle);
                    return result;
                }
            }
        }
        finally
        {
            _free(wasmPtr);
        }
    }

    internal string? ReadDotNetString(int ptr)
    {
        if (ptr == 0)
        {
            return null;
        }

        // The source data is already a .NET string (length-prefixed UTF-16), but we want a string in
        // the host heap so we need to copy the bytes. The following is pretty direct but there might
        // be some marshalling method that does this even more succinctly.
        var stringLength = _memory.ReadInt32(ptr + 8); // MonoString has an 8-byte header for the object type
        var stringUtf16Bytes = _memory.GetSpan<byte>(ptr + 12, stringLength * 2);
        var stringChars = MemoryMarshal.Cast<byte, char>(stringUtf16Bytes);
        return new string(stringChars);
    }

    internal void ReleaseGCHandle(int guestGCHandle)
    {
        if (!_isDisposed)
        {
            _releaseObject(guestGCHandle);
        }
    }

    public void Dispose()
    {
        _isDisposed = true;
        _shadowStack.Dispose();
        _store.Dispose();
    }

    internal int Alloc(int size)
    {
        var ptr = _malloc(size);
        if (ptr == 0)
        {
            throw new OutOfMemoryException($"malloc failed when trying to allocate {size} bytes");
        }
        return ptr;
    }

    internal int Realloc(int malloced_ptr, int new_size)
    {
        var ptr = _realloc(malloced_ptr, new_size);
        if(ptr == 0)
        {
            throw new OutOfMemoryException($"realloc failed when trying to allocate {new_size} bytes");
        }
        return ptr;
    }

    internal void Free(int malloced_ptr)
    {
        _free(malloced_ptr);
    }

    internal Span<byte> GetMemory(int ptr, int size)
    {
        return _memory.GetSpan(ptr, size);
    }

    public void Invoke(Action value)
    {
        // TODO: Find a way of not serializing value.Target if it doesn't contain any fields we care about serializing
        // This makes invoking static lambdas vastly faster. It's not clear to me why the target is nonnull in these cases anyway.
        using var targetInGuest = value.Target is null ? null : CopyObject(value.Target);
        LookupDelegateMethod(value).InvokeVoid(targetInGuest);
    }

    public TRes Invoke<TRes>(Func<TRes> value)
    {
        // TODO: Find a way of not serializing value.Target if it doesn't contain any fields we care about serializing
        // This makes invoking static lambdas vastly faster. It's not clear to me why the target is nonnull in these cases anyway.
        using var targetInGuest = value.Target is null ? null : CopyObject(value.Target);
        return LookupDelegateMethod(value).Invoke<TRes>(targetInGuest);
    }

    public void RegisterCallback(string name, Delegate callback)
        => _registeredCallbacks.Add(name, callback);

    private IsolatedMethod LookupDelegateMethod(MulticastDelegate @delegate)
    {
        var method = @delegate.Method;
        var methodType = method.DeclaringType!;
        var wasmMethod = this.GetMethod(methodType, method.Name, -1);
        return wasmMethod;
    }

    internal int AcceptCallFromGuest(int invocationPtr, int invocationLength, int resultPtrPtr, int resultLengthPtr)
    {
        try
        {
            var invocationInfo = MessagePackSerializer.Deserialize<GuestToHostCall>(
                _memory.GetSpan<byte>(invocationPtr, invocationLength).ToArray(),
                CallFromGuestResolverOptions);

            if (!_registeredCallbacks.TryGetValue(invocationInfo.CallbackName, out var callback))
            {
                var errorString = Encoding.UTF8.GetBytes($"There is no registered callback with name '{invocationInfo.CallbackName}'");
                var errorStringPtr = CopyValue<byte>(errorString, false);
                _memory.WriteInt32(resultPtrPtr, errorStringPtr);
                _memory.WriteInt32(resultLengthPtr, errorString.Length);
                return 0;
            }

            var expectedParameterTypes = callback.Method.GetParameters();
            var deserializedArgs = new object?[expectedParameterTypes.Length];
            for (var i = 0; i < expectedParameterTypes.Length; i++)
            {
                if (invocationInfo.IsRawCall)
                {
                    // Assumes the parameter type is byte[]
                    deserializedArgs[i] = invocationInfo.Args![i]?.ToArray();
                }
                else
                {
                    deserializedArgs[i] = MessagePackSerializer.Deserialize(
                        expectedParameterTypes[i].ParameterType,
                        invocationInfo.Args[i],
                        CallFromGuestResolverOptions);
                }
            }

            var result = callback.DynamicInvoke(deserializedArgs);
            var resultBytes = result is null
                ? null
                : invocationInfo.IsRawCall
                    ? (byte[])result
                    : MessagePackSerializer.Serialize(
                        callback.Method.ReturnType,
                        result,
                        ContractlessStandardResolverAllowPrivate.Options);

            var resultPtr = resultBytes is null ? 0 : CopyValue<byte>(resultBytes, false);
            _memory.WriteInt32(resultPtrPtr, resultPtr);
            _memory.WriteInt32(resultLengthPtr, resultBytes is null ? 0 : resultBytes.Length);
            return 1; // Success
        }
        catch (Exception ex)
        {
            // We could supply the raw exception info to the guest, but since we consider the guest untrusted,
            // we don't want to expose arbitrary information about the host internals. Ideally this behavior would
            // vary based on whether this is a dev or prod scenario, but that's not a concept that exists natively
            // in .NET (whereas it does in ASP.NET Core).
            Console.Error.WriteLine(ex.ToString());
            var resultBytes = Encoding.UTF8.GetBytes("The call failed. See host console logs for details.");
            var resultPtr = CopyValue<byte>(resultBytes, false);
            _memory.WriteInt32(resultPtrPtr, resultPtr);
            _memory.WriteInt32(resultLengthPtr, resultBytes.Length);
            return 0; // Failure
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Invocation
    {
        public const int ResultTypeSerialize = 0;
        public const int ResultTypeHandle = 1;

        public int TargetGCHandle;
        public int MethodPtr;
        public int ResultException;
        public int ResultType;
        public int ResultPtr;
        public int ResultLength;
        public int ResultGCHandle;
        public int ArgsLengthPrefixedBuffers;
        public int ArgsLengthPrefixedBuffersLength;
    }
}
