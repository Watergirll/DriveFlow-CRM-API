using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

using DriveFlow_CRM_API.Controllers;
using DriveFlow_CRM_API.Models;

namespace DriveFlow.Tests.Controllers;

/// <summary>
/// Negative-path unit tests for <see cref="TeachingCategoryController"/>:
/// 400, 403, 404 scenarios.
/// EF Core runs in-memory; UserManager uses UserStore for proper async support.
/// </summary>
public sealed class TeachingCategoryControllerNegativeTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"TeachingCategory_Neg_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext db)
    {
        var store = new UserStore<ApplicationUser>(db);
        var hasher = new PasswordHasher<ApplicationUser>();
        return new UserManager<ApplicationUser>(
            store,
            null,
            hasher,
            null,
            null,
            null,
            null,
            null,
            null);
    }

    private static void AttachSchoolAdmin(ControllerBase controller, string adminId, int schoolId)
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

    // ????????? GET - Negative ????????

    [Fact]
    public async Task GetTeachingCategories_InvalidSchoolId_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = CreateUserManager(db);
        var controller = new TeachingCategoryController(db, userMgr);
        AttachSchoolAdmin(controller, admin.Id, 1);

        // Act
        var result = await controller.GetTeachingCategories(0);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetTeachingCategories_SchoolAdminDifferentSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 2 }; // Different school
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = CreateUserManager(db);
        var controller = new TeachingCategoryController(db, userMgr);
        AttachSchoolAdmin(controller, admin.Id, 2);

        // Act - Try to access school 1
        var result = await controller.GetTeachingCategories(1);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    // ????????? POST - Negative ????????

    [Fact]
    public async Task CreateTeachingCategory_DifferentSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 2 };
        db.Users.Add(admin);
        db.Licenses.Add(new DriveFlow_CRM_API.Models.License { LicenseId = 1, Type = "B" });
        await db.SaveChangesAsync();

        var userMgr = CreateUserManager(db);
        var controller = new TeachingCategoryController(db, userMgr);
        AttachSchoolAdmin(controller, admin.Id, 2);

        var dto = new TeachingCategoryCreateDto
        {
            LicenseId = 1,
            SessionCost = 100,
            SessionDuration = 60,
            MinDrivingLessonsReq = 30
        };

        // Act - Try to create in school 1
        var result = await controller.CreateTeachingCategory(1, dto);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task CreateTeachingCategory_InvalidLicenseId_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = CreateUserManager(db);
        var controller = new TeachingCategoryController(db, userMgr);
        AttachSchoolAdmin(controller, admin.Id, 1);

        var dto = new TeachingCategoryCreateDto
        {
            LicenseId = 0, // Invalid
            SessionCost = 100,
            SessionDuration = 60,
            MinDrivingLessonsReq = 30
        };

        // Act
        var result = await controller.CreateTeachingCategory(1, dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateTeachingCategory_NonExistentLicense_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = CreateUserManager(db);
        var controller = new TeachingCategoryController(db, userMgr);
        AttachSchoolAdmin(controller, admin.Id, 1);

        var dto = new TeachingCategoryCreateDto
        {
            LicenseId = 999, // Non-existent
            SessionCost = 100,
            SessionDuration = 60,
            MinDrivingLessonsReq = 30
        };

        // Act
        var result = await controller.CreateTeachingCategory(1, dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ????????? PUT - Negative ????????

    [Fact]
    public async Task UpdateTeachingCategory_NonExistentCategory_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        db.Licenses.Add(new DriveFlow_CRM_API.Models.License { LicenseId = 1, Type = "B" });
        await db.SaveChangesAsync();

        var userMgr = CreateUserManager(db);
        var controller = new TeachingCategoryController(db, userMgr);
        AttachSchoolAdmin(controller, admin.Id, 1);

        var dto = new TeachingCategoryUpdateDto
        {
            LicenseId = 1,
            SessionCost = 100,
            SessionDuration = 60,
            MinDrivingLessonsReq = 30
        };

        // Act
        var result = await controller.UpdateTeachingCategory(1, 99999, dto);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task UpdateTeachingCategory_DifferentSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 2 };
        db.Users.Add(admin);
        db.Licenses.Add(new DriveFlow_CRM_API.Models.License { LicenseId = 1, Type = "B" });
        db.TeachingCategories.Add(new TeachingCategory
        {
            TeachingCategoryId = 1,
            Code = "B",
            LicenseId = 1,
            AutoSchoolId = 1 // Different school
        });
        await db.SaveChangesAsync();

        var userMgr = CreateUserManager(db);
        var controller = new TeachingCategoryController(db, userMgr);
        AttachSchoolAdmin(controller, admin.Id, 2);

        var dto = new TeachingCategoryUpdateDto
        {
            LicenseId = 1,
            SessionCost = 100,
            SessionDuration = 60,
            MinDrivingLessonsReq = 30
        };

        // Act
        var result = await controller.UpdateTeachingCategory(1, 1, dto);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    // ????????? DELETE - Negative ????????

    [Fact]
    public async Task DeleteTeachingCategory_NonExistent_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = CreateUserManager(db);
        var controller = new TeachingCategoryController(db, userMgr);
        AttachSchoolAdmin(controller, admin.Id, 1);

        // Act
        var result = await controller.DeleteTeachingCategory(1, 99999);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DeleteTeachingCategory_DifferentSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 2 };
        db.Users.Add(admin);
        db.TeachingCategories.Add(new TeachingCategory
        {
            TeachingCategoryId = 1,
            Code = "B",
            AutoSchoolId = 1 // Different school
        });
        await db.SaveChangesAsync();

        var userMgr = CreateUserManager(db);
        var controller = new TeachingCategoryController(db, userMgr);
        AttachSchoolAdmin(controller, admin.Id, 2);

        // Act
        var result = await controller.DeleteTeachingCategory(1, 1);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }
}
