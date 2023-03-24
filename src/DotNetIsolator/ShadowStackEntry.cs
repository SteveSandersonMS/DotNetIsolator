namespace DotNetIsolator;

internal readonly ref struct ShadowStackEntry<T> where T: unmanaged
{
    public readonly ref T Value;
    public readonly int Address;
    private readonly ShadowStack _owner;

    public ShadowStackEntry(ShadowStack owner, ref T value, int address)
    {
        _owner = owner;
        Value = ref value;
        Address = address;
    }

    public void Pop()
    {
        _owner.Pop<T>(Address);
    }
}
