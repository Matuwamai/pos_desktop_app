using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Pos.Core.Sync;
using Pos.Infra.Json;

namespace Pos.Infra.Api;

/// <summary>
/// Thin wrapper over the server's REST API. Two credential kinds pass through here:
/// a short-lived admin JWT, used only during the Setup wizard (login, list locations,
/// register + enroll a terminal), and the long-lived device JWT, used for everything
/// after that (pull/push). Neither is cached by this class — callers own that via
/// <c>Pos.Core.Interfaces.ISecretStore</c>.
/// </summary>
public sealed class ApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = SyncJson.Options;

    private readonly HttpClient _http;

    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    public static ApiClient ForServer(Uri serverBaseUrl)
    {
        var http = new HttpClient { BaseAddress = new Uri(serverBaseUrl, "/api/v1/") };
        return new ApiClient(http);
    }

    private static void Authorize(HttpRequestMessage request, string bearerToken) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

    /// <summary>Downloads an arbitrary file served outside <c>/api/v1/</c> (currently
    /// just a tenant logo, served statically from <c>/uploads/...</c> — see
    /// <c>server/app.js</c>). <paramref name="rootRelativePath"/> starting with <c>/</c>
    /// resolves against this server's origin, not its <c>/api/v1/</c> base — standard
    /// <see cref="Uri"/> combination rules, same as an absolute-path HTML link. No auth
    /// header: these assets are served unauthenticated (same as a browser's own
    /// <c>&lt;img&gt;</c> tag would need).</summary>
    public async Task<byte[]> DownloadBytesAsync(string rootRelativePath, CancellationToken ct = default)
    {
        var url = new Uri(_http.BaseAddress!, rootRelativePath);
        using var response = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    // ---- Setup wizard: admin credentials ----

    public async Task<LoginResult> LoginAsync(string subdomain, string email, string password, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("auth/login", new { subdomain, email, password }, JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);
        var result = await response.Content.ReadFromJsonAsync<LoginResult>(JsonOptions, ct);
        return result ?? throw new ApiClientException("Empty login response");
    }

    public async Task<List<LocationSummary>> ListLocationsAsync(string adminToken, CancellationToken ct = default)
    {
        // validations/location.js caps this endpoint's limit at 100 — a setup wizard for a
        // tenant with more locations than that would need real pagination, but that's well
        // beyond anything Phase 1 needs to handle.
        using var request = new HttpRequestMessage(HttpMethod.Get, "locations?limit=100");
        Authorize(request, adminToken);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<LocationSummary>>(JsonOptions, ct);
        return page?.Data ?? [];
    }

    public async Task<List<TerminalSummary>> ListTerminalsAsync(string adminToken, string? locationId = null, CancellationToken ct = default)
    {
        var query = locationId is null ? "" : $"?locationId={Uri.EscapeDataString(locationId)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"terminals{query}");
        Authorize(request, adminToken);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<TerminalSummary>>(JsonOptions, ct);
        return page?.Data ?? [];
    }

    public async Task<TerminalSummary> RegisterTerminalAsync(string adminToken, string locationId, string name, string deviceIdentifier, string platform, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "terminals")
        {
            Content = JsonContent.Create(new { locationId, name, deviceIdentifier, platform }, options: JsonOptions),
        };
        Authorize(request, adminToken);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<TerminalSummary>(JsonOptions, ct)
               ?? throw new ApiClientException("Empty terminal-registration response");
    }

    public async Task<EnrollDeviceResult> EnrollDeviceAsync(string adminToken, string terminalId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"terminals/{terminalId}/enroll-device");
        Authorize(request, adminToken);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<EnrollDeviceResult>(JsonOptions, ct)
               ?? throw new ApiClientException("Empty device-enrollment response");
    }

    // ---- Steady state: device credential ----

    public async Task<PullResponse> PullAsync(string deviceToken, long since, int limit = 500, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"sync/pull?since={since}&limit={limit}");
        Authorize(request, deviceToken);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<PullResponse>(JsonOptions, ct)
               ?? throw new ApiClientException("Empty pull response");
    }

    public async Task<PushResponse> PushAsync(string deviceToken, IReadOnlyList<SyncOpEnvelope> ops, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "sync/push")
        {
            Content = JsonContent.Create(new PushRequest { Ops = [.. ops] }, options: JsonOptions),
        };
        Authorize(request, deviceToken);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<PushResponse>(JsonOptions, ct)
               ?? throw new ApiClientException("Empty push response");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        throw new ApiClientException($"{(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }
}

public sealed class ApiClientException(string message) : Exception(message);

public sealed class LoginResult
{
    public string? Token { get; set; }
    public bool MfaRequired { get; set; }
    public string? MfaToken { get; set; }
    public JsonElement? User { get; set; }
}

public sealed class PagedResult<T>
{
    public List<T> Data { get; set; } = [];
}

public sealed class LocationSummary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class TerminalSummary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string LocationId { get; set; } = "";
    public bool IsActive { get; set; }
}

public sealed class EnrollDeviceResult
{
    public string TerminalId { get; set; } = "";
    public string Token { get; set; } = "";
    public string ExpiresIn { get; set; } = "";
}
