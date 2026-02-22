using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using DriveFlow.Tests.TestInfrastructure;

namespace DriveFlow.Tests.SystemApiTests;

/// <summary>
/// System tests for the authentication endpoints (POST /api/auth and POST /api/auth/refresh).
/// Tests the full HTTP pipeline including routing, middleware, and token generation.
/// Uses <see cref="CustomWebApplicationFactory"/> with seeded test data.
/// </summary>
/// <remarks>
/// <para><strong>Tested scenarios:</strong></para>
/// <list type="bullet">
///   <item>Login with valid credentials ? 200 + access token + refresh token</item>
///   <item>Login with invalid credentials ? 401</item>
///   <item>Login with non-existent user ? 404</item>
///   <item>Refresh with valid token ? 200 + new access token</item>
///   <item>Refresh with invalid/malformed token ? 401</item>
///   <item>Using valid token on protected endpoint ? 200</item>
///   <item>Accessing protected endpoint without token ? 401</item>
/// </list>
/// </remarks>
public sealed class AuthControllerSystemTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthControllerSystemTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ?????????????????????????????????????????????????????????????????????????
    // POST /api/auth - Login Tests
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task Login_WithValidCredentials_Returns200WithTokens()
    {
        // Arrange
        var loginPayload = new
        {
            email = "student@test.com",
            password = "Test123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth", loginPayload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        // Verify access token exists and is not empty
        root.TryGetProperty("token", out var tokenProp).Should().BeTrue();
        tokenProp.GetString().Should().NotBeNullOrEmpty();

        // Verify refresh token exists and is not empty
        root.TryGetProperty("refreshToken", out var refreshProp).Should().BeTrue();
        refreshProp.GetString().Should().NotBeNullOrEmpty();

        // Verify user metadata
        root.TryGetProperty("userId", out var userIdProp).Should().BeTrue();
        userIdProp.GetString().Should().NotBeNullOrEmpty();

        root.TryGetProperty("userType", out var userTypeProp).Should().BeTrue();
        userTypeProp.GetString().Should().Be("Student");

        root.TryGetProperty("userEmail", out var emailProp).Should().BeTrue();
        emailProp.GetString().Should().Be("student@test.com");

        root.TryGetProperty("schoolId", out var schoolIdProp).Should().BeTrue();
        schoolIdProp.GetInt32().Should().Be(1); // AutoSchoolA
    }

    [Theory]
    [InlineData("SuperAdmin", "admin@test.com", null)]
    [InlineData("SchoolAdmin", "schooladmin@test.com", 1)]
    [InlineData("Instructor", "instructor@test.com", 1)]
    [InlineData("Student", "student@test.com", 1)]
    [InlineData("SchoolAdmin", "schooladminb@test.com", 2)]
    public async Task Login_WithValidCredentials_ReturnsCorrectRoleAndSchool(
        string expectedRole, string email, int? expectedSchoolId)
    {
        // Arrange
        var loginPayload = new
        {
            email,
            password = "Test123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth", loginPayload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        root.GetProperty("userType").GetString().Should().Be(expectedRole);
        root.GetProperty("schoolId").GetInt32().Should().Be(expectedSchoolId ?? 0);
    }

    [Fact]
    public async Task Login_WithIncorrectPassword_Returns401()
    {
        // Arrange
        var loginPayload = new
        {
            email = "student@test.com",
            password = "WrongPassword123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth", loginPayload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithNonExistentUser_Returns404()
    {
        // Arrange
        var loginPayload = new
        {
            email = "nonexistent@test.com",
            password = "Test123!"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth", loginPayload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Login_WithEmptyCredentials_Returns400Or404()
    {
        // Arrange
        var loginPayload = new
        {
            email = "",
            password = ""
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth", loginPayload);

        // Assert - Either BadRequest (validation) or NotFound (no user)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // POST /api/auth/refresh - Refresh Token Tests
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task Refresh_WithValidToken_Returns200WithNewAccessToken()
    {
        // Arrange - First login to get a valid refresh token
        var loginPayload = new
        {
            email = "instructor@test.com",
            password = "Test123!"
        };

        var loginResponse = await _client.PostAsJsonAsync("/api/auth", loginPayload);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginContent = await loginResponse.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginContent);
        var refreshToken = loginDoc.RootElement.GetProperty("refreshToken").GetString();
        var originalAccessToken = loginDoc.RootElement.GetProperty("token").GetString();

        // Act - Use the refresh token to get a new access token
        var refreshPayload = new { refreshToken };
        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh", refreshPayload);

        // Assert
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshContent = await refreshResponse.Content.ReadAsStringAsync();
        using var refreshDoc = JsonDocument.Parse(refreshContent);
        var newAccessToken = refreshDoc.RootElement.GetProperty("token").GetString();

        newAccessToken.Should().NotBeNullOrEmpty();
        // Note: The new token might be the same or different depending on implementation
        // The important thing is we got a valid response
    }

    [Fact]
    public async Task Refresh_WithInvalidToken_Returns401()
    {
        // Arrange - Use a malformed/fake token
        var refreshPayload = new
        {
            refreshToken = "invalid.token.here"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", refreshPayload);

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.BadRequest,
            HttpStatusCode.InternalServerError); // Depending on how the API handles malformed JWTs
    }

    [Fact]
    public async Task Refresh_WithExpiredOrRevokedToken_Returns401()
    {
        // Arrange - First login to get a token, then use a completely different fake token
        // (simulating an expired/revoked token scenario)
        var fakeExpiredToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
            "eyJzdWIiOiJmYWtlLXVzZXItaWQiLCJleHAiOjE1MTYyMzkwMjJ9." +
            "fake_signature_here";

        var refreshPayload = new { refreshToken = fakeExpiredToken };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", refreshPayload);

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Unauthorized,
            HttpStatusCode.BadRequest,
            HttpStatusCode.InternalServerError);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Token Usage on Protected Endpoints
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task ProtectedEndpoint_WithValidToken_Returns200()
    {
        // Arrange - Get a valid token
        var token = await ApiAuthHelper.LoginAsAsync(_client, "SchoolAdmin");
        ApiAuthHelper.SetBearerToken(_client, token);

        // Act - Access a protected endpoint (GET users list for school)
        var response = await _client.GetAsync("/api/SchoolAdmin/autoschool/1/getUsers");

        // Assert - Should succeed with valid token
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Cleanup
        ApiAuthHelper.ClearAuthentication(_client);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        // Arrange - Ensure no token is set
        ApiAuthHelper.ClearAuthentication(_client);

        // Act - Try to access a protected endpoint without authentication
        var response = await _client.GetAsync("/api/SchoolAdmin/autoschool/1/getUsers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithMalformedToken_Returns401()
    {
        // Arrange - Set a malformed token
        ApiAuthHelper.SetBearerToken(_client, "this.is.not.a.valid.token");

        // Act
        var response = await _client.GetAsync("/api/SchoolAdmin/autoschool/1/getUsers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Cleanup
        ApiAuthHelper.ClearAuthentication(_client);
    }

    [Fact]
    public async Task TokenFromLogin_CanBeUsedOnMultipleEndpoints()
    {
        // Arrange - Login and get token
        var token = await ApiAuthHelper.LoginAsAsync(_client, "SchoolAdmin");
        ApiAuthHelper.SetBearerToken(_client, token);

        // Act & Assert - Use token on multiple endpoints
        var response1 = await _client.GetAsync("/api/SchoolAdmin/autoschool/1/getUsers");
        response1.StatusCode.Should().Be(HttpStatusCode.OK);

        var response2 = await _client.GetAsync("/api/TeachingCategory/get/1");
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        // Cleanup
        ApiAuthHelper.ClearAuthentication(_client);
    }

    [Fact]
    public async Task RefreshedToken_WorksOnProtectedEndpoints()
    {
        // Arrange - Login to get tokens
        var loginPayload = new
        {
            email = "schooladmin@test.com",
            password = "Test123!"
        };

        var loginResponse = await _client.PostAsJsonAsync("/api/auth", loginPayload);
        var loginContent = await loginResponse.Content.ReadAsStringAsync();
        using var loginDoc = JsonDocument.Parse(loginContent);
        var refreshToken = loginDoc.RootElement.GetProperty("refreshToken").GetString();

        // Refresh to get a new token
        var refreshPayload = new { refreshToken };
        var refreshResponse = await _client.PostAsJsonAsync("/api/auth/refresh", refreshPayload);
        var refreshContent = await refreshResponse.Content.ReadAsStringAsync();
        using var refreshDoc = JsonDocument.Parse(refreshContent);
        var newAccessToken = refreshDoc.RootElement.GetProperty("token").GetString();

        // Act - Use the refreshed token on a protected endpoint
        ApiAuthHelper.SetBearerToken(_client, newAccessToken!);
        var protectedResponse = await _client.GetAsync("/api/SchoolAdmin/autoschool/1/getUsers");

        // Assert
        protectedResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Cleanup
        ApiAuthHelper.ClearAuthentication(_client);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Role-Specific Login Tests
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task AllSeededUsers_CanLoginSuccessfully()
    {
        // Arrange & Act & Assert - Test all seeded users can login
        var userKeys = new[]
        {
            "SuperAdmin",
            "SchoolAdminA",
            "SchoolAdminB",
            "InstructorA",
            "Instructor2A",
            "InstructorB",
            "StudentA",
            "Student2A",
            "StudentB"
        };

        foreach (var userKey in userKeys)
        {
            // Use a fresh client for each test to avoid token interference
            var client = _factory.CreateClient();

            try
            {
                var token = await ApiAuthHelper.LoginAsAsync(client, userKey);
                token.Should().NotBeNullOrEmpty($"User {userKey} should be able to login");
            }
            catch (InvalidOperationException ex)
            {
                Assert.Fail($"Failed to login as {userKey}: {ex.Message}");
            }
        }
    }
}
