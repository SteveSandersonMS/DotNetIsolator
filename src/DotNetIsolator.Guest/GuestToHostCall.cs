using MessagePack;

namespace DotNetIsolator.Internal;

#pragma warning disable CS0649

[MessagePackObject]
public struct GuestToHostCall
{
    [Key(0)] public string CallbackName;
    [Key(1)] public byte[]?[] ArgsSerialized;
}

#pragma warning restore CS0649
