using DotNetIsolator.Test;
using Xunit;

namespace DotNetIsolator;

public class TimerTest
{
    private readonly IsolatedRuntime _runtime;

    public TimerTest()
    {
        _runtime = new IsolatedRuntime(SharedHost.Instance);
    }

    [Fact]
    public async Task CanUseTaskYield()
    {
        var obj = _runtime.CreateObject<TimerTester>();
        var getCount = obj.FindMethod(nameof(TimerTester.GetCount));
        Assert.Equal(0, getCount.Invoke<int>(obj));

        obj.InvokeVoid(nameof(TimerTester.UseTaskYield));
        Assert.Equal(1, getCount.Invoke<int>(obj));

        await WaitAssert(() => Assert.Equal(2, getCount.Invoke<int>(obj)));

        // TODO: Check there wasn't any unhandled exception in HandleQueueCallback
    }

    [Fact]
    public async Task CanUseTaskDelay()
    {
        var obj = _runtime.CreateObject<TimerTester>();
        var getCount = obj.FindMethod(nameof(TimerTester.GetCount));
        Assert.Equal(0, getCount.Invoke<int>(obj));

        obj.InvokeVoid(nameof(TimerTester.UseTaskDelay), 500);

        await Task.Delay(100);
        Assert.Equal(1, getCount.Invoke<int>(obj));
        await WaitAssert(() => Assert.Equal(2, getCount.Invoke<int>(obj)));
    }

    async Task WaitAssert(Action callback)
    {
        for (var attemptCount = 0; attemptCount < 50; attemptCount++)
        {
            try
            {
                callback();
                return; // Success
            }
            catch (Exception)
            {
            }

            await Task.Delay(100);
        }

        callback();
    }

    class TimerTester
    {
        int count;

        public int GetCount() => count;

        // We don't yet support returning a Task across the boundary,
        // so this has to be async void
        public async void UseTaskYield()
        {
            count++;
            await Task.Yield();
            count++;
        }

        // We don't yet support returning a Task across the boundary,
        // so this has to be async void
        public async void UseTaskDelay(int millisecondsDelay)
        {
            count++;
            await Task.Delay(millisecondsDelay);
            count++;
        }
    }
}
