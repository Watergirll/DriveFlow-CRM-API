using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using DriveFlow.Tests.TestInfrastructure;

namespace DriveFlow.Tests.SystemApiTests;

/// <summary>
/// End-to-end CRUD tests for the Address controller via HTTP.
/// Uses <see cref="CustomWebApplicationFactory"/> with in-memory database.
/// </summary>
/// <remarks>
/// <para><strong>Routes tested:</strong></para>
/// <list type="bullet">
///   <item>GET /api/address/get</item>
///   <item>POST /api/address/create</item>
///   <item>PUT /api/address/update/{addressId}</item>
///   <item>DELETE /api/address/delete/{addressId}</item>
/// </list>
/// </remarks>
public sealed class CrudSystemTests_Address : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CrudSystemTests_Address(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Full CRUD Lifecycle: POST -> GET list -> PUT update -> DELETE -> 404
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task Address_FullCrudLifecycle_WorksCorrectly()
    {
        // Arrange - authenticate as SuperAdmin (can create/delete)
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SuperAdmin");

        // Use seeded city ID (cityId = 1 = Cluj-Napoca)
        var cityId = 1;

        // ??????????????? CREATE (POST /api/address/create) ???????????????
        var createPayload = new
        {
            streetName = "Strada Test CRUD",
            addressNumber = "123A",
            postcode = "400001",
            cityId = cityId
        };

        var createResponse = await _client.PostAsJsonAsync("/api/address/create", createPayload);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var addressId = createDoc.RootElement.GetProperty("addressId").GetInt32();
        addressId.Should().BeGreaterThan(0);

        // ??????????????? GET LIST (GET /api/address/get?cityId={cityId}) ???????????????
        var listResponse = await _client.GetAsync($"/api/address/get?cityId={cityId}");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listContent = await listResponse.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(listContent);
        var addresses = listDoc.RootElement.EnumerateArray();

        // Verify the created address is in the list
        var createdAddress = addresses.FirstOrDefault(a =>
            a.GetProperty("addressId").GetInt32() == addressId);
        createdAddress.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "Created address should be in the list");
        createdAddress.GetProperty("streetName").GetString().Should().Be("Strada Test CRUD");
        createdAddress.GetProperty("addressNumber").GetString().Should().Be("123A");
        createdAddress.GetProperty("postcode").GetString().Should().Be("400001");

        // ??????????????? UPDATE (PUT /api/address/update/{addressId}) ???????????????
        var updatePayload = new
        {
            streetName = "Strada Test CRUD Updated",
            addressNumber = "456B",
            postcode = "400002",
            cityId = cityId
        };

        var updateResponse = await _client.PutAsJsonAsync($"/api/address/update/{addressId}", updatePayload);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify update by getting the list again
        var listAfterUpdateResponse = await _client.GetAsync($"/api/address/get?cityId={cityId}");
        var listAfterUpdateContent = await listAfterUpdateResponse.Content.ReadAsStringAsync();
        using var listAfterUpdateDoc = JsonDocument.Parse(listAfterUpdateContent);
        var addressesAfterUpdate = listAfterUpdateDoc.RootElement.EnumerateArray();

        var updatedAddress = addressesAfterUpdate.FirstOrDefault(a =>
            a.GetProperty("addressId").GetInt32() == addressId);
        updatedAddress.GetProperty("streetName").GetString().Should().Be("Strada Test CRUD Updated");
        updatedAddress.GetProperty("addressNumber").GetString().Should().Be("456B");
        updatedAddress.GetProperty("postcode").GetString().Should().Be("400002");

        // ??????????????? DELETE (DELETE /api/address/delete/{addressId}) ???????????????
        var deleteResponse = await _client.DeleteAsync($"/api/address/delete/{addressId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // ??????????????? VERIFY DELETED (GET list should not contain it) ???????????????
        var listAfterDeleteResponse = await _client.GetAsync($"/api/address/get?cityId={cityId}");
        listAfterDeleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listAfterDeleteContent = await listAfterDeleteResponse.Content.ReadAsStringAsync();
        using var listAfterDeleteDoc = JsonDocument.Parse(listAfterDeleteContent);
        var addressesAfterDelete = listAfterDeleteDoc.RootElement.EnumerateArray();

        var deletedAddress = addressesAfterDelete.FirstOrDefault(a =>
            a.GetProperty("addressId").GetInt32() == addressId);
        deletedAddress.ValueKind.Should().Be(JsonValueKind.Undefined,
            "Deleted address should not be in the list");

        // ??????????????? DELETE again -> 404 ???????????????
        var deleteAgainResponse = await _client.DeleteAsync($"/api/address/delete/{addressId}");
        deleteAgainResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Protected endpoint tests - No token -> 401
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task Address_GetAddresses_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/address/get");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Address_CreateAddress_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        var payload = new
        {
            streetName = "Test",
            addressNumber = "1",
            postcode = "123456",
            cityId = 1
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/address/create", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Address_UpdateAddress_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        var payload = new
        {
            streetName = "Test",
            addressNumber = "1",
            postcode = "123456",
            cityId = 1
        };

        // Act
        var response = await client.PutAsJsonAsync("/api/address/update/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Address_DeleteAddress_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.DeleteAsync("/api/address/delete/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Role-based access tests
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task Address_GetAddresses_AsSchoolAdmin_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        // Act
        var response = await client.GetAsync("/api/address/get");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Address_CreateAddress_AsSchoolAdmin_Returns403()
    {
        // Arrange - SchoolAdmin cannot create addresses (only SuperAdmin)
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        var payload = new
        {
            streetName = "Test Forbidden",
            addressNumber = "1",
            postcode = "123456",
            cityId = 1
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/address/create", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Address_DeleteAddress_AsSchoolAdmin_Returns403()
    {
        // Arrange - SchoolAdmin cannot delete addresses (only SuperAdmin)
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        // Act (using seeded address ID 1)
        var response = await client.DeleteAsync("/api/address/delete/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Address_GetAddressesByCity_FiltersCorrectly()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SuperAdmin");

        // Act - Get addresses for city 1 (Cluj-Napoca)
        var response = await _client.GetAsync("/api/address/get?cityId=1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var addresses = doc.RootElement.EnumerateArray().ToList();

        // All addresses should belong to city 1
        foreach (var address in addresses)
        {
            address.GetProperty("city").GetProperty("cityId").GetInt32().Should().Be(1);
        }
    }
}
