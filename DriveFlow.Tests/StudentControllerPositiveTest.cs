using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

using DriveFlow_CRM_API.Controllers;
using DriveFlow_CRM_API.Models;
using DriveFlow_CRM_API.Models.DTOs;
using License = DriveFlow_CRM_API.Models.License;

namespace DriveFlow.Tests.Controllers;

/// <summary>
/// Positive-path unit tests for <see cref="StudentController"/>:
/// GET files, GET file-details, GET appointments, POST/PUT/DELETE appointments.
/// EF Core runs in-memory.
/// </summary>
public sealed class StudentControllerPositiveTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Student_Pos_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static void AttachStudent(ControllerBase controller, string studentId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Student"),
            new Claim(ClaimTypes.NameIdentifier, studentId)
        }, "mock");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    // ????????? GET /api/student/{studentId}/files ?????????

    [Fact]
    public async Task GetStudentFiles_ReturnsFilesForStudent()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var student = new ApplicationUser { Id = "student-1", FirstName = "Ion", LastName = "Pop", AutoSchoolId = 1 };
        var instructor = new ApplicationUser { Id = "instructor-1", FirstName = "Maria", LastName = "Ionescu", AutoSchoolId = 1 };
        db.Users.AddRange(student, instructor);

        db.Licenses.Add(new DriveFlow_CRM_API.Models.License { LicenseId = 1, Type = "B" });
        db.TeachingCategories.Add(new TeachingCategory { TeachingCategoryId = 1, Code = "B", LicenseId = 1, AutoSchoolId = 1 });

        db.Files.Add(new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = "student-1",
            InstructorId = "instructor-1",
            TeachingCategoryId = 1,
            Status = FileStatus.APPROVED
        });
        await db.SaveChangesAsync();

        var controller = new StudentController(db);
        AttachStudent(controller, student.Id);

        // Act
        var result = await controller.GetStudentFiles("student-1");

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var files = okResult.Value.Should().BeAssignableTo<List<StudentFileDto>>().Subject;
        files.Should().HaveCount(1);
        files[0].FirstName.Should().Be("Maria");
        files[0].Type.Should().Be("B");
    }

    // ????????? GET /api/student/file-details/{fileId} ?????????

    [Fact]
    public async Task GetStudentFileDetails_ExistingFile_ReturnsDetails()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        var instructor = new ApplicationUser { Id = "instructor-1", FirstName = "Ana", LastName = "Test", Email = "ana@test.ro", PhoneNumber = "0721000000", AutoSchoolId = 1 };
        db.Users.AddRange(student, instructor);

        db.Licenses.Add(new DriveFlow_CRM_API.Models.License { LicenseId = 1, Type = "B" });
        db.TeachingCategories.Add(new TeachingCategory { TeachingCategoryId = 1, Code = "B", LicenseId = 1, AutoSchoolId = 1 });
        db.Vehicles.Add(new Vehicle { VehicleId = 1, LicensePlateNumber = "CJ-01-XYZ", TransmissionType = TransmissionType.MANUAL, AutoSchoolId = 1 });

        var file = new DriveFlow_CRM_API.Models.File
        {
            FileId = 100,
            StudentId = "student-1",
            InstructorId = "instructor-1",
            VehicleId = 1,
            TeachingCategoryId = 1,
            Status = FileStatus.APPROVED,
            ScholarshipStartDate = DateTime.Today
        };
        db.Files.Add(file);

        db.Payments.Add(new Payment { PaymentId = 1, FileId = 100, SessionsPayed = 5, ScholarshipBasePayment = true });
        await db.SaveChangesAsync();

        var controller = new StudentController(db);
        AttachStudent(controller, student.Id);

        // Act
        var result = await controller.GetStudentFileDetails(100);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var details = okResult.Value.Should().BeOfType<FileDetailsDto>().Subject;
        details.FileId.Should().Be(100);
        details.Instructor.Should().NotBeNull();
        details.Instructor!.FirstName.Should().Be("Ana");
        details.Vehicle.Should().NotBeNull();
        details.Payment.Should().NotBeNull();
        details.Payment!.SessionsPayed.Should().Be(5);
    }

    // ????????? GET /api/student/future-appointments ?????????

    [Fact]
    public async Task GetFutureAppointments_ReturnsFutureAppointments()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        var instructor = new ApplicationUser { Id = "instructor-1", FirstName = "Ion", LastName = "Pop", AutoSchoolId = 1 };
        db.Users.AddRange(student, instructor);

        db.Licenses.Add(new DriveFlow_CRM_API.Models.License { LicenseId = 1, Type = "B" });
        db.TeachingCategories.Add(new TeachingCategory { TeachingCategoryId = 1, Code = "B", LicenseId = 1, AutoSchoolId = 1 });

        var file = new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = "student-1",
            InstructorId = "instructor-1",
            TeachingCategoryId = 1,
            Status = FileStatus.APPROVED
        };
        db.Files.Add(file);

        db.Appointments.Add(new Appointment
        {
            AppointmentId = 1,
            FileId = 1,
            Date = DateTime.Today.AddDays(5),
            StartHour = TimeSpan.FromHours(10),
            EndHour = TimeSpan.FromHours(12)
        });
        await db.SaveChangesAsync();

        var controller = new StudentController(db);
        AttachStudent(controller, student.Id);

        // Act
        var result = await controller.GetFutureAppointments();

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var appointments = okResult.Value.Should().BeAssignableTo<List<StudentAppointmentDto>>().Subject;
        appointments.Should().HaveCount(1);
        appointments[0].InstructorName.Should().Be("Ion Pop");
    }

    // ????????? GET /api/student/all-appointments ?????????

    [Fact]
    public async Task GetAllAppointments_ReturnsAllAppointmentsWithStatus()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        var instructor = new ApplicationUser { Id = "instructor-1", FirstName = "Ion", LastName = "Test", AutoSchoolId = 1 };
        db.Users.AddRange(student, instructor);

        db.Licenses.Add(new DriveFlow_CRM_API.Models.License { LicenseId = 1, Type = "B" });
        db.TeachingCategories.Add(new TeachingCategory { TeachingCategoryId = 1, Code = "B", LicenseId = 1, AutoSchoolId = 1 });

        var file = new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = "student-1",
            InstructorId = "instructor-1",
            TeachingCategoryId = 1,
            Status = FileStatus.APPROVED
        };
        db.Files.Add(file);

        // Past appointment
        db.Appointments.Add(new Appointment
        {
            AppointmentId = 1,
            FileId = 1,
            Date = DateTime.Today.AddDays(-5),
            StartHour = TimeSpan.FromHours(10),
            EndHour = TimeSpan.FromHours(12)
        });

        // Future appointment
        db.Appointments.Add(new Appointment
        {
            AppointmentId = 2,
            FileId = 1,
            Date = DateTime.Today.AddDays(5),
            StartHour = TimeSpan.FromHours(14),
            EndHour = TimeSpan.FromHours(16)
        });
        await db.SaveChangesAsync();

        var controller = new StudentController(db);
        AttachStudent(controller, student.Id);

        // Act
        var result = await controller.GetAllAppointments();

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var appointments = okResult.Value.Should().BeAssignableTo<List<StudentAppointmentFullDto>>().Subject;
        appointments.Should().HaveCount(2);
        appointments.Should().Contain(a => a.Status == "completed");
        appointments.Should().Contain(a => a.Status == "pending");
    }

    // ????????? POST /api/student/files/{fileId}/appointments ?????????

    [Fact]
    public async Task CreateAppointment_ValidData_Returns201AndCreates()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        var instructor = new ApplicationUser { Id = "instructor-1", AutoSchoolId = 1 };
        db.Users.AddRange(student, instructor);

        db.TeachingCategories.Add(new TeachingCategory { TeachingCategoryId = 1, Code = "B", SessionDuration = 120, AutoSchoolId = 1 });

        var file = new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = "student-1",
            InstructorId = "instructor-1",
            TeachingCategoryId = 1,
            Status = FileStatus.APPROVED
        };
        db.Files.Add(file);

        // Add instructor availability
        db.InstructorAvailabilities.Add(new InstructorAvailability
        {
            IntervalId = 1,
            InstructorId = "instructor-1",
            Date = DateTime.Today.AddDays(3),
            StartHour = TimeSpan.FromHours(8),
            EndHour = TimeSpan.FromHours(18)
        });
        await db.SaveChangesAsync();

        var controller = new StudentController(db);
        AttachStudent(controller, student.Id);

        var dto = new CreateAppointmentDto
        {
            Date = DateTime.Today.AddDays(3),
            StartHour = "10:00",
            EndHour = "12:00"
        };

        // Act
        var result = await controller.CreateAppointment(1, dto);

        // Assert
        result.Should().BeOfType<CreatedResult>();
        db.Appointments.Should().ContainSingle(a => a.FileId == 1);
    }

    // ????????? DELETE /api/student/appointments/delete/{appointmentId} ?????????

    [Fact]
    public async Task DeleteAppointment_FutureAppointment_Returns200AndDeletes()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        db.Users.Add(student);

        var file = new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = "student-1",
            Status = FileStatus.APPROVED
        };
        db.Files.Add(file);

        db.Appointments.Add(new Appointment
        {
            AppointmentId = 100,
            FileId = 1,
            Date = DateTime.Today.AddDays(5),
            StartHour = TimeSpan.FromHours(10),
            EndHour = TimeSpan.FromHours(12)
        });
        await db.SaveChangesAsync();

        var controller = new StudentController(db);
        AttachStudent(controller, student.Id);

        // Act
        var result = await controller.DeleteAppointment(100);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        db.Appointments.Should().BeEmpty();
    }
}
