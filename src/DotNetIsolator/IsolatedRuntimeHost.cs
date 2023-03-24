using Wasmtime;

namespace DotNetIsolator;

public class IsolatedRuntimeHost : IDisposable
{
    private readonly static string _modulePath;
    private readonly static string _wasmBclDir;
    
    private WasiConfiguration? _wasiConfiguration;
    private List<AssemblyLoadCallback> _assemblyLoaders = new();

    static IsolatedRuntimeHost()
    {
        var hostBinariesDir = Path.Combine(
            Path.GetDirectoryName(typeof(IsolatedRuntimeHost).Assembly.Location)!,
            "IsolatedRuntimeHost");
        _modulePath = Path.Combine(hostBinariesDir, "DotNetIsolator.WasmApp.wasm");
        _wasmBclDir = Path.Combine(hostBinariesDir, "WasmAssemblies");
    }

    public IsolatedRuntimeHost()
    {
        Engine = new Engine();
        Linker = new Linker(Engine);
        Module = Module.FromFile(Engine, _modulePath);

        Linker.DefineWasi();
        AddIsolatedImports();
        _assemblyLoaders.Add(LoadAssemblyFromWasmBcl);
    }

    internal Engine Engine { get; }
    internal Linker Linker { get; }
    internal Module Module { get; }
    internal WasiConfiguration WasiConfigurationOrDefault
        => _wasiConfiguration ?? new WasiConfiguration().WithInheritedStandardOutput();

    public IsolatedRuntimeHost WithWasiConfiguration(WasiConfiguration configuration)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (_wasiConfiguration is not null)
        {
            throw new InvalidOperationException($"{WithWasiConfiguration} can only be called once.");
        }

        _wasiConfiguration = configuration;
        return this;
    }

    public IsolatedRuntimeHost WithAssemblyLoader(AssemblyLoadCallback callback)
    {
        if (callback is null)
        {
            throw new ArgumentNullException(nameof(callback));
        }

        _assemblyLoaders.Add(callback);
        return this;
    }

    public IsolatedRuntimeHost WithBinDirectoryAssemblyLoader()
    {
        var binDir = Path.GetDirectoryName(typeof(IsolatedRuntimeHost).Assembly.Location)!;
        return WithAssemblyLoader(assemblyName =>
        {
            var path = Path.Combine(binDir, $"{assemblyName}.dll");
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        });
    }

    public void Dispose()
    {
        Module.Dispose();
        Linker.Dispose();
        Engine.Dispose();
    }

    private static byte[]? LoadAssemblyFromWasmBcl(string assemblyName)
    {
        var path = Path.Combine(_wasmBclDir, $"{assemblyName}.dll");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private void AddIsolatedImports()
    {
        Linker.DefineFunction("dotnetisolator", "request_assembly", (CallerFunc<int, int, int, int, int>)HandleRequestAssembly);
        Linker.DefineFunction("dotnetisolator", "set_timeout", (CallerAction<int>)HandleSetTimeout);
        Linker.DefineFunction("dotnetisolator", "queue_callback", (CallerAction)HandleQueueCallback);
    }

    private void HandleSetTimeout(Caller caller, int timeout)
    {
        var runtime = IsolatedRuntime.FromStore(caller.Store);

        ThreadPool.QueueUserWorkItem(static async data =>
        {
            var (runtime, timeout) = data;

            await Task.Delay(timeout + 10); // TODO: Find a more robust way to make the timer callback run. This doesn't always run if the 10ms buffer is removed.

            try
            {
                // TODO: Have something like a sync context so we know there's only one top-level
                // callstack in the guest at a time. Currently this just relies on hoping that
                // the guest is idle.
                var method = runtime.GetMethod("System.Private.CoreLib.dll", "System.Threading", null, "TimerQueue", "TimeoutCallback", -1);
                method.InvokeVoid(null);
            }
            catch (Exception ex)
            {
                // This logic is like mini-wasm.c's mono_set_timeout_exec, which also has to swallow exceptions because
                // we're outside the context of any callstack.
                // TODO: Consider trying to call some kind of unhandled exception function inside the runtime
                Console.Error.WriteLine(ex);
            }
        }, (runtime, timeout), true);
    }

    private void HandleQueueCallback(Caller caller)
    {
        var runtime = IsolatedRuntime.FromStore(caller.Store);
        ThreadPool.QueueUserWorkItem(static async (runtime) =>
        {
            // TODO: Have something like a sync context so we know only one thread is calling at a time
            await Task.Yield();
            try
            {
                var method = runtime.GetMethod("System.Private.CoreLib.dll", "System.Threading", null, "ThreadPool", "Callback", -1);
                method.InvokeVoid(null);
            }
            catch (Exception ex)
            {
                // This logic is like mini-wasm.c's mono_set_timeout_exec, which also has to swallow exceptions because
                // we're outside the context of any callstack.
                // TODO: Consider trying to call some kind of unhandled exception function inside the runtime
                Console.Error.WriteLine(ex);
            }
        }, runtime, false);
    }

    private int HandleRequestAssembly(Caller caller, int assemblyNamePtr, int assemblyNameLen, int suppliedBytesPtr, int suppliedBytesLen)
    {
        var memory = caller.GetMemory("memory") ?? throw new InvalidOperationException("Caller lacks required export 'memory'");
        var assemblyName = memory.ReadString(assemblyNamePtr, assemblyNameLen);

        foreach (var loader in _assemblyLoaders)
        {
            var assemblyBytes = loader(assemblyName);
            if (assemblyBytes is not null)
            {
                // No need to free this memory after as it's held permanently to represent the assembly
                var malloc = caller.GetFunction("malloc") ?? throw new InvalidOperationException("Caller lacks required export 'malloc'");
                var copiedAssemblyBytesPtr = CopyValue(
                    malloc.WrapFunc<int, int>()!,
                    memory,
                    assemblyBytes);
                memory.Write(suppliedBytesPtr, copiedAssemblyBytesPtr);
                memory.Write(suppliedBytesLen, assemblyBytes.Length);
                return 1;
            }
        }

        return 0;
    }

    private static int CopyValue(Func<int, int> malloc, Memory memory, ReadOnlySpan<byte> value)
    {
        var resultPtr = malloc(value.Length);
        if (resultPtr == 0)
        {
            throw new InvalidOperationException($"malloc failed when trying to allocate {value.Length} bytes");
        }

        var destinationSpan = memory.GetSpan(resultPtr, value.Length);
        value.CopyTo(destinationSpan);
        return resultPtr;
    }
}

public delegate byte[]? AssemblyLoadCallback(string assemblyName);
