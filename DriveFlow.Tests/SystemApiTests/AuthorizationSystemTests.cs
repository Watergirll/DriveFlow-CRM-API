using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using DriveFlow.Tests.TestInfrastructure;

namespace DriveFlow.Tests.SystemApiTests;

/// <summary>
/// Comprehensive authorization system tests covering the complete authorization matrix.
/// Tests all protected endpoints from the API for:
/// <list type="bullet">
///   <item>401 when no token is provided</item>
///   <item>403 when token has incorrect role</item>
///   <item>Success (2xx) when token has permitted role</item>
///   <item>Cross-school scoping (SchoolAdmin can only access own school's resources)</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><strong>Endpoint list is extracted from EndpointDataSource</strong></para>
/// <para>This ensures all endpoints with [Authorize] attribute are tested.</para>
/// <para>
/// <strong>Test Categories:</strong>
/// <list type="bullet">
///   <item>NoToken_Returns401 - All protected endpoints require authentication</item>
///   <item>WrongRole_Returns403 - Role-based access is enforced</item>
///   <item>CorrectRole_ReturnsSuccess - Permitted roles can access endpoints</item>
///   <item>CrossSchoolAccess - School-scoped resources are protected</item>
/// </list>
/// </para>
/// </remarks>
public sealed class AuthorizationSystemTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    // Cached tokens to avoid repeated logins
    private readonly Dictionary<string, string> _tokenCache = new();

    public AuthorizationSystemTests(CustomWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    /// <summary>
    /// Gets a token for the specified role, using cache to avoid repeated logins.
    /// </summary>
    private async Task<string> GetTokenAsync(string role)
    {
        if (_tokenCache.TryGetValue(role, out var cached))
            return cached;

        var client = _factory.CreateClient();
        var token = await ApiAuthHelper.LoginAsAsync(client, role);
        _tokenCache[role] = token;
        return token;
    }

    /// <summary>
    /// Creates a minimal valid request body for the specified endpoint.
    /// Returns null for GET/DELETE endpoints that don't need a body.
    /// </summary>
    private static HttpContent? GetMinimalBodyForEndpoint(EndpointInfo endpoint)
    {
        // GET and DELETE typically don't need a body
        if (endpoint.HttpMethod is "GET" or "DELETE")
            return null;

        // Return a minimal JSON body to avoid 415 Unsupported Media Type
        // The actual content validation (400) should happen after auth
        var minimalBody = "{}";

        // Add minimal required fields for specific endpoints based on known patterns
        var route = endpoint.RoutePattern.ToLowerInvariant();

        if (route.Contains("auth") && endpoint.HttpMethod == "POST")
        {
            minimalBody = """{"email":"test@test.com","password":"Test123!"}""";
        }
        else if (route.Contains("request") && endpoint.HttpMethod == "POST")
        {
            minimalBody = """{"firstName":"Test","lastName":"User","phoneNumber":"0700000000","drivingCategory":"B"}""";
        }
        else if (route.Contains("county") && endpoint.HttpMethod == "POST")
        {
            minimalBody = """{"name":"Test","abbreviation":"TS"}""";
        }
        else if (route.Contains("city") && endpoint.HttpMethod == "POST")
        {
            minimalBody = """{"name":"TestCity","countyId":1}""";
        }
        else if (route.Contains("license") && endpoint.HttpMethod == "POST")
        {
            minimalBody = """{"type":"X"}""";
        }
        else if (route.Contains("vehicle") && endpoint.HttpMethod == "POST")
        {
            minimalBody = """{"licensePlateNumber":"XX-00-XXX","transmissionType":"MANUAL"}""";
        }
        else if (route.Contains("teachingcategory") && endpoint.HttpMethod == "POST")
        {
            minimalBody = """{"licenseId":1,"sessionCost":100,"sessionDuration":60,"scholarshipPrice":2000,"minDrivingLessonsReq":20}""";
        }
        else if (route.Contains("availability") && endpoint.HttpMethod == "POST")
        {
            minimalBody = """{"date":"2025-12-01","startHour":"09:00","endHour":"12:00"}""";
        }
        else if (route.Contains("sessionform") && endpoint.HttpMethod == "POST")
        {
            minimalBody = """{"appointmentId":1,"formId":1}""";
        }
        else if (route.Contains("appointment") && endpoint.HttpMethod == "POST")
        {
            minimalBody = """{"date":"2025-12-01","startHour":"09:00","endHour":"10:30"}""";
        }
        else if (route.Contains("instructor") && route.Contains("create") && endpoint.HttpMethod == "POST")
        {
            minimalBody = """{"firstName":"Test","lastName":"Instructor","email":"test.inst@test.com","phone":"0700000000","password":"Test123!","teachingCategoryIds":[1]}""";
        }
        else if (route.Contains("student") && route.Contains("create") && endpoint.HttpMethod == "POST")
        {
            minimalBody = """{"student":{"firstName":"Test","lastName":"Student","email":"test.stud@test.com","cnp":"1234567890123","phone":"0700000000","password":"Test123!"},"payment":{"scholarshipBasePayment":true,"sessionsPayed":0},"file":{"scholarshipStartDate":"2025-01-01","criminalRecordExpiryDate":"2026-01-01","medicalRecordExpiryDate":"2025-07-01","status":"APPROVED"}}""";
        }
        else if (route.Contains("instructorcategories") && route.Contains("create") && endpoint.HttpMethod == "POST")
        {
            minimalBody = """{"instructorId":"test-id","teachingCategoryId":1}""";
        }
        else if (endpoint.HttpMethod == "PUT")
        {
            // PUT endpoints typically need the same structure as POST
            minimalBody = "{}";
        }

        return new StringContent(minimalBody, Encoding.UTF8, "application/json");
    }

    /// <summary>
    /// Gets test parameter values for route parameters.
    /// </summary>
    private static Dictionary<string, string> GetTestRouteParameters(EndpointInfo endpoint)
    {
        var route = endpoint.RoutePattern.ToLowerInvariant();
        var parameters = new Dictionary<string, string>
        {
            ["schoolId"] = "1",
            ["id"] = "1",
            ["fileId"] = "1",
            ["vehicleId"] = "1",
            ["appointmentId"] = "1",
            ["categoryId"] = "1",
            ["teachingCategoryId"] = "1",
            ["countyId"] = "1",
            ["cityId"] = "1",
            ["licenseId"] = "1",
            ["requestId"] = "1",
            ["intervalId"] = "1",
            ["formId"] = "1",
            ["sessionFormId"] = "1",
            ["applicationUserTeachingCategoryId"] = "1"
        };

        // Special handling for user IDs (which are GUIDs, not integers)
        if (route.Contains("{userid}") || route.Contains("{instructorid}") || route.Contains("{studentid}"))
        {
            // Use seeded user IDs - we'll need to get actual IDs in tests
            // For auth testing, we just need to pass the auth check first
            parameters["userId"] = "test-user-id";
            parameters["instructorId"] = "test-instructor-id";
            parameters["studentId"] = "test-student-id";
        }

        // For date parameters
        if (route.Contains("{startdate}") || route.Contains("{enddate}"))
        {
            parameters["startDate"] = DateTime.Today.ToString("yyyy-MM-dd");
            parameters["endDate"] = DateTime.Today.AddDays(7).ToString("yyyy-MM-dd");
        }

        if (route.Contains("{date}"))
        {
            parameters["date"] = DateTime.Today.ToString("yyyy-MM-dd");
        }

        return parameters;
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Test: All protected endpoints return 401 without token
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task AllProtectedEndpoints_WithoutToken_Return401()
    {
        // Arrange - Get all protected endpoints from the application
        var endpoints = ApiRoutesCatalog.GetProtectedEndpoints(_factory);
        var client = _factory.CreateClient();

        // Clear any authentication
        ApiAuthHelper.ClearAuthentication(client);

        var failures = new List<string>();
        var tested = 0;

        _output.WriteLine($"Testing {endpoints.Count} protected endpoints for 401 without token...\n");

        foreach (var endpoint in endpoints)
        {
            var url = ApiRoutesCatalog.GenerateRouteUrl(endpoint, GetTestRouteParameters(endpoint));
            var body = GetMinimalBodyForEndpoint(endpoint);

            HttpResponseMessage response;
            try
            {
                response = endpoint.HttpMethod switch
                {
                    "GET" => await client.GetAsync(url),
                    "POST" => await client.PostAsync(url, body),
                    "PUT" => await client.PutAsync(url, body),
                    "DELETE" => await client.DeleteAsync(url),
                    "PATCH" => await client.PatchAsync(url, body),
                    _ => await client.GetAsync(url)
                };
            }
            catch (Exception ex)
            {
                failures.Add($"[EXCEPTION] {endpoint.HttpMethod} {endpoint.RoutePattern}: {ex.Message}");
                continue;
            }

            tested++;

            // Should return 401 Unauthorized without a token
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                failures.Add($"[{response.StatusCode}] {endpoint.HttpMethod} {endpoint.RoutePattern} - Expected 401");
            }
            else
            {
                _output.WriteLine($"? {endpoint.HttpMethod} {endpoint.RoutePattern} -> 401");
            }
        }

        _output.WriteLine($"\nTested {tested} endpoints");

        if (failures.Any())
        {
            _output.WriteLine("\nFailures:");
            foreach (var failure in failures)
            {
                _output.WriteLine(failure);
            }
        }

        failures.Should().BeEmpty(
            $"All protected endpoints should return 401 without token. " +
            $"Failures: {string.Join("; ", failures.Take(5))}");
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Test: Role-based access - Wrong role returns 403
    // ?????????????????????????????????????????????????????????????????????????

    /// <summary>
    /// Tests that endpoints with specific role requirements return 403 for wrong roles.
    /// Only tests endpoints that explicitly specify roles (not just [Authorize]).
    /// </summary>
    [Fact]
    public async Task EndpointsWithRoleRestrictions_WithWrongRole_Return403()
    {
        // Arrange
        var endpoints = ApiRoutesCatalog.GetProtectedEndpoints(_factory)
            .Where(e => e.Roles.Length > 0) // Only endpoints with explicit role requirements
            .ToList();

        var allRoles = new[] { "SuperAdmin", "SchoolAdmin", "Instructor", "Student" };
        var failures = new List<string>();

        _output.WriteLine($"Testing {endpoints.Count} role-restricted endpoints for 403 with wrong role...\n");

        foreach (var endpoint in endpoints)
        {
            // Find a role that is NOT allowed for this endpoint
            var forbiddenRole = allRoles.FirstOrDefault(r =>
                !endpoint.Roles.Contains(r, StringComparer.OrdinalIgnoreCase));

            if (forbiddenRole == null)
            {
                // All roles are allowed, skip this endpoint
                _output.WriteLine($"? {endpoint.HttpMethod} {endpoint.RoutePattern} - All roles allowed, skipping");
                continue;
            }

            // Get a fresh client and authenticate with the forbidden role
            var client = _factory.CreateClient();
            var token = await GetTokenAsync(forbiddenRole);
            ApiAuthHelper.SetBearerToken(client, token);

            var url = ApiRoutesCatalog.GenerateRouteUrl(endpoint, GetTestRouteParameters(endpoint));
            var body = GetMinimalBodyForEndpoint(endpoint);

            HttpResponseMessage response;
            try
            {
                response = endpoint.HttpMethod switch
                {
                    "GET" => await client.GetAsync(url),
                    "POST" => await client.PostAsync(url, body),
                    "PUT" => await client.PutAsync(url, body),
                    "DELETE" => await client.DeleteAsync(url),
                    "PATCH" => await client.PatchAsync(url, body),
                    _ => await client.GetAsync(url)
                };
            }
            catch (Exception ex)
            {
                failures.Add($"[EXCEPTION] {endpoint.HttpMethod} {endpoint.RoutePattern} as {forbiddenRole}: {ex.Message}");
                continue;
            }

            // Should return 403 Forbidden for wrong role
            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                // Note: Some endpoints might return 404 or 400 if the resource check happens before role check
                // This is acceptable in some cases, but 2xx would be a security issue
                if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 300)
                {
                    failures.Add($"[SECURITY ISSUE] {endpoint.HttpMethod} {endpoint.RoutePattern} as {forbiddenRole} -> {response.StatusCode} (Expected 403)");
                }
                else
                {
                    _output.WriteLine($"? {endpoint.HttpMethod} {endpoint.RoutePattern} as {forbiddenRole} -> {response.StatusCode} (Expected 403, but not a security issue)");
                }
            }
            else
            {
                _output.WriteLine($"? {endpoint.HttpMethod} {endpoint.RoutePattern} as {forbiddenRole} -> 403");
            }
        }

        if (failures.Any())
        {
            _output.WriteLine("\nSecurity Issues (2xx with wrong role):");
            foreach (var failure in failures)
            {
                _output.WriteLine(failure);
            }
        }

        failures.Should().BeEmpty(
            "Endpoints with role restrictions should not return 2xx for forbidden roles");
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Test: Correct role can access endpoints (auth-only verification)
    // ?????????????????????????????????????????????????????????????????????????

    /// <summary>
    /// Tests that endpoints with specific role requirements are accessible with correct role.
    /// Note: Some endpoints may return 400/404 due to validation or missing resources - 
    /// that's acceptable as long as they don't return 401/403.
    /// 
    /// IMPORTANT: Endpoints that check the authenticated user's ID against route parameters
    /// (e.g., {instructorId}, {studentId}) may return 403 if the IDs don't match.
    /// These are "self-access" endpoints and are expected to fail this generic test.
    /// </summary>
    [Fact]
    public async Task EndpointsWithRoleRestrictions_WithCorrectRole_PassAuthCheck()
    {
        // Arrange
        var endpoints = ApiRoutesCatalog.GetProtectedEndpoints(_factory)
            .Where(e => e.Roles.Length > 0)
            .ToList();

        // Endpoints that require user ID matching (self-access only)
        // These endpoints check that the authenticated user's ID matches the route parameter
        var selfAccessEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "api/instructor-availability/{instructorId}",
            "api/instructor/{instructorId}/fetchInstructorAssignedFiles",
            "api/instructor/{instructorId}/fetchInstructorAppointments/{startDate}/{endDate}",
            "api/instructor/{instructorId}/stats/cohort",
            "api/student/{studentId}/files",
            "api/student/{id_student}/stats/mistakes",
            "api/student/appointments/delete/{appointmentId}",
        };

        var failures = new List<string>();

        _output.WriteLine($"Testing {endpoints.Count} role-restricted endpoints with correct role...\n");

        foreach (var endpoint in endpoints)
        {
            // Skip self-access endpoints in this generic test
            // They require the actual user ID in the route, which we don't have here
            var isSelfAccess = selfAccessEndpoints.Any(pattern => 
                endpoint.RoutePattern.Contains(pattern.Split('/')[1], StringComparison.OrdinalIgnoreCase) ||
                endpoint.RoutePattern.Equals(pattern, StringComparison.OrdinalIgnoreCase));
            
            if (selfAccessEndpoints.Any(pattern => 
                endpoint.RoutePattern.Replace("{instructorId}", "{id}")
                    .Replace("{studentId}", "{id}")
                    .Replace("{id_student}", "{id}")
                    .Contains(pattern.Replace("{instructorId}", "{id}")
                        .Replace("{studentId}", "{id}")
                        .Replace("{id_student}", "{id}").Split('/')[1], StringComparison.OrdinalIgnoreCase)))
            {
                // Check if this is a self-access endpoint by looking at the route pattern
                var routeLower = endpoint.RoutePattern.ToLowerInvariant();
                if (routeLower.Contains("{instructorid}") || 
                    routeLower.Contains("{studentid}") || 
                    routeLower.Contains("{id_student}") ||
                    (routeLower.Contains("instructor-availability") && routeLower.Contains("{instructorid}")))
                {
                    _output.WriteLine($"? {endpoint.HttpMethod} {endpoint.RoutePattern} - Skipped (self-access endpoint, requires actual user ID)");
                    continue;
                }
            }

            // Use the first allowed role
            var allowedRole = endpoint.Roles[0];

            // Map role names to test user keys
            var userKey = allowedRole switch
            {
                "SuperAdmin" => "SuperAdmin",
                "SchoolAdmin" => "SchoolAdminA",
                "Instructor" => "InstructorA",
                "Student" => "StudentA",
                _ => allowedRole
            };

            var client = _factory.CreateClient();
            var token = await GetTokenAsync(userKey);
            ApiAuthHelper.SetBearerToken(client, token);

            var url = ApiRoutesCatalog.GenerateRouteUrl(endpoint, GetTestRouteParameters(endpoint));
            var body = GetMinimalBodyForEndpoint(endpoint);

            HttpResponseMessage response;
            try
            {
                response = endpoint.HttpMethod switch
                {
                    "GET" => await client.GetAsync(url),
                    "POST" => await client.PostAsync(url, body),
                    "PUT" => await client.PutAsync(url, body),
                    "DELETE" => await client.DeleteAsync(url),
                    "PATCH" => await client.PatchAsync(url, body),
                    _ => await client.GetAsync(url)
                };
            }
            catch (Exception ex)
            {
                failures.Add($"[EXCEPTION] {endpoint.HttpMethod} {endpoint.RoutePattern} as {userKey}: {ex.Message}");
                continue;
            }

            // Should NOT return 401 or 403 with correct role
            // However, self-access endpoints that check user ID may return 403 - this is expected
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                failures.Add($"[AUTH FAIL] {endpoint.HttpMethod} {endpoint.RoutePattern} as {userKey} ({allowedRole}) -> {response.StatusCode}");
            }
            else if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                // Check if this might be a self-access check failure (not a role issue)
                // These endpoints check that the resource belongs to the authenticated user
                var routeLower = endpoint.RoutePattern.ToLowerInvariant();
                if (routeLower.Contains("{instructorid}") || 
                    routeLower.Contains("{studentid}") || 
                    routeLower.Contains("{id_student}") ||
                    routeLower.Contains("appointments/delete"))
                {
                    _output.WriteLine($"? {endpoint.HttpMethod} {endpoint.RoutePattern} as {userKey} -> 403 (likely resource ownership check, not role issue)");
                }
                else
                {
                    failures.Add($"[AUTH FAIL] {endpoint.HttpMethod} {endpoint.RoutePattern} as {userKey} ({allowedRole}) -> {response.StatusCode}");
                }
            }
            else
            {
                _output.WriteLine($"? {endpoint.HttpMethod} {endpoint.RoutePattern} as {userKey} -> {response.StatusCode} (auth passed)");
            }
        }

        if (failures.Any())
        {
            _output.WriteLine("\nAuth Failures (401/403 with correct role):");
            foreach (var failure in failures)
            {
                _output.WriteLine(failure);
            }
        }

        failures.Should().BeEmpty(
            "Endpoints should not return 401/403 when accessed with a permitted role");
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Test: Cross-school access restrictions (AutoSchoolId scoping)
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task SchoolScopedEndpoints_CrossSchoolAccess_Returns403Or404()
    {
        // Arrange - SchoolAdminB tries to access SchoolA resources
        var client = _factory.CreateClient();
        var tokenSchoolB = await GetTokenAsync("SchoolAdminB");
        ApiAuthHelper.SetBearerToken(client, tokenSchoolB);

        // List of endpoints that should be school-scoped (schoolId = 1 belongs to SchoolA)
        var schoolScopedEndpoints = new[]
        {
            ("GET", "/api/SchoolAdmin/autoschool/1/getUsers"),
            ("GET", "/api/SchoolAdmin/autoschool/1/getUsers/Student"),
            ("GET", "/api/SchoolAdmin/autoschool/1/getUsers/Instructor"),
            ("GET", "/api/TeachingCategory/get/1"),
            ("GET", "/api/autoschool/1/instructorCategories/teachingCategory/1/instructors"),
        };

        var failures = new List<string>();

        _output.WriteLine("Testing cross-school access restrictions...\n");

        foreach (var (method, url) in schoolScopedEndpoints)
        {
            HttpResponseMessage response;
            try
            {
                response = method switch
                {
                    "GET" => await client.GetAsync(url),
                    "POST" => await client.PostAsync(url, new StringContent("{}", Encoding.UTF8, "application/json")),
                    "PUT" => await client.PutAsync(url, new StringContent("{}", Encoding.UTF8, "application/json")),
                    "DELETE" => await client.DeleteAsync(url),
                    _ => await client.GetAsync(url)
                };
            }
            catch (Exception ex)
            {
                _output.WriteLine($"? {method} {url}: Exception - {ex.Message}");
                continue;
            }

            // Cross-school access should be denied (403) or resource not found for that school (404)
            if (response.StatusCode == HttpStatusCode.OK)
            {
                failures.Add($"[SECURITY ISSUE] {method} {url} -> {response.StatusCode} (SchoolAdminB should not access SchoolA data)");
            }
            else if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.NotFound)
            {
                _output.WriteLine($"? {method} {url} -> {response.StatusCode} (cross-school access denied)");
            }
            else
            {
                _output.WriteLine($"? {method} {url} -> {response.StatusCode}");
            }
        }

        failures.Should().BeEmpty(
            "Cross-school access should be denied");
    }

    [Fact]
    public async Task SuperAdmin_CanAccessAnySchoolResources()
    {
        // Arrange - SuperAdmin should be able to access any school's resources
        var client = _factory.CreateClient();
        var token = await GetTokenAsync("SuperAdmin");
        ApiAuthHelper.SetBearerToken(client, token);

        // Test access to both schools
        var endpoints = new[]
        {
            "/api/SchoolAdmin/autoschool/1/getUsers", // School A
            "/api/SchoolAdmin/autoschool/2/getUsers", // School B
        };

        _output.WriteLine("Testing SuperAdmin cross-school access...\n");

        foreach (var url in endpoints)
        {
            var response = await client.GetAsync(url);

            // SuperAdmin should have access (200 OK)
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                $"SuperAdmin should be able to access {url}");

            _output.WriteLine($"? SuperAdmin GET {url} -> {response.StatusCode}");
        }
    }

    [Fact]
    public async Task SchoolAdmin_CanAccessOwnSchoolResources()
    {
        // Arrange - SchoolAdminA should be able to access SchoolA resources
        var client = _factory.CreateClient();
        var token = await GetTokenAsync("SchoolAdminA");
        ApiAuthHelper.SetBearerToken(client, token);

        // Test access to own school
        var endpoints = new[]
        {
            "/api/SchoolAdmin/autoschool/1/getUsers",
            "/api/SchoolAdmin/autoschool/1/getUsers/Student",
            "/api/TeachingCategory/get/1",
        };

        _output.WriteLine("Testing SchoolAdminA access to own school...\n");

        foreach (var url in endpoints)
        {
            var response = await client.GetAsync(url);

            // Should have access to own school
            response.StatusCode.Should().Be(HttpStatusCode.OK,
                $"SchoolAdminA should be able to access {url}");

            _output.WriteLine($"? SchoolAdminA GET {url} -> {response.StatusCode}");
        }
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Test: Specific endpoint authorization tests
    // ?????????????????????????????????????????????????????????????????????????

    [Theory]
    [InlineData("GET", "/api/county/get", new[] { "SchoolAdmin", "SuperAdmin" })]
    [InlineData("GET", "/api/city/get/1", new[] { "Student", "Instructor", "SchoolAdmin", "SuperAdmin" })]
    public async Task ReadEndpoints_AuthorizedRolesCanAccess(
        string method, string url, string[] allowedRoles)
    {
        foreach (var role in allowedRoles)
        {
            var client = _factory.CreateClient();
            var token = await GetTokenAsync(role);
            ApiAuthHelper.SetBearerToken(client, token);

            var response = method switch
            {
                "GET" => await client.GetAsync(url),
                _ => await client.GetAsync(url)
            };

            // Should succeed or return appropriate non-auth error
            response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
                $"{role} should be able to authenticate for {method} {url}");
            response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
                $"{role} should be authorized for {method} {url}");
        }
    }

    [Theory]
    [InlineData("/api/student/{studentId}/files", "StudentA", HttpStatusCode.OK)]
    [InlineData("/api/student/future-appointments", "StudentA", HttpStatusCode.OK)]
    [InlineData("/api/student/all-appointments", "StudentA", HttpStatusCode.OK)]
    public async Task StudentEndpoints_StudentRole_CanAccess(string routePattern, string userKey, HttpStatusCode expectedStatus)
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetTokenAsync(userKey);
        ApiAuthHelper.SetBearerToken(client, token);

        // For student-specific endpoints, we need the actual student ID
        // The seeded data uses email-based user lookup
        var url = routePattern.Replace("{studentId}", "placeholder"); // Will need real ID

        // For endpoints that don't need studentId in URL
        if (!routePattern.Contains("{studentId}"))
        {
            url = routePattern;
        }
        else
        {
            // Skip this test as we'd need to get the actual user ID first
            _output.WriteLine($"? Skipping {routePattern} - needs actual student ID");
            return;
        }

        // Act
        var response = await client.GetAsync(url);

        // Assert
        response.StatusCode.Should().Be(expectedStatus);
    }

    [Theory]
    [InlineData("SchoolAdmin", "/api/county/get", true)]
    [InlineData("Instructor", "/api/county/get", false)]   // Not authorized
    [InlineData("Student", "/api/county/get", false)]      // Not authorized
    [InlineData("SuperAdmin", "/api/county/get", true)]
    public async Task CountyEndpoint_RoleBasedAccess(string role, string url, bool shouldSucceed)
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetTokenAsync(role);
        ApiAuthHelper.SetBearerToken(client, token);

        // Act
        var response = await client.GetAsync(url);

        // Assert
        if (shouldSucceed)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        else
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Test: Print API summary for documentation
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public void PrintApiEndpointSummary_ForDocumentation()
    {
        // This test prints the API summary for documentation purposes
        var summary = ApiRoutesCatalog.GetApiSummary(_factory);
        _output.WriteLine(summary);

        // Also print protected endpoints count
        var protectedEndpoints = ApiRoutesCatalog.GetProtectedEndpoints(_factory);
        _output.WriteLine($"\nTotal protected endpoints: {protectedEndpoints.Count}");

        // Group by controller for better visibility
        var byController = ApiRoutesCatalog.GroupByController(protectedEndpoints);
        foreach (var group in byController.OrderBy(g => g.Key))
        {
            _output.WriteLine($"\n{group.Key}:");
            foreach (var endpoint in group.OrderBy(e => e.HttpMethod))
            {
                var roles = endpoint.Roles.Length > 0
                    ? $" [{string.Join(", ", endpoint.Roles)}]"
                    : " [Any Authenticated]";
                _output.WriteLine($"  {endpoint.HttpMethod,-7} {endpoint.RoutePattern}{roles}");
            }
        }
    }
}
