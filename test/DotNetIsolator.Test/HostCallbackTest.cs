using DotNetIsolator.Test;
using Xunit;

namespace DotNetIsolator;

public class HostCallbackTest : IDisposable
{
    private readonly IsolatedRuntime _runtime;

    public HostCallbackTest()
    {
        _runtime = new IsolatedRuntime(SharedHost.Instance);
    }

    [Fact]
    public void CanCallParameterlessVoidMethod()
    {
        var receivedCall = false;
        _runtime.RegisterCallback("name", () => receivedCall = true);
        _runtime.Invoke(() => DotNetIsolatorHost.Invoke("name"));
        Assert.True(receivedCall);
    }

    [Fact]
    public void CanSupplyParams()
    {
        Person? receivedPerson = null;
        DateTime? receivedDate = null;
        _runtime.RegisterCallback("name", (Person person, DateTime date) =>
        {
            receivedPerson = person;
            receivedDate = date;
        });

        var suppliedPerson = new Person { Name = "Bert", Age = 123 };
        var suppliedDate = DateTime.Now;
        _runtime.Invoke(() => DotNetIsolatorHost.Invoke("name", suppliedPerson, suppliedDate));

        Assert.Equal(suppliedPerson.Name, receivedPerson!.Name);
        Assert.Equal(suppliedPerson.Age, receivedPerson!.Age);
        Assert.Equal(suppliedDate.ToUniversalTime(), receivedDate!.Value.ToUniversalTime());

        // It was serialized across both hops, so we have a new instance
        Assert.NotSame(suppliedPerson, receivedPerson);
    }

    [Fact]
    public void CanGetResult()
    {
        var suppliedPerson = new Person { Name = Guid.NewGuid().ToString(), Age = Random.Shared.Next() };
        
        _runtime.RegisterCallback("name", () => suppliedPerson);
        var receivedPerson = _runtime.Invoke(() => DotNetIsolatorHost.Invoke<Person>("name"));

        Assert.Equal(suppliedPerson.Name, receivedPerson!.Name);
        Assert.Equal(suppliedPerson.Age, receivedPerson!.Age);

        // It was serialized across both hops, so we have a new instance
        Assert.NotSame(suppliedPerson, receivedPerson);
    }

    [Fact]
    public void GetErrorIfCallbackNameIsUnknown()
    {
        var message = _runtime.Invoke(() =>
        {
            try
            {
                DotNetIsolatorHost.Invoke("UnknownCallbackName", 123, 456);
                throw new InvalidOperationException("Should not be reached");
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        });

        Assert.Contains("UnknownCallbackName", message);
    }

    [Fact]
    public void CanCatchExceptions()
    {
        _runtime.RegisterCallback("willThrow", (Action)(() => throw new InvalidTimeZoneException("secret")));
        var message = _runtime.Invoke(() =>
        {
            try
            {
                DotNetIsolatorHost.Invoke("willThrow");
                throw new InvalidOperationException("Should not be reached");
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        });

        Assert.Equal("Call to host failed: The call failed. See host console logs for details.", message);
        Assert.DoesNotContain("secret", message); // I know this is implied by the other assertion, but let's be really clear
    }

    public void Dispose()
    {
        _runtime.Dispose();
    }

    class Person
    {
        public string? Name { get; set; }
        public int Age { get; set; }
    }
}
