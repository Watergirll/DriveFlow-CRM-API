using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using DriveFlow.Tests.TestInfrastructure;

namespace DriveFlow.Tests.SystemApiTests;

/// <summary>
/// System tests for <c>AutoSchoolPageController</c>.
/// Tests public endpoints for viewing auto school information on the landing page.
/// </summary>
/// <remarks>
/// <para><strong>Routes tested:</strong></para>
/// <list type="bullet">
///   <item>GET /api/schoolspage/schools - Get all active/demo schools (public)</item>
///   <item>GET /api/schoolspage/schools/{schoolId} - Get school details (public)</item>
/// </list>
/// <para>
/// These endpoints are publicly accessible (no authentication required).
/// </para>
/// </remarks>
public sealed class AutoSchoolPageSystemTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AutoSchoolPageSystemTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ?????????????????????????????????????????????????????????????????????????
    // GET /api/schoolspage/schools - Get all schools (public)
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task GetSchools_NoAuth_Returns200()
    {
        // Arrange - This endpoint is public
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/schoolspage/schools");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetSchools_ReturnsActiveAndDemoSchools()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/schoolspage/schools");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);

        var schools = doc.RootElement.EnumerateArray().ToList();
        schools.Should().NotBeEmpty("Seeded data should contain active schools");

        // Verify each school has required fields
        foreach (var school in schools)
        {
            school.GetProperty("id").ValueKind.Should().Be(JsonValueKind.Number);
            school.GetProperty("name").ValueKind.Should().Be(JsonValueKind.String);
            school.GetProperty("status").ValueKind.Should().Be(JsonValueKind.String);

            // Status should be "active" or "demo"
            var status = school.GetProperty("status").GetString();
            status.Should().BeOneOf("active", "demo");
        }
    }

    [Fact]
    public async Task GetSchools_AsAuthenticatedUser_AlsoWorks()
    {
        // Arrange - Public endpoints should work with auth too
        await ApiAuthHelper.AuthenticateAsAsync(_client, "Student");

        // Act
        var response = await _client.GetAsync("/api/schoolspage/schools");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // GET /api/schoolspage/schools/{schoolId} - Get school details (public)
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task GetSchoolDetails_NoAuth_Returns200()
    {
        // Arrange - This endpoint is public
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act - School 1 (AutoSchoolA) exists in seeded data
        var response = await client.GetAsync("/api/schoolspage/schools/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);

        doc.RootElement.GetProperty("autoSchoolId").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("name").GetString().Should().Be("AutoSchoolA");
    }

    [Fact]
    public async Task GetSchoolDetails_ReturnsCompleteInfo()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/schoolspage/schools/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);

        // Verify all expected fields are present
        doc.RootElement.TryGetProperty("autoSchoolId", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("name", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("status", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("studentCount", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("instructorCount", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("vehicles", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("teachingCategories", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetSchoolDetails_NonExistentSchool_Returns404()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/schoolspage/schools/99999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSchoolDetails_InvalidSchoolId_Returns404()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act - Use 0 as invalid ID
        var response = await client.GetAsync("/api/schoolspage/schools/0");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSchoolDetails_School2_Returns200()
    {
        // Arrange - Verify School B (AutoSchoolB) also works
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/schoolspage/schools/2");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);

        doc.RootElement.GetProperty("autoSchoolId").GetInt32().Should().Be(2);
        doc.RootElement.GetProperty("name").GetString().Should().Be("AutoSchoolB");
    }

    [Fact]
    public async Task GetSchoolDetails_ContainsVehiclesAndCategories()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/schoolspage/schools/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);

        var vehicles = doc.RootElement.GetProperty("vehicles");
        vehicles.ValueKind.Should().Be(JsonValueKind.Array);
        vehicles.GetArrayLength().Should().BeGreaterThan(0, "School A should have seeded vehicles");

        var categories = doc.RootElement.GetProperty("teachingCategories");
        categories.ValueKind.Should().Be(JsonValueKind.Array);
        categories.GetArrayLength().Should().BeGreaterThan(0, "School A should have seeded teaching categories");
    }
}
