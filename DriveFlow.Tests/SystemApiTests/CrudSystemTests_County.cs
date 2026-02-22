using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using DriveFlow.Tests.TestInfrastructure;

namespace DriveFlow.Tests.SystemApiTests;

/// <summary>
/// End-to-end CRUD tests for the County controller via HTTP.
/// Uses <see cref="CustomWebApplicationFactory"/> with in-memory database.
/// </summary>
/// <remarks>
/// <para><strong>Routes tested:</strong></para>
/// <list type="bullet">
///   <item>GET /api/county/get</item>
///   <item>POST /api/county</item>
///   <item>DELETE /api/county/{countyId}</item>
/// </list>
/// <para>Note: County controller has no PUT endpoint.</para>
/// </remarks>
public sealed class CrudSystemTests_County : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CrudSystemTests_County(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Full CRUD Lifecycle: POST -> GET list -> DELETE -> GET list (not found)
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task County_FullCrudLifecycle_WorksCorrectly()
    {
        // Arrange - authenticate as SuperAdmin
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SuperAdmin");

        // ??????????????? CREATE (POST /api/county) ???????????????
        var createPayload = new
        {
            name = "TestCounty_CRUD",
            abbreviation = "TC"
        };

        var createResponse = await _client.PostAsJsonAsync("/api/county", createPayload);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var countyId = createDoc.RootElement.GetProperty("countyId").GetInt32();
        countyId.Should().BeGreaterThan(0);

        // ??????????????? GET LIST (GET /api/county/get) ???????????????
        var listResponse = await _client.GetAsync("/api/county/get");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listContent = await listResponse.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(listContent);
        var counties = listDoc.RootElement.EnumerateArray();

        // Verify the created county is in the list
        var createdCounty = counties.FirstOrDefault(c =>
            c.GetProperty("countyId").GetInt32() == countyId);
        createdCounty.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "Created county should be in the list");
        createdCounty.GetProperty("name").GetString().Should().Be("TestCounty_CRUD");
        createdCounty.GetProperty("abbreviation").GetString().Should().Be("TC");

        // ??????????????? DELETE (DELETE /api/county/{countyId}) ???????????????
        var deleteResponse = await _client.DeleteAsync($"/api/county/{countyId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // ??????????????? VERIFY DELETED (GET list should not contain it) ???????????????
        var listAfterDeleteResponse = await _client.GetAsync("/api/county/get");
        listAfterDeleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listAfterDeleteContent = await listAfterDeleteResponse.Content.ReadAsStringAsync();
        using var listAfterDeleteDoc = JsonDocument.Parse(listAfterDeleteContent);
        var countiesAfterDelete = listAfterDeleteDoc.RootElement.EnumerateArray();

        var deletedCounty = countiesAfterDelete.FirstOrDefault(c =>
            c.GetProperty("countyId").GetInt32() == countyId);
        deletedCounty.ValueKind.Should().Be(JsonValueKind.Undefined,
            "Deleted county should not be in the list");

        // ??????????????? DELETE again -> 404 ???????????????
        var deleteAgainResponse = await _client.DeleteAsync($"/api/county/{countyId}");
        deleteAgainResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Protected endpoint tests - No token -> 401
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task County_GetCounties_WithoutToken_Returns401()
    {
        // Arrange - clear any authentication
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/county/get");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task County_CreateCounty_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        var payload = new { name = "Test", abbreviation = "TS" };

        // Act
        var response = await client.PostAsJsonAsync("/api/county", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task County_DeleteCounty_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.DeleteAsync("/api/county/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Role-based access tests
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task County_GetCounties_AsSchoolAdmin_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        // Act
        var response = await client.GetAsync("/api/county/get");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task County_CreateCounty_AsSchoolAdmin_Returns403()
    {
        // Arrange - SchoolAdmin cannot create counties (only SuperAdmin)
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        var payload = new { name = "TestForbidden", abbreviation = "TF" };

        // Act
        var response = await client.PostAsJsonAsync("/api/county", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task County_DeleteCounty_AsSchoolAdmin_Returns403()
    {
        // Arrange - SchoolAdmin cannot delete counties
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        // Act (using seeded county ID 1)
        var response = await client.DeleteAsync("/api/county/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
