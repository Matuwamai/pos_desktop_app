using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pos.Core.Sync;
using Pos.Infra.Api;
using Pos.Infra.Storage;
using Pos.Infra.Sync;

namespace Pos.Tests;

/// <summary>
/// The pull side of offline-first, against a fake server: pagination via
/// <c>hasMore</c>/<c>nextSince</c>, the cursor surviving a restart, and a tombstone
/// (<c>isActive: false</c>) landing as an ordinary upsert — never a delete, matching
/// the server's own soft-deactivation model.
/// </summary>
public class SyncEngineTests : IAsyncDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"pos-sync-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task PullAllAsync_pages_through_hasMore_and_advances_the_cursor()
    {
        var handler = new FakeServerHandler(new Queue<(long Since, PullResponse Response)>(
        [
            (0L, Page(serverTime: 1_700_000_200, nextSince: 100, hasMore: true,
                locations: [new() { Id = "loc-1", Name = "Main", Timezone = "Africa/Nairobi", IsActive = true, UpdatedAt = "2024-01-01T00:00:00Z" }])),
            (100L, Page(serverTime: 1_700_000_200, nextSince: 200, hasMore: false,
                locations: [new() { Id = "loc-2", Name = "Branch", Timezone = "Africa/Nairobi", IsActive = true, UpdatedAt = "2024-01-02T00:00:00Z" }])),
        ]));

        await using var db = await LocalDatabase.OpenAsync(_dbPath, "test-key-1");
        var sync = new SyncEngine(db, ApiClientWith(handler));

        var result = await sync.PullAllAsync("device-token");

        Assert.Equal(2, result.PagesPulled);
        Assert.Equal(200, result.Cursor);
        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM locations ORDER BY id";
        await using var reader = await cmd.ExecuteReaderAsync();
        var ids = new List<string>();
        while (await reader.ReadAsync()) ids.Add(reader.GetString(0));
        Assert.Equal(["loc-1", "loc-2"], ids);
    }

    [Fact]
    public async Task Cursor_survives_a_restart()
    {
        var handler = new FakeServerHandler(new Queue<(long Since, PullResponse Response)>(
        [
            (0L, Page(serverTime: 1_700_000_300, nextSince: 150, hasMore: false)),
        ]));

        await using (var db = await LocalDatabase.OpenAsync(_dbPath, "test-key-2"))
        {
            var sync = new SyncEngine(db, ApiClientWith(handler));
            await sync.PullAllAsync("device-token");
        }

        // Reopen the same file — the cursor must have been persisted, not held in memory.
        await using var reopened = await LocalDatabase.OpenAsync(_dbPath, "test-key-2");
        var secondHandler = new FakeServerHandler(new Queue<(long Since, PullResponse Response)>(
        [
            (150L, Page(serverTime: 1_700_000_400, nextSince: 150, hasMore: false)),
        ]));
        var syncAfterRestart = new SyncEngine(reopened, ApiClientWith(secondHandler));
        await syncAfterRestart.PullAllAsync("device-token"); // throws if it requested since=0 instead of 150
    }

    [Fact]
    public async Task A_tombstoned_row_is_upserted_not_deleted()
    {
        var handler = new FakeServerHandler(new Queue<(long Since, PullResponse Response)>(
        [
            (0L, Page(serverTime: 1_700_000_500, nextSince: 50, hasMore: false,
                locations: [new() { Id = "loc-1", Name = "Main", Timezone = "Africa/Nairobi", IsActive = false, UpdatedAt = "2024-01-03T00:00:00Z" }])),
        ]));

        await using var db = await LocalDatabase.OpenAsync(_dbPath, "test-key-3");
        var sync = new SyncEngine(db, ApiClientWith(handler));
        await sync.PullAllAsync("device-token");

        await using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT is_active FROM locations WHERE id = 'loc-1'";
        var isActive = await cmd.ExecuteScalarAsync();
        Assert.Equal(0L, Convert.ToInt64(isActive, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static ApiClient ApiClientWith(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://fake-server/api/v1/") };
        return new ApiClient(http);
    }

    private static PullResponse Page(long serverTime, long nextSince, bool hasMore, List<LocationRow>? locations = null) => new()
    {
        ServerTime = serverTime,
        NextSince = nextSince,
        HasMore = hasMore,
        Limit = 500,
        Entities = new PullEntities { Locations = locations ?? [] },
    };

    public async ValueTask DisposeAsync()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        GC.SuppressFinalize(this);
        await Task.CompletedTask;
    }

    /// <summary>Answers <c>GET sync/pull?since=N&amp;limit=...</c> from a canned queue, keyed
    /// by the expected <c>since</c> — asserts the engine asked for the right page rather
    /// than silently returning whatever is next in the queue.</summary>
    private sealed class FakeServerHandler(Queue<(long Since, PullResponse Response)> pages) : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var since = long.Parse(ParseQueryValue(request.RequestUri!.Query, "since")!, System.Globalization.CultureInfo.InvariantCulture);

            if (pages.Count == 0) throw new InvalidOperationException($"No more fake pages queued, but got a request for since={since}");
            var (expectedSince, response) = pages.Dequeue();
            if (since != expectedSince)
                throw new InvalidOperationException($"Expected a pull for since={expectedSince}, got since={since}");

            var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(response, options: JsonOptions),
            };
            return Task.FromResult(httpResponse);
        }

        private static string? ParseQueryValue(string query, string key) =>
            query.TrimStart('?').Split('&')
                .Select(pair => pair.Split('=', 2))
                .FirstOrDefault(parts => parts[0] == key)
                ?.ElementAtOrDefault(1);
    }
}
