using DotNetIsolator.Test;
using Xunit;

namespace DotNetIsolator;

public class ObjectCreationTest
{
    [Fact]
    public void ThrowsIfAssemblyNotFound()
    {
        using var configWithoutAssemblyLoader = new IsolatedRuntimeHost();
        using var runtime = new IsolatedRuntime(configWithoutAssemblyLoader);

        var ex = Assert.Throws<IsolatedException>(runtime.CreateObject<ObjectCreationTest>);
        Assert.Equal($"Could not load assembly '{typeof(ObjectCreationTest).Assembly.GetName().Name}'", ex.Message);
    }

    [Fact]
    public void ThrowsIfNamespaceNotFound()
    {
        using var runtime = new IsolatedRuntime(SharedHost.Instance);

        var type = typeof(ObjectCreationTest);
        var ex = Assert.Throws<IsolatedException>(
            () => runtime.CreateObject(type.Assembly.GetName().Name!, type.Namespace + "_fake", type.Name));
        Assert.Equal($"Could not find type '{type.Namespace}_fake.{type.Name}' in assembly '{typeof(ObjectCreationTest).Assembly.GetName().Name}'", ex.Message);
    }

    [Fact]
    public void ThrowsIfTypeNotFound()
    {
        using var runtime = new IsolatedRuntime(SharedHost.Instance);

        var type = typeof(ObjectCreationTest);
        var ex = Assert.Throws<IsolatedException>(
            () => runtime.CreateObject(type.Assembly.GetName().Name!, type.Namespace, type.Name + "_fake"));
        Assert.Equal($"Could not find type '{type.Namespace}.{type.Name}_fake' in assembly '{typeof(ObjectCreationTest).Assembly.GetName().Name}'", ex.Message);
    }

    [Fact]
    public void CanInstantiateTypeWithParameterlessConstructor()
    {
        using var runtime = new IsolatedRuntime(SharedHost.Instance);
        Assert.NotNull(runtime.CreateObject<ObjectCreationTest>());
    }

    [Fact]
    public void CanInstantiateNestedType()
    {
        using var runtime = new IsolatedRuntime(SharedHost.Instance);
        Assert.NotNull(runtime.CreateObject<SomeNestedType>());
    }

    [Fact]
    public void CanCopyObjects()
    {
        using var runtime = new IsolatedRuntime(SharedHost.Instance);
        var obj = runtime.CopyObject(new NestedTypeWithValue { Value = "Hello" });
        Assert.Equal("Hello", obj.Invoke<string>(nameof(NestedTypeWithValue.GetValue)));
    }

    [Fact]
    public void CopyObjectThrowsIfTypeCannotBeDeserialized()
    {
        using var runtime = new IsolatedRuntime(SharedHost.Instance);
        var sourceObj = new TypeThatThrowsDuringDeserialize(ignored: 123);

        var ex = Assert.Throws<IsolatedException>(() => runtime.CopyObject(sourceObj));
        Assert.Contains("This type throws if you try to use its parameterless constructor", ex.Message);
    }

    private class SomeNestedType { }

    private class NestedTypeWithValue
    {
        public string? Value { get; set; }

        public string? GetValue() => Value;
    }

    private class TypeThatThrowsDuringDeserialize
    {
        public TypeThatThrowsDuringDeserialize()
        {
            throw new InvalidTimeZoneException("This type throws if you try to use its parameterless constructor.");
        }

        public TypeThatThrowsDuringDeserialize(int ignored) { }
    }
}
