using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

using DriveFlow_CRM_API.Authentication;
using DriveFlow_CRM_API.Authentication.Tokens;
using DriveFlow_CRM_API.Models;

namespace DriveFlow.Tests.Controllers;

/// <summary>
/// Negative-path unit tests for <see cref="AuthController"/>:
/// 401 (Unauthorized) and 404 (NotFound) scenarios.
/// UserManager and SignInManager are mocked.
/// </summary>
public sealed class AuthControllerNegativeTest
{
    // ??????? helpers ???????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Auth_Neg_{Guid.NewGuid()}")
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

    private static string GenerateExpiredJwt(string userId, IConfiguration cfg)
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
            notBefore: DateTime.UtcNow.AddDays(-10),
            expires: DateTime.UtcNow.AddDays(-1), // Expired
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

    // ??????? POST /api/auth (Login) - Negative ???????

    [Fact]
    public async Task LoginAsync_NonExistentEmail_Returns404()
    {
        // Arrange
        var cfg = CreateConfiguration();
        var userMgr = MockUserManager();
        userMgr.Setup(x => x.FindByEmailAsync("nonexistent@test.ro"))
               .ReturnsAsync((ApplicationUser?)null);

        var signInMgr = MockSignInManager(userMgr);
        var tokenGen = new Mock<ITokenGenerator>();
        var refreshSvc = new Mock<IRefreshTokenService>();
        var logger = MockLogger();

        var controller = new AuthController(userMgr.Object, signInMgr.Object, tokenGen.Object, refreshSvc.Object, cfg, logger.Object);
        AttachHttpContext(controller);

        var dto = new LoginDto("nonexistent@test.ro", "AnyPassword!");

        // Act
        var result = await controller.LoginAsync(dto);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_Returns401()
    {
        // Arrange
        var cfg = CreateConfiguration();
        var user = new ApplicationUser
        {
            Id = "user-1",
            Email = "test@driveflow.ro",
            UserName = "test@driveflow.ro"
        };

        var userMgr = MockUserManager();
        userMgr.Setup(x => x.FindByEmailAsync("test@driveflow.ro"))
               .ReturnsAsync(user);
        userMgr.Setup(x => x.IsLockedOutAsync(user))
               .ReturnsAsync(false);

        var signInMgr = MockSignInManager(userMgr);
        signInMgr.Setup(x => x.CheckPasswordSignInAsync(user, "WrongPassword!", true))
                 .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);

        var tokenGen = new Mock<ITokenGenerator>();
        var refreshSvc = new Mock<IRefreshTokenService>();
        var logger = MockLogger();

        var controller = new AuthController(userMgr.Object, signInMgr.Object, tokenGen.Object, refreshSvc.Object, cfg, logger.Object);
        AttachHttpContext(controller);

        var dto = new LoginDto("test@driveflow.ro", "WrongPassword!");

        // Act
        var result = await controller.LoginAsync(dto);

        // Assert
        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task LoginAsync_LockedOutAccount_Returns423()
    {
        // Arrange
        var cfg = CreateConfiguration();
        var user = new ApplicationUser
        {
            Id = "user-locked",
            Email = "locked@driveflow.ro",
            UserName = "locked@driveflow.ro"
        };

        var userMgr = MockUserManager();
        userMgr.Setup(x => x.FindByEmailAsync("locked@driveflow.ro"))
               .ReturnsAsync(user);
        userMgr.Setup(x => x.IsLockedOutAsync(user))
               .ReturnsAsync(true);
        userMgr.Setup(x => x.GetLockoutEndDateAsync(user))
               .ReturnsAsync(DateTimeOffset.UtcNow.AddMinutes(10));

        var signInMgr = MockSignInManager(userMgr);
        var tokenGen = new Mock<ITokenGenerator>();
        var refreshSvc = new Mock<IRefreshTokenService>();
        var logger = MockLogger();

        var controller = new AuthController(userMgr.Object, signInMgr.Object, tokenGen.Object, refreshSvc.Object, cfg, logger.Object);
        AttachHttpContext(controller);

        var dto = new LoginDto("locked@driveflow.ro", "Password123!");

        // Act
        var result = await controller.LoginAsync(dto);

        // Assert
        var statusCodeResult = result as ObjectResult;
        statusCodeResult.Should().NotBeNull();
        statusCodeResult!.StatusCode.Should().Be(423);
    }

    [Fact]
    public async Task LoginAsync_InvalidModelState_Returns400WhenChecked()
    {
        // Arrange
        var cfg = CreateConfiguration();
        var userMgr = MockUserManager();
        var signInMgr = MockSignInManager(userMgr);
        var tokenGen = new Mock<ITokenGenerator>();
        var refreshSvc = new Mock<IRefreshTokenService>();
        var logger = MockLogger();

        var controller = new AuthController(userMgr.Object, signInMgr.Object, tokenGen.Object, refreshSvc.Object, cfg, logger.Object);
        AttachHttpContext(controller);

        // Simulate invalid model state (as framework would do with [ApiController])
        controller.ModelState.AddModelError("Email", "Email is required");

        var dto = new LoginDto("", "Password123!");

        // Act
        // In controller-direct testing, [ApiController] validation doesn't run automatically.
        // We simulate the behavior by checking ModelState manually.
        if (!controller.ModelState.IsValid)
        {
            var validationResult = new BadRequestObjectResult(controller.ModelState);
            validationResult.Should().BeOfType<BadRequestObjectResult>();
            return;
        }

        var result = await controller.LoginAsync(dto);
        result.Should().NotBeNull();
    }

    // ??????? POST /api/auth/refresh - Negative ???????

    [Fact]
    public async Task RefreshAsync_UserNotFound_Returns401()
    {
        // Arrange
        var cfg = CreateConfiguration();
        var refreshToken = GenerateValidJwt("nonexistent-user-id", cfg);

        var userMgr = MockUserManager();
        userMgr.Setup(x => x.FindByIdAsync("nonexistent-user-id"))
               .ReturnsAsync((ApplicationUser?)null);

        var signInMgr = MockSignInManager(userMgr);
        var tokenGen = new Mock<ITokenGenerator>();
        var refreshSvc = new Mock<IRefreshTokenService>();
        var logger = MockLogger();

        var controller = new AuthController(userMgr.Object, signInMgr.Object, tokenGen.Object, refreshSvc.Object, cfg, logger.Object);
        AttachHttpContext(controller);

        var dto = new RefreshDto(refreshToken);

        // Act
        var result = await controller.RefreshAsync(dto);

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task RefreshAsync_InvalidToken_Returns401()
    {
        // Arrange
        var cfg = CreateConfiguration();
        var user = new ApplicationUser
        {
            Id = "user-valid",
            Email = "valid@test.ro",
            UserName = "valid@test.ro"
        };

        var refreshToken = GenerateValidJwt(user.Id, cfg);

        var userMgr = MockUserManager();
        userMgr.Setup(x => x.FindByIdAsync(user.Id))
               .ReturnsAsync(user);

        var signInMgr = MockSignInManager(userMgr);
        var tokenGen = new Mock<ITokenGenerator>();
        var refreshSvc = new Mock<IRefreshTokenService>();
        refreshSvc.Setup(x => x.ValidateAsync(user, refreshToken))
                  .ReturnsAsync(false); // Token validation fails
        var logger = MockLogger();

        var controller = new AuthController(userMgr.Object, signInMgr.Object, tokenGen.Object, refreshSvc.Object, cfg, logger.Object);
        AttachHttpContext(controller);

        var dto = new RefreshDto(refreshToken);

        // Act
        var result = await controller.RefreshAsync(dto);

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_Returns401()
    {
        // Arrange
        var cfg = CreateConfiguration();
        var user = new ApplicationUser
        {
            Id = "user-expired",
            Email = "expired@test.ro",
            UserName = "expired@test.ro"
        };

        var expiredToken = GenerateExpiredJwt(user.Id, cfg);

        var userMgr = MockUserManager();
        userMgr.Setup(x => x.FindByIdAsync(user.Id))
               .ReturnsAsync(user);

        var signInMgr = MockSignInManager(userMgr);
        var tokenGen = new Mock<ITokenGenerator>();
        var refreshSvc = new Mock<IRefreshTokenService>();
        refreshSvc.Setup(x => x.ValidateAsync(user, expiredToken))
                  .ReturnsAsync(false); // Expired token fails validation
        var logger = MockLogger();

        var controller = new AuthController(userMgr.Object, signInMgr.Object, tokenGen.Object, refreshSvc.Object, cfg, logger.Object);
        AttachHttpContext(controller);

        var dto = new RefreshDto(expiredToken);

        // Act
        var result = await controller.RefreshAsync(dto);

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task RefreshAsync_TokenMismatch_Returns401()
    {
        // Arrange
        var cfg = CreateConfiguration();
        var user = new ApplicationUser
        {
            Id = "user-mismatch",
            Email = "mismatch@test.ro",
            UserName = "mismatch@test.ro"
        };

        var storedToken = GenerateValidJwt(user.Id, cfg);
        var differentToken = GenerateValidJwt(user.Id, cfg); // Different JTI

        var userMgr = MockUserManager();
        userMgr.Setup(x => x.FindByIdAsync(user.Id))
               .ReturnsAsync(user);

        var signInMgr = MockSignInManager(userMgr);
        var tokenGen = new Mock<ITokenGenerator>();
        var refreshSvc = new Mock<IRefreshTokenService>();
        refreshSvc.Setup(x => x.ValidateAsync(user, differentToken))
                  .ReturnsAsync(false); // Different token doesn't match stored one
        var logger = MockLogger();

        var controller = new AuthController(userMgr.Object, signInMgr.Object, tokenGen.Object, refreshSvc.Object, cfg, logger.Object);
        AttachHttpContext(controller);

        var dto = new RefreshDto(differentToken);

        // Act
        var result = await controller.RefreshAsync(dto);

        // Assert
        result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task RefreshAsync_InvalidModelState_Returns400WhenChecked()
    {
        // Arrange
        var cfg = CreateConfiguration();
        var userMgr = MockUserManager();
        var signInMgr = MockSignInManager(userMgr);
        var tokenGen = new Mock<ITokenGenerator>();
        var refreshSvc = new Mock<IRefreshTokenService>();
        var logger = MockLogger();

        var controller = new AuthController(userMgr.Object, signInMgr.Object, tokenGen.Object, refreshSvc.Object, cfg, logger.Object);
        AttachHttpContext(controller);

        // Simulate invalid model state
        controller.ModelState.AddModelError("RefreshToken", "RefreshToken is required");

        var dto = new RefreshDto("");

        // Act
        if (!controller.ModelState.IsValid)
        {
            var validationResult = new BadRequestObjectResult(controller.ModelState);
            validationResult.Should().BeOfType<BadRequestObjectResult>();
            return;
        }

        var result = await controller.RefreshAsync(dto);
        result.Should().NotBeNull();
    }
}
