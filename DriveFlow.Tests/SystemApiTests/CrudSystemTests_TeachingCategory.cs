using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using DriveFlow.Tests.TestInfrastructure;

namespace DriveFlow.Tests.SystemApiTests;

/// <summary>
/// End-to-end CRUD tests for the TeachingCategory controller via HTTP.
/// Uses <see cref="CustomWebApplicationFactory"/> with in-memory database.
/// </summary>
/// <remarks>
/// <para><strong>Routes tested:</strong></para>
/// <list type="bullet">
///   <item>GET /api/teachingCategory/get/{schoolId}</item>
///   <item>POST /api/teachingCategory/create/{schoolId}</item>
///   <item>PUT /api/teachingCategory/update/{schoolId}/{teachingCategoryId}</item>
///   <item>DELETE /api/teachingCategory/delete/{schoolId}/{teachingCategoryId}</item>
/// </list>
/// </remarks>
public sealed class CrudSystemTests_TeachingCategory : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CrudSystemTests_TeachingCategory(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Full CRUD Lifecycle: POST -> GET list -> PUT update -> DELETE -> 404
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task TeachingCategory_FullCrudLifecycle_WorksCorrectly()
    {
        // Arrange - authenticate as SchoolAdmin (can manage teaching categories)
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        // SchoolAdmin is assigned to school 1 in seeded data
        var schoolId = 1;
        // Use seeded license ID (licenseId = 1 = B)
        var licenseId = 1;

        // ??????????????? CREATE (POST /api/teachingCategory/create/{schoolId}) ???????????????
        var createPayload = new
        {
            licenseId = licenseId,
            sessionCost = 150.00m,
            sessionDuration = 60,
            scholarshipPrice = 3000.00m,
            minDrivingLessonsReq = 25
        };

        var createResponse = await _client.PostAsJsonAsync($"/api/teachingCategory/create/{schoolId}", createPayload);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var teachingCategoryId = createDoc.RootElement.GetProperty("teachingCategoryId").GetInt32();
        teachingCategoryId.Should().BeGreaterThan(0);

        // ??????????????? GET LIST (GET /api/teachingCategory/get/{schoolId}) ???????????????
        var listResponse = await _client.GetAsync($"/api/teachingCategory/get/{schoolId}");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listContent = await listResponse.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(listContent);
        var categories = listDoc.RootElement.EnumerateArray();

        // Verify the created category is in the list
        var createdCategory = categories.FirstOrDefault(c =>
            c.GetProperty("teachingCategoryId").GetInt32() == teachingCategoryId);
        createdCategory.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "Created category should be in the list");
        createdCategory.GetProperty("licenseId").GetInt32().Should().Be(licenseId);
        createdCategory.GetProperty("sessionCost").GetDecimal().Should().Be(150.00m);
        createdCategory.GetProperty("sessionDuration").GetInt32().Should().Be(60);
        createdCategory.GetProperty("scholarshipPrice").GetDecimal().Should().Be(3000.00m);
        createdCategory.GetProperty("minDrivingLessonsReq").GetInt32().Should().Be(25);

        // ??????????????? UPDATE (PUT /api/teachingCategory/update/{schoolId}/{id}) ???????????????
        var updatePayload = new
        {
            licenseId = licenseId,
            sessionCost = 180.00m,
            sessionDuration = 90,
            scholarshipPrice = 3500.00m,
            minDrivingLessonsReq = 30
        };

        var updateResponse = await _client.PutAsJsonAsync(
            $"/api/teachingCategory/update/{schoolId}/{teachingCategoryId}", updatePayload);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify update by getting the list again
        var listAfterUpdateResponse = await _client.GetAsync($"/api/teachingCategory/get/{schoolId}");
        var listAfterUpdateContent = await listAfterUpdateResponse.Content.ReadAsStringAsync();
        using var listAfterUpdateDoc = JsonDocument.Parse(listAfterUpdateContent);
        var categoriesAfterUpdate = listAfterUpdateDoc.RootElement.EnumerateArray();

        var updatedCategory = categoriesAfterUpdate.FirstOrDefault(c =>
            c.GetProperty("teachingCategoryId").GetInt32() == teachingCategoryId);
        updatedCategory.GetProperty("sessionCost").GetDecimal().Should().Be(180.00m);
        updatedCategory.GetProperty("sessionDuration").GetInt32().Should().Be(90);
        updatedCategory.GetProperty("scholarshipPrice").GetDecimal().Should().Be(3500.00m);
        updatedCategory.GetProperty("minDrivingLessonsReq").GetInt32().Should().Be(30);

        // ??????????????? DELETE (DELETE /api/teachingCategory/delete/{schoolId}/{id}) ???????????????
        var deleteResponse = await _client.DeleteAsync(
            $"/api/teachingCategory/delete/{schoolId}/{teachingCategoryId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // ??????????????? VERIFY DELETED (GET list should not contain it) ???????????????
        var listAfterDeleteResponse = await _client.GetAsync($"/api/teachingCategory/get/{schoolId}");
        listAfterDeleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listAfterDeleteContent = await listAfterDeleteResponse.Content.ReadAsStringAsync();
        using var listAfterDeleteDoc = JsonDocument.Parse(listAfterDeleteContent);
        var categoriesAfterDelete = listAfterDeleteDoc.RootElement.EnumerateArray();

        var deletedCategory = categoriesAfterDelete.FirstOrDefault(c =>
            c.GetProperty("teachingCategoryId").GetInt32() == teachingCategoryId);
        deletedCategory.ValueKind.Should().Be(JsonValueKind.Undefined,
            "Deleted category should not be in the list");

        // ??????????????? DELETE again -> 404 ???????????????
        var deleteAgainResponse = await _client.DeleteAsync(
            $"/api/teachingCategory/delete/{schoolId}/{teachingCategoryId}");
        deleteAgainResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Protected endpoint tests - No token -> 401
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task TeachingCategory_GetCategories_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/teachingCategory/get/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TeachingCategory_CreateCategory_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        var payload = new
        {
            licenseId = 1,
            sessionCost = 100,
            sessionDuration = 60,
            scholarshipPrice = 2000,
            minDrivingLessonsReq = 20
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/teachingCategory/create/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TeachingCategory_UpdateCategory_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        var payload = new
        {
            licenseId = 1,
            sessionCost = 100,
            sessionDuration = 60,
            scholarshipPrice = 2000,
            minDrivingLessonsReq = 20
        };

        // Act
        var response = await client.PutAsJsonAsync("/api/teachingCategory/update/1/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TeachingCategory_DeleteCategory_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.DeleteAsync("/api/teachingCategory/delete/1/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Role-based access tests
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task TeachingCategory_GetCategories_AsSuperAdmin_ReturnsOk()
    {
        // Arrange - SuperAdmin can view any school's categories
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SuperAdmin");

        // Act
        var response = await client.GetAsync("/api/teachingCategory/get/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TeachingCategory_GetCategories_AsSchoolAdmin_OwnSchool_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        // Act (school 1 is the SchoolAdmin's school)
        var response = await client.GetAsync("/api/teachingCategory/get/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TeachingCategory_GetCategories_AsSchoolAdmin_OtherSchool_Returns403()
    {
        // Arrange - SchoolAdmin cannot access other school's categories
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        // Act (school 999 doesn't belong to this SchoolAdmin)
        var response = await client.GetAsync("/api/teachingCategory/get/999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TeachingCategory_CreateCategory_AsSuperAdmin_Returns403()
    {
        // Arrange - Only SchoolAdmin can create categories
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SuperAdmin");

        var payload = new
        {
            licenseId = 1,
            sessionCost = 100,
            sessionDuration = 60,
            scholarshipPrice = 2000,
            minDrivingLessonsReq = 20
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/teachingCategory/create/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TeachingCategory_CreateCategory_InvalidLicenseId_Returns400()
    {
        // Arrange
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        var payload = new
        {
            licenseId = 99999, // Non-existent license
            sessionCost = 100,
            sessionDuration = 60,
            scholarshipPrice = 2000,
            minDrivingLessonsReq = 20
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/teachingCategory/create/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TeachingCategory_CreateCategory_InvalidDuration_Returns400()
    {
        // Arrange
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        var payload = new
        {
            licenseId = 1,
            sessionCost = 100,
            sessionDuration = 0, // Invalid - must be positive
            scholarshipPrice = 2000,
            minDrivingLessonsReq = 20
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/teachingCategory/create/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
