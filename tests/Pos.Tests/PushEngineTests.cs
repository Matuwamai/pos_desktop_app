using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pos.Core.Sync;
using Pos.Infra.Api;
using Pos.Infra.Storage;
using Pos.Infra.Sync;

namespace Pos.Tests;

/// <summary>
/// The push side of offline-first, against a fake server: draining the outbox, and what
/// happens to the matching local <c>orders</c> row (via its <c>op_id</c>) on
/// <c>CREATED</c> vs. <c>REJECTED</c>. Mirrors <c>SyncEngineTests</c>' fake-handler
/// pattern for the pull side.
/// </summary>
public class PushEngineTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pos-push-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task A_created_result_marks_the_outbox_row_and_its_order_as_synced()
    {
        await using var db = await LocalDatabase.OpenAsync(_dbPath, "test-key-1");
        await SeedPendingOrderAsync(db, opId: "op-1", orderId: "order-1");

        var handler = new FakePushHandler(ops => new PushResponse
        {
            Results = [.. ops.Select(op => new SyncOpResult { OpId = op.OpId, Status = "CREATED", Result = JsonSerializer.SerializeToElement(new { orderId = "order-1", orderNumber = 42, warnings = Array.Empty<string>() }) })],
        });
        var sync = new SyncEngine(db, ApiClientWith(handler));

        var result = await sync.PushPendingAsync("device-token");

        Assert.Equal(1, result.Created);
        Assert.Equal("CREATED", await ScalarAsync(db, "SELECT status FROM outbox WHERE op_id = 'op-1'"));
        Assert.Equal("CREATED", await ScalarAsync(db, "SELECT sync_status FROM orders WHERE id = 'order-1'"));
        Assert.Equal(0, await sync.GetPendingSaleCountAsync()); // no longer PENDING
    }

    [Fact]
    public async Task A_rejected_result_is_preserved_and_not_retried()
    {
        await using var db = await LocalDatabase.OpenAsync(_dbPath, "test-key-2");
        await SeedPendingOrderAsync(db, opId: "op-2", orderId: "order-2");

        var callCount = 0;
        var handler = new FakePushHandler(ops =>
        {
            callCount++;
            return new PushResponse
            {
                Results = [.. ops.Select(op => new SyncOpResult { OpId = op.OpId, Status = "REJECTED", Result = JsonSerializer.SerializeToElement(new { error = "Sale total does not match its lines" }) })],
            };
        });
        var sync = new SyncEngine(db, ApiClientWith(handler));

        var first = await sync.PushPendingAsync("device-token");
        Assert.Equal(1, first.Rejected);
        Assert.Equal("REJECTED", await ScalarAsync(db, "SELECT status FROM outbox WHERE op_id = 'op-2'"));
        Assert.Equal("REJECTED", await ScalarAsync(db, "SELECT sync_status FROM orders WHERE id = 'order-2'"));

        // A second drain must not re-send an already-resolved op — the outbox query only
        // ever selects status = 'PENDING'.
        var second = await sync.PushPendingAsync("device-token");
        Assert.Equal(0, second.Created + second.Rejected + second.Duplicate);
        Assert.Equal(1, callCount);
    }

    private static async Task SeedPendingOrderAsync(LocalDatabase db, string opId, string orderId)
    {
        await using var tx = db.Connection.BeginTransaction();
        await SyncEngine.UpsertAsync(tx, "orders", "id",
            ("id", orderId), ("location_id", "location-1"), ("cash_drawer_session_id", null),
            ("cashier_id", "cashier-1"), ("customer_id", null), ("terminal_receipt_no", "T1-20260101-0001"),
            ("created_at", DateTimeOffset.UtcNow.ToString("O")), ("subtotal", "10.00"), ("discount_total", "0"),
            ("tax_total", "0"), ("total", "10.00"), ("discount_reason", null), ("sync_status", "PENDING"), ("op_id", opId));
        await SyncEngine.UpsertAsync(tx, "outbox", "op_id",
            ("op_id", opId), ("type", SyncOpType.OrderCreate), ("payload_json", "{}"),
            ("client_time", DateTimeOffset.UtcNow.ToString("O")), ("created_at", DateTimeOffset.UtcNow.ToString("O")), ("status", "PENDING"));
        await tx.CommitAsync();
    }

    private static async Task<string?> ScalarAsync(LocalDatabase db, string sql)
    {
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = sql;
        return (string?)await cmd.ExecuteScalarAsync();
    }

    private static ApiClient ApiClientWith(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://fake-server/api/v1/") };
        return new ApiClient(http);
    }

    public async ValueTask DisposeAsync()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        GC.SuppressFinalize(this);
        await Task.CompletedTask;
    }

    private sealed class FakePushHandler(Func<List<SyncOpEnvelope>, PushResponse> respond) : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var json = await request.Content!.ReadAsStringAsync(ct);
            var pushRequest = JsonSerializer.Deserialize<PushRequest>(json, JsonOptions)!;
            var response = respond(pushRequest.Ops);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response, options: JsonOptions) };
        }
    }
}
