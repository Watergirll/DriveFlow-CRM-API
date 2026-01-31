using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using DriveFlow.Tests.TestInfrastructure;

namespace DriveFlow.Tests.SystemApiTests;

/// <summary>
/// System tests for <c>StudentController</c>.
/// Tests student-specific endpoints for files, appointments, and statistics.
/// </summary>
/// <remarks>
/// <para><strong>Routes tested:</strong></para>
/// <list type="bullet">
///   <item>GET /api/student/{studentId}/files - Get student's files</item>
///   <item>GET /api/student/future-appointments - Get future appointments</item>
///   <item>GET /api/student/all-appointments - Get all appointments</item>
///   <item>GET /api/student/{id_student}/stats/mistakes - Get mistake statistics</item>
/// </list>
/// </remarks>
public sealed class StudentSystemTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public StudentSystemTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ?????????????????????????????????????????????????????????????????????????
    // GET /api/student/future-appointments - Get future appointments
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task GetFutureAppointments_AsStudent_Returns200()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "Student");

        // Act
        var response = await _client.GetAsync("/api/student/future-appointments");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetFutureAppointments_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/student/future-appointments");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetFutureAppointments_AsInstructor_Returns403()
    {
        // Arrange - Only students can access this endpoint
        await ApiAuthHelper.AuthenticateAsAsync(_client, "Instructor");

        // Act
        var response = await _client.GetAsync("/api/student/future-appointments");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // GET /api/student/all-appointments - Get all appointments
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task GetAllAppointments_AsStudent_Returns200()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "Student");

        // Act
        var response = await _client.GetAsync("/api/student/all-appointments");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetAllAppointments_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/student/all-appointments");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // GET /api/student/{studentId}/files - Get student's files
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task GetStudentFiles_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/student/some-student-id/files");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetStudentFiles_AsSchoolAdmin_Returns403()
    {
        // Arrange - Only students can access their own files via this endpoint
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        // Act
        var response = await _client.GetAsync("/api/student/some-student-id/files");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // GET /api/student/file-details/{fileId} - Get file details
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task GetFileDetails_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/student/file-details/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetFileDetails_NonExistentFile_Returns404()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "Student");

        // Act
        var response = await _client.GetAsync("/api/student/file-details/99999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
