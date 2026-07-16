using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Tests.Integration;

public class ApiEndpointTests : IClassFixture<PostgreSqlFixture>, IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiEndpointTests(PostgreSqlFixture dbFixture, WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", dbFixture.ConnectionString);
            builder.UseSetting("Encryption:Key", "QUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUE=");
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d =>
                    d.ServiceType == typeof(IHostedService) &&
                    d.ImplementationType?.Name == "BingXConnectorService");
                if (descriptor != null)
                    services.Remove(descriptor);

                descriptor = services.SingleOrDefault(d =>
                    d.ServiceType == typeof(IHostedService) &&
                    d.ImplementationType?.Name == "JupiterWorkerService");
                if (descriptor != null)
                    services.Remove(descriptor);

                descriptor = services.SingleOrDefault(d =>
                    d.ServiceType == typeof(IHostedService) &&
                    d.ImplementationType?.Name == "SpreadEngineService");
                if (descriptor != null)
                    services.Remove(descriptor);

                descriptor = services.SingleOrDefault(d =>
                    d.ServiceType == typeof(IHostedService) &&
                    d.ImplementationType?.Name == "TelegramNotificationService");
                if (descriptor != null)
                    services.Remove(descriptor);

                descriptor = services.SingleOrDefault(d =>
                    d.ServiceType == typeof(IHostedService) &&
                    d.ImplementationType?.Name == "AggregationService");
                if (descriptor != null)
                    services.Remove(descriptor);

                descriptor = services.SingleOrDefault(d =>
                    d.ServiceType == typeof(IHostedService) &&
                    d.ImplementationType?.Name == "RetentionService");
                if (descriptor != null)
                    services.Remove(descriptor);
            });
        }).CreateClient();
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/v1/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        Assert.NotNull(body);
        Assert.Equal("ok", body["status"].GetString());
    }

    [Fact]
    public async Task Tokens_ReturnsList()
    {
        var response = await _client.GetAsync("/api/v1/tokens");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tokens = await response.Content.ReadFromJsonAsync<List<Token>>();
        Assert.NotNull(tokens);
    }

    [Fact]
    public async Task Settings_WithoutAuth_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/v1/settings");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
