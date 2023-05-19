using MessagePack;
using MessagePack.Resolvers;
using System.Buffers;
using System.Reflection;

namespace DotNetIsolator.WasmApp;

public static class Helpers
{
    public static unsafe object? Deserialize<T>(byte* value, int valueLength)
    {
        var memory = new UnmanagedMemoryManager(value, valueLength).Memory;
        return Deserialize<T>(memory);
    }

    internal static unsafe object? Deserialize<T>(Memory<byte> value)
    {
        var result = MessagePackSerializer.Typeless.Deserialize(value, TypelessContractlessStandardResolver.Options);

        if (result != null && result is not T)
        {
            throw new ArgumentException($"Could not deserialize as {typeof(T)}, got {result.GetType()} instead.", nameof(value));
        }

        // Console.WriteLine($"Deserialized value of type {result?.GetType().FullName} with value {result}");
        return result;
    }

    public static byte[] Serialize(object value)
    {
        // TODO: Should we really be pinning the result value here, or is it safe to return
        // a MonoObject* to unmanaged code and then use mono_gchandle_new(..., true) from there?
        return MessagePackSerializer.Serialize(value, ContractlessStandardResolverAllowPrivate.Options);
    }

    public static void GetMemberHandle(object member, ref MemberTypes memberType, ref IntPtr handle)
    {
        memberType = (member as MemberInfo)?.MemberType ?? 0;
        switch (member)
        {
            case Type type:
                handle = type.TypeHandle.Value;
                break;
            case MethodBase method:
                handle = method.MethodHandle.Value;
                break;
            case FieldInfo field:
                handle = field.FieldHandle.Value;
                break;
            default:
                handle = IntPtr.Zero;
                break;
        }
    }

    sealed unsafe class UnmanagedMemoryManager : MemoryManager<byte>
    {
        readonly byte* _pointer;
        readonly int _length;

        public UnmanagedMemoryManager(byte* pointer, int length)
        {
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
            _pointer = pointer;
            _length = length;
        }

        public override Span<byte> GetSpan() => new Span<byte>(_pointer, _length);

        public override MemoryHandle Pin(int elementIndex = 0)
        {
            if (elementIndex < 0 || elementIndex >= _length)
            {
                throw new ArgumentOutOfRangeException(nameof(elementIndex));
            }
            return new MemoryHandle(_pointer + elementIndex);
        }

        public override void Unpin()
        {

        }

        protected override void Dispose(bool disposing)
        {

        }
    }
}
