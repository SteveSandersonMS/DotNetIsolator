namespace DotNetIsolator;

internal readonly ref struct ShadowStackEntry<T> where T: unmanaged
{
    private readonly Span<T> _value;
    public ref T Value => ref _value[0];
    public readonly int Address;
    private readonly ShadowStack _owner;

    public ShadowStackEntry(ShadowStack owner, Span<T> value, int address)
    {
        _owner = owner;
        _value = value;
        Address = address;
    }

    public void Pop()
    {
        _owner.Pop<T>(Address);
    }
}
