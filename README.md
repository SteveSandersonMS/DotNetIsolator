# DotNetIsolator [EXPERIMENTAL]

Lets your .NET code run other .NET code in an isolated environment easily.

Basic concept:

1. Create as many `IsolatedRuntime` instances as you like.
   * Each one is actually a WebAssembly sandbox built with [dotnet-wasi-sdk](https://github.com/dotnet/dotnet-wasi-sdk) and running on [Wasmtime](https://github.com/bytecodealliance/wasmtime-dotnet).
   * Each one has a completely separate memory space and no direct access to the host machine's disk/network/OS/etc.
2. Make .NET calls into `IsolatedRuntime` instances.
   * Either create `IsolatedObject` instances within those runtimes then invoke their methods
   * ... or just call a lambda method directly
   * You can pass/capture/return arbitrary values across the boundary, and they will be serialized automatically via Messagepack

This is **experimental** and **unsupported**. It may or may not be developed any further. There will definitely be functional gaps. There are no guarantees [about security](#security-notes).

## Getting started

First, install the package:

```
dotnet add package DotNetIsolator --prerelease
```

Now try this code:

```cs
// Set up an isolated runtime
using var host = new IsolatedRuntimeHost().WithBinDirectoryAssemblyLoader();
using var runtime = new IsolatedRuntime(host);

// Output: I'm running on X64
Console.WriteLine($"I'm running on {RuntimeInformation.OSArchitecture}");

runtime.Invoke(() =>
{
    // Output: I'm running on Wasm
    Console.WriteLine($"I'm running on {RuntimeInformation.OSArchitecture}");
});
```

Or, for a more involved example:

```cs
// Set up the runtime
using var host = new IsolatedRuntimeHost().WithBinDirectoryAssemblyLoader();
using var isolatedRuntime = new IsolatedRuntime(host);

// Evaluate the environment info in both the host runtime and the isolated one
var realInfo = GetEnvironmentInfo();
var isolatedInfo = isolatedRuntime.Invoke(GetEnvironmentInfo);
Console.WriteLine($"Real env: {realInfo}");
Console.WriteLine($"Isolated env: {isolatedInfo}");

static EnvInfo GetEnvironmentInfo()
{
    var sysRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? "(Not set)";
    return new EnvInfo(
        Environment.GetEnvironmentVariables().Count,
        $"SystemRoot={sysRoot}");
}

// Demonstrates that you can return arbitrarily-typed objects
record EnvInfo(int NumEnvVars, string ExampleEnvVar)
{
    public override string ToString() => $"{NumEnvVars} entries, including {ExampleEnvVar}";
}
```

Output, which will differ slighly on macOS/Linux:

```
Real env: 64 entries, including SystemRoot=C:\WINDOWS
Isolated env: 0 entries, including SystemRoot=(Not set)
```

## API guides

### Creating an `IsolatedRuntimeHost`

First you must create an `IsolatedRuntimeHost`. These host objects can be shared across users, since they don't hold any per-runtime state. If you're using DI, you could register it as a singleton.

The purpose of this is to:

 * Start up [Wasmtime](https://github.com/bytecodealliance/wasmtime-dotnet). This takes ~400ms so you only want to do it once and not every time you instantiate an `IsolatedRuntime`
 * Configure assembly loading

**Configuring assembly loading**

The isolated .NET runtime instances need to load .NET assemblies in order to do anything useful. This package includes a WebAssembly-specific .NET base class library (BCL) for low-level .NET types such as `int`, `string`, `Dictionary<T, U>`, etc. Isolated runtimes always have permission to load these prebundled BCL assemblies.

You will almost always also want to load application-specific assemblies into your isolated runtimes, so that you can run your own code. The easiest way to configure assembly loading is to use `WithBinDirectoryAssemblyLoader`:

```cs
using var host = new IsolatedRuntimeHost()
    .WithBinDirectoryAssemblyLoader();
```

This grants permission to load .NET assemblies from your host application's `bin` directory. This makes it possible to:

 * Invoke lambda methods (since the code for those methods is inside the DLLs in your `bin` directory)
 * Instantiate objects of arbitrary types inside isolated runtimes
 * Pass/return types declared in your own application or other packages that you reference

Note that `WithBinDirectoryAssemblyLoader` does **not** allow the guest code to escape from its sandbox. Even though it can load assemblies from the host application, it can only use them within its sandbox.

If you want to impose stricter controls over assembly loading, then instead of `WithBinDirectoryAssemblyLoader`, you can use `WithAssemblyLoader`:

```cs
using var host = new IsolatedRuntimeHost()
    .WithAssemblyLoader(assemblyName =>
    {
        switch (assemblyName)
        {
            case "MyAssembly":
                return File.ReadAllBytes("some/path/to/MyAssembly.dll");
        }

        return null; // Unknown assembly. Maybe another loader will find it.
    });
```

You can register as many assembly loaders as you wish.

### Creating an `IsolatedRuntime`

Once you have an `IsolatedRuntimeHost`, it's trivial to create as many runtimes as you like:

```cs
using var runtime1 = new IsolatedRuntime(host);
using var runtime2 = new IsolatedRuntime(host);
```

Currently, each runtime takes ~8ms to instantiate.

### Calling lambdas

Once you have an `IsolatedRuntime`, you can dispatch calls into them by using `Invoke` and lambda methods:

```cs
var person1 = new Person(3);
var person2 = new Person(9);

var sumOfAges = runtime.Invoke(() =>
{
    // This runs inside the isolated runtime.
    // Notice that we can use closure-captured values/objects too.
    // They will be serialized in using MessagePack.
    return person1.Age + person2.Age;
});

// Output: The isolated runtime calculated the result: 12
Console.WriteLine($"The isolated runtime calculated the result: {sumOfAges}");

record Person(int Age);
```

Note that if the lambda mutates the value of captured objects or static fields, those changes will only take effect inside the isolated runtime. It cannot affect objects in the host runtime, since there is no direct sharing of memory:

```cs
public static int StaticCounter = 0;

private static void Main(string[] args)
{
    using var host = new IsolatedRuntimeHost().WithBinDirectoryAssemblyLoader();
    using var runtime = new IsolatedRuntime(host);

    int localValue = 0;

    runtime.Invoke(() =>
    {
        StaticCounter++;
        localValue++;
        Console.WriteLine($"(isolated) StaticCounter={StaticCounter}, localValue={localValue}");
    });

    Console.WriteLine($"(host)     StaticCounter={StaticCounter}, localValue={localValue}");

    // The output is:
    // (isolated) StaticCounter=1, localValue=1
    // (host)     StaticCounter=0, localValue=0
}
```

### Instantiating isolated objects

Using lambdas is convenient, but only works if the isolated runtime is allowed to load the assemblies from your `bin` directory (because that's where the code is).

As an alternative, you can manually instantiate isolated objects inside the isolated runtime, then call methods on them. For example:

```cs
// Generic API
IsolatedObject obj1 = runtime.CreateObject<Person>();

// String-based API (useful if the host app doesn't reference the assembly containing the type)
IsolatedObject obj2 = runtime.CreateObject("MyAssembly", "MyNamespace", "Person");
```

`CreateObject` requires the object type to have a parameterless constructor. Support for constructor parameters isn't yet implemented (but would be simple to do).

### Calling methods on isolated objects

You can use `Invoke` or `InvokeVoid` to find a method and invoke it in a single step. For example, if the object has a method `void DoSomething(int value)`:

```cs
isolatedObject.InvokeVoid("DoSomething", 123);
```

If it has a return value, you must specify the type as a generic parameter. For example, if the object has a method `TimeSpan GetAge(bool includeGestation)`:

```cs
TimeSpan result = isolatedObject.Invoke<bool, TimeSpan>("GetAge", /* includeGestation */ true);
```

Alternatively you can capture a reference to an `IsolateMethod` so you can invoke it later. This is similar to a `MethodInfo` so it isn't bound to a specific target object.

```cs
var getAgeMethod = isolatedObject.FindMethod("GetAge");

// ... then later:
var age = getAgeMethod.Invoke<bool, TimeSpan>(isolatedObject, /* includeGestation */ true);
```

You can also find methods without having to instantiate any objects first:

```cs
var getAgeMethod = isolatedRuntime.GetMethod(typeof(Person), "GetAge");
```

## Calling the host from the guest

The host may register named callbacks that can be invoked from guest code. For example:

```cs
using var runtime = new IsolatedRuntime(host);
runtime.RegisterCallback("addTwoNumbers", (int a, int b) => a + b);
runtime.RegisterCallback("getHostTime", () => DateTime.Now);
```

To call these from guest code, have the guest code's project reference the `DotNetIsolator.Guest` package, and then use `DotNetIsolatorHost.Invoke`, e.g.:

```cs
var sum = DotNetIsolatorHost.Invoke<int>("addTwoNumbers", 123, 456);
var hostTime = DotNetIsolatorHost.Invoke<DateTime>("getHostTime");
```

Note that if you're calling via a lambda, then the guest code is in the same assembly as the host code, so in that case you need the host project to reference the `DotNetIsolator.Guest` package.

## Building this repo from source

You'll need to install [wizer](https://github.com/bytecodealliance/wizer):
```
cargo install wizer --all-features
```

## Security notes

If you want to rely on this isolation as a critical security boundary in your application, you should bear in mind that:

 * **This is an experimental prerelease package**. No security review has taken place. There could be defects that allow guest code to cause unintentional effects on the host.
 * WebAssembly itself defines an extremely well-proven sandbox (browsers run untrusted WebAssembly modules from any website, and have done so for years with a solid track record), but:
   * Wasmtime is a different implementation than what runs inside your browser. Learn more at [Security and Correctness in Wasmtime](https://bytecodealliance.org/articles/security-and-correctness-in-wasmtime).
   * The security model for WebAssembly doesn't directly address side-channel attacks (e.g., [spectre](https://en.wikipedia.org/wiki/Spectre_(security_vulnerability))). There are robust solutions for this but it's outside the scope of this repo.

In summary:

 * If you used this as one layer in a multi-layered security model, it would be a pretty good layer! But nobody's promising it's bulletproof on its own.
 * If you're not running potentially hostile code, and are merely using this to manage the isolation of your own code, most of the above considerations don't apply.

## Support and feedback

This is completely unsupported. There are no promises that this will be developed any further. It is published only to help people explore what they could do with this sort of capability.

You are free to [report issues](https://github.com/SteveSandersonMS/DotNetIsolator/issues) but please don't assume you'll get any response, much less a fix.
