namespace DotNetIsolator;

public abstract class IsolatedMember : IDisposable, IIsolatedGCHandle
{
    IsolatedObject? reflectionObject;

    public IsolatedObject ReflectionObject => reflectionObject ??= GetReflectionObject();

    protected abstract IsolatedObject GetReflectionObject();

    public override string ToString()
    {
        return ReflectionObject.ToString();
    }

    public abstract override bool Equals(object obj);

    public abstract override int GetHashCode();

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
