namespace DotNetIsolator;

#pragma warning disable CS0649
internal struct GuestToHostCall
{
    public string CallbackName;
    public byte[]?[] ArgsSerialized;
}
#pragma warning restore CS0649
