// This entrypoint is a no-op because we only want the wasm _start export to start
// the .NET runtime and then not do anything. The real work happens when the host
// uses an export to pass in some other .NET assemblies to execute.

AppContext.SetSwitch("System.Resources.UseSystemResourceKeys", true);
AppContext.SetSwitch("System.Globalization.Invariant", true);

// For preinit, warm up the serialization code paths
var captured = new { prop1 = new List<string> { "a", "b" } };
var lambda = (string a, bool b) => { Console.WriteLine(a + b + captured.prop1.Count + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture); };
var serialized2 = DotNetIsolator.WasmApp.Serialization.Serialize(lambda.Target!);
var deserialized2 = DotNetIsolator.WasmApp.Serialization.Deserialize(serialized2);
