using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using DriveFlow.Tests.TestInfrastructure;

namespace DriveFlow.Tests.SystemApiTests;

/// <summary>
/// End-to-end CRUD tests for the Vehicle controller via HTTP.
/// Uses <see cref="CustomWebApplicationFactory"/> with in-memory database.
/// </summary>
/// <remarks>
/// <para><strong>Routes tested:</strong></para>
/// <list type="bullet">
///   <item>GET /api/vehicle/get/{schoolId}</item>
///   <item>POST /api/vehicle/create/{schoolId}</item>
///   <item>PUT /api/vehicle/update/{vehicleId}</item>
///   <item>DELETE /api/vehicle/delete/{vehicleId}</item>
/// </list>
/// </remarks>
public sealed class CrudSystemTests_Vehicle : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CrudSystemTests_Vehicle(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Full CRUD Lifecycle: POST -> GET list -> GET by id -> PUT update -> DELETE -> 404
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task Vehicle_FullCrudLifecycle_WorksCorrectly()
    {
        // Arrange - authenticate as SchoolAdmin (can create/update/delete vehicles in their school)
        await ApiAuthHelper.AuthenticateAsAsync(_client, "SchoolAdmin");

        // SchoolAdmin is assigned to school 1 in seeded data
        var schoolId = 1;
        // Use seeded license ID (licenseId = 1 = B)
        var licenseId = 1;

        // ??????????????? CREATE (POST /api/vehicle/create/{schoolId}) ???????????????
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..6].ToUpper();
        var createPayload = new
        {
            licensePlateNumber = $"CJ-{uniqueSuffix}",
            transmissionType = "MANUAL",
            color = "Red",
            brand = "Toyota",
            model = "Corolla",
            yearOfProduction = 2023,
            fuelType = "BENZINA",
            engineSizeLiters = 1.8m,
            powertrainType = "COMBUSTIBIL",
            itpExpiryDate = (DateTime?)null,
            insuranceExpiryDate = (DateTime?)null,
            rcaExpiryDate = (DateTime?)null,
            licenseId = licenseId
        };

        var createResponse = await _client.PostAsJsonAsync($"/api/vehicle/create/{schoolId}", createPayload);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createContent = await createResponse.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createContent);
        var vehicleId = createDoc.RootElement.GetProperty("vehicleId").GetInt32();
        vehicleId.Should().BeGreaterThan(0);

        // ??????????????? GET LIST (GET /api/vehicle/get/{schoolId}) ???????????????
        var listResponse = await _client.GetAsync($"/api/vehicle/get/{schoolId}");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listContent = await listResponse.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(listContent);
        var vehicles = listDoc.RootElement.EnumerateArray();

        // Verify the created vehicle is in the list
        var createdVehicle = vehicles.FirstOrDefault(v =>
            v.GetProperty("vehicleId").GetInt32() == vehicleId);
        createdVehicle.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "Created vehicle should be in the list");
        createdVehicle.GetProperty("licensePlateNumber").GetString().Should().Be($"CJ-{uniqueSuffix}");
        createdVehicle.GetProperty("transmissionType").GetString().Should().Be("MANUAL");
        createdVehicle.GetProperty("brand").GetString().Should().Be("Toyota");

        // ??????????????? UPDATE (PUT /api/vehicle/update/{vehicleId}) ???????????????
        var updatePayload = new
        {
            licensePlateNumber = $"CJ-{uniqueSuffix}-U",
            transmissionType = "AUTOMATIC",
            color = "Blue",
            brand = "Toyota",
            model = "Corolla",
            yearOfProduction = 2024,
            fuelType = "BENZINA",
            engineSizeLiters = 2.0m,
            powertrainType = "HIBRID",
            itpExpiryDate = DateTime.UtcNow.AddYears(1),
            insuranceExpiryDate = (DateTime?)null,
            rcaExpiryDate = (DateTime?)null,
            licenseId = licenseId
        };

        var updateResponse = await _client.PutAsJsonAsync($"/api/vehicle/update/{vehicleId}", updatePayload);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify update by getting the list again
        var listAfterUpdateResponse = await _client.GetAsync($"/api/vehicle/get/{schoolId}");
        var listAfterUpdateContent = await listAfterUpdateResponse.Content.ReadAsStringAsync();
        using var listAfterUpdateDoc = JsonDocument.Parse(listAfterUpdateContent);
        var vehiclesAfterUpdate = listAfterUpdateDoc.RootElement.EnumerateArray();

        var updatedVehicle = vehiclesAfterUpdate.FirstOrDefault(v =>
            v.GetProperty("vehicleId").GetInt32() == vehicleId);
        updatedVehicle.GetProperty("licensePlateNumber").GetString().Should().Be($"CJ-{uniqueSuffix}-U");
        updatedVehicle.GetProperty("transmissionType").GetString().Should().Be("AUTOMATIC");
        updatedVehicle.GetProperty("color").GetString().Should().Be("Blue");

        // ??????????????? DELETE (DELETE /api/vehicle/delete/{vehicleId}) ???????????????
        var deleteResponse = await _client.DeleteAsync($"/api/vehicle/delete/{vehicleId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // ??????????????? VERIFY DELETED (GET list should not contain it) ???????????????
        var listAfterDeleteResponse = await _client.GetAsync($"/api/vehicle/get/{schoolId}");
        listAfterDeleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listAfterDeleteContent = await listAfterDeleteResponse.Content.ReadAsStringAsync();
        using var listAfterDeleteDoc = JsonDocument.Parse(listAfterDeleteContent);
        var vehiclesAfterDelete = listAfterDeleteDoc.RootElement.EnumerateArray();

        var deletedVehicle = vehiclesAfterDelete.FirstOrDefault(v =>
            v.GetProperty("vehicleId").GetInt32() == vehicleId);
        deletedVehicle.ValueKind.Should().Be(JsonValueKind.Undefined,
            "Deleted vehicle should not be in the list");

        // ??????????????? DELETE again -> 404 ???????????????
        var deleteAgainResponse = await _client.DeleteAsync($"/api/vehicle/delete/{vehicleId}");
        deleteAgainResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Protected endpoint tests - No token -> 401
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task Vehicle_GetVehicles_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.GetAsync("/api/vehicle/get/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Vehicle_CreateVehicle_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        var payload = new
        {
            licensePlateNumber = "TEST-123",
            transmissionType = "MANUAL",
            licenseId = 1
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/vehicle/create/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Vehicle_UpdateVehicle_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        var payload = new
        {
            licensePlateNumber = "TEST-123",
            transmissionType = "MANUAL",
            licenseId = 1
        };

        // Act
        var response = await client.PutAsJsonAsync("/api/vehicle/update/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Vehicle_DeleteVehicle_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        ApiAuthHelper.ClearAuthentication(client);

        // Act
        var response = await client.DeleteAsync("/api/vehicle/delete/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ?????????????????????????????????????????????????????????????????????????
    // Role-based access tests
    // ?????????????????????????????????????????????????????????????????????????

    [Fact]
    public async Task Vehicle_GetVehicles_AsSuperAdmin_ReturnsOk()
    {
        // Arrange - SuperAdmin can access vehicles from any school
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SuperAdmin");

        // Act
        var response = await client.GetAsync("/api/vehicle/get/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Vehicle_GetVehicles_AsSchoolAdmin_OwnSchool_ReturnsOk()
    {
        // Arrange - SchoolAdmin can access their own school's vehicles
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        // Act (school 1 is the SchoolAdmin's school)
        var response = await client.GetAsync("/api/vehicle/get/1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Vehicle_GetVehicles_AsSchoolAdmin_OtherSchool_Returns403()
    {
        // Arrange - SchoolAdmin cannot access other school's vehicles
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        // Act (school 999 doesn't belong to this SchoolAdmin)
        var response = await client.GetAsync("/api/vehicle/get/999");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Vehicle_CreateVehicle_AsSuperAdmin_Returns403()
    {
        // Arrange - Only SchoolAdmin can create vehicles
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SuperAdmin");

        var payload = new
        {
            licensePlateNumber = "SUPER-TEST",
            transmissionType = "MANUAL",
            licenseId = 1
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/vehicle/create/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Vehicle_CreateVehicle_InvalidLicenseId_Returns400()
    {
        // Arrange
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        var payload = new
        {
            licensePlateNumber = "INVALID-LIC",
            transmissionType = "MANUAL",
            licenseId = 99999 // Non-existent license
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/vehicle/create/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Vehicle_CreateVehicle_InvalidTransmissionType_Returns400()
    {
        // Arrange
        var client = _factory.CreateClient();
        await ApiAuthHelper.AuthenticateAsAsync(client, "SchoolAdmin");

        var payload = new
        {
            licensePlateNumber = "INVALID-TRANS",
            transmissionType = "INVALID",
            licenseId = 1
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/vehicle/create/1", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
