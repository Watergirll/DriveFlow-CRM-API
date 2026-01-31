using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

using DriveFlow_CRM_API.Controllers;
using DriveFlow_CRM_API.Models;

namespace DriveFlow.Tests.Controllers;

/// <summary>
/// Positive-path unit tests for <see cref="InstructorController"/>:
/// Fetch assigned files, file details, and appointments.
/// EF Core runs in-memory.
/// </summary>
public sealed class InstructorControllerPositiveTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Instructor_Pos_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static void AttachInstructor(ControllerBase controller, string instructorId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Instructor"),
            new Claim(ClaimTypes.NameIdentifier, instructorId)
        }, "mock");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
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

    // ????????? GET /api/instructor/{instructorId}/fetchInstructorAssignedFiles ?????????

    [Fact]
    public async Task FetchInstructorAssignedFiles_ReturnsFilesForInstructor()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var instructor = new ApplicationUser { Id = "instructor-1", FirstName = "Ion", LastName = "Popescu", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", FirstName = "Maria", LastName = "Ionescu", Email = "maria@test.ro", AutoSchoolId = 1 };
        db.Users.AddRange(instructor, student);

        db.Licenses.Add(new DriveFlow_CRM_API.Models.License { LicenseId = 1, Type = "B" });
        db.TeachingCategories.Add(new TeachingCategory { TeachingCategoryId = 1, Code = "B", LicenseId = 1, AutoSchoolId = 1 });
        db.Vehicles.Add(new Vehicle { VehicleId = 1, LicensePlateNumber = "CJ-01-ABC", TransmissionType = TransmissionType.MANUAL, AutoSchoolId = 1 });

        db.Files.Add(new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = "student-1",
            InstructorId = "instructor-1",
            VehicleId = 1,
            TeachingCategoryId = 1,
            Status = FileStatus.APPROVED
        });
        await db.SaveChangesAsync();

        var controller = new InstructorController(db);
        AttachInstructor(controller, instructor.Id);

        // Act
        var result = await controller.FetchInstructorAssignedFiles("instructor-1");

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var files = okResult.Value.Should().BeAssignableTo<List<InstructorAssignedFileDto>>().Subject;
        files.Should().HaveCount(1);
        files[0].FirstName.Should().Be("Maria");
        files[0].LicensePlateNumber.Should().Be("CJ-01-ABC");
    }

    [Fact]
    public async Task FetchInstructorAssignedFiles_NoFiles_ReturnsEmptyList()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", AutoSchoolId = 1 };
        db.Users.Add(instructor);
        await db.SaveChangesAsync();

        var controller = new InstructorController(db);
        AttachInstructor(controller, instructor.Id);

        // Act
        var result = await controller.FetchInstructorAssignedFiles("instructor-1");

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var files = okResult.Value.Should().BeAssignableTo<List<InstructorAssignedFileDto>>().Subject;
        files.Should().BeEmpty();
    }

    // ????????? GET /api/instructor/fetchFileDetails/{fileId} ?????????

    [Fact]
    public async Task FetchFileDetails_ExistingFile_ReturnsDetails()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var instructor = new ApplicationUser { Id = "instructor-1", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", FirstName = "Ana", LastName = "Pop", Email = "ana@test.ro", AutoSchoolId = 1 };
        db.Users.AddRange(instructor, student);

        db.TeachingCategories.Add(new TeachingCategory { TeachingCategoryId = 1, Code = "B", MinDrivingLessonsReq = 30, AutoSchoolId = 1 });

        var file = new DriveFlow_CRM_API.Models.File
        {
            FileId = 100,
            StudentId = "student-1",
            InstructorId = "instructor-1",
            TeachingCategoryId = 1,
            Status = FileStatus.APPROVED,
            ScholarshipStartDate = DateTime.Today.AddMonths(-1),
            CriminalRecordExpiryDate = DateTime.Today.AddYears(1),
            MedicalRecordExpiryDate = DateTime.Today.AddMonths(6)
        };
        db.Files.Add(file);

        db.Payments.Add(new Payment { PaymentId = 1, FileId = 100, SessionsPayed = 10, ScholarshipBasePayment = true });
        await db.SaveChangesAsync();

        var controller = new InstructorController(db);
        AttachInstructor(controller, instructor.Id);

        // Act
        var result = await controller.FetchFileDetails(100);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var details = okResult.Value.Should().BeOfType<InstructorFileDetailsDto>().Subject;
        details.FirstName.Should().Be("Ana");
        details.SessionsPayed.Should().Be(10);
        details.MinDrivingLessonsRequired.Should().Be(30);
    }

    // ????????? GET /api/instructor/{instructorId}/fetchInstructorAppointments/{startDate}/{endDate} ?????????

    [Fact]
    public async Task FetchInstructorAppointments_ReturnsAppointmentsInRange()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var instructor = new ApplicationUser { Id = "instructor-1", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", FirstName = "Ion", LastName = "Test", AutoSchoolId = 1 };
        db.Users.AddRange(instructor, student);

        db.Licenses.Add(new DriveFlow_CRM_API.Models.License { LicenseId = 1, Type = "B" });
        db.TeachingCategories.Add(new TeachingCategory { TeachingCategoryId = 1, Code = "B", LicenseId = 1, AutoSchoolId = 1 });
        db.Vehicles.Add(new Vehicle { VehicleId = 1, LicensePlateNumber = "CJ-99-XYZ", AutoSchoolId = 1 });

        var file = new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = "student-1",
            InstructorId = "instructor-1",
            VehicleId = 1,
            TeachingCategoryId = 1,
            Status = FileStatus.APPROVED
        };
        db.Files.Add(file);

        db.Appointments.Add(new Appointment
        {
            AppointmentId = 1,
            FileId = 1,
            Date = DateTime.Today.AddDays(5),
            StartHour = TimeSpan.FromHours(9),
            EndHour = TimeSpan.FromHours(11)
        });
        await db.SaveChangesAsync();

        var controller = new InstructorController(db);
        AttachInstructor(controller, instructor.Id);

        var startDate = DateTime.Today;
        var endDate = DateTime.Today.AddDays(10);

        // Act
        var result = await controller.FetchInstructorAppointments("instructor-1", startDate, endDate);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var appointments = okResult.Value.Should().BeAssignableTo<List<InstructorAppointmentDto>>().Subject;
        appointments.Should().HaveCount(1);
        appointments[0].LicensePlateNumber.Should().Be("CJ-99-XYZ");
    }

    // ????????? GET /api/instructor/{instructorId}/stats/cohort ?????????

    [Fact]
    public async Task GetInstructorCohortStats_NoSessionForms_ReturnsEmptyStats()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", AutoSchoolId = 1 };
        db.Users.Add(instructor);
        await db.SaveChangesAsync();

        var controller = new InstructorController(db);
        AttachInstructor(controller, instructor.Id);

        // Act
        var result = await controller.GetInstructorCohortStats("instructor-1");

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var stats = okResult.Value.Should().BeOfType<InstructorCohortStatsDto>().Subject;
        stats.histogramtotalpoints.Should().HaveCount(3);
        stats.topitemsbystudent.Should().BeEmpty();
        stats.failureRate.Should().Be(0);
    }
}
