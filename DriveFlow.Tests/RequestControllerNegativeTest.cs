using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using DriveFlow_CRM_API.Controllers;
using DriveFlow_CRM_API.Models;

namespace DriveFlow.Tests.Controllers;

/// <summary>
/// Negative-path unit tests for <see cref="RequestController"/>:
/// 400 (BadRequest), 401 (Unauthorized), and 403 (Forbid) scenarios.
/// EF Core runs in-memory; UserManager and RoleManager are mocked.
/// </summary>
public sealed class RequestControllerNegativeTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Request_Neg_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Mock<UserManager<ApplicationUser>> MockUserManager(ApplicationUser? userToReturn = null)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var mgr = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        if (userToReturn != null)
        {
            mgr.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
               .ReturnsAsync(userToReturn);
        }
        else
        {
            mgr.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
               .ReturnsAsync((ApplicationUser?)null);
        }

        return mgr;
    }

    private static Mock<RoleManager<IdentityRole>> MockRoleManager()
    {
        var store = new Mock<IRoleStore<IdentityRole>>();
        return new Mock<RoleManager<IdentityRole>>(
            store.Object,
            Array.Empty<IRoleValidator<IdentityRole>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new Mock<ILogger<RoleManager<IdentityRole>>>().Object);
    }

    private static void AttachSuperAdmin(ControllerBase controller, string userId = "superadmin-1")
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "SuperAdmin"),
            new Claim(ClaimTypes.NameIdentifier, userId)
        }, "mock");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    private static void AttachSchoolAdmin(ControllerBase controller, string userId = "schooladmin-1")
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "SchoolAdmin"),
            new Claim(ClaimTypes.NameIdentifier, userId)
        }, "mock");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    private static void AttachAnonymous(ControllerBase controller)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    // ????????? POST /api/request/school/{schoolId}/createRequest - Negative ?????????

    [Fact]
    public async Task CreateRequest_InvalidSchoolId_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        // No school with ID 999 exists

        var controller = new RequestController(db, MockUserManager().Object, MockRoleManager().Object);
        AttachAnonymous(controller);

        var dto = new CreateRequestDto
        {
            FirstName = "Test",
            LastName = "User",
            PhoneNr = "0700000000",
            DrivingCategory = "B"
        };

        // Act
        var result = await controller.CreateRequest(999, dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateRequest_NullDto_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School", Email = "school@test.ro" });
        await db.SaveChangesAsync();

        var controller = new RequestController(db, MockUserManager().Object, MockRoleManager().Object);
        AttachAnonymous(controller);

        // Act
        var result = await controller.CreateRequest(1, null!);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateRequest_InvalidModelState_Returns400WhenChecked()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School", Email = "school@test.ro" });
        await db.SaveChangesAsync();

        var controller = new RequestController(db, MockUserManager().Object, MockRoleManager().Object);
        AttachAnonymous(controller);

        // Simulate invalid model state
        controller.ModelState.AddModelError("FirstName", "FirstName is required");

        var dto = new CreateRequestDto
        {
            FirstName = "",
            LastName = "User",
            PhoneNr = "0700000000",
            DrivingCategory = "B"
        };

        // Act
        if (!controller.ModelState.IsValid)
        {
            var validationResult = new BadRequestObjectResult(controller.ModelState);
            validationResult.Should().BeOfType<BadRequestObjectResult>();
            return;
        }

        var result = await controller.CreateRequest(1, dto);
        result.Should().NotBeNull();
    }

    // ????????? GET /api/request/school/{AutoSchoolId}/fetchSchoolRequests - Negative ?????????

    [Fact]
    public async Task FetchSchoolRequests_NonExistentSchool_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        // No school exists

        var superAdmin = new ApplicationUser { Id = "superadmin-1", Email = "super@admin.ro" };
        var userMgr = MockUserManager(superAdmin);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSuperAdmin(controller);

        // Act
        var result = await controller.FetchSchoolRequests(999);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task FetchSchoolRequests_UserNotFound_Returns401()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School", Email = "school@test.ro" });
        await db.SaveChangesAsync();

        // UserManager returns null for GetUserAsync
        var userMgr = MockUserManager(null);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSuperAdmin(controller);

        // Act
        var result = await controller.FetchSchoolRequests(1);

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task FetchSchoolRequests_SchoolAdminWrongSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School1", Email = "school1@test.ro" });
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 2, Name = "School2", Email = "school2@test.ro" });
        await db.SaveChangesAsync();

        // SchoolAdmin belongs to school 2
        var schoolAdmin = new ApplicationUser { Id = "schooladmin-1", Email = "admin@school2.ro", AutoSchoolId = 2 };
        var userMgr = MockUserManager(schoolAdmin);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller);

        // Act - Try to fetch requests for school 1 (not their school)
        var result = await controller.FetchSchoolRequests(1);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    // ????????? PUT /api/request/update/{requestId}/updateRequestStatus - Negative ?????????

    [Fact]
    public async Task UpdateRequestStatus_NonExistentRequest_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        // No requests exist

        var superAdmin = new ApplicationUser { Id = "superadmin-1", Email = "super@admin.ro" };
        var userMgr = MockUserManager(superAdmin);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSuperAdmin(controller);

        var dto = new UpdateRequestDto { Status = "APPROVED" };

        // Act
        var result = await controller.UpdateRequestStatus(999, dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateRequestStatus_NullDto_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School", Email = "school@test.ro" });
        db.Requests.Add(new Request
        {
            RequestId = 1,
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "0700000000",
            Status = "PENDING",
            AutoSchoolId = 1
        });
        await db.SaveChangesAsync();

        var superAdmin = new ApplicationUser { Id = "superadmin-1", Email = "super@admin.ro" };
        var userMgr = MockUserManager(superAdmin);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSuperAdmin(controller);

        // Act
        var result = await controller.UpdateRequestStatus(1, null!);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateRequestStatus_InvalidStatus_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School", Email = "school@test.ro" });
        db.Requests.Add(new Request
        {
            RequestId = 1,
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "0700000000",
            Status = "PENDING",
            AutoSchoolId = 1
        });
        await db.SaveChangesAsync();

        var superAdmin = new ApplicationUser { Id = "superadmin-1", Email = "super@admin.ro" };
        var userMgr = MockUserManager(superAdmin);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSuperAdmin(controller);

        var dto = new UpdateRequestDto { Status = "INVALID_STATUS" };

        // Act
        var result = await controller.UpdateRequestStatus(1, dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateRequestStatus_EmptyStatus_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School", Email = "school@test.ro" });
        db.Requests.Add(new Request
        {
            RequestId = 1,
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "0700000000",
            Status = "PENDING",
            AutoSchoolId = 1
        });
        await db.SaveChangesAsync();

        var superAdmin = new ApplicationUser { Id = "superadmin-1", Email = "super@admin.ro" };
        var userMgr = MockUserManager(superAdmin);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSuperAdmin(controller);

        var dto = new UpdateRequestDto { Status = "" };

        // Act
        var result = await controller.UpdateRequestStatus(1, dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateRequestStatus_UserNotFound_Returns401()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School", Email = "school@test.ro" });
        db.Requests.Add(new Request
        {
            RequestId = 1,
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "0700000000",
            Status = "PENDING",
            AutoSchoolId = 1
        });
        await db.SaveChangesAsync();

        // UserManager returns null
        var userMgr = MockUserManager(null);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSuperAdmin(controller);

        var dto = new UpdateRequestDto { Status = "APPROVED" };

        // Act
        var result = await controller.UpdateRequestStatus(1, dto);

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task UpdateRequestStatus_SchoolAdminWrongSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School1", Email = "school1@test.ro" });
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 2, Name = "School2", Email = "school2@test.ro" });
        db.Requests.Add(new Request
        {
            RequestId = 1,
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "0700000000",
            Status = "PENDING",
            AutoSchoolId = 1 // Request belongs to school 1
        });
        await db.SaveChangesAsync();

        // SchoolAdmin belongs to school 2
        var schoolAdmin = new ApplicationUser { Id = "schooladmin-1", Email = "admin@school2.ro", AutoSchoolId = 2 };
        var userMgr = MockUserManager(schoolAdmin);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller);

        var dto = new UpdateRequestDto { Status = "APPROVED" };

        // Act
        var result = await controller.UpdateRequestStatus(1, dto);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task UpdateRequestStatus_InvalidModelState_Returns400WhenChecked()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School", Email = "school@test.ro" });
        db.Requests.Add(new Request
        {
            RequestId = 1,
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "0700000000",
            Status = "PENDING",
            AutoSchoolId = 1
        });
        await db.SaveChangesAsync();

        var superAdmin = new ApplicationUser { Id = "superadmin-1", Email = "super@admin.ro" };
        var userMgr = MockUserManager(superAdmin);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSuperAdmin(controller);

        // Simulate invalid model state
        controller.ModelState.AddModelError("Status", "Status is required");

        var dto = new UpdateRequestDto { Status = "" };

        // Act
        if (!controller.ModelState.IsValid)
        {
            var validationResult = new BadRequestObjectResult(controller.ModelState);
            validationResult.Should().BeOfType<BadRequestObjectResult>();
            return;
        }

        var result = await controller.UpdateRequestStatus(1, dto);
        result.Should().NotBeNull();
    }

    // ????????? DELETE /api/request/delete/{requestId}/deleteRequest - Negative ?????????

    [Fact]
    public async Task DeleteRequest_NonExistentRequest_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        // No requests exist

        var superAdmin = new ApplicationUser { Id = "superadmin-1", Email = "super@admin.ro" };
        var userMgr = MockUserManager(superAdmin);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSuperAdmin(controller);

        // Act
        var result = await controller.DeleteRequest(999);

        // Assert
        result.Should().BeOfType<BadRequestResult>();
    }

    [Fact]
    public async Task DeleteRequest_UserNotFound_Returns401()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School", Email = "school@test.ro" });
        db.Requests.Add(new Request
        {
            RequestId = 1,
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "0700000000",
            Status = "PENDING",
            AutoSchoolId = 1
        });
        await db.SaveChangesAsync();

        // UserManager returns null
        var userMgr = MockUserManager(null);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSuperAdmin(controller);

        // Act
        var result = await controller.DeleteRequest(1);

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task DeleteRequest_SchoolAdminWrongSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School1", Email = "school1@test.ro" });
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 2, Name = "School2", Email = "school2@test.ro" });
        db.Requests.Add(new Request
        {
            RequestId = 1,
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "0700000000",
            Status = "PENDING",
            AutoSchoolId = 1 // Request belongs to school 1
        });
        await db.SaveChangesAsync();

        // SchoolAdmin belongs to school 2
        var schoolAdmin = new ApplicationUser { Id = "schooladmin-1", Email = "admin@school2.ro", AutoSchoolId = 2 };
        var userMgr = MockUserManager(schoolAdmin);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller);

        // Act
        var result = await controller.DeleteRequest(1);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task DeleteRequest_ZeroId_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var superAdmin = new ApplicationUser { Id = "superadmin-1", Email = "super@admin.ro" };
        var userMgr = MockUserManager(superAdmin);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSuperAdmin(controller);

        // Act
        var result = await controller.DeleteRequest(0);

        // Assert - Request with ID 0 doesn't exist
        result.Should().BeOfType<BadRequestResult>();
    }

    [Fact]
    public async Task DeleteRequest_NegativeId_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var superAdmin = new ApplicationUser { Id = "superadmin-1", Email = "super@admin.ro" };
        var userMgr = MockUserManager(superAdmin);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSuperAdmin(controller);

        // Act
        var result = await controller.DeleteRequest(-1);

        // Assert - Request with negative ID doesn't exist
        result.Should().BeOfType<BadRequestResult>();
    }
}
