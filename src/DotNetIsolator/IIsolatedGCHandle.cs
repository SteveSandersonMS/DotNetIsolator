namespace DotNetIsolator;

internal interface IIsolatedGCHandle
{
    int? GetGCHandle(IsolatedRuntime runtime);
}
