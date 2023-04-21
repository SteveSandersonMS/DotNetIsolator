using DotNetIsolator.Guest;
using MessagePack.Resolvers;
using MessagePack;
using System.Text;

namespace DotNetIsolator;

public static class DotNetIsolatorHost
{
    public static unsafe T Invoke<T>()
    {
        var msg = Encoding.UTF8.GetBytes("Hello from guest");
        fixed (void* msgPtr = msg)
        {
            var success = Interop.CallHost(msgPtr, msg.Length, out var resultPtr, out var resultLength);
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
