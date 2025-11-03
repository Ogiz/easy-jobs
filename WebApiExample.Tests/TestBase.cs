using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WebApiExample.Tests;

public class TestBase : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly JsonSerializerOptions _options;
    protected readonly WebApplicationFactory<Program> Factory;

    public TestBase(WebApplicationFactory<Program> factory)
    {
        Factory = factory;
        _options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }
    
    protected async Task<T> GetAsync<T>(string endpoint)
    {
        var client = Factory.CreateClient();
        var response = await client.GetAsync(endpoint);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(content, _options)!;
    }

    public async Task<T> PostAsync<T>(string endpoint)
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsync(endpoint, null);
        response.EnsureSuccessStatusCode();
        var responseContent = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(responseContent, _options)!;
    }
    
    protected async Task<T> PostAsync<T>(string endpoint, object payload)
    {
        var client = Factory.CreateClient();
        var jsonPayload = JsonSerializer.Serialize(payload);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync(endpoint, content);
        response.EnsureSuccessStatusCode();
        var responseContent = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(responseContent, _options)!;
    }
    
    protected async Task PostAsync(string endpoint, object? payload = null)
    {
        var client = Factory.CreateClient();
        StringContent? content = null;
        if (payload != null)
        {
            var jsonPayload = JsonSerializer.Serialize(payload);
            content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        }

        var response = await client.PostAsync(endpoint, content);
        response.EnsureSuccessStatusCode();
    }
    
    protected async Task<HttpResponseMessage> DeleteAsync(string endpoint)
    {
        var client = Factory.CreateClient();
        var response = await client.DeleteAsync(endpoint);
        response.EnsureSuccessStatusCode();
        return response;
    }
}