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
/// Positive-path unit tests for <see cref="InstructorAvailabilityController"/>:
/// GET, POST, PUT, DELETE availability intervals.
/// EF Core runs in-memory; UserManager is mocked.
/// </summary>
public sealed class InstructorAvailabilityControllerPositiveTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"InstrAvail_Pos_{Guid.NewGuid()}")
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

    // ????????? GET /api/instructor-availability/{instructorId} ?????????

    [Fact]
    public async Task GetInstructorAvailability_AsInstructor_Returns200WithIntervals()
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

        // Act
        var result = await controller.GetInstructorAvailability("instructor-1");

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var intervals = okResult.Value.Should().BeAssignableTo<IEnumerable<InstructorAvailabilityDto>>().Subject.ToList();
        intervals.Should().HaveCount(1);
        intervals[0].StartHour.Should().Be("09:00");
        intervals[0].EndHour.Should().Be("12:00");
    }

    [Fact]
    public async Task GetInstructorAvailability_AsSchoolAdmin_Returns200ForSameSchool()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.ro", AutoSchoolId = 1 };
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        db.Users.AddRange(instructor, admin);
        
        var futureDate = DateTime.Today.AddDays(3);
        db.InstructorAvailabilities.Add(new InstructorAvailability
        {
            IntervalId = 1,
            InstructorId = "instructor-1",
            Date = futureDate,
            StartHour = TimeSpan.FromHours(14),
            EndHour = TimeSpan.FromHours(18)
        });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager();
        userMgr.Setup(x => x.FindByIdAsync("admin-1")).ReturnsAsync(admin);
        userMgr.Setup(x => x.FindByIdAsync("instructor-1")).ReturnsAsync(instructor);

        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetInstructorAvailability("instructor-1");

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var intervals = okResult.Value.Should().BeAssignableTo<IEnumerable<InstructorAvailabilityDto>>().Subject.ToList();
        intervals.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetInstructorAvailability_EmptyList_Returns200WithEmptyArray()
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
        var result = await controller.GetInstructorAvailability("instructor-1");

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var intervals = okResult.Value.Should().BeAssignableTo<IEnumerable<InstructorAvailabilityDto>>().Subject.ToList();
        intervals.Should().BeEmpty();
    }

    // ????????? POST /api/instructor-availability/{instructorId} ?????????

    [Fact]
    public async Task CreateInstructorAvailability_ValidData_Returns201AndPersistsInterval()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.ro", AutoSchoolId = 1 };
        db.Users.Add(instructor);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor);
        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        var futureDate = DateTime.Today.AddDays(7);
        var dto = new CreateInstructorAvailabilityDto
        {
            Date = futureDate,
            StartHour = "10:00",
            EndHour = "13:00"
        };

        // Act
        var result = await controller.CreateInstructorAvailability("instructor-1", dto);

        // Assert
        var createdResult = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var createdInterval = createdResult.Value.Should().BeOfType<InstructorAvailabilityDto>().Subject;
        createdInterval.StartHour.Should().Be("10:00");
        createdInterval.EndHour.Should().Be("13:00");

        // Verify DB persistence
        db.InstructorAvailabilities.Should().ContainSingle(a => 
            a.InstructorId == "instructor-1" && 
            a.Date.Date == futureDate.Date);
    }

    [Fact]
    public async Task CreateInstructorAvailability_AsSchoolAdmin_Returns201()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.ro", AutoSchoolId = 1 };
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        db.Users.AddRange(instructor, admin);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager();
        userMgr.Setup(x => x.FindByIdAsync("admin-1")).ReturnsAsync(admin);
        userMgr.Setup(x => x.FindByIdAsync("instructor-1")).ReturnsAsync(instructor);

        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        var futureDate = DateTime.Today.AddDays(5);
        var dto = new CreateInstructorAvailabilityDto
        {
            Date = futureDate,
            StartHour = "08:00",
            EndHour = "12:00"
        };

        // Act
        var result = await controller.CreateInstructorAvailability("instructor-1", dto);

        // Assert
        result.Result.Should().BeOfType<CreatedAtActionResult>();
        db.InstructorAvailabilities.Should().ContainSingle();
    }

    // ????????? PUT /api/instructor-availability/{instructorId}/{intervalId} ?????????

    [Fact]
    public async Task UpdateInstructorAvailability_ValidData_Returns200AndUpdatesInterval()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.ro", AutoSchoolId = 1 };
        db.Users.Add(instructor);
        
        var futureDate = DateTime.Today.AddDays(10);
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

        var newDate = DateTime.Today.AddDays(15);
        var dto = new CreateInstructorAvailabilityDto
        {
            Date = newDate,
            StartHour = "14:00",
            EndHour = "18:00"
        };

        // Act
        var result = await controller.UpdateInstructorAvailability("instructor-1", 1, dto);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var updatedInterval = okResult.Value.Should().BeOfType<InstructorAvailabilityDto>().Subject;
        updatedInterval.StartHour.Should().Be("14:00");
        updatedInterval.EndHour.Should().Be("18:00");

        // Verify DB update
        var interval = await db.InstructorAvailabilities.FindAsync(1);
        interval!.StartHour.Should().Be(TimeSpan.FromHours(14));
        interval.EndHour.Should().Be(TimeSpan.FromHours(18));
    }

    // ????????? DELETE /api/instructor-availability/{instructorId}/{intervalId} ?????????

    [Fact]
    public async Task DeleteInstructorAvailability_ExistingInterval_Returns200AndRemovesEntity()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.ro", AutoSchoolId = 1 };
        db.Users.Add(instructor);
        
        var futureDate = DateTime.Today.AddDays(5);
        db.InstructorAvailabilities.Add(new InstructorAvailability
        {
            IntervalId = 100,
            InstructorId = "instructor-1",
            Date = futureDate,
            StartHour = TimeSpan.FromHours(9),
            EndHour = TimeSpan.FromHours(12)
        });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(instructor);
        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachInstructor(controller, instructor.Id);

        // Act
        var result = await controller.DeleteInstructorAvailability("instructor-1", 100);

        // Assert
        result.Should().BeOfType<OkObjectResult>();

        // Verify DB removal
        db.InstructorAvailabilities.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteInstructorAvailability_AsSchoolAdmin_Returns200()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var instructor = new ApplicationUser { Id = "instructor-1", Email = "inst@test.ro", AutoSchoolId = 1 };
        var admin = new ApplicationUser { Id = "admin-1", Email = "admin@test.ro", AutoSchoolId = 1 };
        db.Users.AddRange(instructor, admin);
        
        var futureDate = DateTime.Today.AddDays(3);
        db.InstructorAvailabilities.Add(new InstructorAvailability
        {
            IntervalId = 200,
            InstructorId = "instructor-1",
            Date = futureDate,
            StartHour = TimeSpan.FromHours(10),
            EndHour = TimeSpan.FromHours(14)
        });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager();
        userMgr.Setup(x => x.FindByIdAsync("admin-1")).ReturnsAsync(admin);
        userMgr.Setup(x => x.FindByIdAsync("instructor-1")).ReturnsAsync(instructor);

        var controller = new InstructorAvailabilityController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.DeleteInstructorAvailability("instructor-1", 200);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        db.InstructorAvailabilities.Should().BeEmpty();
    }
}
