using DotNetIsolator.Test;
using System.Globalization;
using System.Text;
using Xunit;

namespace DotNetIsolator;

public class MethodInvocationTest : IDisposable
{
    private readonly IsolatedRuntime _runtime;

    public MethodInvocationTest()
    {
        _runtime = new IsolatedRuntime(SharedHost.Instance);
    }

    [Fact]
    public void CanInvokePrivateParameterlessVoidMethod()
        => _runtime.CreateObject<TestClass>().InvokeVoid("PrivateVoidMethod");

    [Fact]
    public void CanInvokeParameterlessIntMethod()
        => Assert.Equal(123, _runtime.CreateObject<TestClass>().Invoke<int>(nameof(TestClass.IntMethod)));

    [Fact]
    public void CanInvokeParameterlessStringMethod()
        => Assert.Equal("Hello", _runtime.CreateObject<TestClass>().Invoke<string>(nameof(TestClass.StringMethod)));

    [Fact]
    public void CanInvokeIntParamMethod()
        => Assert.Equal(246, _runtime.CreateObject<TestClass>()
            .Invoke<int, int>(nameof(TestClass.IntParamMethod), 123));

    [Fact]
    public void CanInvokeBoolParamMethod()
    {
        var obj = _runtime.CreateObject<TestClass>();
        Assert.True(obj.Invoke<bool, bool>(nameof(TestClass.BoolParamMethod), true));
        Assert.False(obj.Invoke<bool, bool>(nameof(TestClass.BoolParamMethod), false));
    }

    [Fact]
    public void CanInvokeStringParamMethod()
        => Assert.Equal("HELLO WORLD", _runtime.CreateObject<TestClass>()
            .Invoke<string, string>(nameof(TestClass.StringParamMethod), "Hello world"));

    [Fact]
    public void CanInvokeComplexParamMethod()
    {
        var paramValue = TestClass.MyComplexObject.CreateTestValue();
        var target = _runtime.CreateObject<TestClass>();
        var returnValue = target.Invoke<TestClass.MyComplexObject, string>(nameof(TestClass.ComplexParamMethod), paramValue);
        Assert.Equal(paramValue.ToString().Replace("\r\n", "\n"), returnValue);
    }

    [Fact]
    public void CanReturnComplexType()
    {
        var returnValue = _runtime.CreateObject<TestClass>()
            .Invoke<TestClass.MyComplexObject>(nameof(TestClass.ComplexMethod));
        var expectedReturnValueToString = TestClass.MyComplexObject.CreateTestValue().ToString();
        Assert.Equal(expectedReturnValueToString, returnValue!.ToString());
    }

    [Fact]
    public void CanInvokeMultipleParamsMethod()
        => Assert.Equal("[a=123][b=True][c=Hello]", _runtime.CreateObject<TestClass>()
            .Invoke<int, bool, string, string>(nameof(TestClass.SimpleParamsMethod), 123, true, "Hello"));

    [Fact]
    public void GuestExceptionsSurfaceInHost()
    {
        var obj = _runtime.CreateObject<TestClass>();

        var ex = Assert.Throws<IsolatedException>(() => obj.InvokeVoid(nameof(TestClass.ThrowException)));
        Assert.Contains("System.InvalidTimeZoneException: This is a guest exception", ex.ToString());
        Assert.Contains($"at {typeof(TestClass).FullName!.Replace('+', '.')}.ThrowException()", ex.ToString());
    }

    [Fact]
    public void ThrowsIfAssemblyNotFound()
    {
        var ex = Assert.Throws<IsolatedException>(() => _runtime.GetMethod("FakeAssembly", "SomeNamespace", "SomeDeclaringType", "SomeTypeName", "SomeMethodName"));
        Assert.Equal("Could not find method [FakeAssembly]SomeNamespace.SomeDeclaringType/SomeTypeName::SomeMethodName because the assembly FakeAssembly could not be found.", ex.Message);
    }

    [Fact]
    public void ThrowsIfTypeNotFound()
    {
        var assemblyName = typeof(TestClass).Assembly.GetName().Name!;
        var @namespace = typeof(TestClass).Namespace;
        var declaringTypeName = typeof(TestClass).DeclaringType!.Name;
        var typeName = typeof(TestClass).Name + "_fake";
        var methodName = nameof(TestClass.IntMethod);
        var ex = Assert.Throws<IsolatedException>(() => _runtime.GetMethod(assemblyName, @namespace, declaringTypeName, typeName, methodName));
        Assert.Equal($"Could not find method [{assemblyName}]{@namespace}.{declaringTypeName}/{typeName}::{methodName} because the type {declaringTypeName}/{typeName} could not be found in the assembly.", ex.Message);
    }

    [Fact]
    public void ThrowsIfMethodNotFound()
    {
        var assemblyName = typeof(TestClass).Assembly.GetName().Name!;
        var @namespace = typeof(TestClass).Namespace;
        var declaringTypeName = typeof(TestClass).DeclaringType!.Name;
        var typeName = typeof(TestClass).Name;
        var methodName = nameof(TestClass.IntMethod) + "_fake";
        var ex = Assert.Throws<IsolatedException>(() => _runtime.GetMethod(assemblyName, @namespace, declaringTypeName, typeName, methodName));
        Assert.Equal($"Could not find method [{assemblyName}]{@namespace}.{declaringTypeName}/{typeName}::{methodName} because the method {methodName} could not be found in the type, or it did not have the correct number of parameters.", ex.Message);
    }

    // Test: intentionally passing wrong number of params or wrong types
    // Test: polymorphism - passing a subclass of what the fn declares, or saying the return type is more general than what the fn declares
    // Test: invoking virtual methods that are overridden on your target object type

    public void Dispose()
    {
        _runtime.Dispose();
    }

    class TestClass
    {
        private void PrivateVoidMethod()
        {
        }

        public int IntMethod()
            => 123;

        public string StringMethod()
            => "Hello";

        public MyComplexObject ComplexMethod()
            => MyComplexObject.CreateTestValue();

        public int IntParamMethod(int val)
            => val * 2;

        public bool BoolParamMethod(bool val)
            => val;

        public string StringParamMethod(string val)
            => val.ToUpperInvariant();

        public string ComplexParamMethod(MyComplexObject val)
            => val.ToString()!;

        public string SimpleParamsMethod(int a, bool b, string c)
            => $"[a={a}][b={b}][c={c}]";

            public void ThrowException()
                => throw new InvalidTimeZoneException("This is a guest exception");

        public class MyComplexObject
        {
            private string? _privateField;
            public int PublicIntProp { get; set; }
            public IDictionary<string, object>? PublicDictionaryProp { get; set; }

            public static MyComplexObject CreateTestValue()
            {
                var result = new MyComplexObject
                {
                    PublicIntProp = 123,
                    PublicDictionaryProp = new Dictionary<string, object>
                    {
                        { "key1", "Value 1" },
                        { "key2", true },
                        { "key3", new DateTime(2023, 3, 15) },
                    }
                };
                result.SetPrivateField("This is private");
                return result;
            }

            public void SetPrivateField(string value)
            {
                _privateField = value;
            }

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"{nameof(_privateField)}: {_privateField}");
                sb.AppendLine($"{nameof(PublicIntProp)}: {PublicIntProp}");
                if (PublicDictionaryProp is not null)
                {
                    foreach (var kvp in PublicDictionaryProp)
                    {
                        sb.AppendLine($"{nameof(PublicDictionaryProp)}[{kvp.Key}]: {(kvp.Value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : kvp.Value)}");
                    }
                }
                return sb.ToString();
            }
        }
    }
}
