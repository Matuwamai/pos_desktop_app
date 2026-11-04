using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Pos.Core.Pricing;
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

    // ---- M-Pesa STK push (Phase 4b) — device token, same as pull/push ----

    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(string deviceToken, decimal amount, string phoneNumber, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "payment-intents")
        {
            Content = JsonContent.Create(new { amount, phoneNumber }, options: JsonOptions),
        };
        Authorize(request, deviceToken);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<PaymentIntentResult>(JsonOptions, ct)
               ?? throw new ApiClientException("Empty payment-intent response");
    }

    // The status endpoint IS the confirmation path — it actively queries Daraja while
    // still pending, so repeated calls here are how a caller "polls until done," with
    // no dependency on the server's callback webhook (see the server's
    // services/mpesaPaymentIntent.js for why that's true).
    public async Task<PaymentIntentResult> GetPaymentIntentAsync(string deviceToken, string intentId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"payment-intents/{intentId}");
        Authorize(request, deviceToken);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<PaymentIntentResult>(JsonOptions, ct)
               ?? throw new ApiClientException("Empty payment-intent response");
    }

    // ---- Promo code preview — device token (authenticateTerminal.js's TERMINAL_PERMISSIONS
    // grants exactly 'promotions.byCode', nothing else promotion-related). Only ever used
    // to preview a discount amount live before the cashier applies it; the server
    // re-validates the promotion is still active/in-window again at sync time regardless. ----

    public async Task<PromotionDto> GetPromotionByCodeAsync(string deviceToken, string code, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"promotions/by-code/{Uri.EscapeDataString(code)}");
        Authorize(request, deviceToken);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<PromotionDto>(JsonOptions, ct)
               ?? throw new ApiClientException("Empty promotion response");
    }

    // ---- Refunds (Phase 5) — device token, same as pull/push/payment-intents.
    // 'orders.create' is NOT reachable this way: a till's own sales only ever go
    // through PushAsync's order.create outbox path (see authenticateTerminal.js's
    // TERMINAL_PERMISSIONS, which deliberately omits it). ----

    public async Task<List<OrderSummaryDto>> ListOrdersAsync(string deviceToken, string search, string locationId, CancellationToken ct = default)
    {
        var query = $"orders?search={Uri.EscapeDataString(search)}&locationId={Uri.EscapeDataString(locationId)}&limit=20";
        using var request = new HttpRequestMessage(HttpMethod.Get, query);
        Authorize(request, deviceToken);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<OrderSummaryDto>>(JsonOptions, ct);
        return page?.Data ?? [];
    }

    public async Task<OrderDetailDto> GetOrderAsync(string deviceToken, string orderId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"orders/{orderId}");
        Authorize(request, deviceToken);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions, ct)
               ?? throw new ApiClientException("Empty order response");
    }

    public async Task<OrderDetailDto> RefundOrderAsync(string deviceToken, string orderId, RefundRequest body, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"orders/{orderId}/refund")
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        Authorize(request, deviceToken);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions, ct)
               ?? throw new ApiClientException("Empty refund response");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct);
        var message = $"{(int)response.StatusCode} {response.ReasonPhrase}: {body}";

        // A 401 on an already-enrolled device is never "retry later" — it's
        // authenticateTerminal.js's tokenVersion check failing, which only happens on an
        // explicit revoke, a deactivated terminal/tenant, or the device token's own
        // 365-day expiry (server/services/terminal.js's DEVICE_TOKEN_TTL). All three have
        // the same remedy (wipe local state, re-enroll), so this gets its own exception
        // type rather than forcing every caller to parse the message string.
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new DeviceUnauthorizedException(message);

        throw new ApiClientException(message, response.StatusCode);
    }
}

public class ApiClientException(string message, System.Net.HttpStatusCode? statusCode = null) : Exception(message)
{
    public System.Net.HttpStatusCode? StatusCode { get; } = statusCode;
}

/// <summary>Thrown specifically for a 401 response once a device is already enrolled —
/// see <see cref="ApiClient.EnsureSuccessAsync"/>. Callers that care about this
/// distinction (see <c>AppViewModel.HandleDeviceInvalidAsync</c>) must catch this
/// subclass before the base <see cref="ApiClientException"/>.</summary>
public sealed class DeviceUnauthorizedException(string message)
    : ApiClientException(message, System.Net.HttpStatusCode.Unauthorized);

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

// Shared by both the create (id, status) and status-check (adds resultDesc/
// transactionRef) responses — fields the other call doesn't return are just left
// at their default.
public sealed class PaymentIntentResult
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = ""; // PENDING | COMPLETED | FAILED | CANCELLED | TIMEOUT
    public string? ResultDesc { get; set; }
    public string? TransactionRef { get; set; }
}

/// <summary>Matches the Promotion fields <c>GET /promotions/by-code/:code</c> returns —
/// enough to compute the same preview amount <c>DiscountDialogViewModel</c> shows
/// before the cashier applies it (server/services/order.js's resolveDiscountInput
/// computes the real one the same way, as the final authority at sync time).</summary>
public sealed class PromotionDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string Type { get; set; } = ""; // PERCENTAGE | FIXED_AMOUNT | BOGO
    public decimal Value { get; set; }
    public decimal? MinSubtotal { get; set; }
}

public class OrderSummaryDto
{
    public string Id { get; set; } = "";
    public int OrderNumber { get; set; }
    public string Status { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public decimal Total { get; set; }
    public string CreatedAt { get; set; } = "";
    public CustomerRefDto? Customer { get; set; }

    public string TotalText => Total.ToString(MoneyFormat(Currency), System.Globalization.CultureInfo.InvariantCulture);

    internal static string MoneyFormat(string currency)
    {
        var digits = CurrencyInfo.GetDecimalDigits(currency);
        return digits == 0 ? "0" : "0." + new string('0', digits);
    }
}

public sealed class OrderDetailDto : OrderSummaryDto
{
    public List<OrderItemDto> Items { get; set; } = [];
    public List<OrderPaymentDto> Payments { get; set; } = [];
    public List<OrderRefundDto> Refunds { get; set; } = [];
}

public sealed class CustomerRefDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class OrderItemDto
{
    public string Id { get; set; } = ""; // orderItemId, referenced by RefundRequestItem/RefundItemDto
    public decimal Quantity { get; set; }
    public decimal LineTotal { get; set; }
    public VariantRefDto? Variant { get; set; }
}

public sealed class VariantRefDto
{
    public string Sku { get; set; } = "";
    public ProductRefDto? Product { get; set; }
}

public sealed class ProductRefDto
{
    public string Name { get; set; } = "";
}

public sealed class OrderPaymentDto
{
    public string Method { get; set; } = "";
    public decimal Amount { get; set; }
    public string? TransactionRef { get; set; }
}

public sealed class OrderRefundDto
{
    public string Id { get; set; } = "";
    public decimal TotalAmount { get; set; }
    public List<RefundItemDto> Items { get; set; } = [];
}

public sealed class RefundItemDto
{
    public string OrderItemId { get; set; } = "";
    public decimal Quantity { get; set; }
}

public sealed class RefundRequest
{
    public List<RefundRequestItem> Items { get; set; } = [];
    public string? Reason { get; set; }
    public string RefundMethod { get; set; } = "CASH";
    public string? GiftCardCode { get; set; }
    // Required because a device token has no server-side session user — see
    // the server's services/order.js refundOrder and controllers/order.js.
    public string ProcessedByUserId { get; set; } = "";
}

public sealed class RefundRequestItem
{
    public string OrderItemId { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
}
