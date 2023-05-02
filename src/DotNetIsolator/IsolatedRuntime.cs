using DotNetIsolator.Internal;
using MessagePack;
using MessagePack.Resolvers;
using System.Buffers;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Wasmtime;

namespace DotNetIsolator;

public class IsolatedRuntime : MemoryManager<byte>, IDisposable
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
    private readonly Func<int, int, int, int> _lookupDotNetMethodDesc;
    private readonly Func<int, int, int, int> _lookupGlobalDotNetMethodDesc;
    private readonly Func<int, int, int, int> _deserializeAsDotNetObject;
    private readonly Func<int, int, int> _reflectClass;
    private readonly Func<int, int, int> _reflectMethod;
    private readonly Action<int, int, int, int> _unreflectMember;
    private readonly Action<int> _invokeDotNetMethod;
    private readonly Action<int> _releaseObject;
    private readonly Func<int, int> _getObjectHash;
    private readonly Func<int, int, int, int> _makeGenericClass;
    private readonly Func<int, int, int, int> _makeGenericMethod;

    private readonly ConcurrentDictionary<(string AssemblyName, string? Namespace, string TypeName), IsolatedClass> _classLookupCache = new();
    private readonly ConcurrentDictionary<(int MonoClass, string MethodName, int NumArgs), IsolatedMethod> _methodLookupCache = new();
    private readonly ConcurrentDictionary<(string AssemblyName, string MethodDesc, bool MatchNamespace), IsolatedMethod> _globalMethodLookupCache = new();
    private readonly ConcurrentDictionary<(int MonoClass, string MethodDesc, bool MatchNamespace), IsolatedMethod> _methodDescLookupCache = new();
    private readonly ConditionalWeakTable<object, IsolatedObject> _delegateTargetCache = new();
    private readonly ShadowStack _shadowStack;
    private readonly Dictionary<string, Delegate> _registeredCallbacks = new();
    private bool _isDisposed;

    public IsolatedClass ObjectClass { get; }
    public IsolatedMethod ToStringMethod { get; }
    public IsolatedMethod EqualsMethod { get; }
    public IsolatedMethod ReferenceEqualsMethod { get; }

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
        _lookupDotNetMethodDesc = _instance.GetFunction<int, int, int, int>("dotnetisolator_lookup_method_desc")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_lookup_method_desc'");
        _lookupGlobalDotNetMethodDesc = _instance.GetFunction<int, int, int, int>("dotnetisolator_lookup_global_method_desc")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_lookup_global_method_desc'");
        _deserializeAsDotNetObject = _instance.GetFunction<int, int, int, int>("dotnetisolator_deserialize_object")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_deserialize_object'");
        _reflectClass = _instance.GetFunction<int, int, int>("dotnetisolator_reflect_class")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_reflect_class'");
        _reflectMethod = _instance.GetFunction<int, int, int>("dotnetisolator_reflect_method")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_reflect_method'");
        _unreflectMember = _instance.GetAction<int, int, int, int>("dotnetisolator_unreflect_member")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_unreflect_member'");
        _invokeDotNetMethod = _instance.GetAction<int>("dotnetisolator_invoke_method")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_invoke_method'");
        _releaseObject = _instance.GetAction<int>("dotnetisolator_release_object")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_release_object'");
        _getObjectHash = _instance.GetFunction<int, int>("dotnetisolator_get_object_hash")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_get_object_hash'");
        _makeGenericClass = _instance.GetFunction<int, int, int, int>("dotnetisolator_make_generic_class")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_make_generic_class'");
        _makeGenericMethod = _instance.GetFunction<int, int, int, int>("dotnetisolator_make_generic_method")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_make_generic_method'");

        _shadowStack = new ShadowStack(_memory, _malloc, _free);

        // _start is already called in preinitialization, so we can skip it now
        // var startExport = _instance.GetAction("_start") ?? throw new InvalidOperationException("Couldn't find export '_start'");
        // startExport.Invoke();

        var getObjectClass = _instance.GetFunction<int>("dotnetisolator_get_object_class")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_get_object_class'");

        ObjectClass = new IsolatedClass(this, getObjectClass());

        ToStringMethod = ObjectClass.GetMethod(nameof(Object.ToString), 0)
            ?? throw new InvalidOperationException("System.Object.ToString could not be found");

        EqualsMethod = ObjectClass.GetMethod(nameof(Object.Equals), 2)
            ?? throw new InvalidOperationException("System.Object.Equals could not be found");

        ReferenceEqualsMethod = ObjectClass.GetMethod(nameof(Object.ReferenceEquals), 2)
            ?? throw new InvalidOperationException("System.Object.ReferenceEquals could not be found");
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

    internal int GetHashCode(int gcHandle)
    {
        return _getObjectHash(gcHandle);
    }

    public IsolatedObject CopyObject<T>(T value)
    {
        using var allocator = GetAllocator();
        MessagePackSerializer.Typeless.Serialize(allocator, value);
        var serializedBytesAddress = allocator.Release();
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

    internal void WriteInt32(int address, int value)
    {
        _memory.WriteInt32(address, value);
    }
    
    public IsolatedMethod? GetMethod(string assemblyName, string methodDesc, bool matchNamespace)
    {
        try
        {
            return _globalMethodLookupCache.GetOrAdd((assemblyName, methodDesc, matchNamespace), info => {
                // All these CopyValue strings are freed inside the C code
                var monoMethodPtr = _lookupGlobalDotNetMethodDesc(
                    CopyValue(info.AssemblyName),
                    CopyValue(info.MethodDesc),
                    info.MatchNamespace ? 1 : 0);

                if (monoMethodPtr == 0)
                {
                    throw new IsolatedException();
                }

                return new IsolatedMethod(this, monoMethodPtr);
            });
        }
        catch (IsolatedException)
        {
            return null;
        }
    }

    internal IsolatedMethod? GetMethod(int monoClassPtr, string methodName, int numArgs)
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

    internal IsolatedMethod? GetMethod(int monoClassPtr, string methodDesc, bool matchNamespace)
    {
        try
        {
            return _methodDescLookupCache.GetOrAdd((monoClassPtr, methodDesc, matchNamespace), info =>
            {
                // All these CopyValue strings are freed inside the C code
                var methodPtr = _lookupDotNetMethodDesc(
                    info.MonoClass,
                    CopyValue(info.MethodDesc),
                    info.MatchNamespace ? 1 : 0);

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
                throw new IsolatedException(exceptionString == "" ? null : exceptionString, new IsolatedObject(this, invocation.ResultGCHandle, invocation.ResultPtr));
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
                    var resultBytes = base.CreateMemory(invocation.ResultPtr, invocation.ResultLength);

                    try
                    {
                        // Note that we don't deserialize using MessagePackSerializer.Typeless because we don't want the guest code
                        // to be able to make the host instantiate arbitrary types. The host will only instantiate the types statically
                        // defined by the type graph of TRes.
                        return MessagePackSerializer.Deserialize<TRes>(resultBytes, ContractlessStandardResolverAllowPrivate.Options)!;
                    }
                    finally
                    {
                        ReleaseGCHandle(invocation.ResultGCHandle);
                    }
                }
            }
        }
        finally
        {
            _free(wasmPtr);
        }
    }

    internal IsolatedObject GetReflectionClass(int monoClassPtr)
    {
        using var resultClassPtrBuf = _shadowStack.Push<int>();
        var handle = _reflectClass(monoClassPtr, resultClassPtrBuf.Address);
        return new IsolatedObject(this, handle, resultClassPtrBuf.Value);
    }

    internal IsolatedObject GetReflectionMethod(int monoMethodPtr)
    {
        using var resultClassPtrBuf = _shadowStack.Push<int>();
        var handle = _reflectMethod(monoMethodPtr, resultClassPtrBuf.Address);
        return new IsolatedObject(this, handle, resultClassPtrBuf.Value);
    }

    internal IsolatedMember GetReflectedMember(int gcHandle)
    {
        using var resultMemberType = _shadowStack.Push<MemberTypes>();
        using var resultMemberPtr = _shadowStack.Push<int>();
        using var errorMessageBuf = _shadowStack.Push<int>();
        _unreflectMember(gcHandle, resultMemberType.Address, resultMemberPtr.Address, errorMessageBuf.Address);

        if (errorMessageBuf.Value != 0)
        {
            var errorMessage = ReadDotNetString(errorMessageBuf.Value);
            throw new IsolatedException(errorMessage);
        }

        switch (resultMemberType.Value)
        {
            case MemberTypes.TypeInfo:
            case MemberTypes.NestedType:
                return new IsolatedClass(this, resultMemberPtr.Value);
            case MemberTypes.Method:
            case MemberTypes.Constructor:
                return new IsolatedMethod(this, resultMemberPtr.Value);
            default:
                throw new ArgumentException("Handle does not point to a valid reflection object.", nameof(gcHandle));
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

    internal IsolatedClass? MakeGenericClass(int monoClassPtr, ReadOnlySpan<int> genericArguments)
    {
        int args = CopyValue<int>(genericArguments, false);
        var resultPtr = _makeGenericClass(monoClassPtr, genericArguments.Length, args);
        if (resultPtr == 0)
        {
            return null;
        }
        return new IsolatedClass(this, resultPtr);
    }

    internal IsolatedMethod? MakeGenericMethod(int monoMethodPtr, ReadOnlySpan<int> genericArguments)
    {
        int args = CopyValue<int>(genericArguments, false);
        var resultPtr = _makeGenericMethod(monoMethodPtr, genericArguments.Length, args);
        if (resultPtr == 0)
        {
            return null;
        }
        return new IsolatedMethod(this, resultPtr);
    }

    internal void ReleaseGCHandle(int guestGCHandle)
    {
        if (!_isDisposed)
        {
            _releaseObject(guestGCHandle);
        }
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _isDisposed = true;
            _shadowStack.Dispose();
            _store.Dispose();
        }
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

    public void Invoke(Action action)
    {
        using var target = GetDelegateTarget(action);
        LookupDelegateMethod(action).InvokeVoid(target);
    }

    public void Invoke<T0>(Action<T0> action, T0 param0)
    {
        using var target = GetDelegateTarget(action);
        LookupDelegateMethod(action).InvokeVoid(target, param0);
    }

    public TRes Invoke<TRes>(Func<TRes> func)
    {
        using var target = GetDelegateTarget(func);
        return LookupDelegateMethod(func).Invoke<TRes>(target);
    }

    public TRes Invoke<T0, TRes>(Func<TRes> func, T0 param0)
    {
        using var target = GetDelegateTarget(func);
        return LookupDelegateMethod(func).Invoke<T0, TRes>(target, param0);
    }

    private IsolatedObject? GetDelegateTarget(Delegate @delegate)
    {
        // TODO: Find a way of not serializing value.Target if it doesn't contain any fields we care about serializing
        // This makes invoking static lambdas vastly faster. It's not clear to me why the target is nonnull in these cases anyway.
        var target = @delegate.Target;
        if (target == null)
        {
            return null;
        }
        return _delegateTargetCache.GetValue(target, CopyObject<object>);
    }

    private IsolatedMethod LookupDelegateMethod(Delegate @delegate)
    {
        return this.GetMethod(@delegate.Method)
            ?? throw new ArgumentException($"Cannot find closure method {@delegate.Method.Name}.");
    }

    public void RegisterCallback(string name, Delegate callback)
        => _registeredCallbacks.Add(name, callback);

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

    public override Span<byte> GetSpan()
    {
        return _memory.GetSpan(0, (int)_memory.GetLength());
    }

    public override MemoryHandle Pin(int elementIndex = 0)
    {
        throw new NotSupportedException();
    }

    public override void Unpin()
    {
        throw new NotSupportedException();
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
