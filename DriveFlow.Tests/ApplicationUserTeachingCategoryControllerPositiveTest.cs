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
using License = DriveFlow_CRM_API.Models.License;

namespace DriveFlow.Tests.Controllers;

/// <summary>
/// Positive-path unit tests for <see cref="ApplicationUserTeachingCategoryController"/>:
/// CRUD for instructor-teaching category links.
/// EF Core runs in-memory; UserManager is mocked.
/// </summary>
public sealed class ApplicationUserTeachingCategoryControllerPositiveTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"AUTC_Pos_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Mock<UserManager<ApplicationUser>> MockUserManager(
        IQueryable<ApplicationUser> users,
        Action<Mock<UserManager<ApplicationUser>>>? additionalSetup = null)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var mgr = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        mgr.SetupGet(x => x.Users).Returns(users);

        mgr.Setup(x => x.GetUserId(It.IsAny<ClaimsPrincipal>()))
           .Returns((ClaimsPrincipal p) => p.FindFirstValue(ClaimTypes.NameIdentifier));

        mgr.Setup(x => x.IsInRoleAsync(It.IsAny<ApplicationUser>(), "Instructor"))
           .ReturnsAsync((ApplicationUser u, string r) => u.Id.StartsWith("instructor"));

        mgr.Setup(x => x.GetUsersInRoleAsync("Instructor"))
           .ReturnsAsync(users.Where(u => u.Id.StartsWith("instructor")).ToList());

        additionalSetup?.Invoke(mgr);

        return mgr;
    }

    private static void AttachSchoolAdmin(ControllerBase controller, string adminId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "SchoolAdmin"),
            new Claim(ClaimTypes.NameIdentifier, adminId)
        }, "mock");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    // ????????? GET /api/autoschool/{schoolId}/instructorCategories/instructor/{instructorId}/teachingCategories ?????????

    [Fact]
    public async Task GetInstructorTeachingCategories_ReturnsCategories()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        var instructor = new ApplicationUser { Id = "instructor-1", AutoSchoolId = 1 };
        db.Users.AddRange(admin, instructor);

        db.Licenses.Add(new License { LicenseId = 1, Type = "B" });
        db.TeachingCategories.Add(new TeachingCategory
        {
            TeachingCategoryId = 1,
            Code = "B",
            LicenseId = 1,
            SessionCost = 100,
            SessionDuration = 60,
            ScholarshipPrice = 2000,
            MinDrivingLessonsReq = 30,
            AutoSchoolId = 1
        });

        db.ApplicationUserTeachingCategories.Add(new ApplicationUserTeachingCategory
        {
            ApplicationUserTeachingCategoryId = 1,
            UserId = "instructor-1",
            TeachingCategoryId = 1
        });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var controller = new ApplicationUserTeachingCategoryController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetInstructorTeachingCategories(1, "instructor-1");

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var categories = okResult.Value.Should().BeAssignableTo<List<InstructorTeachingCategoryResponseDto>>().Subject;
        categories.Should().HaveCount(1);
        categories[0].Code.Should().Be("B");
        categories[0].SessionCost.Should().Be(100);
    }

    // ????????? GET /api/autoschool/{schoolId}/instructorCategories/teachingCategory/{teachingCategoryId}/instructors ?????????

    [Fact]
    public async Task GetTeachingCategoryInstructors_ReturnsInstructors()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        var instructor = new ApplicationUser { Id = "instructor-1", FirstName = "Ion", LastName = "Pop", Email = "ion@test.ro", PhoneNumber = "0721000000", AutoSchoolId = 1 };
        db.Users.AddRange(admin, instructor);

        db.TeachingCategories.Add(new TeachingCategory { TeachingCategoryId = 1, Code = "B", AutoSchoolId = 1 });

        db.ApplicationUserTeachingCategories.Add(new ApplicationUserTeachingCategory
        {
            ApplicationUserTeachingCategoryId = 1,
            UserId = "instructor-1",
            TeachingCategoryId = 1
        });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var controller = new ApplicationUserTeachingCategoryController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetTeachingCategoryInstructors(1, 1);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var instructors = okResult.Value.Should().BeAssignableTo<List<TeachingCategoryInstructorResponseDto>>().Subject;
        instructors.Should().HaveCount(1);
        instructors[0].FirstName.Should().Be("Ion");
        instructors[0].LastName.Should().Be("Pop");
    }

    // ????????? POST /api/autoschool/{schoolId}/instructorCategories/create ?????????

    [Fact]
    public async Task LinkInstructorToTeachingCategory_ValidData_Returns201()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        var instructor = new ApplicationUser { Id = "instructor-1", AutoSchoolId = 1 };
        db.Users.AddRange(admin, instructor);

        db.TeachingCategories.Add(new TeachingCategory { TeachingCategoryId = 1, Code = "B", AutoSchoolId = 1 });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var controller = new ApplicationUserTeachingCategoryController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new InstructorTeachingCategoryLinkDto
        {
            InstructorId = "instructor-1",
            TeachingCategoryId = 1
        };

        // Act
        var result = await controller.LinkInstructorToTeachingCategory(1, dto);

        // Assert
        result.Should().BeOfType<CreatedResult>();
        db.ApplicationUserTeachingCategories.Should().ContainSingle(l => l.UserId == "instructor-1" && l.TeachingCategoryId == 1);
    }

    // ????????? DELETE /api/autoschool/{schoolId}/instructorCategories/delete/{applicationUserTeachingCategoryId} ?????????

    [Fact]
    public async Task UnlinkInstructorFromTeachingCategory_ValidId_Returns200()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        var instructor = new ApplicationUser { Id = "instructor-1", AutoSchoolId = 1 };
        db.Users.AddRange(admin, instructor);

        db.TeachingCategories.Add(new TeachingCategory { TeachingCategoryId = 1, Code = "B", AutoSchoolId = 1 });

        db.ApplicationUserTeachingCategories.Add(new ApplicationUserTeachingCategory
        {
            ApplicationUserTeachingCategoryId = 100,
            UserId = "instructor-1",
            TeachingCategoryId = 1
        });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var controller = new ApplicationUserTeachingCategoryController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.UnlinkInstructorFromTeachingCategory(1, 100);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        db.ApplicationUserTeachingCategories.Should().BeEmpty();
    }
}
