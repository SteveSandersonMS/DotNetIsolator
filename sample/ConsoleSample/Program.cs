using DotNetIsolator;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal class Program
{
    private static void Main(string[] args)
    {
        using var host = new IsolatedRuntimeHost().WithBinDirectoryAssemblyLoader();

        int numCalls = 10;
        var sw = new Stopwatch();
        sw.Start();
        for (var i = 0; i < numCalls; i++)
        {
            using var runtime = new IsolatedRuntime(host);

            runtime.Invoke(() =>
            {
                Console.WriteLine($"Hello from {RuntimeInformation.OSArchitecture}");
            });
        }
        sw.Stop();
        Console.WriteLine($"Done in {sw.ElapsedMilliseconds:F0}ms ({(double)sw.ElapsedMilliseconds/numCalls:F4} ms/call)");
    }
}
