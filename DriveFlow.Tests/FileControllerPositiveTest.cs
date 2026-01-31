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
using DriveFlow_CRM_API.Models.DTOs;

namespace DriveFlow.Tests.Controllers;

/// <summary>
/// Positive-path unit tests for <see cref="FileController"/>:
/// Create, FetchAll, Edit, EditPayment, Delete, GetDetails for student files.
/// EF Core runs in-memory; UserManager and RoleManager are mocked.
/// </summary>
public sealed class FileControllerPositiveTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"File_Pos_{Guid.NewGuid()}")
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

    private static void AttachSuperAdmin(ControllerBase controller, string adminId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "SuperAdmin"),
            new Claim(ClaimTypes.NameIdentifier, adminId)
        }, "mock");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    // ????????? GET /api/file/fetchAll/{schoolId} ?????????

    [Fact]
    public async Task GetStudentFileRecords_AsSchoolAdmin_Returns200WithFiles()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        
        var school = new AutoSchool { AutoSchoolId = 1, Name = "TestSchool", Email = "school@test.ro" };
        db.AutoSchools.Add(school);
        
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        var student = new ApplicationUser 
        { 
            Id = "student-1", 
            Email = "student@test.ro", 
            AutoSchoolId = 1,
            FirstName = "Ion",
            LastName = "Popescu"
        };
        var instructor = new ApplicationUser
        {
            Id = "instructor-1",
            Email = "instructor@test.ro",
            AutoSchoolId = 1,
            FirstName = "Maria",
            LastName = "Ionescu"
        };
        db.Users.AddRange(admin, student, instructor);
        
        var teachingCategory = new TeachingCategory
        {
            TeachingCategoryId = 1,
            Code = "B",
            AutoSchoolId = 1,
            SessionCost = 100,
            SessionDuration = 60,
            MinDrivingLessonsReq = 30
        };
        db.TeachingCategories.Add(teachingCategory);
        
        var vehicle = new Vehicle
        {
            VehicleId = 1,
            LicensePlateNumber = "CJ-01-ABC",
            TransmissionType = TransmissionType.MANUAL,
            AutoSchoolId = 1
        };
        db.Vehicles.Add(vehicle);
        
        var file = new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = "student-1",
            InstructorId = "instructor-1",
            VehicleId = 1,
            TeachingCategoryId = 1,
            Status = FileStatus.APPROVED,
            ScholarshipStartDate = DateTime.Today
        };
        db.Files.Add(file);
        
        var payment = new Payment
        {
            PaymentId = 1,
            FileId = 1,
            SessionsPayed = 5,
            ScholarshipBasePayment = true
        };
        db.Payments.Add(payment);
        
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetStudentFileRecords(1);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var records = okResult.Value.Should().BeAssignableTo<List<StudentFileRecordsDto>>().Subject;
        records.Should().HaveCount(1);
        records[0].StudentData.FirstName.Should().Be("Ion");
    }

    [Fact]
    public async Task GetStudentFileRecords_EmptySchool_Returns200WithEmptyList()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        
        var school = new AutoSchool { AutoSchoolId = 1, Name = "EmptySchool", Email = "empty@test.ro" };
        db.AutoSchools.Add(school);
        
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetStudentFileRecords(1);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var records = okResult.Value.Should().BeAssignableTo<List<StudentFileRecordsDto>>().Subject;
        records.Should().BeEmpty();
    }

    // ????????? POST /api/file/createFile/{studentId} ?????????

    [Fact]
    public async Task CreateFile_ValidData_Returns201AndPersistsFile()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        
        var school = new AutoSchool { AutoSchoolId = 1, Name = "TestSchool", Email = "school@test.ro" };
        db.AutoSchools.Add(school);
        
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.ro", AutoSchoolId = 1 };
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "instructor@test.ro", AutoSchoolId = 1 };
        db.Users.AddRange(admin, student, instructor);
        
        var teachingCategory = new TeachingCategory
        {
            TeachingCategoryId = 1,
            Code = "B",
            AutoSchoolId = 1
        };
        db.TeachingCategories.Add(teachingCategory);
        
        var vehicle = new Vehicle
        {
            VehicleId = 1,
            LicensePlateNumber = "CJ-01-XYZ",
            AutoSchoolId = 1
        };
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
            instructorId = "instructor-1",
            payment = new PaymentDto
            {
                SessionsPayed = 3,
                ScholarshipBasePayment = true
            }
        };

        // Act
        var result = await controller.CreateFile("student-1", dto);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedResult>().Subject;
        var response = createdResult.Value.Should().BeOfType<CreateFileResponseDto>().Subject;
        response.Message.Should().Be("File created successfully");

        // Verify DB persistence
        db.Files.Should().ContainSingle(f => f.StudentId == "student-1");
        db.Payments.Should().ContainSingle(p => p.SessionsPayed == 3);
    }

    // ????????? PUT /api/file/editFile/{fileId} ?????????

    [Fact]
    public async Task EditFile_ValidData_Returns200AndUpdatesFile()
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
        
        var file = new DriveFlow_CRM_API.Models.File
        {
            FileId = 100,
            StudentId = "student-1",
            Status = FileStatus.APPROVED
        };
        db.Files.Add(file);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new EditFileDto
        {
            Status = "FINALISED",
            InstructorId = "instructor-1",
            VehicleId = 1,
            TeachingCategoryId = 1
        };

        // Act
        var result = await controller.EditFile(100, dto);

        // Assert
        result.Should().BeOfType<OkObjectResult>();

        // Verify DB update
        var updatedFile = await db.Files.FindAsync(100);
        updatedFile!.Status.Should().Be(FileStatus.FINALISED);
    }

    // ????????? PUT /api/file/editPayment/{paymentId} ?????????

    [Fact]
    public async Task EditPayment_ValidData_Returns200AndUpdatesPayment()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        
        var school = new AutoSchool { AutoSchoolId = 1, Name = "TestSchool", Email = "school@test.ro" };
        db.AutoSchools.Add(school);
        
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.ro", AutoSchoolId = 1 };
        db.Users.AddRange(admin, student);
        
        var file = new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = "student-1",
            Status = FileStatus.APPROVED
        };
        db.Files.Add(file);
        
        var payment = new Payment
        {
            PaymentId = 50,
            FileId = 1,
            SessionsPayed = 5,
            ScholarshipBasePayment = false
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        var dto = new PaymentDto
        {
            SessionsPayed = 10,
            ScholarshipBasePayment = true
        };

        // Act
        var result = await controller.EditPayment(50, dto);

        // Assert
        result.Should().BeOfType<OkObjectResult>();

        // Verify DB update
        var updatedPayment = await db.Payments.FindAsync(50);
        updatedPayment!.SessionsPayed.Should().Be(10);
        updatedPayment.ScholarshipBasePayment.Should().BeTrue();
    }

    // ????????? DELETE /api/file/delete/{fileId} ?????????

    [Fact]
    public async Task DeleteFile_ExistingFile_Returns200AndRemovesEntity()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        
        var school = new AutoSchool { AutoSchoolId = 1, Name = "TestSchool", Email = "school@test.ro" };
        db.AutoSchools.Add(school);
        
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.ro", AutoSchoolId = 1 };
        db.Users.AddRange(admin, student);
        
        var file = new DriveFlow_CRM_API.Models.File
        {
            FileId = 200,
            StudentId = "student-1",
            Status = FileStatus.APPROVED
        };
        db.Files.Add(file);
        
        var payment = new Payment { PaymentId = 1, FileId = 200 };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.DeleteFile(200);

        // Assert
        result.Should().BeOfType<OkObjectResult>();

        // Verify DB removal
        db.Files.Should().NotContain(f => f.FileId == 200);
    }

    // ????????? GET /api/file/details/{fileId} ?????????

    [Fact]
    public async Task GetFileDetails_AsSchoolAdmin_Returns200WithDetails()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        
        var school = new AutoSchool { AutoSchoolId = 1, Name = "TestSchool", Email = "school@test.ro" };
        db.AutoSchools.Add(school);
        
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        var student = new ApplicationUser 
        { 
            Id = "student-1", 
            Email = "student@test.ro", 
            AutoSchoolId = 1,
            FirstName = "Ion",
            LastName = "Popescu"
        };
        var instructor = new ApplicationUser
        {
            Id = "instructor-1",
            Email = "instructor@test.ro",
            AutoSchoolId = 1,
            FirstName = "Maria",
            LastName = "Ionescu"
        };
        db.Users.AddRange(admin, student, instructor);
        
        var teachingCategory = new TeachingCategory { TeachingCategoryId = 1, Code = "B", AutoSchoolId = 1 };
        db.TeachingCategories.Add(teachingCategory);
        
        var vehicle = new Vehicle { VehicleId = 1, LicensePlateNumber = "CJ-01-ABC", AutoSchoolId = 1 };
        db.Vehicles.Add(vehicle);
        
        var file = new DriveFlow_CRM_API.Models.File
        {
            FileId = 300,
            StudentId = "student-1",
            InstructorId = "instructor-1",
            VehicleId = 1,
            TeachingCategoryId = 1,
            Status = FileStatus.APPROVED,
            ScholarshipStartDate = DateTime.Today
        };
        db.Files.Add(file);
        
        var payment = new Payment { PaymentId = 1, FileId = 300, SessionsPayed = 5 };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin);
        var controller = new FileController(db, userMgr.Object, MockRoleManager().Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetFileDetails(300);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var details = okResult.Value.Should().BeOfType<FileDetailsDto>().Subject;
        details.FileId.Should().Be(300);
        details.Status.Should().Be("APPROVED");
    }
}
