using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using DriveFlow.Tests.TestInfrastructure;

namespace DriveFlow.Tests.SystemApiTests;

/// <summary>
/// End-to-end CRUD tests for the ExamForm controller via HTTP.
/// Uses <see cref="CustomWebApplicationFactory"/> with in-memory database.
/// </summary>
/// <remarks>
/// <para><strong>Routes tested:</strong></para>
/// <list type="bullet">
///   <item>GET /api/forms/by-license/{licenseId}</item>
///   <item>GET /api/forms/by-category/{id_categ}</item>
///   <item>POST /api/forms/seed/{licenseId}</item>
/// </list>
/// <para>Note: ExamForm controller has no PUT or DELETE endpoints - it uses seed/upsert pattern.</para>
/// </remarks>
public sealed class CrudSystemTests_ExamForm : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CrudSystemTests_ExamForm(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Full Lifecycle: POST seed -> GET by license -> update via POST seed -> verify
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task ExamForm_FullLifecycle_WorksCorrectly()
    {
        // Arrange - authenticate as SuperAdmin (can seed forms)
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SuperAdmin");

        // First, create a new license to use for our test
        var createLicensePayload = new { type = "TESTFORM" };
        var createLicenseResponse = await _client.PostAsJsonAsync("/api/license/create", createLicensePayload);
        createLicenseResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var licenseContent = await createLicenseResponse.Content.ReadAsStringAsync();
        using var licenseDoc = JsonDocument.Parse(licenseContent);
        var licenseId = licenseDoc.RootElement.GetProperty("licenseId").GetInt32();

        // ??????????????? GET BY LICENSE (should be 404 - no form yet) ???????????????
        var getBeforeSeedResponse = await _client.GetAsync($"/api/forms/by-license/{licenseId}");
        getBeforeSeedResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // ??????????????? SEED/CREATE (POST /api/forms/seed/{licenseId}) ???????????????
        var seedPayload = new
        {
            maxPoints = 21,
            items = new[]
            {
                new { description = "Semnalizare la schimbarea direc?iei", penaltyPoints = 3, orderIndex = 1 },
                new { description = "Neasigurare la plecarea de pe loc", penaltyPoints = 3, orderIndex = 2 },
                new { description = "Viteza neadaptat?", penaltyPoints = 5, orderIndex = 3 }
            }
        };

        var seedResponse = await _client.PostAsJsonAsync($"/api/forms/seed/{licenseId}", seedPayload);
        seedResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var seedContent = await seedResponse.Content.ReadAsStringAsync();
        using var seedDoc = JsonDocument.Parse(seedContent);
        var formId = seedDoc.RootElement.GetProperty("formId").GetInt32();
        formId.Should().BeGreaterThan(0);

        // ??????????????? GET BY LICENSE (GET /api/forms/by-license/{licenseId}) ???????????????
        var getResponse = await _client.GetAsync($"/api/forms/by-license/{licenseId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getContent = await getResponse.Content.ReadAsStringAsync();
        using var getDoc = JsonDocument.Parse(getContent);

        // Verify form properties
        getDoc.RootElement.GetProperty("id_formular").GetInt32().Should().Be(formId);
        getDoc.RootElement.GetProperty("licenseId").GetInt32().Should().Be(licenseId);
        getDoc.RootElement.GetProperty("maxPoints").GetInt32().Should().Be(21);
        getDoc.RootElement.GetProperty("licenseType").GetString().Should().Be("TESTFORM");

        var items = getDoc.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(3);
        items[0].GetProperty("description").GetString().Should().Be("Semnalizare la schimbarea direc?iei");
        items[0].GetProperty("penaltyPoints").GetInt32().Should().Be(3);

        // ??????????????? UPDATE VIA SEED (POST /api/forms/seed/{licenseId}) ???????????????
        var updateSeedPayload = new
        {
            maxPoints = 25,
            items = new[]
            {
                new { description = "Updated item 1", penaltyPoints = 5, orderIndex = 1 },
                new { description = "Updated item 2", penaltyPoints = 10, orderIndex = 2 }
            }
        };

        var updateSeedResponse = await _client.PostAsJsonAsync($"/api/forms/seed/{licenseId}", updateSeedPayload);
        updateSeedResponse.StatusCode.Should().Be(HttpStatusCode.OK); // 200 for update (not 201)

        // Verify update
        var getAfterUpdateResponse = await _client.GetAsync($"/api/forms/by-license/{licenseId}");
        var getAfterUpdateContent = await getAfterUpdateResponse.Content.ReadAsStringAsync();
        using var getAfterUpdateDoc = JsonDocument.Parse(getAfterUpdateContent);

        getAfterUpdateDoc.RootElement.GetProperty("maxPoints").GetInt32().Should().Be(25);
        var updatedItems = getAfterUpdateDoc.RootElement.GetProperty("items").EnumerateArray().ToList();
        updatedItems.Should().HaveCount(2);
        updatedItems[0].GetProperty("description").GetString().Should().Be("Updated item 1");

        // Cleanup - delete the license (this won't delete the form due to FK constraints,
        // but that's expected behavior in a real system)
        await _client.DeleteAsync($"/api/license/delete/{licenseId}");
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Protected endpoint tests - No token -> 401
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task ExamForm_GetByLicense_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/forms/by-license/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExamForm_GetByCategory_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/forms/by-category/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExamForm_Seed_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        var payload = new
        {
            maxPoints = 21,
            items = new[] { new { description = "Test", penaltyPoints = 3, orderIndex = 1 } }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/forms/seed/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Role-based access tests
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task ExamForm_GetByLicense_AsSchoolAdmin_ReturnsOk()
    {
        // Arrange - Any authenticated user can view forms
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        // Seed data should have license 1 (B) with or without form
        // If no form exists, it should return 404, not 403
        // Act
        var response = await client.GetAsync("/api/forms/by-license/1");

        // Assert - either OK or NotFound is acceptable (depends on seed data)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExamForm_Seed_AsSchoolAdmin_Returns403()
    {
        // Arrange - Only SuperAdmin can seed forms
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        var payload = new
        {
            maxPoints = 21,
            items = new[] { new { description = "Test", penaltyPoints = 3, orderIndex = 1 } }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/forms/seed/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ExamForm_GetByLicense_InvalidId_Returns400()
    {
        // Arrange
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SuperAdmin");

        // Act
        var response = await client.GetAsync("/api/forms/by-license/-1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExamForm_Seed_NonExistentLicense_Returns404()
    {
        // Arrange
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SuperAdmin");

        var payload = new
        {
            maxPoints = 21,
            items = new[] { new { description = "Test", penaltyPoints = 3, orderIndex = 1 } }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/forms/seed/99999", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
