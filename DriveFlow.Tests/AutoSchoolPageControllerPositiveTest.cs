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
/// Positive-path unit tests for <see cref="AutoSchoolPageController"/>:
/// Public endpoints for landing page data.
/// EF Core runs in-memory; UserManager and RoleManager are mocked.
/// </summary>
public sealed class AutoSchoolPageControllerPositiveTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"AutoSchoolPage_Pos_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Mock<UserManager<ApplicationUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static Mock<RoleManager<IdentityRole>> MockRoleManager(IdentityRole? studentRole = null, IdentityRole? instructorRole = null)
    {
        var store = new Mock<IRoleStore<IdentityRole>>();
        var mgr = new Mock<RoleManager<IdentityRole>>(
            store.Object,
            Array.Empty<IRoleValidator<IdentityRole>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new Mock<ILogger<RoleManager<IdentityRole>>>().Object);

        mgr.Setup(r => r.FindByNameAsync("Student"))
           .ReturnsAsync(studentRole);
        mgr.Setup(r => r.FindByNameAsync("Instructor"))
           .ReturnsAsync(instructorRole);

        return mgr;
    }

    // ????????? GET /api/schoolspage/schools ?????????

    [Fact]
    public async Task GetAutoSchoolsForLanding_ReturnsActiveAndDemoSchools()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        db.AutoSchools.AddRange(
            new AutoSchool { AutoSchoolId = 1, Name = "ActiveSchool", Status = AutoSchoolStatus.Active, Description = "Test active" },
            new AutoSchool { AutoSchoolId = 2, Name = "DemoSchool", Status = AutoSchoolStatus.Demo, Description = "Test demo" }
        );
        await db.SaveChangesAsync();

        var controller = new AutoSchoolPageController(db, MockUserManager().Object, MockRoleManager().Object);

        // Act
        var result = await controller.GetAutoSchoolsForLanding();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var schools = okResult.Value.Should().BeAssignableTo<List<AutoSchoolLandingDto>>().Subject;
        schools.Should().HaveCount(2);
        schools.Should().Contain(s => s.Name == "ActiveSchool");
        schools.Should().Contain(s => s.Name == "DemoSchool");
    }

    [Fact]
    public async Task GetAutoSchoolsForLanding_NoSchools_ReturnsEmptyList()
    {
        // Arrange
        await using var db = CreateInMemoryDb();
        var controller = new AutoSchoolPageController(db, MockUserManager().Object, MockRoleManager().Object);

        // Act
        var result = await controller.GetAutoSchoolsForLanding();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var schools = okResult.Value.Should().BeAssignableTo<List<AutoSchoolLandingDto>>().Subject;
        schools.Should().BeEmpty();
    }

    // ????????? GET /api/schoolspage/schools/{schoolId} ?????????

    [Fact]
    public async Task GetAutoSchoolDetails_ExistingSchool_ReturnsCompleteDetails()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        // Create roles
        var studentRole = new IdentityRole("Student") { Id = "student-role-id", NormalizedName = "STUDENT" };
        var instructorRole = new IdentityRole("Instructor") { Id = "instructor-role-id", NormalizedName = "INSTRUCTOR" };
        db.Roles.AddRange(studentRole, instructorRole);

        // Create county and city
        db.Counties.Add(new County { CountyId = 1, Name = "Cluj", Abbreviation = "CJ" });
        db.Cities.Add(new City { CityId = 1, Name = "Cluj-Napoca", CountyId = 1 });
        db.Addresses.Add(new Address { AddressId = 1, StreetName = "Main St", AddressNumber = "10", CityId = 1 });

        // Create license
        db.Licenses.Add(new DriveFlow_CRM_API.Models.License { LicenseId = 1, Type = "B" });

        // Create school
        db.AutoSchools.Add(new AutoSchool
        {
            AutoSchoolId = 1,
            Name = "DriveFlow",
            Description = "Best school",
            WebSite = "https://driveflow.ro",
            PhoneNumber = "0721000000",
            Email = "contact@driveflow.ro",
            Status = AutoSchoolStatus.Active,
            AddressId = 1
        });

        // Create teaching category
        db.TeachingCategories.Add(new TeachingCategory
        {
            TeachingCategoryId = 1,
            Code = "B",
            LicenseId = 1,
            SessionDuration = 60,
            MinDrivingLessonsReq = 30,
            AutoSchoolId = 1
        });

        // Create vehicle
        db.Vehicles.Add(new Vehicle
        {
            VehicleId = 1,
            LicensePlateNumber = "CJ-01-ABC",
            TransmissionType = TransmissionType.MANUAL,
            Color = "Red",
            LicenseId = 1,
            AutoSchoolId = 1
        });

        // Create users
        db.Users.Add(new ApplicationUser { Id = "student1", AutoSchoolId = 1 });
        db.Users.Add(new ApplicationUser { Id = "instructor1", AutoSchoolId = 1 });
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = "student1", RoleId = "student-role-id" });
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = "instructor1", RoleId = "instructor-role-id" });

        await db.SaveChangesAsync();

        var roleMgr = MockRoleManager(studentRole, instructorRole);
        var controller = new AutoSchoolPageController(db, MockUserManager().Object, roleMgr.Object);

        // Act
        var result = await controller.GetAutoSchoolDetails(1);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var details = okResult.Value.Should().BeOfType<AutoSchoolDetailsDto>().Subject;
        details.Name.Should().Be("DriveFlow");
        details.Email.Should().Be("contact@driveflow.ro");
        details.StudentCount.Should().Be(1);
        details.InstructorCount.Should().Be(1);
        details.Vehicles.Should().HaveCount(1);
        details.TeachingCategories.Should().HaveCount(1);
    }
}
