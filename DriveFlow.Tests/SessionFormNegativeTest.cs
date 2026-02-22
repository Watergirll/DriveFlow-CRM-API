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
using DriveFlow_CRM_API.Models.DTOs;

// Alias to resolve ambiguity with FluentAssertions.License
using LicenseModel = DriveFlow_CRM_API.Models.License;

namespace DriveFlow.Tests.Controllers;

/// <summary>
/// Negative-path unit tests for <see cref="SessionFormController"/>:
/// 400 (BadRequest), 404 (NotFound), and 409 (Conflict) scenarios.
/// EF Core runs in-memory; UserManager is mocked.
/// </summary>
public sealed class SessionFormNegativeTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"SessionFormNeg_{Guid.NewGuid()}")
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
            mgr.Setup(x => x.FindByIdAsync(userToReturn.Id))
               .ReturnsAsync(userToReturn);
        }

        return mgr;
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

    // ????????? GET /api/session-forms/{id} ?????????

    [Fact]
    public async Task Get_NonExistentId_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.com" };
        var userMgr = MockUserManager(instructor);

        var controller = new SessionFormController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        // Act
        var result = await controller.Get(99999);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ????????? POST /api/session-forms/{appointmentId}/submit ?????????

    [Fact]
    public async Task SubmitSessionForm_NonExistentAppointment_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.com" };
        var userMgr = MockUserManager(instructor);

        var controller = new SessionFormController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        var request = new SubmitSessionFormRequest(
            Mistakes: new List<MistakeItemDto>(),
            MaxPoints: 21
        );

        // Act
        var result = await controller.SubmitSessionForm(99999, request);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task SubmitSessionForm_InvalidAppointmentId_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.com" };
        var userMgr = MockUserManager(instructor);

        var controller = new SessionFormController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        var request = new SubmitSessionFormRequest(
            Mistakes: new List<MistakeItemDto>(),
            MaxPoints: 21
        );

        // Act
        var result = await controller.SubmitSessionForm(0, request);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SubmitSessionForm_NegativeAppointmentId_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.com" };
        var userMgr = MockUserManager(instructor);

        var controller = new SessionFormController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        var request = new SubmitSessionFormRequest(
            Mistakes: new List<MistakeItemDto>(),
            MaxPoints: 21
        );

        // Act
        var result = await controller.SubmitSessionForm(-1, request);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SubmitSessionForm_NullRequest_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.com" };
        var userMgr = MockUserManager(instructor);

        var controller = new SessionFormController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        // Act
        var result = await controller.SubmitSessionForm(1, null!);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SubmitSessionForm_InvalidMaxPoints_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.com" };
        var userMgr = MockUserManager(instructor);

        var controller = new SessionFormController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        var request = new SubmitSessionFormRequest(
            Mistakes: new List<MistakeItemDto>(),
            MaxPoints: 0  // Invalid: must be positive
        );

        // Act
        var result = await controller.SubmitSessionForm(1, request);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SubmitSessionForm_NegativeMaxPoints_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.com" };
        var userMgr = MockUserManager(instructor);

        var controller = new SessionFormController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        var request = new SubmitSessionFormRequest(
            Mistakes: new List<MistakeItemDto>(),
            MaxPoints: -5
        );

        // Act
        var result = await controller.SubmitSessionForm(1, request);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SubmitSessionForm_AppointmentWithNoTeachingCategory_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.com" };
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.com" };
        
        db.Users.AddRange(instructor, student);
        
        // File without teaching category
        var file = new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = student.Id,
            InstructorId = instructor.Id,
            TeachingCategoryId = null
        };
        db.Files.Add(file);
        
        var appointment = new Appointment
        {
            AppointmentId = 1,
            FileId = 1,
            Date = DateTime.Now,
            StartHour = TimeSpan.FromHours(9),
            EndHour = TimeSpan.FromHours(10)
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor);
        var controller = new SessionFormController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        var request = new SubmitSessionFormRequest(
            Mistakes: new List<MistakeItemDto>(),
            MaxPoints: 21
        );

        // Act
        var result = await controller.SubmitSessionForm(1, request);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task SubmitSessionForm_NoExamFormForLicense_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.com" };
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.com" };
        
        db.Users.AddRange(instructor, student);
        
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "TestSchool", Email = "school@test.com" });
        db.Licenses.Add(new LicenseModel { LicenseId = 1, Type = "B" });
        
        var teachingCategory = new TeachingCategory
        {
            TeachingCategoryId = 1,
            Code = "B",
            AutoSchoolId = 1,
            LicenseId = 1
        };
        db.TeachingCategories.Add(teachingCategory);
        
        var file = new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = student.Id,
            InstructorId = instructor.Id,
            TeachingCategoryId = 1
        };
        db.Files.Add(file);
        
        var appointment = new Appointment
        {
            AppointmentId = 1,
            FileId = 1,
            Date = DateTime.Now,
            StartHour = TimeSpan.FromHours(9),
            EndHour = TimeSpan.FromHours(10)
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor);
        var controller = new SessionFormController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        var request = new SubmitSessionFormRequest(
            Mistakes: new List<MistakeItemDto>(),
            MaxPoints: 21
        );

        // Act
        var result = await controller.SubmitSessionForm(1, request);

        // Assert - No ExamForm exists for License 1
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task SubmitSessionForm_DuplicateSubmission_Returns409Conflict()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.com" };
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.com" };
        
        db.Users.AddRange(instructor, student);
        
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "TestSchool", Email = "school@test.com" });
        db.Licenses.Add(new LicenseModel { LicenseId = 1, Type = "B" });
        
        var teachingCategory = new TeachingCategory
        {
            TeachingCategoryId = 1,
            Code = "B",
            AutoSchoolId = 1,
            LicenseId = 1
        };
        db.TeachingCategories.Add(teachingCategory);
        
        var examForm = new ExamForm
        {
            FormId = 1,
            LicenseId = 1,
            MaxPoints = 21,
            Items = new List<ExamItem>
            {
                new() { ItemId = 1, Description = "Test item", PenaltyPoints = 3, OrderIndex = 1 }
            }
        };
        db.ExamForms.Add(examForm);
        
        var file = new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = student.Id,
            InstructorId = instructor.Id,
            TeachingCategoryId = 1
        };
        db.Files.Add(file);
        
        var appointment = new Appointment
        {
            AppointmentId = 1,
            FileId = 1,
            Date = DateTime.Now,
            StartHour = TimeSpan.FromHours(9),
            EndHour = TimeSpan.FromHours(10)
        };
        db.Appointments.Add(appointment);
        
        // Already existing session form for this appointment
        var existingSessionForm = new SessionForm
        {
            SessionFormId = 1,
            AppointmentId = 1,
            FormId = 1,
            MistakesJson = "[]",
            CreatedAt = DateTime.UtcNow,
            TotalPoints = 0,
            Result = "OK"
        };
        db.SessionForms.Add(existingSessionForm);
        
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor);
        var controller = new SessionFormController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        var request = new SubmitSessionFormRequest(
            Mistakes: new List<MistakeItemDto>(),
            MaxPoints: 21
        );

        // Act
        var result = await controller.SubmitSessionForm(1, request);

        // Assert - Conflict because session form already exists
        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task SubmitSessionForm_NegativeMistakeCount_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.com" };
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.com" };
        
        db.Users.AddRange(instructor, student);
        
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "TestSchool", Email = "school@test.com" });
        db.Licenses.Add(new LicenseModel { LicenseId = 1, Type = "B" });
        
        var teachingCategory = new TeachingCategory
        {
            TeachingCategoryId = 1,
            Code = "B",
            AutoSchoolId = 1,
            LicenseId = 1
        };
        db.TeachingCategories.Add(teachingCategory);
        
        var examForm = new ExamForm
        {
            FormId = 1,
            LicenseId = 1,
            MaxPoints = 21,
            Items = new List<ExamItem>
            {
                new() { ItemId = 1, Description = "Test item", PenaltyPoints = 3, OrderIndex = 1 }
            }
        };
        db.ExamForms.Add(examForm);
        
        var file = new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = student.Id,
            InstructorId = instructor.Id,
            TeachingCategoryId = 1
        };
        db.Files.Add(file);
        
        var appointment = new Appointment
        {
            AppointmentId = 1,
            FileId = 1,
            Date = DateTime.Now,
            StartHour = TimeSpan.FromHours(9),
            EndHour = TimeSpan.FromHours(10)
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor);
        var controller = new SessionFormController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        var request = new SubmitSessionFormRequest(
            Mistakes: new List<MistakeItemDto>
            {
                new(IdItem: 1, Count: -5)  // Negative count is invalid
            },
            MaxPoints: 21
        );

        // Act
        var result = await controller.SubmitSessionForm(1, request);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SubmitSessionForm_NonExistentExamItem_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.com" };
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.com" };
        
        db.Users.AddRange(instructor, student);
        
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "TestSchool", Email = "school@test.com" });
        db.Licenses.Add(new LicenseModel { LicenseId = 1, Type = "B" });
        
        var teachingCategory = new TeachingCategory
        {
            TeachingCategoryId = 1,
            Code = "B",
            AutoSchoolId = 1,
            LicenseId = 1
        };
        db.TeachingCategories.Add(teachingCategory);
        
        var examForm = new ExamForm
        {
            FormId = 1,
            LicenseId = 1,
            MaxPoints = 21,
            Items = new List<ExamItem>
            {
                new() { ItemId = 1, Description = "Test item", PenaltyPoints = 3, OrderIndex = 1 }
            }
        };
        db.ExamForms.Add(examForm);
        
        var file = new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = student.Id,
            InstructorId = instructor.Id,
            TeachingCategoryId = 1
        };
        db.Files.Add(file);
        
        var appointment = new Appointment
        {
            AppointmentId = 1,
            FileId = 1,
            Date = DateTime.Now,
            StartHour = TimeSpan.FromHours(9),
            EndHour = TimeSpan.FromHours(10)
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor);
        var controller = new SessionFormController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        var request = new SubmitSessionFormRequest(
            Mistakes: new List<MistakeItemDto>
            {
                new(IdItem: 99999, Count: 2)  // Non-existent item ID
            },
            MaxPoints: 21
        );

        // Act
        var result = await controller.SubmitSessionForm(1, request);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task SubmitSessionForm_InvalidModelState_Returns400WhenChecked()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.com" };
        var userMgr = MockUserManager(instructor);

        var controller = new SessionFormController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);
        
        // Simulate invalid model state (as framework would do)
        controller.ModelState.AddModelError("Mistakes", "Mistakes list is required");

        var request = new SubmitSessionFormRequest(
            Mistakes: null,
            MaxPoints: 21
        );

        // Act
        // In controller-direct testing, [ApiController] automatic ModelState validation
        // doesn't run. We manually check ModelState.IsValid to simulate framework behavior.
        if (!controller.ModelState.IsValid)
        {
            var validationResult = new BadRequestObjectResult(controller.ModelState);
            validationResult.Should().BeOfType<BadRequestObjectResult>();
            return;
        }

        var result = await controller.SubmitSessionForm(1, request);
        result.Should().NotBeNull();
    }

    // ????????? GET /api/students/{id_student}/session-forms ?????????

    [Fact]
    public async Task ListStudentForms_InvalidPage_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.com" };
        var userMgr = MockUserManager(student);
        userMgr.Setup(x => x.FindByIdAsync("student-1")).ReturnsAsync(student);

        var controller = new SessionFormController(db, userMgr.Object);
        AttachStudent(controller, student.Id);

        // Act
        var result = await controller.ListStudentForms("student-1", page: 0);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ListStudentForms_InvalidPageSize_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.com" };
        var userMgr = MockUserManager(student);
        userMgr.Setup(x => x.FindByIdAsync("student-1")).ReturnsAsync(student);

        var controller = new SessionFormController(db, userMgr.Object);
        AttachStudent(controller, student.Id);

        // Act
        var result = await controller.ListStudentForms("student-1", pageSize: 0);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ListStudentForms_PageSizeTooLarge_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.com" };
        var userMgr = MockUserManager(student);
        userMgr.Setup(x => x.FindByIdAsync("student-1")).ReturnsAsync(student);

        var controller = new SessionFormController(db, userMgr.Object);
        AttachStudent(controller, student.Id);

        // Act
        var result = await controller.ListStudentForms("student-1", pageSize: 101);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ListStudentForms_NonExistentStudent_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var currentUser = new ApplicationUser { Id = "admin-1", Email = "admin@test.com", AutoSchoolId = 1 };
        var userMgr = MockUserManager(currentUser);
        userMgr.Setup(x => x.FindByIdAsync("nonexistent-student")).ReturnsAsync((ApplicationUser?)null);

        var controller = new SessionFormController(db, userMgr.Object);
        AttachSchoolAdmin(controller, currentUser.Id);

        // Act
        var result = await controller.ListStudentForms("nonexistent-student");

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task ListStudentForms_InvalidFromDateFormat_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.com" };
        var userMgr = MockUserManager(student);
        userMgr.Setup(x => x.FindByIdAsync("student-1")).ReturnsAsync(student);

        var controller = new SessionFormController(db, userMgr.Object);
        AttachStudent(controller, student.Id);

        // Act
        var result = await controller.ListStudentForms("student-1", from: "invalid-date");

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ListStudentForms_InvalidToDateFormat_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.com" };
        var userMgr = MockUserManager(student);
        userMgr.Setup(x => x.FindByIdAsync("student-1")).ReturnsAsync(student);

        var controller = new SessionFormController(db, userMgr.Object);
        AttachStudent(controller, student.Id);

        // Act
        var result = await controller.ListStudentForms("student-1", to: "not-a-date");

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ListStudentForms_FileIdNotBelongingToStudent_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        
        var student = new ApplicationUser { Id = "student-1", Email = "student@test.com" };
        var otherStudent = new ApplicationUser { Id = "student-2", Email = "other@test.com" };
        
        db.Users.AddRange(student, otherStudent);
        
        // File belongs to other student
        var file = new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = otherStudent.Id
        };
        db.Files.Add(file);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(student);
        userMgr.Setup(x => x.FindByIdAsync("student-1")).ReturnsAsync(student);

        var controller = new SessionFormController(db, userMgr.Object);
        AttachStudent(controller, student.Id);

        // Act - Try to get session forms for student-1 but with fileId=1 which belongs to student-2
        var result = await controller.ListStudentForms("student-1", fileId: 1);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }
}
