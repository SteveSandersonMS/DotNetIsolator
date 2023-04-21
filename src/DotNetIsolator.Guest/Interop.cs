using System.Runtime.CompilerServices;

namespace DotNetIsolator.Guest;

internal static class Interop
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void CallHost();
}
