using System.Buffers;

namespace DotNetIsolator;

public sealed class IsolatedAllocator : MemoryManager<byte>, IBufferWriter<byte>, IDisposable
{
    readonly IsolatedRuntime _runtimeInstance;

    const int minSize = 4;

    int _memoryPtr;
    int _offset = sizeof(int);
    int _allocatedSize;

    public IsolatedAllocator(IsolatedRuntime runtimeInstance)
    {
        _runtimeInstance = runtimeInstance;
    }

    public int Release()
    {
        int ptr = _memoryPtr;

        if (ptr == 0)
        {
            // allocate empty buffer to hold the length
            ptr = _runtimeInstance.Alloc(sizeof(int));
        }

        _runtimeInstance.WriteInt32(ptr, _offset - sizeof(int));

        _memoryPtr = 0;
        _offset = sizeof(int);
        _allocatedSize = 0;

        return ptr;
    }

    private void Reserve(int size)
    {
        if (_allocatedSize == 0)
        {
            int newSize = minSize;
            while (newSize < size)
            {
                newSize *= 2;
            }
            _memoryPtr = _runtimeInstance.Alloc(newSize);
            _allocatedSize = newSize;
        }
        else if (size > _allocatedSize)
        {
            int newSize = _allocatedSize;
            while (newSize < size)
            {
                newSize *= 2;
            }
            _memoryPtr = _runtimeInstance.Realloc(_memoryPtr, newSize);
            _allocatedSize = newSize;
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
        Reserve(_offset + Math.Max(minSize, sizeHint));
        return CreateMemory(_offset, _allocatedSize - _offset);
    }

    protected override void Dispose(bool disposing)
    {
        if(_memoryPtr != 0)
        {
            _runtimeInstance.Free(_memoryPtr);
            _memoryPtr = 0;
        }
    }

    public override Span<byte> GetSpan()
    {
        return _runtimeInstance.GetMemory(_memoryPtr, _allocatedSize);
    }

    public override MemoryHandle Pin(int elementIndex = 0)
    {
        throw new NotSupportedException();
    }

    public override void Unpin()
    {
        throw new NotSupportedException();
    }
}