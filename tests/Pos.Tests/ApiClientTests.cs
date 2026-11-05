using System.Net;
using Pos.Infra.Api;

namespace Pos.Tests;

public class ApiClientTests
{
    private static ApiClient FakeApi(HttpStatusCode status) =>
        new(new HttpClient(new FixedStatusHandler(status)) { BaseAddress = new Uri("http://fake-server/api/v1/") });

    [Fact]
    public async Task A_401_response_throws_DeviceUnauthorizedException()
    {
        var api = FakeApi(HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<DeviceUnauthorizedException>(
            () => api.ListOrdersAsync("tok", "", "loc-1"));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_non_401_error_still_throws_the_plain_ApiClientException(HttpStatusCode status)
    {
        var api = FakeApi(status);

        var ex = await Assert.ThrowsAsync<ApiClientException>(
            () => api.ListOrdersAsync("tok", "", "loc-1"));

        Assert.IsNotType<DeviceUnauthorizedException>(ex);
        Assert.Equal(status, ex.StatusCode);
    }

    private sealed class FixedStatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") });
    }
}
