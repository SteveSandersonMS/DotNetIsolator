using MessagePack;

namespace DotNetIsolator.Internal;

#pragma warning disable CS0649

[MessagePackObject]
public struct GuestToHostCall
{
    [Key(0)] public string CallbackName;
    [Key(1)] public byte[]?[] Args;
    [Key(2)] public bool IsRawCall; // Means the args aren't seralized - they are raw byte arrays
}

#pragma warning restore CS0649
