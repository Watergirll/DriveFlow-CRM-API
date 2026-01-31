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
/// Positive-path unit tests for <see cref="SchoolAdminController"/>:
/// CRUD for Instructors and Students.
/// EF Core runs in-memory; UserManager and RoleManager are mocked.
/// </summary>
public sealed class SchoolAdminControllerPositiveTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"SchoolAdmin_Pos_{Guid.NewGuid()}")
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

        mgr.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
           .ReturnsAsync(IdentityResult.Success);

        mgr.Setup(x => x.DeleteAsync(It.IsAny<ApplicationUser>()))
           .ReturnsAsync(IdentityResult.Success);

        mgr.Setup(x => x.GeneratePasswordResetTokenAsync(It.IsAny<ApplicationUser>()))
           .ReturnsAsync("reset-token");

        mgr.Setup(x => x.ResetPasswordAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<string>()))
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

    // ????????? GET /api/SchoolAdmin/autoschool/{schoolId}/getUsers ?????????

    [Fact]
    public async Task GetUsersAsync_ReturnsUsersFromSchool()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", FirstName = "Ion", LastName = "Pop", Email = "ion@test.ro", AutoSchoolId = 1 };
        var instructor = new ApplicationUser { Id = "instructor-1", FirstName = "Ana", LastName = "Test", Email = "ana@test.ro", AutoSchoolId = 1 };
        db.Users.AddRange(admin, student, instructor);

        var studentRole = new IdentityRole("Student") { Id = "role-student" };
        var instructorRole = new IdentityRole("Instructor") { Id = "role-instructor" };
        db.Roles.AddRange(studentRole, instructorRole);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = "student-1", RoleId = "role-student" });
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = "instructor-1", RoleId = "role-instructor" });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var roleMgr = MockRoleManager();
        var controller = new SchoolAdminController(db, userMgr.Object, roleMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetUsersAsync(1);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var users = okResult.Value.Should().BeAssignableTo<IEnumerable<UserListItemDto>>().Subject.ToList();
        users.Should().HaveCount(2);
        users.Should().Contain(u => u.FirstName == "Ion" && u.Role == "Student");
        users.Should().Contain(u => u.FirstName == "Ana" && u.Role == "Instructor");
    }

    // ????????? GET /api/SchoolAdmin/autoschool/{schoolId}/getUsers/{type} ?????????

    [Fact]
    public async Task GetUsersByTypeAsync_ReturnsOnlyStudents()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", FirstName = "Ion", LastName = "Pop", Email = "ion@test.ro", AutoSchoolId = 1 };
        var instructor = new ApplicationUser { Id = "instructor-1", FirstName = "Ana", LastName = "Test", Email = "ana@test.ro", AutoSchoolId = 1 };
        db.Users.AddRange(admin, student, instructor);

        var studentRole = new IdentityRole("Student") { Id = "role-student" };
        var instructorRole = new IdentityRole("Instructor") { Id = "role-instructor" };
        db.Roles.AddRange(studentRole, instructorRole);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = "student-1", RoleId = "role-student" });
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = "instructor-1", RoleId = "role-instructor" });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var roleMgr = MockRoleManager();
        var controller = new SchoolAdminController(db, userMgr.Object, roleMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetUsersByTypeAsync(1, "Student");

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var users = okResult.Value.Should().BeAssignableTo<List<UserListItemDto>>().Subject;
        users.Should().HaveCount(1);
        users[0].Role.Should().Be("Student");
    }

    // ????????? GET /api/SchoolAdmin/autoschool/{schoolId}/getUser/{userId} ?????????

    [Fact]
    public async Task GetUserAsync_Student_ReturnsStudentDto()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", FirstName = "Ion", LastName = "Pop", Email = "ion@test.ro", Cnp = "1234567890123", AutoSchoolId = 1 };
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
        var result = await controller.GetUserAsync(1, "student-1");

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var dto = okResult.Value.Should().BeOfType<StudentUserDto>().Subject;
        dto.FirstName.Should().Be("Ion");
        dto.Role.Should().Be("Student");
        dto.Cnp.Should().Be("1234567890123");
    }

    // ????????? POST /api/SchoolAdmin/autoschool/{schoolId}/create/instructor ?????????

    [Fact]
    public async Task CreateInstructorAsync_ValidData_Returns201()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        db.Licenses.Add(new DriveFlow_CRM_API.Models.License { LicenseId = 1, Type = "B" });
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School1" });
        db.TeachingCategories.Add(new TeachingCategory { TeachingCategoryId = 1, Code = "B", LicenseId = 1, AutoSchoolId = 1 });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var roleMgr = MockRoleManager();
        var controller = new SchoolAdminController(db, userMgr.Object, roleMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new InstructorCreateDto
        {
            FirstName = "Marius",
            LastName = "Popescu",
            Email = "marius@school.ro",
            Phone = "0721000000",
            Password = "Pass123!",
            TeachingCategoryIds = new List<int> { 1 }
        };

        // Act
        var result = await controller.CreateInstructorAsync(1, dto);

        // Assert
        result.Should().BeOfType<CreatedResult>();
        userMgr.Verify(x => x.CreateAsync(It.IsAny<ApplicationUser>(), "Pass123!"), Times.Once);
        userMgr.Verify(x => x.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Instructor"), Times.Once);
    }

    // ????????? POST /api/SchoolAdmin/autoschool/{schoolId}/create/student ?????????

    [Fact]
    public async Task CreateStudentAsync_ValidData_Returns201()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School1" });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db.Users);
        var roleMgr = MockRoleManager();
        var controller = new SchoolAdminController(db, userMgr.Object, roleMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new StudentCreateDto
        {
            Student = new StudentDto
            {
                FirstName = "Ioana",
                LastName = "Marin",
                Email = "ioana@student.ro",
                Cnp = "2990101223344",
                Phone = "0721000000",
                Password = "Pass123!"
            },
            Payment = new PaymentDto
            {
                ScholarshipBasePayment = true,
                SessionsPayed = 0
            },
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
        result.Should().BeOfType<CreatedResult>();
        userMgr.Verify(x => x.CreateAsync(It.IsAny<ApplicationUser>(), "Pass123!"), Times.Once);
        userMgr.Verify(x => x.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Student"), Times.Once);
    }

    // ????????? PUT /api/SchoolAdmin/autoschool/{schoolId}/update/student/{userId} ?????????

    [Fact]
    public async Task UpdateStudentAsync_ValidData_Returns200()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", FirstName = "Ion", LastName = "Pop", Email = "ion@test.ro", Cnp = "1234567890123", AutoSchoolId = 1 };
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
            FirstName = "Ion-Updated",
            LastName = "Pop",
            Email = "ion.updated@test.ro",
            Cnp = "1234567890123",
            Phone = "0721000001"
        };

        // Act
        var result = await controller.UpdateStudentAsync(1, "student-1", dto);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        userMgr.Verify(x => x.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Once);
    }

    // ????????? DELETE /api/SchoolAdmin/autoschool/{schoolId}/deleteUser/{userId} ?????????

    [Fact]
    public async Task DeleteUserAsync_Student_Returns204()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
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
        result.Should().BeOfType<NoContentResult>();
        userMgr.Verify(x => x.DeleteAsync(It.IsAny<ApplicationUser>()), Times.Once);
    }
}
