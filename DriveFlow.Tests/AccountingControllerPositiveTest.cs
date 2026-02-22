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
/// Positive-path unit tests for <see cref="AccountingController"/>:
/// Invoice generation.
/// EF Core runs in-memory; UserManager and IHttpClientFactory are mocked.
/// </summary>
public sealed class AccountingControllerPositiveTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Accounting_Pos_{Guid.NewGuid()}")
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

        additionalSetup?.Invoke(mgr);

        return mgr;
    }

    private static Mock<IHttpClientFactory> MockHttpClientFactory(HttpResponseMessage response)
    {
        var messageHandler = new Mock<HttpMessageHandler>();
        messageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var client = new HttpClient(messageHandler.Object);
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

    // ????????? GET /api/accounting/file/{fileId}/invoice ?????????

    [Fact]
    public async Task GetInvoice_StudentOwnsFile_FullyPaid_ReturnsFile()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var student = new ApplicationUser { Id = "student-1", FirstName = "Ion", LastName = "Pop", Email = "ion@test.ro", Cnp = "1234567890123", AutoSchoolId = 1 };
        db.Users.Add(student);

        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "DriveFlow", Email = "contact@driveflow.ro" });
        db.Licenses.Add(new License { LicenseId = 1, Type = "B" });
        db.TeachingCategories.Add(new TeachingCategory
        {
            TeachingCategoryId = 1,
            Code = "B",
            LicenseId = 1,
            MinDrivingLessonsReq = 30,
            ScholarshipPrice = 2000,
            SessionCost = 100,
            SessionDuration = 60,
            AutoSchoolId = 1
        });

        db.Files.Add(new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = "student-1",
            TeachingCategoryId = 1,
            Status = FileStatus.APPROVED,
            ScholarshipStartDate = DateTime.Today.AddMonths(-1)
        });

        db.Payments.Add(new Payment
        {
            PaymentId = 1,
            FileId = 1,
            SessionsPayed = 30, // Meets minimum
            ScholarshipBasePayment = true
        });
        await db.SaveChangesAsync();

        // Setup mocks
        var userMgr = MockUserManager(student, m =>
        {
            m.Setup(x => x.IsInRoleAsync(student, "Student")).ReturnsAsync(true);
            m.Setup(x => x.IsInRoleAsync(student, "SchoolAdmin")).ReturnsAsync(false);
        });

        var pdfContent = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // PDF magic bytes
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(pdfContent)
        };
        var httpFactory = MockHttpClientFactory(httpResponse);

        // Set environment variable for invoice service
        Environment.SetEnvironmentVariable("INVOICE_SERVICE_URL", "http://mock-invoice-service/generate");

        var config = MockConfiguration("http://mock-invoice-service/generate");

        var controller = new AccountingController(db, userMgr.Object, httpFactory.Object, config.Object);
        AttachStudent(controller, student.Id);

        // Act
        var result = await controller.GetInvoice(1);

        // Assert
        result.Should().BeOfType<FileStreamResult>();
        var fileResult = result as FileStreamResult;
        fileResult!.ContentType.Should().Be("application/pdf");

        // Cleanup
        Environment.SetEnvironmentVariable("INVOICE_SERVICE_URL", null);
    }

    [Fact]
    public async Task GetInvoice_SchoolAdminSameSchool_FullyPaid_ReturnsFile()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        var student = new ApplicationUser { Id = "student-1", FirstName = "Ion", LastName = "Pop", Email = "ion@test.ro", Cnp = "1234567890123", AutoSchoolId = 1 };
        db.Users.AddRange(admin, student);

        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "DriveFlow", Email = "contact@driveflow.ro" });
        db.Licenses.Add(new License { LicenseId = 1, Type = "B" });
        db.TeachingCategories.Add(new TeachingCategory
        {
            TeachingCategoryId = 1,
            Code = "B",
            LicenseId = 1,
            MinDrivingLessonsReq = 30,
            ScholarshipPrice = 2000,
            SessionCost = 100,
            SessionDuration = 60,
            AutoSchoolId = 1
        });

        db.Files.Add(new DriveFlow_CRM_API.Models.File
        {
            FileId = 1,
            StudentId = "student-1",
            TeachingCategoryId = 1,
            Status = FileStatus.APPROVED,
            ScholarshipStartDate = DateTime.Today.AddMonths(-1)
        });

        db.Payments.Add(new Payment
        {
            PaymentId = 1,
            FileId = 1,
            SessionsPayed = 30,
            ScholarshipBasePayment = true
        });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(admin, m =>
        {
            m.Setup(x => x.IsInRoleAsync(admin, "Student")).ReturnsAsync(false);
            m.Setup(x => x.IsInRoleAsync(admin, "SchoolAdmin")).ReturnsAsync(true);
        });

        var pdfContent = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(pdfContent)
        };
        var httpFactory = MockHttpClientFactory(httpResponse);

        Environment.SetEnvironmentVariable("INVOICE_SERVICE_URL", "http://mock-invoice-service/generate");

        var config = MockConfiguration("http://mock-invoice-service/generate");

        var controller = new AccountingController(db, userMgr.Object, httpFactory.Object, config.Object);
        AttachSchoolAdmin(controller, admin.Id);

        // Act
        var result = await controller.GetInvoice(1);

        // Assert
        result.Should().BeOfType<FileStreamResult>();

        // Cleanup
        Environment.SetEnvironmentVariable("INVOICE_SERVICE_URL", null);
    }
}
