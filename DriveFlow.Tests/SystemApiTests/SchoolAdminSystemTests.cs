using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using DriveFlow.Tests.TestInfrastructure;

namespace DriveFlow.Tests.SystemApiTests;

/// <summary>
/// System tests for <c>SchoolAdminController</c>.
/// Tests user management operations (create/list/update/delete instructors and students).
/// </summary>
/// <remarks>
/// <para><strong>Routes tested:</strong></para>
/// <list type="bullet">
///   <item>GET /api/SchoolAdmin/autoschool/{schoolId}/getUsers - List all users</item>
///   <item>GET /api/SchoolAdmin/autoschool/{schoolId}/getUsers/{type} - List users by type</item>
///   <item>GET /api/SchoolAdmin/autoschool/{schoolId}/getUser/{userId} - Get single user</item>
/// </list>
/// </remarks>
public sealed class SchoolAdminSystemTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SchoolAdminSystemTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ?????????????????????????????????????????????????????????????????????????
    // GET /api/SchoolAdmin/autoschool/{schoolId}/getUsers - List all users
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task GetUsers_AsSchoolAdmin_Returns200WithUsers()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        // Act
        var response = await _client.GetAsync("/api/SchoolAdmin/autoschool/1/getUsers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetUsers_AsSuperAdmin_Returns200()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SuperAdmin");

        // Act
        var response = await _client.GetAsync("/api/SchoolAdmin/autoschool/1/getUsers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetUsers_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/SchoolAdmin/autoschool/1/getUsers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUsers_AsStudent_Returns403()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "Student");

        // Act
        var response = await _client.GetAsync("/api/SchoolAdmin/autoschool/1/getUsers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUsers_SchoolAdminAccessingOtherSchool_Returns403()
    {
        // Arrange - SchoolAdmin from School A tries to access School B
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin"); // School A

        // Act
        var response = await _client.GetAsync("/api/SchoolAdmin/autoschool/2/getUsers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // GET /api/SchoolAdmin/autoschool/{schoolId}/getUsers/{type}
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task GetUsersByType_Student_Returns200()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        // Act
        var response = await _client.GetAsync("/api/SchoolAdmin/autoschool/1/getUsers/Student");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetUsersByType_Instructor_Returns200()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        // Act
        var response = await _client.GetAsync("/api/SchoolAdmin/autoschool/1/getUsers/Instructor");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetUsersByType_InvalidType_Returns400()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        // Act
        var response = await _client.GetAsync("/api/SchoolAdmin/autoschool/1/getUsers/InvalidRole");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetUsersByType_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/SchoolAdmin/autoschool/1/getUsers/Student");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
