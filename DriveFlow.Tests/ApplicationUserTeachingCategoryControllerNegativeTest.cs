using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

using DriveFlow_CRM_API.Controllers;
using DriveFlow_CRM_API.Models;

namespace DriveFlow.Tests.Controllers;

/// <summary>
/// Negative-path unit tests for <see cref="ApplicationUserTeachingCategoryController"/>:
/// 400, 403, 404, 409 scenarios.
/// EF Core runs in-memory; UserManager is mocked.
/// </summary>
public sealed class ApplicationUserTeachingCategoryControllerNegativeTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"AUTC_Neg_{Guid.NewGuid()}")
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

    // ????????? GetInstructorTeachingCategories - Negative ?????????

    [Fact]
    public async Task GetInstructorTeachingCategories_InvalidSchoolId_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var controller = new ApplicationUserTeachingCategoryController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetInstructorTeachingCategories(0, "instructor-1");

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetInstructorTeachingCategories_InstructorNotFound_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var controller = new ApplicationUserTeachingCategoryController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetInstructorTeachingCategories(1, "non-existent");

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetInstructorTeachingCategories_UserNotInstructor_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 }; // Not an instructor
        db.Users.AddRange(admin, student);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var controller = new ApplicationUserTeachingCategoryController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetInstructorTeachingCategories(1, "student-1");

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetInstructorTeachingCategories_InstructorDifferentSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        var instructor = new ApplicationUser { Id = "instructor-1", AutoSchoolId = 2 }; // Different school
        db.Users.AddRange(admin, instructor);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var controller = new ApplicationUserTeachingCategoryController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetInstructorTeachingCategories(1, "instructor-1");

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    // ????????? GetTeachingCategoryInstructors - Negative ?????????

    [Fact]
    public async Task GetTeachingCategoryInstructors_InvalidSchoolId_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var controller = new ApplicationUserTeachingCategoryController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetTeachingCategoryInstructors(-1, 1);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetTeachingCategoryInstructors_CategoryNotFound_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var controller = new ApplicationUserTeachingCategoryController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetTeachingCategoryInstructors(1, 99999);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetTeachingCategoryInstructors_CategoryDifferentSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        db.TeachingCategories.Add(new TeachingCategory { TeachingCategoryId = 1, Code = "B", AutoSchoolId = 2 }); // Different school
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var controller = new ApplicationUserTeachingCategoryController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetTeachingCategoryInstructors(1, 1);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    // ????????? LinkInstructorToTeachingCategory - Negative ?????????

    [Fact]
    public async Task LinkInstructorToTeachingCategory_DifferentSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 2 }; // Different school
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
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task LinkInstructorToTeachingCategory_InstructorNotFound_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        db.TeachingCategories.Add(new TeachingCategory { TeachingCategoryId = 1, Code = "B", AutoSchoolId = 1 });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var controller = new ApplicationUserTeachingCategoryController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new InstructorTeachingCategoryLinkDto
        {
            InstructorId = "non-existent",
            TeachingCategoryId = 1
        };

        // Act
        var result = await controller.LinkInstructorToTeachingCategory(1, dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task LinkInstructorToTeachingCategory_UserNotInstructor_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 }; // Not an instructor
        db.Users.AddRange(admin, student);
        db.TeachingCategories.Add(new TeachingCategory { TeachingCategoryId = 1, Code = "B", AutoSchoolId = 1 });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var controller = new ApplicationUserTeachingCategoryController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new InstructorTeachingCategoryLinkDto
        {
            InstructorId = "student-1",
            TeachingCategoryId = 1
        };

        // Act
        var result = await controller.LinkInstructorToTeachingCategory(1, dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task LinkInstructorToTeachingCategory_CategoryNotFound_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        var instructor = new ApplicationUser { Id = "instructor-1", AutoSchoolId = 1 };
        db.Users.AddRange(admin, instructor);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var controller = new ApplicationUserTeachingCategoryController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new InstructorTeachingCategoryLinkDto
        {
            InstructorId = "instructor-1",
            TeachingCategoryId = 99999
        };

        // Act
        var result = await controller.LinkInstructorToTeachingCategory(1, dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task LinkInstructorToTeachingCategory_LinkAlreadyExists_Returns409()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        var instructor = new ApplicationUser { Id = "instructor-1", AutoSchoolId = 1 };
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

        var dto = new InstructorTeachingCategoryLinkDto
        {
            InstructorId = "instructor-1",
            TeachingCategoryId = 1
        };

        // Act
        var result = await controller.LinkInstructorToTeachingCategory(1, dto);

        // Assert
        result.Should().BeOfType<ConflictObjectResult>();
    }

    // ????????? UnlinkInstructorFromTeachingCategory - Negative ?????????

    [Fact]
    public async Task UnlinkInstructorFromTeachingCategory_LinkNotFound_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var controller = new ApplicationUserTeachingCategoryController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.UnlinkInstructorFromTeachingCategory(1, 99999);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task UnlinkInstructorFromTeachingCategory_DifferentSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 2 }; // Different school
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
        result.Should().BeOfType<ForbidResult>();
    }
}
