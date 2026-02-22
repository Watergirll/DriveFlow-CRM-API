using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using DriveFlow.Tests.TestInfrastructure;

namespace DriveFlow.Tests.SystemApiTests;

/// <summary>
/// End-to-end business flow tests for the Request lifecycle via HTTP.
/// Tests the complete flow: Create Request -> Fetch -> Approve/Reject -> Verify status update.
/// Uses <see cref="CustomWebApplicationFactory"/> with in-memory database.
/// </summary>
/// <remarks>
/// <para><strong>Routes tested:</strong></para>
/// <list type="bullet">
///   <item>POST /api/request/school/{schoolId}/createRequest - Create enrollment request (public)</item>
///   <item>GET /api/request/school/{schoolId}/fetchSchoolRequests - Fetch all requests for school</item>
///   <item>PUT /api/request/update/{requestId}/updateRequestStatus - Update request status</item>
///   <item>DELETE /api/request/delete/{requestId}/deleteRequest - Delete request</item>
/// </list>
/// </remarks>
public sealed class RequestFlowSystemTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RequestFlowSystemTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Complete Request Flow: Create -> Fetch -> Approve -> Verify -> Delete
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task RequestFlow_CreateAndApprove_WorksCorrectly()
    {
        // ??????????????? STEP 1: Create request (no auth required) ???????????????
        var schoolId = 1;
        var createPayload = new
        {
            firstName = "TestFlow",
            lastName = "Approval",
            phoneNr = "0722111222",
            drivingCategory = "B"
        };

        var createResponse = await _client.PostAsJsonAsync(
            $"/api/request/school/{schoolId}/createRequest", createPayload);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var requestId = createDoc.RootElement.GetProperty("id").GetInt32();
        requestId.Should().BeGreaterThan(0);

        // Verify initial status is PENDING
        var initialStatus = createDoc.RootElement.GetProperty("status").GetString();
        initialStatus.Should().Be("PENDING");

        // ??????????????? STEP 2: Fetch requests as SchoolAdmin ???????????????
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        var fetchResponse = await _client.GetAsync(
            $"/api/request/school/{schoolId}/fetchSchoolRequests");
        fetchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetchContent = await fetchResponse.Content.ReadAsStringAsync();
        using var fetchDoc = JsonDocument.Parse(fetchContent);
        var requests = fetchDoc.RootElement.EnumerateArray().ToList();

        // Verify our created request is in the list
        var createdRequest = requests.FirstOrDefault(r =>
            r.GetProperty("id").GetInt32() == requestId);
        createdRequest.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "Created request should be in the fetch list");
        createdRequest.GetProperty("status").GetString().Should().Be("PENDING");

        // ??????????????? STEP 3: Approve the request ???????????????
        var approvePayload = new { status = "APPROVED" };

        var approveResponse = await _client.PutAsJsonAsync(
            $"/api/request/update/{requestId}/updateRequestStatus", approvePayload);
        approveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var approveContent = await approveResponse.Content.ReadAsStringAsync();
        using var approveDoc = JsonDocument.Parse(approveContent);
        var updatedStatus = approveDoc.RootElement.GetProperty("status").GetString();
        updatedStatus.Should().Be("APPROVED");

        // ??????????????? STEP 4: Verify status was updated ???????????????
        var verifyResponse = await _client.GetAsync(
            $"/api/request/school/{schoolId}/fetchSchoolRequests");
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var verifyContent = await verifyResponse.Content.ReadAsStringAsync();
        using var verifyDoc = JsonDocument.Parse(verifyContent);
        var verifyRequests = verifyDoc.RootElement.EnumerateArray().ToList();

        var approvedRequest = verifyRequests.FirstOrDefault(r =>
            r.GetProperty("id").GetInt32() == requestId);
        approvedRequest.GetProperty("status").GetString().Should().Be("APPROVED");

        // ??????????????? STEP 5: Delete the request ???????????????
        var deleteResponse = await _client.DeleteAsync(
            $"/api/request/delete/{requestId}/deleteRequest");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify it's deleted
        var finalFetchResponse = await _client.GetAsync(
            $"/api/request/school/{schoolId}/fetchSchoolRequests");
        var finalContent = await finalFetchResponse.Content.ReadAsStringAsync();
        using var finalDoc = JsonDocument.Parse(finalContent);
        var finalRequests = finalDoc.RootElement.EnumerateArray().ToList();

        var deletedRequest = finalRequests.FirstOrDefault(r =>
            r.GetProperty("id").GetInt32() == requestId);
        deletedRequest.ValueKind.Should().Be(JsonValueKind.Undefined,
            "Deleted request should not be in the list");
    }

    [Fact]
    public async Task RequestFlow_CreateAndReject_WorksCorrectly()
    {
        // ??????????????? STEP 1: Create request ???????????????
        var schoolId = 1;
        var createPayload = new
        {
            firstName = "TestFlow",
            lastName = "Rejection",
            phoneNr = "0722333444",
            drivingCategory = "A"
        };

        var createResponse = await _client.PostAsJsonAsync(
            $"/api/request/school/{schoolId}/createRequest", createPayload);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var requestId = createDoc.RootElement.GetProperty("id").GetInt32();

        // ??????????????? STEP 2: Reject the request as SuperAdmin ???????????????
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SuperAdmin");

        var rejectPayload = new { status = "REJECTED" };

        var rejectResponse = await _client.PutAsJsonAsync(
            $"/api/request/update/{requestId}/updateRequestStatus", rejectPayload);
        rejectResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var rejectContent = await rejectResponse.Content.ReadAsStringAsync();
        using var rejectDoc = JsonDocument.Parse(rejectContent);
        var updatedStatus = rejectDoc.RootElement.GetProperty("status").GetString();
        updatedStatus.Should().Be("REJECTED");

        // ??????????????? STEP 3: Verify status and cleanup ???????????????
        var deleteResponse = await _client.DeleteAsync(
            $"/api/request/delete/{requestId}/deleteRequest");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RequestFlow_RevertToPending_WorksCorrectly()
    {
        // Test that status can be changed back to PENDING
        var schoolId = 1;
        var createPayload = new
        {
            firstName = "TestFlow",
            lastName = "RevertPending",
            phoneNr = "0722555666",
            drivingCategory = "B"
        };

        var createResponse = await _client.PostAsJsonAsync(
            $"/api/request/school/{schoolId}/createRequest", createPayload);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var requestId = createDoc.RootElement.GetProperty("id").GetInt32();

        // Authenticate as SchoolAdmin
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        // Approve first
        var approvePayload = new { status = "APPROVED" };
        await _client.PutAsJsonAsync(
            $"/api/request/update/{requestId}/updateRequestStatus", approvePayload);

        // Revert to PENDING
        var pendingPayload = new { status = "PENDING" };
        var pendingResponse = await _client.PutAsJsonAsync(
            $"/api/request/update/{requestId}/updateRequestStatus", pendingPayload);
        pendingResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var pendingContent = await pendingResponse.Content.ReadAsStringAsync();
        using var pendingDoc = JsonDocument.Parse(pendingContent);
        pendingDoc.RootElement.GetProperty("status").GetString().Should().Be("PENDING");

        // Cleanup
        await _client.DeleteAsync($"/api/request/delete/{requestId}/deleteRequest");
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Negative Tests
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task RequestFlow_FetchRequests_WithoutToken_Returns401()
    {
        // Arrange - clear authentication
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/request/school/1/fetchSchoolRequests");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RequestFlow_UpdateRequest_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        var payload = new { status = "APPROVED" };

        // Act
        var response = await client.PutAsJsonAsync(
            "/api/request/update/1/updateRequestStatus", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RequestFlow_DeleteRequest_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.DeleteAsync("/api/request/delete/1/deleteRequest");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RequestFlow_StudentCannotFetchRequests_Returns403()
    {
        // Arrange - Students shouldn't be able to fetch/manage requests
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "Student");

        // Act
        var response = await client.GetAsync("/api/request/school/1/fetchSchoolRequests");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RequestFlow_InstructorCannotUpdateRequest_Returns403()
    {
        // Arrange - Instructors shouldn't be able to update requests
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "Instructor");

        var payload = new { status = "APPROVED" };

        // Act
        var response = await client.PutAsJsonAsync(
            "/api/request/update/1/updateRequestStatus", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RequestFlow_SchoolAdminCannotAccessOtherSchool_Returns403()
    {
        // Arrange - SchoolAdmin of School A cannot access School B requests
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin"); // School A

        // Act - Try to fetch requests from School B (schoolId = 2)
        var response = await client.GetAsync("/api/request/school/2/fetchSchoolRequests");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RequestFlow_UpdateNonExistentRequest_Returns400()
    {
        // Arrange
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SuperAdmin");

        var payload = new { status = "APPROVED" };

        // Act
        var response = await client.PutAsJsonAsync(
            "/api/request/update/99999/updateRequestStatus", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RequestFlow_InvalidStatusValue_Returns400()
    {
        // First create a request
        var schoolId = 1;
        var createPayload = new
        {
            firstName = "Test",
            lastName = "Invalid",
            phoneNr = "0722999888",
            drivingCategory = "B"
        };

        var createResponse = await _client.PostAsJsonAsync(
            $"/api/request/school/{schoolId}/createRequest", createPayload);
        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var requestId = createDoc.RootElement.GetProperty("id").GetInt32();

        // Authenticate and try invalid status
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        var invalidPayload = new { status = "INVALID_STATUS" };

        var response = await _client.PutAsJsonAsync(
            $"/api/request/update/{requestId}/updateRequestStatus", invalidPayload);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Cleanup
        await _client.DeleteAsync($"/api/request/delete/{requestId}/deleteRequest");
    }

    [Fact]
    public async Task RequestFlow_DeleteNonExistentRequest_Returns400()
    {
        // Arrange
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SuperAdmin");

        // Act
        var response = await client.DeleteAsync("/api/request/delete/99999/deleteRequest");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RequestFlow_CreateRequestInvalidSchool_Returns400()
    {
        // Arrange
        var payload = new
        {
            firstName = "Test",
            lastName = "User",
            phoneNr = "0722000000",
            drivingCategory = "B"
        };

        // Act - Try to create request for non-existent school
        var response = await _client.PostAsJsonAsync(
            "/api/request/school/99999/createRequest", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RequestFlow_SchoolAdminCannotUpdateOtherSchoolRequest_Returns403()
    {
        // Use seeded request from School B (RequestId = 3)
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin"); // School A admin

        var payload = new { status = "APPROVED" };

        // Act - Try to update School B's request
        var response = await client.PutAsJsonAsync(
            "/api/request/update/3/updateRequestStatus", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Cross-role access tests
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task RequestFlow_SuperAdminCanAccessAnySchool()
    {
        // Arrange
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SuperAdmin");

        // Act - SuperAdmin should access both schools
        var responseA = await client.GetAsync("/api/request/school/1/fetchSchoolRequests");
        var responseB = await client.GetAsync("/api/request/school/2/fetchSchoolRequests");

        // Assert
        responseA.StatusCode.Should().Be(HttpStatusCode.OK);
        responseB.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RequestFlow_SchoolAdminBCanAccessOwnSchool()
    {
        // Arrange
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdminB"); // School B admin

        // Act
        var response = await client.GetAsync("/api/request/school/2/fetchSchoolRequests");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
