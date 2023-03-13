using DotNetIsolator.Test;
using System.Runtime.InteropServices;
using Xunit;

namespace DotNetIsolator;

public class LambdaInvocationTest : IDisposable
{
    private readonly IsolatedRuntime _runtime;

    public LambdaInvocationTest()
    {
        _runtime = new IsolatedRuntime(SharedHost.Instance);
    }

    [Fact]
    public void CanInvokeLambda_Static_Void()
    {
        _runtime.Invoke(static () => { });
    }

    [Fact]
    public void CanInvokeLambda_Static_IntReturning()
    {
        Assert.Equal(123, _runtime.Invoke(static () => 123));
    }

    [Fact]
    public void CanInvokeLambda_Capturing()
    {
        var capturedValue = 123;
        Assert.Equal(123, _runtime.Invoke(() => capturedValue));
    }

    [Fact]
    public void CanInvokeLambda_Capturing_Complex()
    {
        var intVal = 123;
        var boolVal = true;
        var complexVal = new Person { Name = "SomeName", Hobbies = new List<string> { "H1", "H2" } };
        var expectedResult = $"[intVal={intVal}][boolVal={boolVal}][complexVal={complexVal}][arch=Wasm]";

        Assert.Equal(expectedResult,
            _runtime.Invoke(() => $"[intVal={intVal}][boolVal={boolVal}][complexVal={complexVal}][arch={RuntimeInformation.OSArchitecture}]"));
    }

    public void Dispose()
    {
        _runtime.Dispose();
    }

    private class Person
    {
        public string? Name { get; set; }
        public List<string>? Hobbies { get; set; }

        public override string ToString()
        {
            return $"{Name} with hobbies {string.Join(",", Hobbies ?? Enumerable.Empty<string>())}";
        }
    }
}
