using System.Runtime.CompilerServices;

namespace DotNetIsolator.Guest;

internal static class Interop
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static unsafe extern bool CallHost(void* invocationPtr, int invocationLength, out void* result, out int resultLength);
}
