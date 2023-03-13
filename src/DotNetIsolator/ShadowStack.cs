using System.Runtime.InteropServices;
using Wasmtime;

namespace DotNetIsolator;

internal class ShadowStack : IDisposable
{
    const int Length = 8 * 1024; // It's only used for passing small numbers of parameters, so this is huge

    private readonly Memory _memory;
    private readonly Action<int> _free;
    private int _stackBasePtr;
    private int _stackPtr;

    public ShadowStack(Memory memory, Func<int, int> malloc, Action<int> free)
    {
        _memory = memory;
        _free = free;

        _stackBasePtr = malloc(Length);
        if (_stackBasePtr == 0)
        {
            throw new InvalidOperationException("Failed to malloc ShadowStack");
        }

        _stackPtr = _stackBasePtr;
    }

    public ShadowStackEntry<T> Push<T>() where T: struct
    {
        var ptr = _stackPtr;

        var len = Marshal.SizeOf<T>();
        _stackPtr += len;

        var valueBytes = _memory.GetSpan(ptr, len);
        ref var value = ref MemoryMarshal.AsRef<T>(valueBytes);

        return new ShadowStackEntry<T>(this, ref value, ptr);
    }

    public void Pop<T>(int expectedAddress) where T : struct
    {
        var len = Marshal.SizeOf<T>();
        _stackPtr -= len;

        if (_stackPtr != expectedAddress)
        {
            throw new InvalidOperationException("Mismatching push/pop");
        }

        var resultBytes = _memory.GetSpan(_stackPtr, len);
        resultBytes.Clear();
    }

    public void Dispose()
    {
        _free(_stackBasePtr);
    }
}
