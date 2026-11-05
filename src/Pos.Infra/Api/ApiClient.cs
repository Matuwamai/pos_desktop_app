using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Pos.Core.Pricing;
using Pos.Core.Sync;
using Pos.Infra.Json;

namespace Pos.Infra.Api;

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

    public async Task<byte[]> DownloadBytesAsync(string rootRelativePath, CancellationToken ct = default)
    {
        var url = new Uri(_http.BaseAddress!, rootRelativePath);
        using var response = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<LoginResult> LoginAsync(string subdomain, string email, string password, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("auth/login", new { subdomain, email, password }, JsonOptions, ct);
        await EnsureSuccessAsync(response, ct);
        var result = await response.Content.ReadFromJsonAsync<LoginResult>(JsonOptions, ct);
        return result ?? throw new ApiClientException("Empty login response");
    }

    public async Task<List<LocationSummary>> ListLocationsAsync(string adminToken, CancellationToken ct = default)
    {
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
    public async Task<PaymentIntentResult> GetPaymentIntentAsync(string deviceToken, string intentId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"payment-intents/{intentId}");
        Authorize(request, deviceToken);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<PaymentIntentResult>(JsonOptions, ct)
               ?? throw new ApiClientException("Empty payment-intent response");
    }
    public async Task<PromotionDto> GetPromotionByCodeAsync(string deviceToken, string code, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"promotions/by-code/{Uri.EscapeDataString(code)}");
        Authorize(request, deviceToken);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<PromotionDto>(JsonOptions, ct)
               ?? throw new ApiClientException("Empty promotion response");
    }
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
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new DeviceUnauthorizedException(message);

        throw new ApiClientException(message, response.StatusCode);
    }
}

public class ApiClientException(string message, System.Net.HttpStatusCode? statusCode = null) : Exception(message)
{
    public System.Net.HttpStatusCode? StatusCode { get; } = statusCode;
}
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
public sealed class PaymentIntentResult
{
    public string Id { get; set; } = "";
    public string Status { get; set; } = ""; // PENDING | COMPLETED | FAILED | CANCELLED | TIMEOUT
    public string? ResultDesc { get; set; }
    public string? TransactionRef { get; set; }
}
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
    public string Id { get; set; } = "";
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
    public string ProcessedByUserId { get; set; } = "";
}

public sealed class RefundRequestItem
{
    public string OrderItemId { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
}
