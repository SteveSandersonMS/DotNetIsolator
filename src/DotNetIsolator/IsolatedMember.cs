namespace DotNetIsolator;

public abstract class IsolatedMember : IDisposable, IEquatable<IsolatedMember>, IIsolatedGCHandle
{
    public IsolatedRuntime Runtime { get; }

    internal readonly int _monoPtr;

    public nint Handle => _monoPtr;

    IsolatedObject? reflectionObject;

    public IsolatedObject ReflectionObject => reflectionObject ??= GetReflectionObject();

    public IsolatedMember(IsolatedRuntime runtimeInstance, int monoPtr)
    {
        Runtime = runtimeInstance;
        _monoPtr = monoPtr;
    }

    protected abstract IsolatedObject GetReflectionObject();

    public virtual bool Equals(IsolatedMember other)
    {
        return
            Runtime == other.Runtime &&
            Handle == other.Handle;
    }

    public override bool Equals(object obj)
    {
        return obj is IsolatedMember method && Equals(method);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Runtime, Handle);
    }

    public override string ToString()
    {
        return ReflectionObject.ToString();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (reflectionObject != null)
            {
                reflectionObject.Dispose();
                reflectionObject = null;
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    int? IIsolatedGCHandle.GetGCHandle(IsolatedRuntime runtime)
    {
        return ReflectionObject.GetGCHandle(runtime);
    }
}
