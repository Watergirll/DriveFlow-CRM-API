using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using DriveFlow.Tests.TestInfrastructure;

namespace DriveFlow.Tests.SystemApiTests;

/// <summary>
/// End-to-end CRUD tests for the License controller via HTTP.
/// Uses <see cref="CustomWebApplicationFactory"/> with in-memory database.
/// </summary>
/// <remarks>
/// <para><strong>Routes tested:</strong></para>
/// <list type="bullet">
///   <item>GET /api/license/get</item>
///   <item>POST /api/license/create</item>
///   <item>PUT /api/license/update/{licenseId}</item>
///   <item>DELETE /api/license/delete/{licenseId}</item>
/// </list>
/// </remarks>
public sealed class CrudSystemTests_License : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CrudSystemTests_License(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Full CRUD Lifecycle: POST -> GET list -> PUT update -> DELETE -> 404
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task License_FullCrudLifecycle_WorksCorrectly()
    {
        // Arrange - authenticate as SuperAdmin
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SuperAdmin");

        // ??????????????? CREATE (POST /api/license/create) ???????????????
        var createPayload = new
        {
            type = "Z"  // Unique type not in seed
        };

        var createResponse = await _client.PostAsJsonAsync("/api/license/create", createPayload);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var licenseId = createDoc.RootElement.GetProperty("licenseId").GetInt32();
        licenseId.Should().BeGreaterThan(0);

        // ??????????????? GET LIST (GET /api/license/get) ???????????????
        var listResponse = await _client.GetAsync("/api/license/get");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listContent = await listResponse.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(listContent);
        var licenses = listDoc.RootElement.EnumerateArray();

        // Verify the created license is in the list
        var createdLicense = licenses.FirstOrDefault(l =>
            l.GetProperty("licenseId").GetInt32() == licenseId);
        createdLicense.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "Created license should be in the list");
        createdLicense.GetProperty("type").GetString().Should().Be("Z");

        // ??????????????? UPDATE (PUT /api/license/update/{licenseId}) ???????????????
        var updatePayload = new
        {
            type = "Z1"  // Updated type
        };

        var updateResponse = await _client.PutAsJsonAsync($"/api/license/update/{licenseId}", updatePayload);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify update by getting the list again
        var listAfterUpdateResponse = await _client.GetAsync("/api/license/get");
        var listAfterUpdateContent = await listAfterUpdateResponse.Content.ReadAsStringAsync();
        using var listAfterUpdateDoc = JsonDocument.Parse(listAfterUpdateContent);
        var licensesAfterUpdate = listAfterUpdateDoc.RootElement.EnumerateArray();

        var updatedLicense = licensesAfterUpdate.FirstOrDefault(l =>
            l.GetProperty("licenseId").GetInt32() == licenseId);
        updatedLicense.GetProperty("type").GetString().Should().Be("Z1");

        // ??????????????? DELETE (DELETE /api/license/delete/{licenseId}) ???????????????
        var deleteResponse = await _client.DeleteAsync($"/api/license/delete/{licenseId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // ??????????????? VERIFY DELETED (GET list should not contain it) ???????????????
        var listAfterDeleteResponse = await _client.GetAsync("/api/license/get");
        listAfterDeleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listAfterDeleteContent = await listAfterDeleteResponse.Content.ReadAsStringAsync();
        using var listAfterDeleteDoc = JsonDocument.Parse(listAfterDeleteContent);
        var licensesAfterDelete = listAfterDeleteDoc.RootElement.EnumerateArray();

        var deletedLicense = licensesAfterDelete.FirstOrDefault(l =>
            l.GetProperty("licenseId").GetInt32() == licenseId);
        deletedLicense.ValueKind.Should().Be(JsonValueKind.Undefined,
            "Deleted license should not be in the list");

        // ??????????????? DELETE again -> 404 ???????????????
        var deleteAgainResponse = await _client.DeleteAsync($"/api/license/delete/{licenseId}");
        deleteAgainResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Protected endpoint tests - No token -> 401
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task License_GetLicenses_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/license/get");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task License_CreateLicense_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        var payload = new { type = "TEST" };

        // Act
        var response = await client.PostAsJsonAsync("/api/license/create", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task License_UpdateLicense_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        var payload = new { type = "TEST" };

        // Act
        var response = await client.PutAsJsonAsync("/api/license/update/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task License_DeleteLicense_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.DeleteAsync("/api/license/delete/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Role-based access tests
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task License_GetLicenses_AsSchoolAdmin_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        // Act
        var response = await client.GetAsync("/api/license/get");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task License_CreateLicense_AsSchoolAdmin_Returns403()
    {
        // Arrange - SchoolAdmin cannot create licenses (only SuperAdmin)
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        var payload = new { type = "TESTFORBIDDEN" };

        // Act
        var response = await client.PostAsJsonAsync("/api/license/create", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task License_UpdateLicense_AsSchoolAdmin_Returns403()
    {
        // Arrange - SchoolAdmin cannot update licenses (only SuperAdmin)
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        var payload = new { type = "UPDATED" };

        // Act (using seeded license ID 1)
        var response = await client.PutAsJsonAsync("/api/license/update/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task License_DeleteLicense_AsSchoolAdmin_Returns403()
    {
        // Arrange - SchoolAdmin cannot delete licenses (only SuperAdmin)
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        // Act (using seeded license ID 1)
        var response = await client.DeleteAsync("/api/license/delete/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task License_CreateDuplicate_Returns400()
    {
        // Arrange - try to create a license with existing type
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SuperAdmin");

        // Seeded data has license type "B"
        var payload = new { type = "B" };

        // Act
        var response = await client.PostAsJsonAsync("/api/license/create", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
