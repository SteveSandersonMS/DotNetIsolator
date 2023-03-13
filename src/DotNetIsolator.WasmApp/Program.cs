// This entrypoint is a no-op because we only want the wasm _start export to start
// the .NET runtime and then not do anything. The real work happens when the host
// uses an export to pass in some other .NET assemblies to execute.

AppContext.SetSwitch("System.Resources.UseSystemResourceKeys", true);
AppContext.SetSwitch("System.Globalization.Invariant", true);

// For preinit, warm up the serialization code paths
var serialized = DotNetIsolator.WasmApp.Serialization.Serialize(new Dictionary<string, object>
{
    { "key1", new List<string> { "a", "b" } },
    { "key2", true },
    { "key3", 123 },
});
DotNetIsolator.WasmApp.Serialization.Deserialize(serialized);
