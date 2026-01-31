// This file establishes the SystemApiTests namespace and folder.
// Add your system/integration HTTP tests here.

namespace DriveFlow.Tests.SystemApiTests;

/// <summary>
/// Marker class that establishes the SystemApiTests namespace.
/// System tests in this namespace use <see cref="TestInfrastructure.CustomWebApplicationFactory"/>
/// to run HTTP-based integration tests against the full application stack.
/// </summary>
/// <remarks>
/// <para>
/// <strong>System tests differ from unit tests in that they:</strong>
/// <list type="bullet">
///   <item>Test the full HTTP pipeline including routing, middleware, and controllers</item>
///   <item>Use an in-memory database (no external dependencies)</item>
///   <item>Replace external services (AI, file storage) with fake implementations</item>
///   <item>Authenticate via the real auth endpoints using test credentials</item>
/// </list>
/// </para>
/// 
/// <para>
/// <strong>Seeded test data includes:</strong>
/// <list type="bullet">
///   <item>Roles: SuperAdmin, SchoolAdmin, Instructor, Student</item>
///   <item>Two auto schools: AutoSchoolA (Id=1), AutoSchoolB (Id=2)</item>
///   <item>Users for each role in both schools</item>
///   <item>Geography: Counties, Cities, Addresses</item>
///   <item>Licenses: B, A, C, BE</item>
///   <item>TeachingCategories per school</item>
///   <item>Vehicles per school</item>
///   <item>Files (student enrollments)</item>
///   <item>Payments</item>
///   <item>Requests (enrollment contact requests)</item>
///   <item>InstructorAvailability slots</item>
///   <item>Appointments</item>
///   <item>ExamForms and ExamItems</item>
///   <item>SessionForms with sample mistakes</item>
/// </list>
/// </para>
/// 
/// <para>
/// <strong>Example: Basic authenticated request test</strong>
/// <code>
/// public class MySystemTest : IClassFixture&lt;CustomWebApplicationFactory&gt;
/// {
///     private readonly CustomWebApplicationFactory _factory;
///     private readonly HttpClient _client;
///
///     public MySystemTest(CustomWebApplicationFactory factory)
///     {
///         _factory = factory;
///         _client = factory.CreateClient();
///     }
///
///     [Fact]
///     public async Task Get_ProtectedEndpoint_Returns200_WhenAuthenticated()
///     {
///         // Authenticate as a specific role
///         await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");
///
///         // Make request
///         var response = await _client.GetAsync("/api/some-endpoint");
///
///         // Assert
///         response.StatusCode.Should().Be(HttpStatusCode.OK);
///     }
/// }
/// </code>
/// </para>
/// 
/// <para>
/// <strong>Example: Cross-school authorization test</strong>
/// <code>
/// [Fact]
/// public async Task Get_SchoolData_Returns403_WhenWrongSchool()
/// {
///     // Authenticate as user from School B
///     await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdminB");
///
///     // Try to access School A data
///     var response = await _client.GetAsync("/api/autoschool/1/data");
///
///     // Should be forbidden
///     response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
/// }
/// </code>
/// </para>
/// 
/// <para>
/// <strong>Example: Discovering endpoints dynamically</strong>
/// <code>
/// [Fact]
/// public void All_ProtectedEndpoints_Should_RequireAuthentication()
/// {
///     var endpoints = ApiRoutesCatalog.GetProtectedEndpoints(_factory);
///     
///     foreach (var endpoint in endpoints)
///     {
///         // Test each endpoint
///         var url = ApiRoutesCatalog.GenerateRouteUrl(endpoint);
///         // ...
///     }
/// }
/// </code>
/// </para>
/// 
/// <para>
/// <strong>Available test users:</strong>
/// </para>
/// <para>
/// <em>School-independent:</em>
/// <list type="bullet">
///   <item>SuperAdmin: admin@test.com / Test123!</item>
/// </list>
/// </para>
/// <para>
/// <em>AutoSchoolA (Id=1):</em>
/// <list type="bullet">
///   <item>SchoolAdmin / SchoolAdminA: schooladmin@test.com / Test123!</item>
///   <item>Instructor / InstructorA: instructor@test.com / Test123!</item>
///   <item>Instructor2A: instructor2a@test.com / Test123!</item>
///   <item>Student / StudentA: student@test.com / Test123!</item>
///   <item>Student2A: student2a@test.com / Test123!</item>
/// </list>
/// </para>
/// <para>
/// <em>AutoSchoolB (Id=2):</em>
/// <list type="bullet">
///   <item>SchoolAdminB: schooladminb@test.com / Test123!</item>
///   <item>InstructorB: instructorb@test.com / Test123!</item>
///   <item>StudentB: studentb@test.com / Test123!</item>
/// </list>
/// </para>
/// </remarks>
public static class SystemApiTestsInfo
{
    /// <summary>
    /// The namespace for system API tests.
    /// </summary>
    public const string Namespace = "DriveFlow.Tests.SystemApiTests";

    /// <summary>
    /// AutoSchoolA identifier (used for primary test users).
    /// </summary>
    public const int AutoSchoolAId = 1;

    /// <summary>
    /// AutoSchoolB identifier (used for cross-school testing).
    /// </summary>
    public const int AutoSchoolBId = 2;

    /// <summary>
    /// Default test password for all seeded users.
    /// </summary>
    public const string DefaultPassword = "Test123!";
}
