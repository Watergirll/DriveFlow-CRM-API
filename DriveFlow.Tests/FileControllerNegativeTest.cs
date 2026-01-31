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
/// Negative-path unit tests for <see cref="FileController"/>:
/// 400 (BadRequest), 401 (Unauthorized), 403 (Forbid), and 404 (NotFound) scenarios.
/// EF Core runs in-memory; UserManager and RoleManager are mocked.
/// </summary>
public sealed class FileControllerNegativeTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"File_Neg_{Guid.NewGuid()}")
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
            mgr.Setup(x => x.IsInRoleAsync(userToReturn, "SchoolAdmin"))
               .ReturnsAsync(true);
            mgr.Setup(x => x.IsInRoleAsync(userToReturn, "SuperAdmin"))
               .ReturnsAsync(false);
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

    // ????????? GET /api/file/fetchAll/{schoolId} - Negative ?????????

    [Fact]
    public async Task GetStudentFileRecords_NonExistentSchool_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act - School 999 doesn't exist
        var result = await controller.GetStudentFileRecords(999);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetStudentFileRecords_UserNotFound_Returns401()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var school = new AutoSchool { AutoSchoolId = 1, Name = "TestSchool", Email = "school@test.ro" };
        db.AutoSchools.Add(school);
        await db.SaveChangesAsync();

        // UserManager returns null for GetUserAsync
        var userMgr = MockUserManager(null);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, "admin-1");

        // Act
        var result = await controller.GetStudentFileRecords(1);

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task GetStudentFileRecords_SchoolAdminDifferentSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var school1 = new AutoSchool { AutoSchoolId = 1, Name = "School1", Email = "school1@test.ro" };
        var school2 = new AutoSchool { AutoSchoolId = 2, Name = "School2", Email = "school2@test.ro" };
        db.AutoSchools.AddRange(school1, school2);
        
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 2 }; // Belongs to school 2
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act - Try to access school 1
        var result = await controller.GetStudentFileRecords(1);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    // ????????? POST /api/file/createFile/{studentId} - Negative ?????????

    [Fact]
    public async Task CreateFile_NonExistentStudent_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new CreateFileDto
        {
            scholarshipStartDate = DateTime.Today,
            criminalRecordExpiryDate = DateTime.Today.AddYears(1),
            medicalRecordExpiryDate = DateTime.Today.AddMonths(6),
            status = "APPROVED",
            teachingCategoryId = 1,
            payment = new PaymentDto { SessionsPayed = 1, ScholarshipBasePayment = true }
        };

        // Act
        var result = await controller.CreateFile("nonexistent-student", dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateFile_StudentFromDifferentSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var school1 = new AutoSchool { AutoSchoolId = 1, Name = "School1", Email = "school1@test.ro" };
        var school2 = new AutoSchool { AutoSchoolId = 2, Name = "School2", Email = "school2@test.ro" };
        db.AutoSchools.AddRange(school1, school2);
        
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.ro", AutoSchoolId = 2 }; // Different school
        db.Users.AddRange(admin, student);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new CreateFileDto
        {
            scholarshipStartDate = DateTime.Today,
            criminalRecordExpiryDate = DateTime.Today.AddYears(1),
            medicalRecordExpiryDate = DateTime.Today.AddMonths(6),
            status = "APPROVED",
            teachingCategoryId = 1,
            payment = new PaymentDto { SessionsPayed = 1, ScholarshipBasePayment = true }
        };

        // Act
        var result = await controller.CreateFile("student-1", dto);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task CreateFile_NonExistentInstructor_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var school = new AutoSchool { AutoSchoolId = 1, Name = "TestSchool", Email = "school@test.ro" };
        db.AutoSchools.Add(school);
        
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.ro", AutoSchoolId = 1 };
        db.Users.AddRange(admin, student);
        
        var teachingCategory = new TeachingCategory { TeachingCategoryId = 1, Code = "B", AutoSchoolId = 1 };
        db.TeachingCategories.Add(teachingCategory);
        
        var vehicle = new Vehicle { VehicleId = 1, LicensePlateNumber = "CJ-01-ABC", AutoSchoolId = 1 };
        db.Vehicles.Add(vehicle);
        
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new CreateFileDto
        {
            scholarshipStartDate = DateTime.Today,
            criminalRecordExpiryDate = DateTime.Today.AddYears(1),
            medicalRecordExpiryDate = DateTime.Today.AddMonths(6),
            status = "APPROVED",
            teachingCategoryId = 1,
            vehicleId = 1,
            instructorId = "nonexistent-instructor",
            payment = new PaymentDto { SessionsPayed = 1, ScholarshipBasePayment = true }
        };

        // Act
        var result = await controller.CreateFile("student-1", dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateFile_NonExistentVehicle_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var school = new AutoSchool { AutoSchoolId = 1, Name = "TestSchool", Email = "school@test.ro" };
        db.AutoSchools.Add(school);
        
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.ro", AutoSchoolId = 1 };
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "instructor@test.ro", AutoSchoolId = 1 };
        db.Users.AddRange(admin, student, instructor);
        
        var teachingCategory = new TeachingCategory { TeachingCategoryId = 1, Code = "B", AutoSchoolId = 1 };
        db.TeachingCategories.Add(teachingCategory);
        
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new CreateFileDto
        {
            scholarshipStartDate = DateTime.Today,
            criminalRecordExpiryDate = DateTime.Today.AddYears(1),
            medicalRecordExpiryDate = DateTime.Today.AddMonths(6),
            status = "APPROVED",
            teachingCategoryId = 1,
            vehicleId = 999, // Doesn't exist
            instructorId = "instructor-1",
            payment = new PaymentDto { SessionsPayed = 1, ScholarshipBasePayment = true }
        };

        // Act
        var result = await controller.CreateFile("student-1", dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateFile_NonExistentTeachingCategory_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var school = new AutoSchool { AutoSchoolId = 1, Name = "TestSchool", Email = "school@test.ro" };
        db.AutoSchools.Add(school);
        
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.ro", AutoSchoolId = 1 };
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "instructor@test.ro", AutoSchoolId = 1 };
        db.Users.AddRange(admin, student, instructor);
        
        var vehicle = new Vehicle { VehicleId = 1, LicensePlateNumber = "CJ-01-ABC", AutoSchoolId = 1 };
        db.Vehicles.Add(vehicle);
        
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new CreateFileDto
        {
            scholarshipStartDate = DateTime.Today,
            criminalRecordExpiryDate = DateTime.Today.AddYears(1),
            medicalRecordExpiryDate = DateTime.Today.AddMonths(6),
            status = "APPROVED",
            teachingCategoryId = 999, // Doesn't exist
            vehicleId = 1,
            instructorId = "instructor-1",
            payment = new PaymentDto { SessionsPayed = 1, ScholarshipBasePayment = true }
        };

        // Act
        var result = await controller.CreateFile("student-1", dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateFile_InvalidStatus_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var school = new AutoSchool { AutoSchoolId = 1, Name = "TestSchool", Email = "school@test.ro" };
        db.AutoSchools.Add(school);
        
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.ro", AutoSchoolId = 1 };
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "instructor@test.ro", AutoSchoolId = 1 };
        db.Users.AddRange(admin, student, instructor);
        
        var teachingCategory = new TeachingCategory { TeachingCategoryId = 1, Code = "B", AutoSchoolId = 1 };
        db.TeachingCategories.Add(teachingCategory);
        
        var vehicle = new Vehicle { VehicleId = 1, LicensePlateNumber = "CJ-01-ABC", AutoSchoolId = 1 };
        db.Vehicles.Add(vehicle);
        
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new CreateFileDto
        {
            scholarshipStartDate = DateTime.Today,
            criminalRecordExpiryDate = DateTime.Today.AddYears(1),
            medicalRecordExpiryDate = DateTime.Today.AddMonths(6),
            status = "INVALID_STATUS", // Invalid
            teachingCategoryId = 1,
            vehicleId = 1,
            instructorId = "instructor-1",
            payment = new PaymentDto { SessionsPayed = 1, ScholarshipBasePayment = true }
        };

        // Act
        var result = await controller.CreateFile("student-1", dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ????????? PUT /api/file/editFile/{fileId} - Negative ?????????

    [Fact]
    public async Task EditFile_NonExistentFile_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new EditFileDto { Status = "APPROVED" };

        // Act
        var result = await controller.EditFile(99999, dto);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task EditFile_FileBelongsToDifferentSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var school1 = new AutoSchool { AutoSchoolId = 1, Name = "School1", Email = "school1@test.ro" };
        var school2 = new AutoSchool { AutoSchoolId = 2, Name = "School2", Email = "school2@test.ro" };
        db.AutoSchools.AddRange(school1, school2);
        
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.ro", AutoSchoolId = 2 }; // Different school
        db.Users.AddRange(admin, student);
        
        var file = new DriveFlow_CRM_API.Models.File { FileId = 1, StudentId = "student-1", Status = FileStatus.APPROVED };
        db.Files.Add(file);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new EditFileDto { Status = "FINALISED" };

        // Act
        var result = await controller.EditFile(1, dto);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task EditFile_InvalidStatus_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var school = new AutoSchool { AutoSchoolId = 1, Name = "TestSchool", Email = "school@test.ro" };
        db.AutoSchools.Add(school);
        
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.ro", AutoSchoolId = 1 };
        db.Users.AddRange(admin, student);
        
        var file = new DriveFlow_CRM_API.Models.File { FileId = 1, StudentId = "student-1", Status = FileStatus.APPROVED };
        db.Files.Add(file);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new EditFileDto { Status = "INVALID_STATUS" };

        // Act
        var result = await controller.EditFile(1, dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ????????? PUT /api/file/editPayment/{paymentId} - Negative ?????????

    [Fact]
    public async Task EditPayment_NonExistentPayment_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new PaymentDto { SessionsPayed = 10, ScholarshipBasePayment = true };

        // Act
        var result = await controller.EditPayment(99999, dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task EditPayment_PaymentBelongsToDifferentSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var school1 = new AutoSchool { AutoSchoolId = 1, Name = "School1", Email = "school1@test.ro" };
        var school2 = new AutoSchool { AutoSchoolId = 2, Name = "School2", Email = "school2@test.ro" };
        db.AutoSchools.AddRange(school1, school2);
        
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.ro", AutoSchoolId = 2 }; // Different school
        db.Users.AddRange(admin, student);
        
        var file = new DriveFlow_CRM_API.Models.File { FileId = 1, StudentId = "student-1", Status = FileStatus.APPROVED };
        db.Files.Add(file);
        
        var payment = new Payment { PaymentId = 1, FileId = 1, SessionsPayed = 5 };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new PaymentDto { SessionsPayed = 10, ScholarshipBasePayment = true };

        // Act
        var result = await controller.EditPayment(1, dto);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    // ????????? DELETE /api/file/delete/{fileId} - Negative ?????????

    [Fact]
    public async Task DeleteFile_NonExistentFile_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.DeleteFile(99999);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DeleteFile_FileBelongsToDifferentSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var school1 = new AutoSchool { AutoSchoolId = 1, Name = "School1", Email = "school1@test.ro" };
        var school2 = new AutoSchool { AutoSchoolId = 2, Name = "School2", Email = "school2@test.ro" };
        db.AutoSchools.AddRange(school1, school2);
        
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.ro", AutoSchoolId = 2 }; // Different school
        db.Users.AddRange(admin, student);
        
        var file = new DriveFlow_CRM_API.Models.File { FileId = 1, StudentId = "student-1", Status = FileStatus.APPROVED };
        db.Files.Add(file);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.DeleteFile(1);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task DeleteFile_UserNotFound_Returns401()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var school = new AutoSchool { AutoSchoolId = 1, Name = "TestSchool", Email = "school@test.ro" };
        db.AutoSchools.Add(school);
        
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.ro", AutoSchoolId = 1 };
        db.Users.Add(student);
        
        var file = new DriveFlow_CRM_API.Models.File { FileId = 1, StudentId = "student-1", Status = FileStatus.APPROVED };
        db.Files.Add(file);
        await db.SaveChangesAsync();

        // UserManager returns null
        var userMgr = MockUserManager(null);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, "admin-1");

        // Act
        var result = await controller.DeleteFile(1);

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    // ????????? GET /api/file/details/{fileId} - Negative ?????????

    [Fact]
    public async Task GetFileDetails_NonExistentFile_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetFileDetails(99999);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetFileDetails_UserNotFound_Returns401()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var school = new AutoSchool { AutoSchoolId = 1, Name = "TestSchool", Email = "school@test.ro" };
        db.AutoSchools.Add(school);
        
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.ro", AutoSchoolId = 1 };
        db.Users.Add(student);
        
        var file = new DriveFlow_CRM_API.Models.File { FileId = 1, StudentId = "student-1", Status = FileStatus.APPROVED };
        db.Files.Add(file);
        await db.SaveChangesAsync();

        // UserManager returns null
        var userMgr = MockUserManager(null);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, "admin-1");

        // Act
        var result = await controller.GetFileDetails(1);

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task CreateFile_InvalidModelState_Returns400WhenChecked()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Simulate invalid model state
        controller.ModelState.AddModelError("status", "Status is required");

        var dto = new CreateFileDto
        {
            scholarshipStartDate = DateTime.Today,
            criminalRecordExpiryDate = DateTime.Today.AddYears(1),
            medicalRecordExpiryDate = DateTime.Today.AddMonths(6),
            status = "",
            teachingCategoryId = 1,
            payment = new PaymentDto { SessionsPayed = 1, ScholarshipBasePayment = true }
        };

        // Act
        if (!controller.ModelState.IsValid)
        {
            var validationResult = new BadRequestObjectResult(controller.ModelState);
            validationResult.Should().BeOfType<BadRequestObjectResult>();
            return;
        }

        var result = await controller.CreateFile("student-1", dto);
        result.Should().NotBeNull();
    }
}
