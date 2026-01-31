using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using DriveFlow.Tests.TestInfrastructure;

namespace DriveFlow.Tests.SystemApiTests;

/// <summary>
/// End-to-end CRUD tests for the ApplicationUserTeachingCategory controller via HTTP.
/// Uses <see cref="CustomWebApplicationFactory"/> with in-memory database.
/// </summary>
/// <remarks>
/// <para><strong>Routes tested:</strong></para>
/// <list type="bullet">
///   <item>GET /api/autoschool/{schoolId}/instructorCategories/instructor/{instructorId}/teachingCategories</item>
///   <item>GET /api/autoschool/{schoolId}/instructorCategories/teachingCategory/{teachingCategoryId}/instructors</item>
///   <item>POST /api/autoschool/{schoolId}/instructorCategories/create</item>
///   <item>DELETE /api/autoschool/{schoolId}/instructorCategories/delete/{applicationUserTeachingCategoryId}</item>
/// </list>
/// <para>Note: This controller manages the many-to-many relationship between instructors and teaching categories.</para>
/// </remarks>
public sealed class CrudSystemTests_ApplicationUserTeachingCategory : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CrudSystemTests_ApplicationUserTeachingCategory(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Full CRUD Lifecycle: POST -> GET list -> DELETE -> verify deleted
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task ApplicationUserTeachingCategory_FullCrudLifecycle_WorksCorrectly()
    {
        // Arrange - authenticate as SchoolAdmin
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        // SchoolAdmin is assigned to school 1 in seeded data
        var schoolId = 1;

        // First, get the instructor ID for instructorA2 (instructor2a@test.com)
        // This instructor is already linked to teaching category 1 (B), so we'll test with category 2 (A)
        var instructorEmail = "instructor2a@test.com";

        // Get instructor teaching categories to find instructor ID
        // We need to use the seeded instructor (instructor@test.com)
        // Let's use instructor2a which is linked to category 1, and link to category 2
        
        // First, let's create a new teaching category to link to instructor
        var createCategoryPayload = new
        {
            licenseId = 2, // License A
            sessionCost = 100.00m,
            sessionDuration = 45,
            scholarshipPrice = 1500.00m,
            minDrivingLessonsReq = 15
        };

        var createCategoryResponse = await _client.PostAsJsonAsync(
            $"/api/teachingCategory/create/{schoolId}", createCategoryPayload);
        createCategoryResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var categoryContent = await createCategoryResponse.Content.ReadAsStringAsync();
        using var categoryDoc = JsonDocument.Parse(categoryContent);
        var newTeachingCategoryId = categoryDoc.RootElement.GetProperty("teachingCategoryId").GetInt32();

        // Get existing instructor teaching categories to find instructor ID
        // Use instructor@test.com (main instructor)
        var instructorCategoriesResponse = await _client.GetAsync(
            $"/api/autoschool/{schoolId}/instructorCategories/teachingCategory/1/instructors");
        instructorCategoriesResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var instructorCategoriesContent = await instructorCategoriesResponse.Content.ReadAsStringAsync();
        using var instructorDoc = JsonDocument.Parse(instructorCategoriesContent);
        var instructors = instructorDoc.RootElement.EnumerateArray().ToList();
        instructors.Should().NotBeEmpty("Seeded data should have instructors linked to category 1");

        var instructorId = instructors[0].GetProperty("instructorId").GetString();

        // ??????????????? CREATE (POST /api/autoschool/{schoolId}/instructorCategories/create) ???????????????
        var createPayload = new
        {
            instructorId = instructorId,
            teachingCategoryId = newTeachingCategoryId
        };

        var createResponse = await _client.PostAsJsonAsync(
            $"/api/autoschool/{schoolId}/instructorCategories/create", createPayload);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var linkId = createDoc.RootElement.GetProperty("applicationUserTeachingCategoryId").GetInt32();
        linkId.Should().BeGreaterThan(0);

        // ??????????????? GET INSTRUCTOR CATEGORIES (verify link exists) ???????????????
        var getInstructorCategoriesResponse = await _client.GetAsync(
            $"/api/autoschool/{schoolId}/instructorCategories/instructor/{instructorId}/teachingCategories");
        getInstructorCategoriesResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getContent = await getInstructorCategoriesResponse.Content.ReadAsStringAsync();
        using var getDoc = JsonDocument.Parse(getContent);
        var categories = getDoc.RootElement.EnumerateArray().ToList();

        var linkedCategory = categories.FirstOrDefault(c =>
            c.GetProperty("teachingCategoryId").GetInt32() == newTeachingCategoryId);
        linkedCategory.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "New teaching category should be linked to instructor");

        // ??????????????? GET CATEGORY INSTRUCTORS (verify instructor is linked) ???????????????
        var getCategoryInstructorsResponse = await _client.GetAsync(
            $"/api/autoschool/{schoolId}/instructorCategories/teachingCategory/{newTeachingCategoryId}/instructors");
        getCategoryInstructorsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getCategoryContent = await getCategoryInstructorsResponse.Content.ReadAsStringAsync();
        using var getCategoryDoc = JsonDocument.Parse(getCategoryContent);
        var linkedInstructors = getCategoryDoc.RootElement.EnumerateArray().ToList();

        var linkedInstructor = linkedInstructors.FirstOrDefault(i =>
            i.GetProperty("instructorId").GetString() == instructorId);
        linkedInstructor.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "Instructor should be listed in category's instructors");

        // ??????????????? DELETE (DELETE /api/autoschool/{schoolId}/instructorCategories/delete/{id}) ???????????????
        var deleteResponse = await _client.DeleteAsync(
            $"/api/autoschool/{schoolId}/instructorCategories/delete/{linkId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // ??????????????? VERIFY DELETED (GET instructor categories should not contain it) ???????????????
        var getAfterDeleteResponse = await _client.GetAsync(
            $"/api/autoschool/{schoolId}/instructorCategories/instructor/{instructorId}/teachingCategories");
        getAfterDeleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getAfterDeleteContent = await getAfterDeleteResponse.Content.ReadAsStringAsync();
        using var getAfterDeleteDoc = JsonDocument.Parse(getAfterDeleteContent);
        var categoriesAfterDelete = getAfterDeleteDoc.RootElement.EnumerateArray().ToList();

        var deletedLink = categoriesAfterDelete.FirstOrDefault(c =>
            c.GetProperty("applicationUserTeachingCategoryId").GetInt32() == linkId);
        deletedLink.ValueKind.Should().Be(JsonValueKind.Undefined,
            "Deleted link should not be in the list");

        // ??????????????? DELETE again -> 404 ???????????????
        var deleteAgainResponse = await _client.DeleteAsync(
            $"/api/autoschool/{schoolId}/instructorCategories/delete/{linkId}");
        deleteAgainResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Cleanup - delete the teaching category we created
        await _client.DeleteAsync($"/api/teachingCategory/delete/{schoolId}/{newTeachingCategoryId}");
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Protected endpoint tests - No token -> 401
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task AppUserTeachingCategory_GetInstructorCategories_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync(
            "/api/autoschool/1/instructorCategories/instructor/test-id/teachingCategories");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AppUserTeachingCategory_GetCategoryInstructors_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync(
            "/api/autoschool/1/instructorCategories/teachingCategory/1/instructors");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AppUserTeachingCategory_Create_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        var payload = new
        {
            instructorId = "test-id",
            teachingCategoryId = 1
        };

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/autoschool/1/instructorCategories/create", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AppUserTeachingCategory_Delete_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.DeleteAsync(
            "/api/autoschool/1/instructorCategories/delete/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Role-based access tests
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task AppUserTeachingCategory_GetInstructorCategories_AsSuperAdmin_ReturnsOk()
    {
        // Arrange - SuperAdmin can view any school's data
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SuperAdmin");

        // Get the instructor ID from the seeded data (we need a valid instructor ID)
        // First authenticate as SchoolAdmin to get an instructor ID
        var adminClient = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(adminClient, "SchoolAdmin");
        var instructorsResponse = await adminClient.GetAsync(
            "/api/autoschool/1/instructorCategories/teachingCategory/1/instructors");
        var content = await instructorsResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var instructors = doc.RootElement.EnumerateArray().ToList();
        
        if (instructors.Any())
        {
            var instructorId = instructors[0].GetProperty("instructorId").GetString();

            // Act
            var response = await client.GetAsync(
                $"/api/autoschool/1/instructorCategories/instructor/{instructorId}/teachingCategories");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task AppUserTeachingCategory_GetCategoryInstructors_AsSchoolAdmin_OwnSchool_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        // Act (school 1 is the SchoolAdmin's school, category 1 belongs to school 1)
        var response = await client.GetAsync(
            "/api/autoschool/1/instructorCategories/teachingCategory/1/instructors");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AppUserTeachingCategory_GetCategoryInstructors_AsSchoolAdmin_OtherSchool_Returns403()
    {
        // Arrange - SchoolAdmin cannot access other school's data
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        // Act (school 2 doesn't belong to this SchoolAdmin)
        var response = await client.GetAsync(
            "/api/autoschool/2/instructorCategories/teachingCategory/3/instructors");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AppUserTeachingCategory_Create_AsSuperAdmin_Returns403()
    {
        // Arrange - Only SchoolAdmin can create links
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SuperAdmin");

        var payload = new
        {
            instructorId = "test-id",
            teachingCategoryId = 1
        };

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/autoschool/1/instructorCategories/create", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AppUserTeachingCategory_Create_DuplicateLink_Returns409()
    {
        // Arrange - Try to create a link that already exists in seeded data
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        // Get existing instructor ID from seeded data
        var instructorsResponse = await client.GetAsync(
            "/api/autoschool/1/instructorCategories/teachingCategory/1/instructors");
        var content = await instructorsResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var instructors = doc.RootElement.EnumerateArray().ToList();

        if (instructors.Any())
        {
            var instructorId = instructors[0].GetProperty("instructorId").GetString();

            // Try to link the same instructor to the same category again
            var payload = new
            {
                instructorId = instructorId,
                teachingCategoryId = 1 // Already linked in seed data
            };

            // Act
            var response = await client.PostAsJsonAsync(
                "/api/autoschool/1/instructorCategories/create", payload);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
    }

    [Fact]
    public async Task AppUserTeachingCategory_Create_InvalidInstructorId_Returns400()
    {
        // Arrange
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        var payload = new
        {
            instructorId = "non-existent-instructor-id",
            teachingCategoryId = 1
        };

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/autoschool/1/instructorCategories/create", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AppUserTeachingCategory_Create_InvalidTeachingCategoryId_Returns400()
    {
        // Arrange
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        // Get a valid instructor ID first
        var instructorsResponse = await client.GetAsync(
            "/api/autoschool/1/instructorCategories/teachingCategory/1/instructors");
        var content = await instructorsResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var instructors = doc.RootElement.EnumerateArray().ToList();

        if (instructors.Any())
        {
            var instructorId = instructors[0].GetProperty("instructorId").GetString();

            var payload = new
            {
                instructorId = instructorId,
                teachingCategoryId = 99999 // Non-existent category
            };

            // Act
            var response = await client.PostAsJsonAsync(
                "/api/autoschool/1/instructorCategories/create", payload);

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }
}
