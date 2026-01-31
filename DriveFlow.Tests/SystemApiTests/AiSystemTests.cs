using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using DriveFlow.Tests.TestInfrastructure;

namespace DriveFlow.Tests.SystemApiTests;

/// <summary>
/// System tests for <c>AiController</c>.
/// Tests AI chat streaming endpoint with fake AI service.
/// </summary>
/// <remarks>
/// <para><strong>Routes tested:</strong></para>
/// <list type="bullet">
///   <item>POST /api/ai/chat/stream - Stream AI chat responses (SSE)</item>
///   <item>GET /api/ai/health - Health check endpoint</item>
/// </list>
/// <para>
/// The <see cref="CustomWebApplicationFactory"/> replaces the real AI services with fakes:
/// - <c>FakeAiStreamingService</c>: Returns "event: done\ndata:\n\n" immediately
/// - <c>FakeAiContextBuilder</c>: Returns minimal fake context
/// </para>
/// </remarks>
public sealed class AiSystemTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AiSystemTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ?????????????????????????????????????????????????????????????????????????
    // GET /api/ai/health - Health check (public)
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task Health_NoAuth_Returns200()
    {
        // Arrange - Health endpoint is public (AllowAnonymous)
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/ai/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);

        doc.RootElement.GetProperty("status").GetString().Should().Be("healthy");
        doc.RootElement.GetProperty("service").GetString().Should().Be("ai-chat");
    }

    // ?????????????????????????????????????????????????????????????????????????
    // POST /api/ai/chat/stream - AI chat streaming
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task ChatStream_AsStudent_Returns200()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "Student");

        var payload = new
        {
            messages = new[]
            {
                new { role = "user", content = "Test message" }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/ai/chat/stream", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");
    }

    [Fact]
    public async Task ChatStream_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        var payload = new
        {
            messages = new[]
            {
                new { role = "user", content = "Test message" }
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/ai/chat/stream", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChatStream_AsInstructor_Returns403()
    {
        // Arrange - Only students can access AI chat
        await ApiAuthHelper.AuthenticateAsAsync(_client, "Instructor");

        var payload = new
        {
            messages = new[]
            {
                new { role = "user", content = "Test message" }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/ai/chat/stream", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ChatStream_AsSchoolAdmin_Returns403()
    {
        // Arrange - Only students can access AI chat
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        var payload = new
        {
            messages = new[]
            {
                new { role = "user", content = "Test message" }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/ai/chat/stream", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ChatStream_WithEmptyMessages_Returns200()
    {
        // Arrange - Empty messages should still work (context-only query)
        await ApiAuthHelper.AuthenticateAsAsync(_client, "Student");

        var payload = new
        {
            messages = Array.Empty<object>()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/ai/chat/stream", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChatStream_WithOptionalParameters_Returns200()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "Student");

        var payload = new
        {
            messages = new[]
            {
                new { role = "user", content = "Ce gre?eli fac cel mai des?" }
            },
            historySessions = 10,
            language = "ro"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/ai/chat/stream", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChatStream_ResponseIsSSE()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "Student");

        var payload = new
        {
            messages = new[]
            {
                new { role = "user", content = "Hello" }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/ai/chat/stream", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify SSE content type
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        // Read the fake response
        var content = await response.Content.ReadAsStringAsync();
        // FakeAiStreamingService returns "event: done\ndata:\n\n"
        content.Should().Contain("done");
    }
}
