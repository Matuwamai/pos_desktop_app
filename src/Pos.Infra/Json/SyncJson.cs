using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pos.Infra.Json;

/// <summary>
/// The one <see cref="JsonSerializerOptions"/> instance used everywhere an outbox
/// payload crosses a boundary — written to <c>outbox.payload_json</c> by the sale/
/// drawer/customer write path, and read back by <see cref="Pos.Infra.Sync.SyncEngine"/>
/// to build the <c>SyncOpEnvelope</c> actually POSTed by
/// <see cref="Pos.Infra.Api.ApiClient"/>. Deliberately a single shared instance rather
/// than three separate `new JsonSerializerOptions(...)` calls: those would all look
/// identical today, but any future drift between them would silently corrupt what gets
/// pushed (wrong casing on a field name the server's zod schema then rejects).
///
/// <c>DefaultIgnoreCondition = WhenWritingNull</c> is load-bearing, not cosmetic:
/// every optional field in the server's zod schemas (<c>services/offlineSync.js</c>'s
/// <c>ORDER_PAYLOAD</c> etc.) is <c>.optional()</c> — meaning the field may be *absent*
/// — not <c>.nullable()</c>. Without this, a null <c>customerId</c>/<c>transactionRef</c>
/// serializes as an explicit JSON <c>null</c>, which zod rejects as "expected string,
/// received null" — confirmed live: every real sale pushed before this fix came back
/// REJECTED for exactly that reason, silently, since the push happens in the
/// background after the sale is already written locally.
/// </summary>
public static class SyncJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
