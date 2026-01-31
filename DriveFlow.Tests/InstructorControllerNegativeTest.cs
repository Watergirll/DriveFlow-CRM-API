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
/// Negative-path unit tests for <see cref="InstructorController"/>:
/// 401, 403, 404 scenarios.
/// EF Core runs in-memory.
/// </summary>
public sealed class InstructorControllerNegativeTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Instructor_Neg_{Guid.NewGuid()}")
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

    private static void AttachNoUser(ControllerBase controller)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }
        };
    }

    // ????????? FetchInstructorAssignedFiles - Negative ?????????

    [Fact]
    public async Task FetchInstructorAssignedFiles_NoAuthentication_Returns401()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var controller = new InstructorController(db);
        AttachNoUser(controller);

        // Act
        var result = await controller.FetchInstructorAssignedFiles("instructor-1");

        // Assert
        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task FetchInstructorAssignedFiles_DifferentInstructor_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor1 = new ApplicationUser { Id = "instructor-1", AutoSchoolId = 1 };
        var instructor2 = new ApplicationUser { Id = "instructor-2", AutoSchoolId = 1 };
        db.Users.AddRange(instructor1, instructor2);
        await db.SaveChangesAsync();

        var controller = new InstructorController(db);
        AttachInstructor(controller, instructor1.Id);

        // Act - instructor-1 tries to access instructor-2's files
        var result = await controller.FetchInstructorAssignedFiles("instructor-2");

        // Assert
        result.Result.Should().BeOfType<ForbidResult>();
    }

    // ????????? FetchFileDetails - Negative ?????????

    [Fact]
    public async Task FetchFileDetails_NoAuthentication_Returns401()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var controller = new InstructorController(db);
        AttachNoUser(controller);

        // Act
        var result = await controller.FetchFileDetails(1);

        // Assert
        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task FetchFileDetails_NonExistentFile_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", AutoSchoolId = 1 };
        db.Users.Add(instructor);
        await db.SaveChangesAsync();

        var controller = new InstructorController(db);
        AttachInstructor(controller, instructor.Id);

        // Act
        var result = await controller.FetchFileDetails(99999);

        // Assert
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task FetchFileDetails_FileNotAssignedToInstructor_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor1 = new ApplicationUser { Id = "instructor-1", AutoSchoolId = 1 };
        var instructor2 = new ApplicationUser { Id = "instructor-2", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        db.Users.AddRange(instructor1, instructor2, student);

        db.Files.Add(new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = "student-1",
            InstructorId = "instructor-2", // Assigned to instructor-2
            Status = FileStatus.APPROVED
        });
        await db.SaveChangesAsync();

        var controller = new InstructorController(db);
        AttachInstructor(controller, instructor1.Id);

        // Act - instructor-1 tries to access file assigned to instructor-2
        var result = await controller.FetchFileDetails(1);

        // Assert
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    // ????????? FetchInstructorAppointments - Negative ?????????

    [Fact]
    public async Task FetchInstructorAppointments_NoAuthentication_Returns401()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var controller = new InstructorController(db);
        AttachNoUser(controller);

        // Act
        var result = await controller.FetchInstructorAppointments("instructor-1", DateTime.Today, DateTime.Today.AddDays(7));

        // Assert
        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task FetchInstructorAppointments_DifferentInstructor_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor1 = new ApplicationUser { Id = "instructor-1", AutoSchoolId = 1 };
        var instructor2 = new ApplicationUser { Id = "instructor-2", AutoSchoolId = 1 };
        db.Users.AddRange(instructor1, instructor2);
        await db.SaveChangesAsync();

        var controller = new InstructorController(db);
        AttachInstructor(controller, instructor1.Id);

        // Act - instructor-1 tries to access instructor-2's appointments
        var result = await controller.FetchInstructorAppointments("instructor-2", DateTime.Today, DateTime.Today.AddDays(7));

        // Assert
        result.Result.Should().BeOfType<ForbidResult>();
    }

    // ????????? GetInstructorCohortStats - Negative ?????????

    [Fact]
    public async Task GetInstructorCohortStats_NoAuthentication_Returns401()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var controller = new InstructorController(db);
        AttachNoUser(controller);

        // Act
        var result = await controller.GetInstructorCohortStats("instructor-1");

        // Assert
        result.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task GetInstructorCohortStats_DifferentInstructor_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor1 = new ApplicationUser { Id = "instructor-1", AutoSchoolId = 1 };
        var instructor2 = new ApplicationUser { Id = "instructor-2", AutoSchoolId = 1 };
        db.Users.AddRange(instructor1, instructor2);
        await db.SaveChangesAsync();

        var controller = new InstructorController(db);
        AttachInstructor(controller, instructor1.Id);

        // Act
        var result = await controller.GetInstructorCohortStats("instructor-2");

        // Assert
        result.Result.Should().BeOfType<ForbidResult>();
    }
}
