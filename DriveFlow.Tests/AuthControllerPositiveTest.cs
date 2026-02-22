using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

using DriveFlow_CRM_API.Authentication;
using DriveFlow_CRM_API.Authentication.Tokens;
using DriveFlow_CRM_API.Authentication.Tokens.Handlers;
using DriveFlow_CRM_API.Models;

namespace DriveFlow.Tests.Controllers;

/// <summary>
/// Positive-path unit tests for <see cref="AuthController"/>:
/// Login and Refresh token scenarios.
/// UserManager and SignInManager are mocked; token services are mocked for isolation.
/// </summary>
public sealed class AuthControllerPositiveTest
{
    // ??????? helpers ???????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Auth_Pos_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static IConfiguration CreateConfiguration()
    {
        var configData = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "ThisIsAVeryLongSecretKeyForTestingPurposesOnly123456",
            ["Jwt:Issuer"] = "DriveFlowTest",
            ["Jwt:Audience"] = "DriveFlowTestAudience",
            ["Jwt:AccessExpiresMinutes"] = "60",
            ["Jwt:RefreshExpiresDays"] = "7"
        };
        return new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    private static Mock<UserManager<ApplicationUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static Mock<SignInManager<ApplicationUser>> MockSignInManager(
        Mock<UserManager<ApplicationUser>> userManager)
    {
        var contextAccessor = new Mock<IHttpContextAccessor>();
        var claimsFactory = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
        return new Mock<SignInManager<ApplicationUser>>(
            userManager.Object,
            contextAccessor.Object,
            claimsFactory.Object,
            null!, null!, null!, null!);
    }

    private static Mock<ILogger<AuthController>> MockLogger()
    {
        return new Mock<ILogger<AuthController>>();
    }

    private static string GenerateValidJwt(string userId, IConfiguration cfg, int expireDays = 7)
    {
        var secret = cfg["Jwt:Key"]!;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("typ", "refresh")
        };

        var token = new JwtSecurityToken(
            issuer: cfg["Jwt:Issuer"],
            audience: cfg["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(expireDays),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static void AttachHttpContext(ControllerBase controller)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    /// <summary>
    /// Creates an IServiceProvider that properly handles GetRequiredService calls
    /// for TokenGeneratorFactory.
    /// </summary>
    private static IServiceProvider CreateServiceProvider(IConfiguration cfg)
    {
        var services = new ServiceCollection();
        services.AddSingleton(cfg);
        // Register empty collection of claim handlers (GetServices will return empty enumerable)
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Helper to get property value from anonymous object using reflection.
    /// </summary>
    private static T GetPropertyValue<T>(object obj, string propertyName)
    {
        var property = obj.GetType().GetProperty(propertyName);
        property.Should().NotBeNull($"Property '{propertyName}' should exist on the object");
        return (T)property!.GetValue(obj)!;
    }

    // ??????? POST /api/auth (Login) ???????

    [Fact]
    public async Task LoginAsync_ValidCredentials_Returns200WithTokens()
    {
        // Arrange
        var cfg = CreateConfiguration();
        var user = new ApplicationUser
        {
            Id = "user-1",
            Email = "test@driveflow.ro",
            UserName = "test@driveflow.ro",
            FirstName = "Ion",
            LastName = "Popescu",
            PhoneNumber = "0700000000",
            AutoSchoolId = 1
        };

        var userMgr = MockUserManager();
        userMgr.Setup(x => x.FindByEmailAsync("test@driveflow.ro"))
               .ReturnsAsync(user);
        userMgr.Setup(x => x.GetRolesAsync(user))
               .ReturnsAsync(new List<string> { "Student" });
        userMgr.Setup(x => x.IsLockedOutAsync(user))
               .ReturnsAsync(false);

        var signInMgr = MockSignInManager(userMgr);
        signInMgr.Setup(x => x.CheckPasswordSignInAsync(user, "Password123!", true))
                 .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        var tokenGen = new Mock<ITokenGenerator>();
        tokenGen.Setup(x => x.GenerateToken(user, It.IsAny<IList<string>>(), 1))
                .Returns("mock-access-token");

        var refreshSvc = new Mock<IRefreshTokenService>();
        refreshSvc.Setup(x => x.StoreAsync(user, It.IsAny<string>(), It.IsAny<DateTime>()))
                  .Returns(Task.CompletedTask);

        var logger = MockLogger();

        // Use a real service provider that properly supports GetRequiredService
        var serviceProvider = CreateServiceProvider(cfg);
        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };

        var controller = new AuthController(userMgr.Object, signInMgr.Object, tokenGen.Object, refreshSvc.Object, cfg, logger.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var dto = new LoginDto("test@driveflow.ro", "Password123!");

        // Act
        var result = await controller.LoginAsync(dto);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.Value.Should().NotBeNull();

        // Verify token was generated
        tokenGen.Verify(x => x.GenerateToken(user, It.IsAny<IList<string>>(), 1), Times.Once);
        refreshSvc.Verify(x => x.StoreAsync(user, It.IsAny<string>(), It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsCorrectUserMetadata()
    {
        // Arrange
        var cfg = CreateConfiguration();
        var user = new ApplicationUser
        {
            Id = "user-2",
            Email = "admin@school.ro",
            UserName = "admin@school.ro",
            FirstName = "Maria",
            LastName = "Ionescu",
            PhoneNumber = "0711111111",
            AutoSchoolId = 5
        };

        var userMgr = MockUserManager();
        userMgr.Setup(x => x.FindByEmailAsync("admin@school.ro"))
               .ReturnsAsync(user);
        userMgr.Setup(x => x.GetRolesAsync(user))
               .ReturnsAsync(new List<string> { "SchoolAdmin" });
        userMgr.Setup(x => x.IsLockedOutAsync(user))
               .ReturnsAsync(false);

        var signInMgr = MockSignInManager(userMgr);
        signInMgr.Setup(x => x.CheckPasswordSignInAsync(user, "AdminPass!", true))
                 .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        var tokenGen = new Mock<ITokenGenerator>();
        tokenGen.Setup(x => x.GenerateToken(user, It.IsAny<IList<string>>(), 5))
                .Returns("access-token-for-admin");

        var refreshSvc = new Mock<IRefreshTokenService>();
        refreshSvc.Setup(x => x.StoreAsync(user, It.IsAny<string>(), It.IsAny<DateTime>()))
                  .Returns(Task.CompletedTask);

        var logger = MockLogger();

        // Use a real service provider that properly supports GetRequiredService
        var serviceProvider = CreateServiceProvider(cfg);
        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };

        var controller = new AuthController(userMgr.Object, signInMgr.Object, tokenGen.Object, refreshSvc.Object, cfg, logger.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var dto = new LoginDto("admin@school.ro", "AdminPass!");

        // Act
        var result = await controller.LoginAsync(dto);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value!;

        // Verify user metadata in response using reflection helper
        GetPropertyValue<string>(response, "userId").Should().Be("user-2");
        GetPropertyValue<string>(response, "userType").Should().Be("SchoolAdmin");
        GetPropertyValue<string>(response, "userEmail").Should().Be("admin@school.ro");
        GetPropertyValue<string>(response, "firstName").Should().Be("Maria");
        GetPropertyValue<string>(response, "lastName").Should().Be("Ionescu");
        GetPropertyValue<int>(response, "schoolId").Should().Be(5);
    }

    // ??????? POST /api/auth/refresh ???????

    [Fact]
    public async Task RefreshAsync_ValidToken_Returns200WithNewAccessToken()
    {
        // Arrange
        var cfg = CreateConfiguration();
        var user = new ApplicationUser
        {
            Id = "user-3",
            Email = "refresh@test.ro",
            UserName = "refresh@test.ro",
            AutoSchoolId = 2
        };

        var refreshToken = GenerateValidJwt(user.Id, cfg);

        var userMgr = MockUserManager();
        userMgr.Setup(x => x.FindByIdAsync(user.Id))
               .ReturnsAsync(user);
        userMgr.Setup(x => x.GetRolesAsync(user))
               .ReturnsAsync(new List<string> { "Instructor" });

        var signInMgr = MockSignInManager(userMgr);

        var tokenGen = new Mock<ITokenGenerator>();
        tokenGen.Setup(x => x.GenerateToken(user, It.IsAny<IList<string>>(), 2))
                .Returns("new-access-token");

        var refreshSvc = new Mock<IRefreshTokenService>();
        refreshSvc.Setup(x => x.ValidateAsync(user, refreshToken))
                  .ReturnsAsync(true);

        var logger = MockLogger();

        var controller = new AuthController(userMgr.Object, signInMgr.Object, tokenGen.Object, refreshSvc.Object, cfg, logger.Object);
        AttachHttpContext(controller);

        var dto = new RefreshDto(refreshToken);

        // Act
        var result = await controller.RefreshAsync(dto);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value!;
        GetPropertyValue<string>(response, "token").Should().Be("new-access-token");

        // Verify interactions
        refreshSvc.Verify(x => x.ValidateAsync(user, refreshToken), Times.Once);
        tokenGen.Verify(x => x.GenerateToken(user, It.IsAny<IList<string>>(), 2), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_UserWithNoRoles_DefaultsToStudent()
    {
        // Arrange
        var cfg = CreateConfiguration();
        var user = new ApplicationUser
        {
            Id = "user-no-role",
            Email = "norole@test.ro",
            UserName = "norole@test.ro",
            FirstName = "NoRole",
            LastName = "User",
            AutoSchoolId = null
        };

        var userMgr = MockUserManager();
        userMgr.Setup(x => x.FindByEmailAsync("norole@test.ro"))
               .ReturnsAsync(user);
        userMgr.Setup(x => x.GetRolesAsync(user))
               .ReturnsAsync(new List<string>()); // Empty roles
        userMgr.Setup(x => x.IsLockedOutAsync(user))
               .ReturnsAsync(false);

        var signInMgr = MockSignInManager(userMgr);
        signInMgr.Setup(x => x.CheckPasswordSignInAsync(user, "Pass123!", true))
                 .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

        var tokenGen = new Mock<ITokenGenerator>();
        tokenGen.Setup(x => x.GenerateToken(user, It.IsAny<IList<string>>(), 0))
                .Returns("token-for-norole");

        var refreshSvc = new Mock<IRefreshTokenService>();
        refreshSvc.Setup(x => x.StoreAsync(user, It.IsAny<string>(), It.IsAny<DateTime>()))
                  .Returns(Task.CompletedTask);

        var logger = MockLogger();

        // Use a real service provider that properly supports GetRequiredService
        var serviceProvider = CreateServiceProvider(cfg);
        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };

        var controller = new AuthController(userMgr.Object, signInMgr.Object, tokenGen.Object, refreshSvc.Object, cfg, logger.Object);
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var dto = new LoginDto("norole@test.ro", "Pass123!");

        // Act
        var result = await controller.LoginAsync(dto);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value!;
        GetPropertyValue<string>(response, "userType").Should().Be("Student"); // Defaults to Student
    }
}
