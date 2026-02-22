using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using DriveFlow.Tests.TestInfrastructure;

namespace DriveFlow.Tests.SystemApiTests;

/// <summary>
/// End-to-end CRUD tests for the City controller via HTTP.
/// Uses <see cref="CustomWebApplicationFactory"/> with in-memory database.
/// </summary>
/// <remarks>
/// <para><strong>Routes tested:</strong></para>
/// <list type="bullet">
///   <item>GET /api/city</item>
///   <item>POST /api/city/create</item>
///   <item>DELETE /api/city/{cityId}</item>
/// </list>
/// <para>Note: City controller has no PUT endpoint.</para>
/// </remarks>
public sealed class CrudSystemTests_City : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CrudSystemTests_City(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Full CRUD Lifecycle: POST -> GET list -> DELETE -> verify deleted
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task City_FullCrudLifecycle_WorksCorrectly()
    {
        // Arrange - authenticate as SuperAdmin
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SuperAdmin");

        // Use seeded county ID (countyId = 1 = Cluj)
        var countyId = 1;

        // ??????????????? CREATE (POST /api/city/create) ???????????????
        var createPayload = new
        {
            name = "TestCity_CRUD",
            countyId = countyId
        };

        var createResponse = await _client.PostAsJsonAsync("/api/city/create", createPayload);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var cityId = createDoc.RootElement.GetProperty("cityId").GetInt32();
        cityId.Should().BeGreaterThan(0);

        // ??????????????? GET LIST (GET /api/city?countyId={countyId}) ???????????????
        var listResponse = await _client.GetAsync($"/api/city?countyId={countyId}");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listContent = await listResponse.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(listContent);
        var cities = listDoc.RootElement.EnumerateArray();

        // Verify the created city is in the list
        var createdCity = cities.FirstOrDefault(c =>
            c.GetProperty("cityId").GetInt32() == cityId);
        createdCity.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "Created city should be in the list");
        createdCity.GetProperty("name").GetString().Should().Be("TestCity_CRUD");

        // ??????????????? DELETE (DELETE /api/city/{cityId}) ???????????????
        var deleteResponse = await _client.DeleteAsync($"/api/city/{cityId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // ??????????????? VERIFY DELETED (GET list should not contain it) ???????????????
        var listAfterDeleteResponse = await _client.GetAsync($"/api/city?countyId={countyId}");
        listAfterDeleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listAfterDeleteContent = await listAfterDeleteResponse.Content.ReadAsStringAsync();
        using var listAfterDeleteDoc = JsonDocument.Parse(listAfterDeleteContent);
        var citiesAfterDelete = listAfterDeleteDoc.RootElement.EnumerateArray();

        var deletedCity = citiesAfterDelete.FirstOrDefault(c =>
            c.GetProperty("cityId").GetInt32() == cityId);
        deletedCity.ValueKind.Should().Be(JsonValueKind.Undefined,
            "Deleted city should not be in the list");

        // ??????????????? DELETE again -> 404 ???????????????
        var deleteAgainResponse = await _client.DeleteAsync($"/api/city/{cityId}");
        deleteAgainResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Protected endpoint tests - No token -> 401
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task City_GetCities_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/city");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task City_CreateCity_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        var payload = new { name = "Test", countyId = 1 };

        // Act
        var response = await client.PostAsJsonAsync("/api/city/create", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task City_DeleteCity_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.DeleteAsync("/api/city/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Role-based access tests
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task City_GetCities_AsSchoolAdmin_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        // Act
        var response = await client.GetAsync("/api/city");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task City_CreateCity_AsSchoolAdmin_Returns403()
    {
        // Arrange - SchoolAdmin cannot create cities (only SuperAdmin)
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        var payload = new { name = "TestForbidden", countyId = 1 };

        // Act
        var response = await client.PostAsJsonAsync("/api/city/create", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task City_DeleteCity_AsSchoolAdmin_Returns403()
    {
        // Arrange - SchoolAdmin cannot delete cities
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        // Act (using seeded city ID 1)
        var response = await client.DeleteAsync("/api/city/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task City_GetCitiesByCounty_FiltersCorrectly()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SuperAdmin");

        // Act - Get cities for county 1 (Cluj)
        var response = await _client.GetAsync("/api/city?countyId=1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var cities = doc.RootElement.EnumerateArray().ToList();

        // All cities should belong to county 1
        foreach (var city in cities)
        {
            city.GetProperty("county").GetProperty("countyId").GetInt32().Should().Be(1);
        }
    }
}
