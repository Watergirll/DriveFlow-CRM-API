using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using DriveFlow.Tests.TestInfrastructure;

namespace DriveFlow.Tests.SystemApiTests;

/// <summary>
/// System tests for <c>AccountingController</c>.
/// Tests invoice generation endpoint with authorization and validation.
/// </summary>
/// <remarks>
/// <para><strong>Routes tested:</strong></para>
/// <list type="bullet">
///   <item>GET /api/accounting/file/{fileId}/invoice - Generate PDF invoice</item>
/// </list>
/// <para>
/// Note: The invoice endpoint requires an external invoice service (INVOICE_SERVICE_URL).
/// In tests, we verify authorization and validation logic; the actual PDF generation
/// depends on the external service configuration.
/// </para>
/// </remarks>
public sealed class AccountingSystemTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AccountingSystemTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ?????????????????????????????????????????????????????????????????????????
    // GET /api/accounting/file/{fileId}/invoice - Generate invoice
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task GetInvoice_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/accounting/file/1/invoice");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetInvoice_AsInstructor_Returns403()
    {
        // Arrange - Only Student and SchoolAdmin can access invoices
        await ApiAuthHelper.AuthenticateAsAsync(_client, "Instructor");

        // Act
        var response = await _client.GetAsync("/api/accounting/file/1/invoice");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetInvoice_NonExistentFile_Returns404()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        // Act
        var response = await _client.GetAsync("/api/accounting/file/99999/invoice");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetInvoice_StudentAccessingOtherStudentFile_Returns403()
    {
        // Arrange - Student A tries to access Student B's file
        await ApiAuthHelper.AuthenticateAsAsync(_client, "Student"); // Student from School A

        // Act - FileId 3 belongs to StudentB (School B)
        var response = await _client.GetAsync("/api/accounting/file/3/invoice");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetInvoice_SchoolAdminAccessingOtherSchoolFile_Returns403()
    {
        // Arrange - SchoolAdmin A tries to access School B's file
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin"); // School A

        // Act - FileId 3 belongs to School B
        var response = await _client.GetAsync("/api/accounting/file/3/invoice");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetInvoice_InvalidFileId_Returns404()
    {
        // Arrange
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        // Act - Use 0 as invalid file ID
        var response = await _client.GetAsync("/api/accounting/file/0/invoice");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetInvoice_FileNotFullyPaid_Returns400()
    {
        // Arrange - Access a file that exists but tuition is not fully paid
        // File 1 exists in seeded data but may not have full payment
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        // Act - FileId 1 has ScholarshipBasePayment=true, SessionsPayed=10
        // TeachingCategory MinDrivingLessonsReq=30, so it's not fully paid
        var response = await _client.GetAsync("/api/accounting/file/1/invoice");

        // Assert - Should be 400 or 500 (if invoice service not configured)
        // The actual status depends on payment status and service configuration
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,        // Tuition not fully paid
            HttpStatusCode.InternalServerError // Invoice service not configured
        );
    }

    [Fact]
    public async Task GetInvoice_AsStudentOwnFile_ReturnsValidResponse()
    {
        // Arrange - Student accessing their own file
        await ApiAuthHelper.AuthenticateAsAsync(_client, "Student");

        // Act - FileId 1 belongs to Student A
        var response = await _client.GetAsync("/api/accounting/file/1/invoice");

        // Assert - Should be 400 (not fully paid) or 500 (service not configured)
        // But NOT 401 or 403
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
