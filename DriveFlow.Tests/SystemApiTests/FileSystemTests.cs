using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using DriveFlow.Tests.TestInfrastructure;

namespace DriveFlow.Tests.SystemApiTests;

/// <summary>
/// System tests for <c>FileController</c>.
/// Tests file management endpoints (CRUD operations on student files).
/// </summary>
/// <remarks>
/// <para><strong>Routes tested:</strong></para>
/// <list type="bullet">
///   <item>GET /api/file/fetchAll/{schoolId} - Get all files for a school</item>
///   <item>GET /api/file/details/{fileId} - Get file details</item>
///   <item>POST /api/file/createFile/{studentId} - Create a new file</item>
///   <item>PUT /api/file/editFile/{fileId} - Edit an existing file</item>
///   <item>PUT /api/file/editPayment/{paymentId} - Edit payment</item>
///   <item>DELETE /api/file/delete/{fileId} - Delete a file</item>
/// </list>
/// </remarks>
public sealed class FileSystemTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public FileSystemTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ?????????????????????????????????????????????????????????????????????????
    // GET /api/file/fetchAll/{schoolId} - Get all files for school
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task FetchAll_AsSchoolAdmin_Returns200()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        // Act
        var response = await _client.GetAsync("/api/file/fetchAll/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task FetchAll_AsSuperAdmin_Returns200()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SuperAdmin");

        // Act
        var response = await _client.GetAsync("/api/file/fetchAll/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task FetchAll_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/file/fetchAll/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FetchAll_AsStudent_Returns403()
    {
        // Arrange - Students cannot access file management
        await ApiAuthHelper.AuthenticateAsAsync(_client, "Student");

        // Act
        var response = await _client.GetAsync("/api/file/fetchAll/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task FetchAll_SchoolAdminAccessingOtherSchool_Returns403()
    {
        // Arrange - SchoolAdmin A cannot access School B's files
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin"); // School A

        // Act
        var response = await _client.GetAsync("/api/file/fetchAll/2"); // School B

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task FetchAll_InvalidSchoolId_Returns400()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SuperAdmin");

        // Act
        var response = await _client.GetAsync("/api/file/fetchAll/99999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // GET /api/file/details/{fileId} - Get file details
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task GetDetails_AsSchoolAdmin_Returns200()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        // Act - FileId 1 belongs to School A
        var response = await _client.GetAsync("/api/file/details/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        doc.RootElement.GetProperty("fileId").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task GetDetails_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/file/details/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDetails_NonExistentFile_Returns404()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        // Act
        var response = await _client.GetAsync("/api/file/details/99999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetDetails_SchoolAdminAccessingOtherSchool_Returns403()
    {
        // Arrange - SchoolAdmin A cannot access School B's files
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin"); // School A

        // Act - FileId 3 belongs to School B
        var response = await _client.GetAsync("/api/file/details/3");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // DELETE /api/file/delete/{fileId} - Delete a file
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task Delete_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.DeleteAsync("/api/file/delete/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Delete_AsStudent_Returns403()
    {
        // Arrange - Students cannot delete files
        await ApiAuthHelper.AuthenticateAsAsync(_client, "Student");

        // Act
        var response = await _client.DeleteAsync("/api/file/delete/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Delete_NonExistentFile_Returns404()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        // Act
        var response = await _client.DeleteAsync("/api/file/delete/99999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_SchoolAdminAccessingOtherSchool_Returns403()
    {
        // Arrange - SchoolAdmin A cannot delete School B's files
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin"); // School A

        // Act - FileId 3 belongs to School B
        var response = await _client.DeleteAsync("/api/file/delete/3");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // PUT /api/file/editFile/{fileId} - Edit a file
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task EditFile_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        var payload = new { status = "APPROVED" };

        // Act
        var response = await client.PutAsJsonAsync("/api/file/editFile/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EditFile_AsStudent_Returns403()
    {
        // Arrange - Students cannot edit files
        await ApiAuthHelper.AuthenticateAsAsync(_client, "Student");

        var payload = new { status = "APPROVED" };

        // Act
        var response = await _client.PutAsJsonAsync("/api/file/editFile/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EditFile_InvalidStatus_Returns400()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        var payload = new { Status = "INVALID_STATUS" };

        // Act
        var response = await _client.PutAsJsonAsync("/api/file/editFile/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // PUT /api/file/editPayment/{paymentId} - Edit payment
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task EditPayment_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        var payload = new { scholarshipBasePayment = true, sessionsPayed = 5 };

        // Act
        var response = await client.PutAsJsonAsync("/api/file/editPayment/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EditPayment_NonExistentPayment_Returns400()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        var payload = new { scholarshipBasePayment = true, sessionsPayed = 5 };

        // Act
        var response = await _client.PutAsJsonAsync("/api/file/editPayment/99999", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
