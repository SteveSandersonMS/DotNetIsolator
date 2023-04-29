using System.Buffers;

namespace DotNetIsolator;

public sealed class IsolatedAllocator : IBufferWriter<byte>, IDisposable
{
    readonly IsolatedRuntime _runtimeInstance;

    const int minSize = 4;

    int _memoryPtr;
    int _offset;
    int _allocatedSize;

    public IsolatedAllocator(IsolatedRuntime runtimeInstance)
    {
        _runtimeInstance = runtimeInstance;

        _memoryPtr = runtimeInstance.Alloc(minSize);
        _allocatedSize = minSize;
    }

    public int Release()
    {
        int ptr = _memoryPtr;

        _memoryPtr = _runtimeInstance.Alloc(minSize);
        _offset = 0;
        _allocatedSize = minSize;

        return ptr;
    }

    private void Reserve(int size)
    {
        if (size > _allocatedSize)
        {
            _memoryPtr = _runtimeInstance.Realloc(_memoryPtr, _allocatedSize * 2);
            _allocatedSize *= 2;
        }
    }

    public void Advance(int count)
    {
        Reserve(Math.Max(_allocatedSize, _offset + count + minSize));
        _offset += count;
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Reserve(_offset + Math.Max(minSize, sizeHint));
        return _runtimeInstance.GetMemory(_memoryPtr + _offset, _allocatedSize - _offset);
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        if (_memoryPtr != 0)
        {
            _runtimeInstance.Free(_memoryPtr);
            _memoryPtr = 0;
        }
        GC.SuppressFinalize(this);
    }

    ~IsolatedAllocator()
    {
        Dispose();
    }
}