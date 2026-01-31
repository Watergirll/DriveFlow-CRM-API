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
/// Positive-path unit tests for <see cref="RequestController"/>:
/// Create, Fetch, Update, and Delete enrollment requests.
/// EF Core runs in-memory; UserManager and RoleManager are mocked.
/// </summary>
public sealed class RequestControllerPositiveTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Request_Pos_{Guid.NewGuid()}")
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

    // ????????? POST /api/request/school/{schoolId}/createRequest ?????????

    [Fact]
    public async Task CreateRequest_ValidData_Returns201AndPersistsRequest()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "DriveFlow", Email = "contact@driveflow.ro" });
        await db.SaveChangesAsync();

        var controller = new RequestController(db, MockUserManager().Object, MockRoleManager().Object);
        AttachAnonymous(controller);

        var dto = new CreateRequestDto
        {
            FirstName = "Maria",
            LastName = "Ionescu",
            PhoneNr = "0721234567",
            DrivingCategory = "B"
        };

        // Act
        var result = await controller.CreateRequest(1, dto);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedResult>().Subject;
        createdResult.Value.Should().BeOfType<FetchRequestDto>();

        var responseDto = (FetchRequestDto)createdResult.Value!;
        responseDto.firstName.Should().Be("Maria");
        responseDto.lastName.Should().Be("Ionescu");
        responseDto.status.Should().Be("PENDING");

        // Verify DB persistence
        db.Requests.Should().ContainSingle(r => r.FirstName == "Maria" && r.LastName == "Ionescu");
    }

    [Fact]
    public async Task CreateRequest_SetsCorrectAutoSchoolId()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 5, Name = "TestSchool", Email = "test@school.ro" });
        await db.SaveChangesAsync();

        var controller = new RequestController(db, MockUserManager().Object, MockRoleManager().Object);
        AttachAnonymous(controller);

        var dto = new CreateRequestDto
        {
            FirstName = "Ion",
            LastName = "Popescu",
            PhoneNr = "0700000000",
            DrivingCategory = "A2"
        };

        // Act
        var result = await controller.CreateRequest(5, dto);

        // Assert
        result.Should().BeOfType<CreatedResult>();

        var request = await db.Requests.FirstOrDefaultAsync(r => r.FirstName == "Ion");
        request.Should().NotBeNull();
        request!.AutoSchoolId.Should().Be(5);
    }

    // ????????? GET /api/request/school/{AutoSchoolId}/fetchSchoolRequests ?????????

    [Fact]
    public async Task FetchSchoolRequests_AsSuperAdmin_Returns200WithAllRequests()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "DriveFlow", Email = "contact@driveflow.ro" });
        db.Requests.AddRange(
            new Request { RequestId = 1, FirstName = "Maria", LastName = "Ion", PhoneNumber = "0700000001", Status = "PENDING", AutoSchoolId = 1 },
            new Request { RequestId = 2, FirstName = "Ion", LastName = "Pop", PhoneNumber = "0700000002", Status = "APPROVED", AutoSchoolId = 1 }
        );
        await db.SaveChangesAsync();

        var superAdmin = new ApplicationUser { Id = "superadmin-1", Email = "super@admin.ro" };
        var userMgr = MockUserManager(superAdmin);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSuperAdmin(controller);

        // Act
        var result = await controller.FetchSchoolRequests(1);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var requests = okResult.Value.Should().BeAssignableTo<List<FetchRequestDto>>().Subject;
        requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task FetchSchoolRequests_AsSchoolAdmin_Returns200ForOwnSchool()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 2, Name = "MySchool", Email = "my@school.ro" });
        db.Requests.Add(new Request
        {
            RequestId = 1,
            FirstName = "Student",
            LastName = "Test",
            PhoneNumber = "0700000000",
            Status = "PENDING",
            AutoSchoolId = 2
        });
        await db.SaveChangesAsync();

        var schoolAdmin = new ApplicationUser { Id = "schooladmin-1", Email = "admin@school.ro", AutoSchoolId = 2 };
        var userMgr = MockUserManager(schoolAdmin);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller);

        // Act
        var result = await controller.FetchSchoolRequests(2);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var requests = okResult.Value.Should().BeAssignableTo<List<FetchRequestDto>>().Subject;
        requests.Should().HaveCount(1);
        requests[0].firstName.Should().Be("Student");
    }

    [Fact]
    public async Task FetchSchoolRequests_EmptyList_Returns200WithEmptyArray()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 3, Name = "EmptySchool", Email = "empty@school.ro" });
        await db.SaveChangesAsync();

        var superAdmin = new ApplicationUser { Id = "superadmin-1", Email = "super@admin.ro" };
        var userMgr = MockUserManager(superAdmin);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSuperAdmin(controller);

        // Act
        var result = await controller.FetchSchoolRequests(3);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var requests = okResult.Value.Should().BeAssignableTo<List<FetchRequestDto>>().Subject;
        requests.Should().BeEmpty();
    }

    // ????????? PUT /api/request/update/{requestId}/updateRequestStatus ?????????

    [Fact]
    public async Task UpdateRequestStatus_ApproveRequest_Returns200AndUpdatesStatus()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School", Email = "school@test.ro" });
        db.Requests.Add(new Request
        {
            RequestId = 10,
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

        var dto = new UpdateRequestDto { Status = "APPROVED" };

        // Act
        var result = await controller.UpdateRequestStatus(10, dto);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var responseDto = okResult.Value.Should().BeOfType<FetchRequestDto>().Subject;
        responseDto.status.Should().Be("APPROVED");

        // Verify DB update
        var request = await db.Requests.FindAsync(10);
        request!.Status.Should().Be("APPROVED");
    }

    [Fact]
    public async Task UpdateRequestStatus_RejectRequest_Returns200AndUpdatesStatus()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School", Email = "school@test.ro" });
        db.Requests.Add(new Request
        {
            RequestId = 20,
            FirstName = "Reject",
            LastName = "Test",
            PhoneNumber = "0711111111",
            Status = "PENDING",
            AutoSchoolId = 1
        });
        await db.SaveChangesAsync();

        var superAdmin = new ApplicationUser { Id = "superadmin-1", Email = "super@admin.ro" };
        var userMgr = MockUserManager(superAdmin);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSuperAdmin(controller);

        var dto = new UpdateRequestDto { Status = "REJECTED" };

        // Act
        var result = await controller.UpdateRequestStatus(20, dto);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var responseDto = okResult.Value.Should().BeOfType<FetchRequestDto>().Subject;
        responseDto.status.Should().Be("REJECTED");

        // Verify DB update
        var request = await db.Requests.FindAsync(20);
        request!.Status.Should().Be("REJECTED");
    }

    [Fact]
    public async Task UpdateRequestStatus_SetToPending_Returns200()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School", Email = "school@test.ro" });
        db.Requests.Add(new Request
        {
            RequestId = 30,
            FirstName = "Reset",
            LastName = "Test",
            PhoneNumber = "0722222222",
            Status = "APPROVED",
            AutoSchoolId = 1
        });
        await db.SaveChangesAsync();

        var superAdmin = new ApplicationUser { Id = "superadmin-1", Email = "super@admin.ro" };
        var userMgr = MockUserManager(superAdmin);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSuperAdmin(controller);

        var dto = new UpdateRequestDto { Status = "PENDING" };

        // Act
        var result = await controller.UpdateRequestStatus(30, dto);

        // Assert
        result.Should().BeOfType<OkObjectResult>();

        var request = await db.Requests.FindAsync(30);
        request!.Status.Should().Be("PENDING");
    }

    // ????????? DELETE /api/request/delete/{requestId}/deleteRequest ?????????

    [Fact]
    public async Task DeleteRequest_ExistingRequest_Returns204AndRemovesEntity()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School", Email = "school@test.ro" });
        db.Requests.Add(new Request
        {
            RequestId = 100,
            FirstName = "ToDelete",
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
        var result = await controller.DeleteRequest(100);

        // Assert
        result.Should().BeOfType<NoContentResult>();

        // Verify DB removal
        var request = await db.Requests.FindAsync(100);
        request.Should().BeNull();
    }

    [Fact]
    public async Task DeleteRequest_AsSchoolAdminForOwnSchool_Returns204()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 5, Name = "MySchool", Email = "my@school.ro" });
        db.Requests.Add(new Request
        {
            RequestId = 200,
            FirstName = "Delete",
            LastName = "Me",
            PhoneNumber = "0700000000",
            Status = "REJECTED",
            AutoSchoolId = 5
        });
        await db.SaveChangesAsync();

        var schoolAdmin = new ApplicationUser { Id = "schooladmin-1", Email = "admin@myschool.ro", AutoSchoolId = 5 };
        var userMgr = MockUserManager(schoolAdmin);

        var controller = new RequestController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller);

        // Act
        var result = await controller.DeleteRequest(200);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        db.Requests.Should().BeEmpty();
    }
}
