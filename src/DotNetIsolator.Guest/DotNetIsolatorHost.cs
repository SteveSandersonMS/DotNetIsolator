using DotNetIsolator.Guest;
using MessagePack.Resolvers;
using MessagePack;
using System.Text;

namespace DotNetIsolator;

public static class DotNetIsolatorHost
{
    public static unsafe T Invoke<T>(string callbackName, params object[] args)
    {
        var argsSerialized = args.Select(a => a is null
            ? null
            : MessagePackSerializer.Serialize(a.GetType(), a, ContractlessStandardResolverAllowPrivate.Options))
            .ToArray();

        var callInfo = new GuestToHostCall { CallbackName = callbackName, ArgsSerialized = argsSerialized };
        var callInfoBytes = MessagePackSerializer.Serialize(callInfo, ContractlessStandardResolverAllowPrivate.Options);

        fixed (void* callInfoPtr = callInfoBytes)
        {
            var success = Interop.CallHost(callInfoPtr, callInfoBytes.Length, out var resultPtr, out var resultLength);
            var result = new Span<byte>(resultPtr, resultLength);
            if (success)
            {
                return MessagePackSerializer.Deserialize<T>(result.ToArray(), ContractlessStandardResolverAllowPrivate.Options);
            }
            else
            {
                var errorString = Encoding.UTF8.GetString(result);
                throw new InvalidOperationException($"Call to host failed: {errorString}");
            }
        }
    }
}
