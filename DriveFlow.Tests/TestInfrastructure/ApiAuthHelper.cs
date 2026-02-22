using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DriveFlow.Tests.TestInfrastructure;

/// <summary>
/// Helper class for handling authentication in system tests.
/// Provides methods for logging in as different roles and setting bearer tokens.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Available test users (seeded by CustomWebApplicationFactory):</strong>
/// </para>
/// <para>
/// <strong>School-independent:</strong>
/// <list type="bullet">
///   <item>SuperAdmin: admin@test.com / Test123!</item>
/// </list>
/// </para>
/// <para>
/// <strong>AutoSchoolA (Id=1):</strong>
/// <list type="bullet">
///   <item>SchoolAdmin: schooladmin@test.com / Test123!</item>
///   <item>Instructor: instructor@test.com / Test123!</item>
///   <item>Instructor2: instructor2a@test.com / Test123!</item>
///   <item>Student: student@test.com / Test123!</item>
///   <item>Student2: student2a@test.com / Test123!</item>
/// </list>
/// </para>
/// <para>
/// <strong>AutoSchoolB (Id=2):</strong>
/// <list type="bullet">
///   <item>SchoolAdminB: schooladminb@test.com / Test123!</item>
///   <item>InstructorB: instructorb@test.com / Test123!</item>
///   <item>StudentB: studentb@test.com / Test123!</item>
/// </list>
/// </para>
/// </remarks>
public static class ApiAuthHelper
{
    /// <summary>
    /// Test user credentials mapped by role.
    /// Primary users (default for each role, from AutoSchoolA except SuperAdmin).
    /// </summary>
    private static readonly Dictionary<string, (string Email, string Password)> TestUsers = new()
    {
        ["SuperAdmin"] = ("admin@test.com", "Test123!"),
        ["SchoolAdmin"] = ("schooladmin@test.com", "Test123!"),
        ["Instructor"] = ("instructor@test.com", "Test123!"),
        ["Student"] = ("student@test.com", "Test123!")
    };

    /// <summary>
    /// All test users including those from both schools.
    /// </summary>
    private static readonly Dictionary<string, (string Email, string Password, string Role, int? AutoSchoolId)> AllTestUsers = new()
    {
        // School-independent
        ["SuperAdmin"] = ("admin@test.com", "Test123!", "SuperAdmin", null),
        
        // AutoSchoolA (Id=1)
        ["SchoolAdmin"] = ("schooladmin@test.com", "Test123!", "SchoolAdmin", 1),
        ["SchoolAdminA"] = ("schooladmin@test.com", "Test123!", "SchoolAdmin", 1),
        ["Instructor"] = ("instructor@test.com", "Test123!", "Instructor", 1),
        ["InstructorA"] = ("instructor@test.com", "Test123!", "Instructor", 1),
        ["Instructor2A"] = ("instructor2a@test.com", "Test123!", "Instructor", 1),
        ["Student"] = ("student@test.com", "Test123!", "Student", 1),
        ["StudentA"] = ("student@test.com", "Test123!", "Student", 1),
        ["Student2A"] = ("student2a@test.com", "Test123!", "Student", 1),
        
        // AutoSchoolB (Id=2)
        ["SchoolAdminB"] = ("schooladminb@test.com", "Test123!", "SchoolAdmin", 2),
        ["InstructorB"] = ("instructorb@test.com", "Test123!", "Instructor", 2),
        ["StudentB"] = ("studentb@test.com", "Test123!", "Student", 2)
    };

    /// <summary>
    /// Logs in as a user with the specified role and returns the JWT token.
    /// </summary>
    /// <param name="client">The HTTP client to use for the login request.</param>
    /// <param name="role">
    /// The role/user key to log in as. Supports:
    /// <list type="bullet">
    ///   <item>Role names: SuperAdmin, SchoolAdmin, Instructor, Student (defaults to AutoSchoolA)</item>
    ///   <item>Specific users: SchoolAdminA, SchoolAdminB, InstructorA, InstructorB, Instructor2A, StudentA, StudentB, Student2A</item>
    /// </list>
    /// </param>
    /// <returns>The JWT access token.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the role is not recognized or login fails.
    /// </exception>
    public static async Task<string> LoginAsAsync(HttpClient client, string role)
    {
        if (!AllTestUsers.TryGetValue(role, out var userInfo))
        {
            throw new InvalidOperationException(
                $"Unknown role/user '{role}'. Valid options are: {string.Join(", ", AllTestUsers.Keys)}");
        }

        var loginPayload = new
        {
            email = userInfo.Email,
            password = userInfo.Password
        };

        var response = await client.PostAsJsonAsync("/api/auth", loginPayload);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Login failed for '{role}' ({userInfo.Email}) with status {response.StatusCode}. " +
                $"Response: {errorContent}");
        }

        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);

        if (!document.RootElement.TryGetProperty("token", out var tokenElement))
        {
            throw new InvalidOperationException(
                $"Login response for '{role}' did not contain a 'token' property. " +
                $"Response: {content}");
        }

        return tokenElement.GetString()
            ?? throw new InvalidOperationException("Token was null in login response.");
    }

    /// <summary>
    /// Sets the Authorization header with a Bearer token on the HTTP client.
    /// </summary>
    /// <param name="client">The HTTP client to set the header on.</param>
    /// <param name="token">The JWT token to use.</param>
    public static void SetBearerToken(HttpClient client, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Token cannot be null or empty.", nameof(token));
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Convenience method that logs in and sets the bearer token in one call.
    /// </summary>
    /// <param name="client">The HTTP client to authenticate.</param>
    /// <param name="role">The role/user key to log in as.</param>
    /// <returns>The JWT access token that was set.</returns>
    public static async Task<string> AuthenticateAsAsync(HttpClient client, string role)
    {
        var token = await LoginAsAsync(client, role);
        SetBearerToken(client, token);
        return token;
    }

    /// <summary>
    /// Clears the Authorization header from the HTTP client.
    /// </summary>
    /// <param name="client">The HTTP client to clear authentication from.</param>
    public static void ClearAuthentication(HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = null;
    }

    /// <summary>
    /// Gets the email address for a given role/user key.
    /// </summary>
    /// <param name="role">The role/user key to get the email for.</param>
    /// <returns>The email address associated with the role.</returns>
    public static string GetEmailForRole(string role)
    {
        if (!AllTestUsers.TryGetValue(role, out var userInfo))
        {
            throw new InvalidOperationException(
                $"Unknown role/user '{role}'. Valid options are: {string.Join(", ", AllTestUsers.Keys)}");
        }

        return userInfo.Email;
    }

    /// <summary>
    /// Gets the auto school ID for a given role/user key.
    /// </summary>
    /// <param name="role">The role/user key to get the school ID for.</param>
    /// <returns>The auto school ID, or null if the user is not associated with a school.</returns>
    public static int? GetAutoSchoolIdForRole(string role)
    {
        if (!AllTestUsers.TryGetValue(role, out var userInfo))
        {
            throw new InvalidOperationException(
                $"Unknown role/user '{role}'. Valid options are: {string.Join(", ", AllTestUsers.Keys)}");
        }

        return userInfo.AutoSchoolId;
    }

    /// <summary>
    /// Gets the actual role name for a given user key.
    /// </summary>
    /// <param name="userKey">The user key (e.g., "SchoolAdminB").</param>
    /// <returns>The role name (e.g., "SchoolAdmin").</returns>
    public static string GetRoleForUser(string userKey)
    {
        if (!AllTestUsers.TryGetValue(userKey, out var userInfo))
        {
            throw new InvalidOperationException(
                $"Unknown user '{userKey}'. Valid options are: {string.Join(", ", AllTestUsers.Keys)}");
        }

        return userInfo.Role;
    }

    /// <summary>
    /// Gets all available test roles (primary roles).
    /// </summary>
    /// <returns>A collection of available primary role names.</returns>
    public static IEnumerable<string> GetAvailableRoles() => TestUsers.Keys;

    /// <summary>
    /// Gets all available test user keys.
    /// </summary>
    /// <returns>A collection of all available user keys.</returns>
    public static IEnumerable<string> GetAllUserKeys() => AllTestUsers.Keys;

    /// <summary>
    /// Gets user keys for a specific auto school.
    /// </summary>
    /// <param name="autoSchoolId">The auto school ID (1 for AutoSchoolA, 2 for AutoSchoolB).</param>
    /// <returns>Collection of user keys belonging to the specified school.</returns>
    public static IEnumerable<string> GetUserKeysForSchool(int autoSchoolId)
    {
        return AllTestUsers
            .Where(kvp => kvp.Value.AutoSchoolId == autoSchoolId)
            .Select(kvp => kvp.Key);
    }

    /// <summary>
    /// Gets user keys for a specific role across all schools.
    /// </summary>
    /// <param name="role">The role name (e.g., "SchoolAdmin", "Instructor", "Student").</param>
    /// <returns>Collection of user keys with the specified role.</returns>
    public static IEnumerable<string> GetUserKeysForRole(string role)
    {
        return AllTestUsers
            .Where(kvp => kvp.Value.Role.Equals(role, StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Key);
    }
}
