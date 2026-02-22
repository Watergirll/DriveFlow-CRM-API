using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Moq.Protected;
using System.Net;
using Xunit;

using DriveFlow_CRM_API.Controllers;
using DriveFlow_CRM_API.Models;
using License = DriveFlow_CRM_API.Models.License;

namespace DriveFlow.Tests.Controllers;

/// <summary>
/// Negative-path unit tests for <see cref="AccountingController"/>:
/// 400, 403, 404 scenarios.
/// EF Core runs in-memory; UserManager and IHttpClientFactory are mocked.
/// </summary>
public sealed class AccountingControllerNegativeTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Accounting_Neg_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Mock<UserManager<ApplicationUser>> MockUserManager(
        ApplicationUser? user = null,
        Action<Mock<UserManager<ApplicationUser>>>? additionalSetup = null)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var mgr = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        if (user != null)
        {
            mgr.Setup(x => x.GetUserId(It.IsAny<ClaimsPrincipal>()))
               .Returns(user.Id);
            mgr.Setup(x => x.FindByIdAsync(user.Id))
               .ReturnsAsync(user);
        }
        else
        {
            mgr.Setup(x => x.GetUserId(It.IsAny<ClaimsPrincipal>()))
               .Returns((string?)null);
        }

        additionalSetup?.Invoke(mgr);

        return mgr;
    }

    private static Mock<IHttpClientFactory> MockHttpClientFactory()
    {
        var client = new HttpClient();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return factory;
    }

    private static Mock<IConfiguration> MockConfiguration(string? invoiceUrl = null)
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["InvoiceService:Url"]).Returns(invoiceUrl);
        return config;
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

    // ????????? GetInvoice - Negative ?????????

    [Fact]
    public async Task GetInvoice_NoAuthentication_Returns401()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var userMgr = MockUserManager(null);
        var httpFactory = MockHttpClientFactory();
        var config = MockConfiguration();

        var controller = new AccountingController(db, userMgr.Object, httpFactory.Object, config.Object);
        AttachNoUser(controller);

        // Act
        var result = await controller.GetInvoice(1);

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task GetInvoice_UserNotFound_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var userMgr = MockUserManager(null, m =>
        {
            m.Setup(x => x.GetUserId(It.IsAny<ClaimsPrincipal>())).Returns("user-1");
            m.Setup(x => x.FindByIdAsync("user-1")).ReturnsAsync((ApplicationUser?)null);
        });
        var httpFactory = MockHttpClientFactory();
        var config = MockConfiguration();

        var controller = new AccountingController(db, userMgr.Object, httpFactory.Object, config.Object);
        AttachStudent(controller, "user-1");

        // Act
        var result = await controller.GetInvoice(1);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetInvoice_FileNotFound_Returns404()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        db.Users.Add(student);
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(student, m =>
        {
            m.Setup(x => x.IsInRoleAsync(student, "Student")).ReturnsAsync(true);
            m.Setup(x => x.IsInRoleAsync(student, "SchoolAdmin")).ReturnsAsync(false);
        });
        var httpFactory = MockHttpClientFactory();
        var config = MockConfiguration();

        var controller = new AccountingController(db, userMgr.Object, httpFactory.Object, config.Object);
        AttachStudent(controller, student.Id);

        // Act
        var result = await controller.GetInvoice(99999);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetInvoice_StudentNotOwner_Returns403()
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

        var userMgr = MockUserManager(student1, m =>
        {
            m.Setup(x => x.IsInRoleAsync(student1, "Student")).ReturnsAsync(true);
            m.Setup(x => x.IsInRoleAsync(student1, "SchoolAdmin")).ReturnsAsync(false);
        });
        var httpFactory = MockHttpClientFactory();
        var config = MockConfiguration();

        var controller = new AccountingController(db, userMgr.Object, httpFactory.Object, config.Object);
        AttachStudent(controller, student1.Id);

        // Act - student-1 tries to access student-2's file
        var result = await controller.GetInvoice(1);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetInvoice_PaymentNotFound_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        db.Users.Add(student);

        db.Files.Add(new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = "student-1",
            Status = FileStatus.APPROVED
        });
        // No payment added
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(student, m =>
        {
            m.Setup(x => x.IsInRoleAsync(student, "Student")).ReturnsAsync(true);
            m.Setup(x => x.IsInRoleAsync(student, "SchoolAdmin")).ReturnsAsync(false);
        });
        var httpFactory = MockHttpClientFactory();
        var config = MockConfiguration();

        var controller = new AccountingController(db, userMgr.Object, httpFactory.Object, config.Object);
        AttachStudent(controller, student.Id);

        // Act
        var result = await controller.GetInvoice(1);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetInvoice_TeachingCategoryNotFound_Returns400()
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

        db.Payments.Add(new Payment
        {
            PaymentId = 1,
            FileId = 1,
            SessionsPayed = 30,
            ScholarshipBasePayment = true
        });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(student, m =>
        {
            m.Setup(x => x.IsInRoleAsync(student, "Student")).ReturnsAsync(true);
            m.Setup(x => x.IsInRoleAsync(student, "SchoolAdmin")).ReturnsAsync(false);
        });
        var httpFactory = MockHttpClientFactory();
        var config = MockConfiguration();

        var controller = new AccountingController(db, userMgr.Object, httpFactory.Object, config.Object);
        AttachStudent(controller, student.Id);

        // Act
        var result = await controller.GetInvoice(1);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetInvoice_TuitionNotFullyPaid_Returns400()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        db.Users.Add(student);

        db.TeachingCategories.Add(new TeachingCategory
        {
            TeachingCategoryId = 1,
            Code = "B",
            MinDrivingLessonsReq = 30,
            AutoSchoolId = 1
        });

        db.Files.Add(new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = "student-1",
            TeachingCategoryId = 1,
            Status = FileStatus.APPROVED
        });

        db.Payments.Add(new Payment
        {
            PaymentId = 1,
            FileId = 1,
            SessionsPayed = 10, // Less than minimum
            ScholarshipBasePayment = false // Not fully paid
        });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(student, m =>
        {
            m.Setup(x => x.IsInRoleAsync(student, "Student")).ReturnsAsync(true);
            m.Setup(x => x.IsInRoleAsync(student, "SchoolAdmin")).ReturnsAsync(false);
        });
        var httpFactory = MockHttpClientFactory();
        var config = MockConfiguration();

        var controller = new AccountingController(db, userMgr.Object, httpFactory.Object, config.Object);
        AttachStudent(controller, student.Id);

        // Act
        var result = await controller.GetInvoice(1);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetInvoice_SchoolAdminDifferentSchool_Returns403()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 2 }; // Different school
        var student = new ApplicationUser { Id = "student-1", AutoSchoolId = 1 };
        db.Users.AddRange(admin, student);

        db.Files.Add(new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = "student-1",
            Status = FileStatus.APPROVED
        });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin, m =>
        {
            m.Setup(x => x.IsInRoleAsync(admin, "Student")).ReturnsAsync(false);
            m.Setup(x => x.IsInRoleAsync(admin, "SchoolAdmin")).ReturnsAsync(true);
        });
        var httpFactory = MockHttpClientFactory();
        var config = MockConfiguration();

        var controller = new AccountingController(db, userMgr.Object, httpFactory.Object, config.Object);

        // Attach as SchoolAdmin
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "SchoolAdmin"),
            new Claim(ClaimTypes.NameIdentifier, admin.Id)
        }, "mock");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };

        // Act
        var result = await controller.GetInvoice(1);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }
}
