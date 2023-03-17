using MessagePack;
using MessagePack.Resolvers;

namespace DotNetIsolator.WasmApp;

public static class Serialization
{
    public static unsafe object? Deserialize(byte* value, int valueLength)
    {
        // Instead of using ToArray (or UnmanagedMemoryStream) you could have a pool of byte[]
        // buffers on the guest side and have the host serialize directly into their memory, then
        // there would be no allocations on either side, and this code could work with a Memory<byte>
        // for whatever region within one of those buffers.
        var memoryCopy = new Span<byte>(value, valueLength).ToArray();
        return Deserialize(memoryCopy);
    }

    internal static unsafe object? Deserialize(Memory<byte> value)
    {
        // TODO: Instead of using Typeless, consider making this a generic method and having the
        // C code produce the closed type based on the declared parameter types of the method
        var result = MessagePackSerializer.Typeless.Deserialize(value);

        // Console.WriteLine($"Deserialized value of type {result?.GetType().FullName} with value {result}");
        return result;
    }

    public static byte[] Serialize(object value)
    {
        // TODO: Should we really be pinning the result value here, or is it safe to return
        // a MonoObject* to unmanaged code and then use mono_gchandle_new(..., true) from there?
        return MessagePackSerializer.Serialize(value, ContractlessStandardResolverAllowPrivate.Options);
    }
}
