using MessagePack;
using MessagePack.Resolvers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Wasmtime;

namespace DotNetIsolator;

public class IsolatedRuntime : IDisposable
{
    private readonly Store _store;
    private readonly Instance _instance;
    private readonly Memory _memory;
    private readonly Func<int, int> _malloc;
    private readonly Action<int> _free;
    private readonly Func<int, int, int, int, int> _instantiateDotNetClass;
    private readonly Func<int, int, int, int, int, int, int> _lookupDotNetMethod;
    private readonly Func<int, int, int> _deserializeAsDotNetObject;
    private readonly Action<int> _invokeDotNetMethod;
    private readonly Action<int> _releaseObject;
    private readonly ConcurrentDictionary<(string AssemblyName, string? Namespace, string TypeName, string MethodName, int NumArgs), IsolatedMethod> _methodLookupCache = new();
    private readonly ShadowStack _shadowStack;
    private readonly Dictionary<string, MulticastDelegate> _registeredCallbacks = new();
    private bool _isDisposed;

    public IsolatedRuntime(IsolatedRuntimeHost host)
    {
        var store = new Store(host.Engine);
        store.SetWasiConfiguration(host.WasiConfigurationOrDefault);

        _store = store;
        _instance = host.Linker.Instantiate(store, host.Module);
        _memory = _instance.GetMemory("memory") ?? throw new InvalidOperationException("Couldn't find memory 'memory'");
        _malloc = _instance.GetFunction<int, int>("malloc")
            ?? throw new InvalidOperationException("Missing required export 'malloc'");
        _free = _instance.GetAction<int>("free")
            ?? throw new InvalidOperationException("Missing required export 'free'");
        _instantiateDotNetClass = _instance.GetFunction<int, int, int, int, int>("dotnetisolator_instantiate_class")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_instantiate_class'");
        _lookupDotNetMethod = _instance.GetFunction<int, int, int, int, int, int, int>("dotnetisolator_lookup_method")
            ?? throw new InvalidOperationException("Missing required export 'dotnetisolator_lookup_method'");
        _deserializeAsDotNetObject = _instance.GetFunction<int, int, int>("dotnetisolator_deserialize_object")
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

    internal ShadowStack ShadowStack => _shadowStack;

    public IsolatedObject CreateObject(string assemblyName, string? @namespace, string className)
        => CreateObject(assemblyName, @namespace, declaringTypeName: null, className);

    public IsolatedObject CreateObject(string assemblyName, string? @namespace, string? declaringTypeName, string className)
    {
        var errorMessageParam = _shadowStack.Push<int>();
        try
        {
            // All these CopyValue strings are freed inside the C code
            var monoClassName = declaringTypeName is null ? className : $"{declaringTypeName}/{className}";
            var gcHandle = _instantiateDotNetClass(CopyValue(assemblyName), CopyValue(@namespace), CopyValue(monoClassName), errorMessageParam.Address);

            if (errorMessageParam.Value != 0)
            {
                var errorString = _memory.ReadNullTerminatedString(errorMessageParam.Value);
                _free(errorMessageParam.Value);
                throw new IsolatedException(errorString);
            }

            return new IsolatedObject(this, gcHandle, assemblyName, @namespace, declaringTypeName, className);
        }
        finally
        {
            errorMessageParam.Pop();
        }
    }

    public IsolatedObject CreateObject<T>()
        => CreateObject(typeof(T).Assembly.GetName().Name!, typeof(T).Namespace, typeof(T).DeclaringType?.Name, typeof(T).Name);

    public IsolatedObject CopyObject<T>(T value)
    {
        var serializedBytes = MessagePackSerializer.Typeless.Serialize(value);
        var serializedBytesAddress = CopyValueLengthPrefixed(serializedBytes);
        var errorMessageBuf = _shadowStack.Push<int>();
        try
        {
            var gcHandle = _deserializeAsDotNetObject(serializedBytesAddress, errorMessageBuf.Address);

            if (errorMessageBuf.Value != 0)
            {
                var errorMessage = ReadDotNetString(errorMessageBuf.Value);
                throw new IsolatedException(errorMessage);
            }

            return new IsolatedObject(this, gcHandle, typeof(T).Assembly.GetName().Name!, typeof(T).Namespace, typeof(T).DeclaringType?.Name, typeof(T).Name);
        }
        finally
        {
            errorMessageBuf.Pop();
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
        var resultPtr = _malloc(valueUtf8Length + 1);
        if (resultPtr == 0)
        {
            throw new InvalidOperationException($"malloc failed when trying to allocate {valueUtf8Length} bytes");
        }

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
        var resultPtr = _malloc(length + lengthPrefixSize);
        if (resultPtr == 0)
        {
            throw new InvalidOperationException($"malloc failed when trying to allocate {length + 4} bytes");
        }

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

    public IsolatedMethod GetMethod(Type type, string methodName)
        => GetMethod(type.Assembly.GetName().Name!, type.Namespace, type.DeclaringType?.Name, type.Name, methodName);

    public IsolatedMethod GetMethod(Type type, string methodName, int numArgs)
        => GetMethod(type.Assembly.GetName().Name!, type.Namespace, type.DeclaringType?.Name, type.Name, methodName, numArgs);

    public IsolatedMethod GetMethod(string assemblyName, string? @namespace, string? declaringTypeName, string typeName, string methodName, int numArgs = -1)
    {
        // Consider a multilevel cache keyed first by type so that successive "GetMethod" calls on the same type
        // don't have to hash so many strings. Also handle lookup failures in a better way.
        return _methodLookupCache.GetOrAdd((assemblyName, @namespace, typeName, methodName, numArgs), info =>
        {
            // All these CopyValue strings are freed inside the C code
            var monoClassName = declaringTypeName is null ? typeName : $"{declaringTypeName}/{typeName}";
            
            var errorMessageParam = _shadowStack.Push<int>();
            try
            {
                var methodPtr = _lookupDotNetMethod(
                    CopyValue(info.AssemblyName),
                    CopyValue(info.Namespace),
                    CopyValue(monoClassName),
                    CopyValue(info.MethodName),
                    info.NumArgs,
                    errorMessageParam.Address);

                if (errorMessageParam.Value != 0)
                {
                    var errorString = _memory.ReadNullTerminatedString(errorMessageParam.Value);
                    _free(errorMessageParam.Value);
                    throw new IsolatedException(errorString);
                }

                return new IsolatedMethod(this, info.MethodName, methodPtr);
            }
            finally
            {
                errorMessageParam.Pop();
            }
        });
    }

    // Internal because you only need to call it via DotNetMethod
    internal TRes InvokeDotNetMethod<TRes>(int monoMethodPtr, IsolatedObject? instance, ReadOnlySpan<int> argAddresses)
    {
        // Prepare an Invocation struct within guest memory
        var len = Marshal.SizeOf<Invocation>();
        var wasmPtr = _malloc(len); // Freed below
        try
        {
            var invocationStruct = _memory.GetSpan(wasmPtr, len);
            ref var invocation = ref MemoryMarshal.AsRef<Invocation>(invocationStruct);
            invocation = new()
            {
                TargetGCHandle = instance is IsolatedObject o ? o.GuestGCHandle : 0,
                MethodPtr = monoMethodPtr,
                ArgsLengthPrefixedBuffers = CopyValue(argAddresses, addLengthPrefix: false), // Freed in C code
                ArgsLengthPrefixedBuffersLength = argAddresses.Length,
            };

            _invokeDotNetMethod(wasmPtr);
            if (invocation.ResultException != 0)
            {
                var exceptionString = ReadDotNetString(invocation.ResultException);
                throw new IsolatedException(exceptionString);
            }

            if (invocation.ResultSerialized == 0)
            {
                return default!;
            }
            else
            {
                var resultBytes = _memory
                    .GetSpan(invocation.ResultSerialized, invocation.ResultSerializedLength)
                    .ToArray();

                // Note that we don't deserialize using MessagePackSerializer.Typeless because we don't want the guest code
                // to be able to make the host instantiate arbitrary types. The host will only instantiate the types statically
                // defined by the type graph of TRes.
                var result = MessagePackSerializer.Deserialize<TRes>(resultBytes, ContractlessStandardResolverAllowPrivate.Options)!;

                ReleaseGCHandle(invocation.ResultSerializedGCHandle);
                return result;
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

    internal void Free(int malloced_ptr)
    {
        _free(malloced_ptr);
    }

    public void Invoke(Action value)
    {
        // TODO: Find a way of not serializing value.Target if it doesn't contain any fields we care about serializing
        // This makes invoking static lambdas vastly faster. It's not clear to me why the target is nonnull in these cases anyway.
        var targetInGuest = value.Target is null ? null : CopyObject(value.Target);
        try
        {
            LookupDelegateMethod(value).InvokeVoid(targetInGuest);
        }
        finally
        {
            targetInGuest?.ReleaseGCHandle(); // TODO: Make this into a 'Dispose' call on IsolatedObject?
        }
    }

    public TRes Invoke<TRes>(Func<TRes> value)
    {
        // TODO: Find a way of not serializing value.Target if it doesn't contain any fields we care about serializing
        // This makes invoking static lambdas vastly faster. It's not clear to me why the target is nonnull in these cases anyway.
        var targetInGuest = value.Target is null ? null : CopyObject(value.Target);
        try
        {
            return LookupDelegateMethod(value).Invoke<TRes>(targetInGuest);
        }
        finally
        {
            targetInGuest?.ReleaseGCHandle(); // TODO: Make this into a 'Dispose' call on IsolatedObject?
        }
    }

    public void RegisterCallback<T0, T1, TResult>(string name, Func<T0, T1, TResult> callback)
    {
        _registeredCallbacks.Add(name, callback);
    }

    private IsolatedMethod LookupDelegateMethod(MulticastDelegate @delegate)
    {
        var method = @delegate.Method;
        var methodType = method.DeclaringType!;
        var wasmMethod = GetMethod(methodType.Assembly.GetName().Name!, methodType.Namespace, methodType.DeclaringType?.Name, methodType.Name, method.Name, -1);
        return wasmMethod;
    }

    internal int AcceptCallFromGuest(int invocationPtr, int invocationLength, int resultPtrPtr, int resultLengthPtr)
    {
        try
        {
            var invocationInfo = MessagePackSerializer.Deserialize<GuestToHostCall>(
                _memory.GetSpan<byte>(invocationPtr, invocationLength).ToArray(), ContractlessStandardResolverAllowPrivate.Options);
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
                deserializedArgs[i] = MessagePackSerializer.Deserialize(
                    expectedParameterTypes[i].ParameterType,
                    invocationInfo.ArgsSerialized[i],
                    ContractlessStandardResolverAllowPrivate.Options);
            }

            var result = callback.DynamicInvoke(deserializedArgs);
            var resultBytes = MessagePackSerializer.Serialize(
                callback.Method.ReturnType,
                result,
                ContractlessStandardResolverAllowPrivate.Options); ; ;

            var resultPtr = CopyValue<byte>(resultBytes, false);
            _memory.WriteInt32(resultPtrPtr, resultPtr);
            _memory.WriteInt32(resultLengthPtr, resultBytes.Length);
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
        public int TargetGCHandle;
        public int MethodPtr;
        public int ResultException;
        public int ResultSerialized;
        public int ResultSerializedLength;
        public int ResultSerializedGCHandle;
        public int ArgsLengthPrefixedBuffers;
        public int ArgsLengthPrefixedBuffersLength;
    }
}
