namespace DotNetIsolator;

internal static class ParameterSerializer
{
    public static int SerializeParameter<T>(T value)
    {
        if (value is int intValue)
        {
            return intValue;
        }

        throw new NotSupportedException($"{nameof(SerializeParameter)} does not support parameter type {typeof(T).FullName}");
    }
}
