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
/// Negative-path unit tests for <see cref="SchoolAdminController"/>:
/// 400, 403, 404 scenarios.
/// EF Core runs in-memory; UserManager and RoleManager are mocked.
/// </summary>
public sealed class SchoolAdminControllerNegativeTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"SchoolAdmin_Neg_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
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

        mgr.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
           .ReturnsAsync((ClaimsPrincipal p) =>
           {
               var id = p.FindFirstValue(ClaimTypes.NameIdentifier);
               return users.FirstOrDefault(u => u.Id == id);
           });

        mgr.Setup(x => x.GetRolesAsync(It.IsAny<ApplicationUser>()))
           .ReturnsAsync(new List<string>());

        mgr.Setup(x => x.IsInRoleAsync(It.IsAny<ApplicationUser>(), "SchoolAdmin"))
           .ReturnsAsync(true);

        mgr.Setup(x => x.IsInRoleAsync(It.IsAny<ApplicationUser>(), "SuperAdmin"))
           .ReturnsAsync(false);

        mgr.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
           .ReturnsAsync(IdentityResult.Success);

        mgr.Setup(x => x.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
           .ReturnsAsync(IdentityResult.Success);

        additionalSetup?.Invoke(mgr);

        return mgr;
    }

    private static Mock<RoleManager<IdentityRole>> MockRoleManager()
    {
        var store = new Mock<IRoleStore<IdentityRole>>();
        var mgr = new Mock<RoleManager<IdentityRole>>(
            store.Object,
            Array.Empty<IRoleValidator<IdentityRole>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new Mock<ILogger<RoleManager<IdentityRole>>>().Object);

        mgr.Setup(r => r.RoleExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        mgr.Setup(r => r.CreateAsync(It.IsAny<IdentityRole>())).ReturnsAsync(IdentityResult.Success);

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

    // ????????? GetUsersAsync - Negative ?????????

    [Fact]
    public async Task GetUsersAsync_DifferentSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 2 }; // Different school
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var roleMgr = MockRoleManager();
        var controller = new SchoolAdminController(db, userMgr.Object, roleMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act - Try to access school 1
        var result = await controller.GetUsersAsync(1);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    // ????????? GetUsersByTypeAsync - Negative ?????????

    [Fact]
    public async Task GetUsersByTypeAsync_InvalidType_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var roleMgr = MockRoleManager();
        var controller = new SchoolAdminController(db, userMgr.Object, roleMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetUsersByTypeAsync(1, "InvalidType");

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetUsersByTypeAsync_EmptyType_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var roleMgr = MockRoleManager();
        var controller = new SchoolAdminController(db, userMgr.Object, roleMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetUsersByTypeAsync(1, "");

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ????????? GetUserAsync - Negative ?????????

    [Fact]
    public async Task GetUserAsync_NonExistentUser_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var roleMgr = MockRoleManager();
        var controller = new SchoolAdminController(db, userMgr.Object, roleMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetUserAsync(1, "non-existent-user");

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetUserAsync_EmptyUserId_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var roleMgr = MockRoleManager();
        var controller = new SchoolAdminController(db, userMgr.Object, roleMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetUserAsync(1, "");

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ????????? CreateInstructorAsync - Negative ?????????

    [Fact]
    public async Task CreateInstructorAsync_NoTeachingCategories_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var roleMgr = MockRoleManager();
        var controller = new SchoolAdminController(db, userMgr.Object, roleMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new InstructorCreateDto
        {
            FirstName = "Test",
            LastName = "User",
            Email = "test@test.ro",
            Phone = "0721000000",
            Password = "Pass123!",
            TeachingCategoryIds = new List<int>() // Empty
        };

        // Act
        var result = await controller.CreateInstructorAsync(1, dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateInstructorAsync_DuplicateEmail_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        var existingUser = new ApplicationUser { Id = "existing-1", Email = "test@test.ro", AutoSchoolId = 1 };
        db.Users.AddRange(admin, existingUser);
        db.TeachingCategories.Add(new TeachingCategory { TeachingCategoryId = 1, Code = "B", AutoSchoolId = 1 });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var roleMgr = MockRoleManager();
        var controller = new SchoolAdminController(db, userMgr.Object, roleMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new InstructorCreateDto
        {
            FirstName = "Test",
            LastName = "User",
            Email = "test@test.ro", // Duplicate email
            Phone = "0721000000",
            Password = "Pass123!",
            TeachingCategoryIds = new List<int> { 1 }
        };

        // Act
        var result = await controller.CreateInstructorAsync(1, dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateInstructorAsync_InvalidTeachingCategoryId_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        db.TeachingCategories.Add(new TeachingCategory { TeachingCategoryId = 1, Code = "B", AutoSchoolId = 2 }); // Different school
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var roleMgr = MockRoleManager();
        var controller = new SchoolAdminController(db, userMgr.Object, roleMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new InstructorCreateDto
        {
            FirstName = "Test",
            LastName = "User",
            Email = "test@test.ro",
            Phone = "0721000000",
            Password = "Pass123!",
            TeachingCategoryIds = new List<int> { 1 } // Belongs to different school
        };

        // Act
        var result = await controller.CreateInstructorAsync(1, dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ????????? CreateStudentAsync - Negative ?????????

    [Fact]
    public async Task CreateStudentAsync_InvalidCnp_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var roleMgr = MockRoleManager();
        var controller = new SchoolAdminController(db, userMgr.Object, roleMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new StudentCreateDto
        {
            Student = new StudentDto
            {
                FirstName = "Test",
                LastName = "User",
                Email = "test@test.ro",
                Cnp = "123", // Invalid CNP - too short
                Phone = "0721000000",
                Password = "Pass123!"
            },
            Payment = new PaymentDto { ScholarshipBasePayment = true, SessionsPayed = 0 },
            File = new FileDto
            {
                ScholarshipStartDate = DateTime.Today,
                CriminalRecordExpiryDate = DateTime.Today.AddYears(1),
                MedicalRecordExpiryDate = DateTime.Today.AddMonths(6),
                Status = "APPROVED"
            }
        };

        // Act
        var result = await controller.CreateStudentAsync(1, dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateStudentAsync_NegativeSessionsPayed_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var roleMgr = MockRoleManager();
        var controller = new SchoolAdminController(db, userMgr.Object, roleMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new StudentCreateDto
        {
            Student = new StudentDto
            {
                FirstName = "Test",
                LastName = "User",
                Email = "test@test.ro",
                Cnp = "1234567890123",
                Phone = "0721000000",
                Password = "Pass123!"
            },
            Payment = new PaymentDto { ScholarshipBasePayment = true, SessionsPayed = -1 }, // Negative
            File = new FileDto
            {
                ScholarshipStartDate = DateTime.Today,
                CriminalRecordExpiryDate = DateTime.Today.AddYears(1),
                MedicalRecordExpiryDate = DateTime.Today.AddMonths(6),
                Status = "APPROVED"
            }
        };

        // Act
        var result = await controller.CreateStudentAsync(1, dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ????????? UpdateStudentAsync - Negative ?????????

    [Fact]
    public async Task UpdateStudentAsync_NonExistentUser_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var roleMgr = MockRoleManager();
        var controller = new SchoolAdminController(db, userMgr.Object, roleMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new UpdateStudentDto
        {
            FirstName = "Test",
            LastName = "User",
            Email = "test@test.ro",
            Cnp = "1234567890123",
            Phone = "0721000000"
        };

        // Act
        var result = await controller.UpdateStudentAsync(1, "non-existent-user", dto);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task UpdateStudentAsync_DifferentSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 2 }; // Different school
        db.Users.AddRange(admin, student);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users, m =>
        {
            m.Setup(x => x.GetRolesAsync(It.Is<ApplicationUser>(u => u.Id == "student-1")))
             .ReturnsAsync(new List<string> { "Student" });
        });

        var roleMgr = MockRoleManager();
        var controller = new SchoolAdminController(db, userMgr.Object, roleMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new UpdateStudentDto
        {
            FirstName = "Test",
            LastName = "User",
            Email = "test@test.ro",
            Cnp = "1234567890123",
            Phone = "0721000000"
        };

        // Act
        var result = await controller.UpdateStudentAsync(1, "student-1", dto);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    // ????????? DeleteUserAsync - Negative ?????????

    [Fact]
    public async Task DeleteUserAsync_NonExistentUser_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var roleMgr = MockRoleManager();
        var controller = new SchoolAdminController(db, userMgr.Object, roleMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.DeleteUserAsync(1, "non-existent-user");

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DeleteUserAsync_SchoolAdminUser_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        var anotherAdmin = new ApplicationUser { Id = "admin-2", AutoSchoolId = 1 };
        db.Users.AddRange(admin, anotherAdmin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users, m =>
        {
            m.Setup(x => x.GetRolesAsync(It.Is<ApplicationUser>(u => u.Id == "admin-2")))
             .ReturnsAsync(new List<string> { "SchoolAdmin" });
        });

        var roleMgr = MockRoleManager();
        var controller = new SchoolAdminController(db, userMgr.Object, roleMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.DeleteUserAsync(1, "admin-2");

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task DeleteUserAsync_DifferentSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 2 }; // Different school
        db.Users.AddRange(admin, student);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users, m =>
        {
            m.Setup(x => x.GetRolesAsync(It.Is<ApplicationUser>(u => u.Id == "student-1")))
             .ReturnsAsync(new List<string> { "Student" });
        });

        var roleMgr = MockRoleManager();
        var controller = new SchoolAdminController(db, userMgr.Object, roleMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.DeleteUserAsync(1, "student-1");

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }
}
