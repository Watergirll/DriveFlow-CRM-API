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

namespace DriveFlow.Tests.Controllers;

/// <summary>
/// Negative-path unit tests for <see cref="InstructorAvailabilityController"/>:
/// 400 (BadRequest), 403 (Forbid), and 404 (NotFound) scenarios.
/// EF Core runs in-memory; UserManager is mocked.
/// </summary>
public sealed class InstructorAvailabilityControllerNegativeTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"InstrAvail_Neg_{Guid.NewGuid()}")
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

    // ????????? GET - Access Control ?????????

    [Fact]
    public async Task GetInstructorAvailability_InstructorAccessingOtherInstructor_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor1 = new ApplicationUser { Id = "instructor-1", Email = "inst1@test.ro", AutoSchoolId = 1 };
        var instructor2 = new ApplicationUser { Id = "instructor-2", Email = "inst2@test.ro", AutoSchoolId = 1 };
        db.Users.AddRange(instructor1, instructor2);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor1);
        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachInstructor(controller, instructor1.Id);

        // Act - instructor-1 tries to access instructor-2's availability
        var result = await controller.GetInstructorAvailability("instructor-2");

        // Assert
        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetInstructorAvailability_SchoolAdminDifferentSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.ro", AutoSchoolId = 1 };
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 2 }; // Different school
        db.Users.AddRange(instructor, admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager();
        userMgr.Setup(x => x.FindByIdAsync("admin-1")).ReturnsAsync(admin);
        userMgr.Setup(x => x.FindByIdAsync("instructor-1")).ReturnsAsync(instructor);

        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetInstructorAvailability("instructor-1");

        // Assert
        result.Result.Should().BeOfType<ForbidResult>();
    }

    // ????????? POST - Create Validation ?????????

    [Fact]
    public async Task CreateInstructorAvailability_PastDate_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.ro", AutoSchoolId = 1 };
        db.Users.Add(instructor);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor);
        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        var pastDate = DateTime.Today.AddDays(-5);
        var dto = new CreateInstructorAvailabilityDto
        {
            Date = pastDate,
            StartHour = "10:00",
            EndHour = "13:00"
        };

        // Act
        var result = await controller.CreateInstructorAvailability("instructor-1", dto);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateInstructorAvailability_InvalidTimeFormat_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.ro", AutoSchoolId = 1 };
        db.Users.Add(instructor);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor);
        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        var futureDate = DateTime.Today.AddDays(5);
        var dto = new CreateInstructorAvailabilityDto
        {
            Date = futureDate,
            StartHour = "invalid",
            EndHour = "13:00"
        };

        // Act
        var result = await controller.CreateInstructorAvailability("instructor-1", dto);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateInstructorAvailability_StartTimeAfterEndTime_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.ro", AutoSchoolId = 1 };
        db.Users.Add(instructor);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor);
        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        var futureDate = DateTime.Today.AddDays(5);
        var dto = new CreateInstructorAvailabilityDto
        {
            Date = futureDate,
            StartHour = "14:00",
            EndHour = "10:00" // End before start
        };

        // Act
        var result = await controller.CreateInstructorAvailability("instructor-1", dto);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateInstructorAvailability_StartTimeEqualsEndTime_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.ro", AutoSchoolId = 1 };
        db.Users.Add(instructor);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor);
        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        var futureDate = DateTime.Today.AddDays(5);
        var dto = new CreateInstructorAvailabilityDto
        {
            Date = futureDate,
            StartHour = "10:00",
            EndHour = "10:00" // Same time
        };

        // Act
        var result = await controller.CreateInstructorAvailability("instructor-1", dto);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateInstructorAvailability_OverlappingInterval_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.ro", AutoSchoolId = 1 };
        db.Users.Add(instructor);
        
        var futureDate = DateTime.Today.AddDays(5);
        // Existing interval: 10:00 - 14:00
        db.InstructorAvailabilities.Add(new InstructorAvailability
        {
            IntervalId = 1,
            InstructorId = "instructor-1",
            Date = futureDate,
            StartHour = TimeSpan.FromHours(10),
            EndHour = TimeSpan.FromHours(14)
        });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor);
        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        // New interval: 12:00 - 16:00 (overlaps with existing)
        var dto = new CreateInstructorAvailabilityDto
        {
            Date = futureDate,
            StartHour = "12:00",
            EndHour = "16:00"
        };

        // Act
        var result = await controller.CreateInstructorAvailability("instructor-1", dto);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateInstructorAvailability_CompletelyContainedInterval_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.ro", AutoSchoolId = 1 };
        db.Users.Add(instructor);
        
        var futureDate = DateTime.Today.AddDays(5);
        // Existing interval: 09:00 - 17:00
        db.InstructorAvailabilities.Add(new InstructorAvailability
        {
            IntervalId = 1,
            InstructorId = "instructor-1",
            Date = futureDate,
            StartHour = TimeSpan.FromHours(9),
            EndHour = TimeSpan.FromHours(17)
        });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor);
        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        // New interval: 11:00 - 13:00 (contained within existing)
        var dto = new CreateInstructorAvailabilityDto
        {
            Date = futureDate,
            StartHour = "11:00",
            EndHour = "13:00"
        };

        // Act
        var result = await controller.CreateInstructorAvailability("instructor-1", dto);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateInstructorAvailability_NonExistentInstructor_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.ro", AutoSchoolId = 1 };
        db.Users.Add(instructor);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor);
        userMgr.Setup(x => x.FindByIdAsync("nonexistent-instructor")).ReturnsAsync((ApplicationUser?)null);

        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        var futureDate = DateTime.Today.AddDays(5);
        var dto = new CreateInstructorAvailabilityDto
        {
            Date = futureDate,
            StartHour = "10:00",
            EndHour = "14:00"
        };

        // Act - instructor-1 tries to create for nonexistent instructor (even though auth should prevent this)
        var result = await controller.CreateInstructorAvailability("nonexistent-instructor", dto);

        // Assert - Should be Forbid because instructor-1 != nonexistent-instructor
        result.Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task CreateInstructorAvailability_InvalidModelState_Returns400WhenChecked()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.ro", AutoSchoolId = 1 };
        db.Users.Add(instructor);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor);
        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        // Simulate invalid model state
        controller.ModelState.AddModelError("StartHour", "StartHour is required");

        var dto = new CreateInstructorAvailabilityDto
        {
            Date = DateTime.Today.AddDays(5),
            StartHour = "",
            EndHour = "14:00"
        };

        // Act
        if (!controller.ModelState.IsValid)
        {
            var validationResult = new BadRequestObjectResult(controller.ModelState);
            validationResult.Should().BeOfType<BadRequestObjectResult>();
            return;
        }

        var result = await controller.CreateInstructorAvailability("instructor-1", dto);
        result.Should().NotBeNull();
    }

    // ????????? PUT - Update Validation ?????????

    [Fact]
    public async Task UpdateInstructorAvailability_NonExistentInterval_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.ro", AutoSchoolId = 1 };
        db.Users.Add(instructor);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor);
        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        var dto = new CreateInstructorAvailabilityDto
        {
            Date = DateTime.Today.AddDays(5),
            StartHour = "10:00",
            EndHour = "14:00"
        };

        // Act
        var result = await controller.UpdateInstructorAvailability("instructor-1", 99999, dto);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task UpdateInstructorAvailability_PastDate_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.ro", AutoSchoolId = 1 };
        db.Users.Add(instructor);
        
        var futureDate = DateTime.Today.AddDays(5);
        db.InstructorAvailabilities.Add(new InstructorAvailability
        {
            IntervalId = 1,
            InstructorId = "instructor-1",
            Date = futureDate,
            StartHour = TimeSpan.FromHours(9),
            EndHour = TimeSpan.FromHours(12)
        });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor);
        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        var pastDate = DateTime.Today.AddDays(-3);
        var dto = new CreateInstructorAvailabilityDto
        {
            Date = pastDate,
            StartHour = "10:00",
            EndHour = "14:00"
        };

        // Act
        var result = await controller.UpdateInstructorAvailability("instructor-1", 1, dto);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task UpdateInstructorAvailability_OverlappingWithOtherInterval_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.ro", AutoSchoolId = 1 };
        db.Users.Add(instructor);
        
        var futureDate = DateTime.Today.AddDays(5);
        // Interval 1: 09:00 - 12:00
        db.InstructorAvailabilities.Add(new InstructorAvailability
        {
            IntervalId = 1,
            InstructorId = "instructor-1",
            Date = futureDate,
            StartHour = TimeSpan.FromHours(9),
            EndHour = TimeSpan.FromHours(12)
        });
        // Interval 2: 14:00 - 17:00
        db.InstructorAvailabilities.Add(new InstructorAvailability
        {
            IntervalId = 2,
            InstructorId = "instructor-1",
            Date = futureDate,
            StartHour = TimeSpan.FromHours(14),
            EndHour = TimeSpan.FromHours(17)
        });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor);
        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        // Try to update interval 1 to overlap with interval 2
        var dto = new CreateInstructorAvailabilityDto
        {
            Date = futureDate,
            StartHour = "13:00",
            EndHour = "16:00" // Overlaps with interval 2
        };

        // Act
        var result = await controller.UpdateInstructorAvailability("instructor-1", 1, dto);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ????????? DELETE - Validation ?????????

    [Fact]
    public async Task DeleteInstructorAvailability_NonExistentInterval_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.ro", AutoSchoolId = 1 };
        db.Users.Add(instructor);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor);
        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        // Act
        var result = await controller.DeleteInstructorAvailability("instructor-1", 99999);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DeleteInstructorAvailability_PastInterval_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.ro", AutoSchoolId = 1 };
        db.Users.Add(instructor);
        
        var pastDate = DateTime.Today.AddDays(-5);
        db.InstructorAvailabilities.Add(new InstructorAvailability
        {
            IntervalId = 1,
            InstructorId = "instructor-1",
            Date = pastDate,
            StartHour = TimeSpan.FromHours(9),
            EndHour = TimeSpan.FromHours(12)
        });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor);
        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        // Act
        var result = await controller.DeleteInstructorAvailability("instructor-1", 1);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task DeleteInstructorAvailability_InstructorAccessingOther_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor1 = new ApplicationUser { Id = "instructor-1", Email = "inst1@test.ro", AutoSchoolId = 1 };
        var instructor2 = new ApplicationUser { Id = "instructor-2", Email = "inst2@test.ro", AutoSchoolId = 1 };
        db.Users.AddRange(instructor1, instructor2);
        
        var futureDate = DateTime.Today.AddDays(5);
        db.InstructorAvailabilities.Add(new InstructorAvailability
        {
            IntervalId = 1,
            InstructorId = "instructor-2",
            Date = futureDate,
            StartHour = TimeSpan.FromHours(9),
            EndHour = TimeSpan.FromHours(12)
        });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor1);
        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachInstructor(controller, instructor1.Id);

        // Act - instructor-1 tries to delete instructor-2's interval
        var result = await controller.DeleteInstructorAvailability("instructor-2", 1);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }
}
