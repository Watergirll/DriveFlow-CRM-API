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
/// Negative-path unit tests for <see cref="StudentController"/>:
/// 400, 401, 403, 404 scenarios.
/// EF Core runs in-memory.
/// </summary>
public sealed class StudentControllerNegativeTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Student_Neg_{Guid.NewGuid()}")
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

    private static void AttachNoUser(ControllerBase controller)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }
        };
    }

    // ????????? GetStudentFiles - Negative ?????????

    [Fact]
    public async Task GetStudentFiles_NoAuthentication_Returns401()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var controller = new StudentController(db);
        AttachNoUser(controller);

        // Act
        var result = await controller.GetStudentFiles("student-1");

        // Assert
        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task GetStudentFiles_DifferentStudent_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var student1 = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        var student2 = new ApplicationUser { Id = "student-2", AutoSchoolId = 1 };
        db.Users.AddRange(student1, student2);
        await db.SaveChangesAsync();

        var controller = new StudentController(db);
        AttachStudent(controller, student1.Id);

        // Act - student-1 tries to access student-2's files
        var result = await controller.GetStudentFiles("student-2");

        // Assert
        result.Result.Should().BeOfType<ForbidResult>();
    }

    // ????????? GetStudentFileDetails - Negative ?????????

    [Fact]
    public async Task GetStudentFileDetails_NonExistentFile_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        db.Users.Add(student);
        await db.SaveChangesAsync();

        var controller = new StudentController(db);
        AttachStudent(controller, student.Id);

        // Act
        var result = await controller.GetStudentFileDetails(99999);

        // Assert
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetStudentFileDetails_FileNotOwnedByStudent_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var student1 = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        var student2 = new ApplicationUser { Id = "student-2", AutoSchoolId = 1 };
        db.Users.AddRange(student1, student2);

        db.Files.Add(new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = "student-2", // Belongs to student-2
            Status = FileStatus.APPROVED
        });
        await db.SaveChangesAsync();

        var controller = new StudentController(db);
        AttachStudent(controller, student1.Id);

        // Act - student-1 tries to access student-2's file
        var result = await controller.GetStudentFileDetails(1);

        // Assert
        result.Result.Should().BeOfType<ForbidResult>();
    }

    // ????????? CreateAppointment - Negative ?????????

    [Fact]
    public async Task CreateAppointment_FileNotFound_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        db.Users.Add(student);
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
        var result = await controller.CreateAppointment(99999, dto);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task CreateAppointment_FileNotOwnedByStudent_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var student1 = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        var student2 = new ApplicationUser { Id = "student-2", AutoSchoolId = 1 };
        db.Users.AddRange(student1, student2);

        db.Files.Add(new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = "student-2",
            Status = FileStatus.APPROVED
        });
        await db.SaveChangesAsync();

        var controller = new StudentController(db);
        AttachStudent(controller, student1.Id);

        var dto = new CreateAppointmentDto
        {
            Date = DateTime.Today.AddDays(3),
            StartHour = "10:00",
            EndHour = "12:00"
        };

        // Act
        var result = await controller.CreateAppointment(1, dto);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task CreateAppointment_NoTeachingCategory_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        db.Users.Add(student);

        db.Files.Add(new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = "student-1",
            TeachingCategoryId = null, // No teaching category
            Status = FileStatus.APPROVED
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
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
    }

    [Fact]
    public async Task CreateAppointment_NoInstructor_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        db.Users.Add(student);

        db.TeachingCategories.Add(new TeachingCategory { TeachingCategoryId = 1, Code = "B", AutoSchoolId = 1 });

        db.Files.Add(new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = "student-1",
            TeachingCategoryId = 1,
            InstructorId = null, // No instructor
            Status = FileStatus.APPROVED
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
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateAppointment_PastDate_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        var instructor = new ApplicationUser { Id = "instructor-1", AutoSchoolId = 1 };
        db.Users.AddRange(student, instructor);

        db.TeachingCategories.Add(new TeachingCategory { TeachingCategoryId = 1, Code = "B", SessionDuration = 120, AutoSchoolId = 1 });

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

        var dto = new CreateAppointmentDto
        {
            Date = DateTime.Today.AddDays(-1), // Past date
            StartHour = "10:00",
            EndHour = "12:00"
        };

        // Act
        var result = await controller.CreateAppointment(1, dto);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ????????? DeleteAppointment - Negative ?????????

    [Fact]
    public async Task DeleteAppointment_NonExistent_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        db.Users.Add(student);
        await db.SaveChangesAsync();

        var controller = new StudentController(db);
        AttachStudent(controller, student.Id);

        // Act
        var result = await controller.DeleteAppointment(99999);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DeleteAppointment_NotOwned_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var student1 = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        var student2 = new ApplicationUser { Id = "student-2", AutoSchoolId = 1 };
        db.Users.AddRange(student1, student2);

        var file = new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = "student-2", // Belongs to student-2
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
        AttachStudent(controller, student1.Id);

        // Act - student-1 tries to delete student-2's appointment
        var result = await controller.DeleteAppointment(1);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task DeleteAppointment_PastAppointment_Returns400()
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
            AppointmentId = 1,
            FileId = 1,
            Date = DateTime.Today.AddDays(-5), // Past appointment
            StartHour = TimeSpan.FromHours(10),
            EndHour = TimeSpan.FromHours(12)
        });
        await db.SaveChangesAsync();

        var controller = new StudentController(db);
        AttachStudent(controller, student.Id);

        // Act
        var result = await controller.DeleteAppointment(1);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ????????? UpdateAppointment - Negative ?????????

    [Fact]
    public async Task UpdateAppointment_NonExistent_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        db.Users.Add(student);
        await db.SaveChangesAsync();

        var controller = new StudentController(db);
        AttachStudent(controller, student.Id);

        var dto = new UpdateAppointmentDto
        {
            Date = DateTime.Today.AddDays(5),
            StartHour = "10:00",
            EndHour = "12:00"
        };

        // Act
        var result = await controller.UpdateAppointment(99999, dto);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }
}
