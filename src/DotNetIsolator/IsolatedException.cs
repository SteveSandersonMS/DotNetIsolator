namespace DotNetIsolator;

public class IsolatedException : Exception
{
    public IsolatedObject? InnerExceptionObject { get; }

    internal IsolatedException()
    {

    }

    public IsolatedException(string? message) : base(message)
    {

    }

    public IsolatedException(string? message, IsolatedObject innerExceptionObject) : base(message, DeserializeException(innerExceptionObject))
    {
        InnerExceptionObject = innerExceptionObject;
    }

    static Exception DeserializeException(IsolatedObject innerExceptionObject)
    {
        try
        {
            return innerExceptionObject.Deserialize<Exception>();
        }
        catch (Exception e)
        {
            return e;
        }
    }
}