using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pos.Infra.Json;

public static class SyncJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
