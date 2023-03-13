namespace DotNetIsolator.Test;

public static class SharedHost
{
    // The tests are vastly faster if most of them share the same IsolatedRuntimeHost instance,
    // because the Module will be reused across them all. In most tests the following host config suffices.
    public static IsolatedRuntimeHost Instance { get; }
        = new IsolatedRuntimeHost().WithBinDirectoryAssemblyLoader();
}
