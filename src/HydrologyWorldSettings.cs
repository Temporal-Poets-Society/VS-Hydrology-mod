using ProtoBuf;

namespace Hydrology
{
    // The world settings channel (0.1.13, decision 0002): a client asks, and the server answers with its effective Water
    // flooding value. Hydrology is required on clients since 0.6.1; the request-first design still means the server
    // never sends to a client that did not register the channel. Nothing on the client acts on the value yet.
    [ProtoContract]
    public class HydrologySettingsRequest { }

    [ProtoContract]
    public class HydrologyWorldSettings
    {
        [ProtoMember(1)] public bool Flooding;
    }
}
