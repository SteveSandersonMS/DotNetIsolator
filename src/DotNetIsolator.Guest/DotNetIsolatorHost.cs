using DotNetIsolator.Guest;
using MessagePack.Resolvers;
using MessagePack;
using System.Text;
using DotNetIsolator.Internal;

namespace DotNetIsolator;

public static class DotNetIsolatorHost
{
    static readonly MessagePackSerializerOptions CallFromGuestResolverOptions =
        MessagePackSerializerOptions.Standard.WithResolver(
            CompositeResolver.Create(
                GeneratedResolver.Instance,
                ContractlessStandardResolverAllowPrivate.Instance));

    public static byte[] InvokeRaw(string callbackName, params byte[]?[] args)
        => InvokeRaw<object>(callbackName, args);

    public static void Invoke(string callbackName, params object[] args)
        => Invoke<object>(callbackName, args);

    public static unsafe byte[] InvokeRaw<T>(string callbackName, params byte[]?[] args)
    {
        return PerformCall<byte[]>(new GuestToHostCall
        {
            CallbackName = callbackName,
            Args = args,
            IsRawCall = true,
        });
    }

    public static unsafe T Invoke<T>(string callbackName, params object[] args)
    {
        var argsSerialized = args.Select(a => a is null
            ? null
            : MessagePackSerializer.Serialize(a.GetType(), a, ContractlessStandardResolverAllowPrivate.Options))
            .ToArray();

        // Note that this overload won't work if the host is AOT compiled because it will be unable to
        // deserialize these arbitrary arg types. For that scenario, use the Memory<byte>[] overload instead.
        return PerformCall<T>(new GuestToHostCall
        {
            CallbackName = callbackName,
            Args = argsSerialized,
            IsRawCall = false,
        });
    }

    private static unsafe T PerformCall<T>(GuestToHostCall callInfo)
    {
        var callInfoBytes = MessagePackSerializer.Serialize(callInfo, CallFromGuestResolverOptions);

        fixed (void* callInfoPtr = callInfoBytes)
        {
            var success = Interop.CallHost(callInfoPtr, callInfoBytes.Length, out var resultPtr, out var resultLength);
            var result = (int)resultPtr == 0 ? null : new Span<byte>(resultPtr, resultLength);
            if (success)
            {
                return (int)resultPtr == 0
                    ? default!
                    : callInfo.IsRawCall
                        ? (T)(object)result.ToArray()
                        : MessagePackSerializer.Deserialize<T>(result.ToArray(), ContractlessStandardResolverAllowPrivate.Options);
            }
            else
            {
                var errorString = Encoding.UTF8.GetString(result);
                throw new InvalidOperationException($"Call to host failed: {errorString}");
            }
        }
    }
}
