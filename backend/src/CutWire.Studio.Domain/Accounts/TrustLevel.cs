using System.Text.Json.Serialization;

namespace CutWire.Studio.Domain.Accounts;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TrustLevel
{
    T0 = 0,
    T1 = 1,
    T2 = 2,
    T3 = 3,
}
