using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using DriveFlow.Tests.TestInfrastructure;

namespace DriveFlow.Tests.SystemApiTests;

/// <summary>
/// End-to-end CRUD tests for the AutoSchool controller via HTTP.
/// Uses <see cref="CustomWebApplicationFactory"/> with in-memory database.
/// </summary>
/// <remarks>
/// <para><strong>Routes tested:</strong></para>
/// <list type="bullet">
///   <item>GET /api/autoschool/get</item>
///   <item>POST /api/autoschool/create</item>
///   <item>PUT /api/autoschool/update/{autoSchoolId}</item>
///   <item>DELETE /api/autoschool/delete/{autoSchoolId}</item>
/// </list>
/// </remarks>
public sealed class CrudSystemTests_AutoSchool : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CrudSystemTests_AutoSchool(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Full CRUD Lifecycle: POST -> GET list -> PUT update -> DELETE -> 404
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task AutoSchool_FullCrudLifecycle_WorksCorrectly()
    {
        // Arrange - authenticate as SuperAdmin
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SuperAdmin");

        // Use seeded address ID (addressId = 1)
        var addressId = 1;

        // ??????????????? CREATE (POST /api/autoschool/create) ???????????????
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var createPayload = new
        {
            autoSchool = new
            {
                name = $"TestSchool_{uniqueSuffix}",
                description = "Test driving school",
                website = "https://testschool.ro",
                phoneNumber = "0712345678",
                email = $"school_{uniqueSuffix}@test.ro",
                status = "Active",
                addressId = addressId
            },
            schoolAdmin = new
            {
                firstName = "Test",
                lastName = "Admin",
                email = $"admin_{uniqueSuffix}@test.ro",
                phone = "0723456789",
                password = "TestPass123!"
            }
        };

        var createResponse = await _client.PostAsJsonAsync("/api/autoschool/create", createPayload);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var autoSchoolId = createDoc.RootElement.GetProperty("autoSchoolId").GetInt32();
        autoSchoolId.Should().BeGreaterThan(0);

        // ??????????????? GET LIST (GET /api/autoschool/get) ???????????????
        var listResponse = await _client.GetAsync("/api/autoschool/get");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listContent = await listResponse.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(listContent);
        var schools = listDoc.RootElement.EnumerateArray();

        // Verify the created school is in the list
        var createdSchool = schools.FirstOrDefault(s =>
            s.GetProperty("autoSchoolId").GetInt32() == autoSchoolId);
        createdSchool.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "Created school should be in the list");
        createdSchool.GetProperty("name").GetString().Should().Be($"TestSchool_{uniqueSuffix}");
        createdSchool.GetProperty("status").GetString().Should().Be("active");

        // ??????????????? UPDATE (PUT /api/autoschool/update/{autoSchoolId}) ???????????????
        var updatePayload = new
        {
            name = $"UpdatedSchool_{uniqueSuffix}",
            description = "Updated description",
            website = "https://updatedschool.ro",
            phoneNumber = "0799999999",
            email = $"updated_{uniqueSuffix}@test.ro",
            status = "Demo",
            addressId = addressId
        };

        var updateResponse = await _client.PutAsJsonAsync($"/api/autoschool/update/{autoSchoolId}", updatePayload);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify update by getting the list again
        var listAfterUpdateResponse = await _client.GetAsync("/api/autoschool/get");
        var listAfterUpdateContent = await listAfterUpdateResponse.Content.ReadAsStringAsync();
        using var listAfterUpdateDoc = JsonDocument.Parse(listAfterUpdateContent);
        var schoolsAfterUpdate = listAfterUpdateDoc.RootElement.EnumerateArray();

        var updatedSchool = schoolsAfterUpdate.FirstOrDefault(s =>
            s.GetProperty("autoSchoolId").GetInt32() == autoSchoolId);
        updatedSchool.GetProperty("name").GetString().Should().Be($"UpdatedSchool_{uniqueSuffix}");
        updatedSchool.GetProperty("status").GetString().Should().Be("demo");

        // ??????????????? DELETE (DELETE /api/autoschool/delete/{autoSchoolId}) ???????????????
        var deleteResponse = await _client.DeleteAsync($"/api/autoschool/delete/{autoSchoolId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // ??????????????? VERIFY DELETED (GET list should not contain it) ???????????????
        var listAfterDeleteResponse = await _client.GetAsync("/api/autoschool/get");
        listAfterDeleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listAfterDeleteContent = await listAfterDeleteResponse.Content.ReadAsStringAsync();
        using var listAfterDeleteDoc = JsonDocument.Parse(listAfterDeleteContent);
        var schoolsAfterDelete = listAfterDeleteDoc.RootElement.EnumerateArray();

        var deletedSchool = schoolsAfterDelete.FirstOrDefault(s =>
            s.GetProperty("autoSchoolId").GetInt32() == autoSchoolId);
        deletedSchool.ValueKind.Should().Be(JsonValueKind.Undefined,
            "Deleted school should not be in the list");

        // ??????????????? DELETE again -> 404 ???????????????
        var deleteAgainResponse = await _client.DeleteAsync($"/api/autoschool/delete/{autoSchoolId}");
        deleteAgainResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Protected endpoint tests - No token -> 401
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task AutoSchool_GetAutoSchools_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/autoschool/get");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AutoSchool_CreateAutoSchool_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        var payload = new
        {
            autoSchool = new
            {
                name = "Test",
                phoneNumber = "0712345678",
                email = "test@test.ro",
                status = "Active",
                addressId = 1
            },
            schoolAdmin = new
            {
                firstName = "Test",
                lastName = "Admin",
                email = "testadmin@test.ro",
                phone = "0723456789",
                password = "TestPass123!"
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/autoschool/create", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AutoSchool_UpdateAutoSchool_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        var payload = new { name = "Updated" };

        // Act
        var response = await client.PutAsJsonAsync("/api/autoschool/update/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AutoSchool_DeleteAutoSchool_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.DeleteAsync("/api/autoschool/delete/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Role-based access tests
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task AutoSchool_GetAutoSchools_AsSchoolAdmin_Returns403()
    {
        // Arrange - GET /api/autoschool/get is SuperAdmin only
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        // Act
        var response = await client.GetAsync("/api/autoschool/get");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AutoSchool_CreateAutoSchool_AsSchoolAdmin_Returns403()
    {
        // Arrange - Only SuperAdmin can create schools
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        var payload = new
        {
            autoSchool = new
            {
                name = "TestForbidden",
                phoneNumber = "0712345678",
                email = "forbidden@test.ro",
                status = "Active",
                addressId = 1
            },
            schoolAdmin = new
            {
                firstName = "Test",
                lastName = "Admin",
                email = "forbiddenadmin@test.ro",
                phone = "0723456789",
                password = "TestPass123!"
            }
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/autoschool/create", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AutoSchool_DeleteAutoSchool_AsSchoolAdmin_Returns403()
    {
        // Arrange - Only SuperAdmin can delete schools
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        // Act (using seeded school ID 1)
        var response = await client.DeleteAsync("/api/autoschool/delete/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AutoSchool_Update_AsSchoolAdmin_OwnSchool_ReturnsOk()
    {
        // Arrange - SchoolAdmin can update their own school (partial update)
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        // SchoolAdmin is assigned to school 1 in seeded data
        var payload = new
        {
            description = "Updated by SchoolAdmin"
        };

        // Act
        var response = await client.PutAsJsonAsync("/api/autoschool/update/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
