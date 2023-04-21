using DotNetIsolator.Guest;

namespace DotNetIsolator;

public static class HostInvoker
{
    public static void InvokeHost()
    {
        Interop.CallHost();
    }
}
