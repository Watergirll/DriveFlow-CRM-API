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
/// Negative-path unit tests for <see cref="AutoSchoolPageController"/>:
/// 404 scenarios for school details.
/// EF Core runs in-memory; UserManager and RoleManager are mocked.
/// </summary>
public sealed class AutoSchoolPageControllerNegativeTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"AutoSchoolPage_Neg_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Mock<UserManager<ApplicationUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static Mock<RoleManager<IdentityRole>> MockRoleManager(IdentityRole? studentRole = null, IdentityRole? instructorRole = null)
    {
        var store = new Mock<IRoleStore<IdentityRole>>();
        var mgr = new Mock<RoleManager<IdentityRole>>(
            store.Object,
            Array.Empty<IRoleValidator<IdentityRole>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new Mock<ILogger<RoleManager<IdentityRole>>>().Object);

        mgr.Setup(r => r.FindByNameAsync("Student"))
           .ReturnsAsync(studentRole);
        mgr.Setup(r => r.FindByNameAsync("Instructor"))
           .ReturnsAsync(instructorRole);

        return mgr;
    }

    // ????????? GET /api/schoolspage/schools/{schoolId} - Negative ?????????

    [Fact]
    public async Task GetAutoSchoolDetails_NonExistentSchool_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var studentRole = new IdentityRole("Student") { Id = "student-role-id" };
        var instructorRole = new IdentityRole("Instructor") { Id = "instructor-role-id" };
        db.Roles.AddRange(studentRole, instructorRole);
        await db.SaveChangesAsync();

        var roleMgr = MockRoleManager(studentRole, instructorRole);
        var controller = new AutoSchoolPageController(db, MockUserManager().Object, roleMgr.Object);

        // Act
        var result = await controller.GetAutoSchoolDetails(99999);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetAutoSchoolDetails_MissingRoles_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        db.AutoSchools.Add(new AutoSchool
        {
            AutoSchoolId = 1,
            Name = "TestSchool",
            Status = AutoSchoolStatus.Active
        });
        await db.SaveChangesAsync();

        // Role manager returns null for roles
        var roleMgr = MockRoleManager(null, null);
        var controller = new AutoSchoolPageController(db, MockUserManager().Object, roleMgr.Object);

        // Act
        var result = await controller.GetAutoSchoolDetails(1);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }
}
