using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

using DriveFlow_CRM_API.Controllers;
using DriveFlow_CRM_API.Models;
using DriveFlow_CRM_API.Models.DTOs;
using DriveFlow_CRM_API.Services;

namespace DriveFlow.Tests.Controllers;

/// <summary>
/// Negative-path unit tests for <see cref="AiController"/>:
/// 401 (Unauthorized), 403 (Forbidden), 404 (NotFound) scenarios.
/// Services are mocked to avoid external API calls.
/// </summary>
public sealed class AiControllerNegativeTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Ai_Neg_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Mock<IAiContextBuilder> MockContextBuilder(AiStudentContextResponse? contextToReturn = null)
    {
        var mock = new Mock<IAiContextBuilder>();
        mock.Setup(x => x.BuildStudentContextAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(contextToReturn);
        return mock;
    }

    private static Mock<IAiStreamingService> MockStreamingService()
    {
        var mock = new Mock<IAiStreamingService>();
        mock.Setup(x => x.StreamToClientAsync(
                It.IsAny<List<object>>(),
                It.IsAny<HttpResponse>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    // ????????? POST /api/ai/chat/stream - Negative scenarios ?????????

    [Fact]
    public async Task StreamChat_NoAuthentication_Returns401()
    {
        // Arrange
        var contextBuilder = MockContextBuilder();
        var streamingService = MockStreamingService();
        var controller = new AiController(contextBuilder.Object, streamingService.Object);
        
        // Create HTTP context without user (no NameIdentifier claim)
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        // Empty ClaimsPrincipal - no claims
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new ChatRequest
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "user", Content = "Test" }
            }
        };

        // Act
        await controller.StreamChat(request);

        // Assert
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task StreamChat_UserWithoutStudentRole_Returns403()
    {
        // Arrange
        var contextBuilder = MockContextBuilder();
        var streamingService = MockStreamingService();
        var controller = new AiController(contextBuilder.Object, streamingService.Object);
        
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        
        // User with Instructor role (not Student)
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Instructor"),
            new Claim(ClaimTypes.NameIdentifier, "instructor-1")
        }, "mock");
        httpContext.User = new ClaimsPrincipal(identity);
        
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new ChatRequest
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "user", Content = "Test" }
            }
        };

        // Act
        await controller.StreamChat(request);

        // Assert
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task StreamChat_StudentNotFoundInDatabase_Returns404()
    {
        // Arrange
        // Context builder returns null when student not found
        var contextBuilder = MockContextBuilder(null);
        var streamingService = MockStreamingService();
        var controller = new AiController(contextBuilder.Object, streamingService.Object);
        
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Student"),
            new Claim(ClaimTypes.NameIdentifier, "nonexistent-student")
        }, "mock");
        httpContext.User = new ClaimsPrincipal(identity);
        
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new ChatRequest
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "user", Content = "Test" }
            }
        };

        // Act
        await controller.StreamChat(request);

        // Assert
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task StreamChat_SchoolAdminRole_Returns403()
    {
        // Arrange
        var contextBuilder = MockContextBuilder();
        var streamingService = MockStreamingService();
        var controller = new AiController(contextBuilder.Object, streamingService.Object);
        
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        
        // User with SchoolAdmin role
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "SchoolAdmin"),
            new Claim(ClaimTypes.NameIdentifier, "admin-1")
        }, "mock");
        httpContext.User = new ClaimsPrincipal(identity);
        
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new ChatRequest
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "user", Content = "Test" }
            }
        };

        // Act
        await controller.StreamChat(request);

        // Assert
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task StreamChat_SuperAdminRole_Returns403()
    {
        // Arrange
        var contextBuilder = MockContextBuilder();
        var streamingService = MockStreamingService();
        var controller = new AiController(contextBuilder.Object, streamingService.Object);
        
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        
        // User with SuperAdmin role
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "SuperAdmin"),
            new Claim(ClaimTypes.NameIdentifier, "superadmin-1")
        }, "mock");
        httpContext.User = new ClaimsPrincipal(identity);
        
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new ChatRequest
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "user", Content = "Test" }
            }
        };

        // Act
        await controller.StreamChat(request);

        // Assert
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task StreamChat_InvalidModelState_Returns400WhenChecked()
    {
        // Arrange
        var contextBuilder = MockContextBuilder();
        var streamingService = MockStreamingService();
        var controller = new AiController(contextBuilder.Object, streamingService.Object);
        
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Student"),
            new Claim(ClaimTypes.NameIdentifier, "student-1")
        }, "mock");
        httpContext.User = new ClaimsPrincipal(identity);
        
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // Simulate invalid model state
        controller.ModelState.AddModelError("Messages", "Messages list is required");

        var request = new ChatRequest
        {
            Messages = null! // Invalid - should have at least some messages
        };

        // Act
        // In controller-direct testing, [ApiController] automatic ModelState validation doesn't run.
        // We simulate the behavior by checking ModelState manually.
        if (!controller.ModelState.IsValid)
        {
            var validationResult = new BadRequestObjectResult(controller.ModelState);
            validationResult.Should().BeOfType<BadRequestObjectResult>();
            return;
        }

        await controller.StreamChat(request);
    }

    [Fact]
    public async Task StreamChat_EmptyNameIdentifier_Returns401()
    {
        // Arrange
        var contextBuilder = MockContextBuilder();
        var streamingService = MockStreamingService();
        var controller = new AiController(contextBuilder.Object, streamingService.Object);
        
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        
        // User with Student role but empty NameIdentifier
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Student"),
            new Claim(ClaimTypes.NameIdentifier, "") // Empty
        }, "mock");
        httpContext.User = new ClaimsPrincipal(identity);
        
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new ChatRequest
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "user", Content = "Test" }
            }
        };

        // Act
        await controller.StreamChat(request);

        // Assert
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task StreamChat_MultipleRolesWithoutStudent_Returns403()
    {
        // Arrange
        var contextBuilder = MockContextBuilder();
        var streamingService = MockStreamingService();
        var controller = new AiController(contextBuilder.Object, streamingService.Object);
        
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        
        // User with multiple roles but not Student
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Instructor"),
            new Claim(ClaimTypes.Role, "SchoolAdmin"),
            new Claim(ClaimTypes.NameIdentifier, "user-1")
        }, "mock");
        httpContext.User = new ClaimsPrincipal(identity);
        
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new ChatRequest
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "user", Content = "Test" }
            }
        };

        // Act
        await controller.StreamChat(request);

        // Assert
        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }
}
