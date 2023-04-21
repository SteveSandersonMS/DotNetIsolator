using DotNetIsolator.Guest;
using System.Text;

namespace DotNetIsolator;

public static class HostInvoker
{
    public static unsafe void InvokeHost()
    {
        var msg = Encoding.UTF8.GetBytes("Hello from guest");
        fixed (void* msgPtr = msg)
        {
            var success = Interop.CallHost(msgPtr, msg.Length, out var resultPtr, out var resultLength);
            var result = new Span<byte>(resultPtr, resultLength);
            if (success)
            {
                var resultString = Encoding.UTF8.GetString(result);
                Console.WriteLine($"Guest got result: [{resultString}]");
            }
            else
            {
                var errorString = Encoding.UTF8.GetString(result);
                throw new InvalidOperationException($"Call to host failed: {errorString}");
            }
        }
    }
}
