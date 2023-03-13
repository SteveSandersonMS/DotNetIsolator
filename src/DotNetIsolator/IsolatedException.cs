namespace DotNetIsolator;

public class IsolatedException : Exception
{
    public IsolatedException(string? message) : base(message)
    {
    }
}