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
        return WithDirectoryAssemblyLoader(binDir);
    }

    public IsolatedRuntimeHost WithDirectoryAssemblyLoader(string directoryPath)
    {
        return WithAssemblyLoader(assemblyName =>
        {
            var path = Path.Combine(directoryPath, $"{assemblyName}.dll");
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
        Linker.DefineFunction("dotnetisolator", "call_host", (CallerAction)HandleCallHost);
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

    private void HandleCallHost(Caller caller)
    {
        Console.WriteLine(".NET HandleCallHost");
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
