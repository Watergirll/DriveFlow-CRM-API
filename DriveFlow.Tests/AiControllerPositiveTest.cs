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
/// Positive-path unit tests for <see cref="AiController"/>:
/// Health check and streaming chat (mocked services).
/// Services are mocked to avoid external API calls.
/// </summary>
public sealed class AiControllerPositiveTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Ai_Pos_{Guid.NewGuid()}")
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

    private static void AttachAnonymous(ControllerBase controller)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    private static AiStudentContextResponse CreateMockContextResponse()
    {
        return new AiStudentContextResponse(
            GeneratedAt: DateTime.UtcNow,
            SystemPrompt: "Test system prompt",
            Context: new StudentContextDto
            {
                Student = new StudentSummaryDto
                {
                    FullName = "Ion Popescu",
                    Email = "ion@test.ro",
                    SchoolName = "DriveFlow School",
                    TotalEnrollments = 1,
                    TotalCompletedSessions = 5
                },
                Categories = new List<CategoryProgressDto>(),
                OverallProgress = new OverallProgressDto
                {
                    TotalSessions = 5,
                    TotalEvaluatedSessions = 4,
                    OverallTrend = "improving"
                },
                CommonMistakes = new List<MistakeSummaryDto>(),
                StrongSkills = new List<string> { "Semnalizare corect?" },
                SkillsNeedingImprovement = new List<string> { "Parcare lateral?" },
                LatestSessionHighlights = new List<SessionHighlightDto>(),
                CoachingNotes = new List<string> { "Test coaching note" },
                DataAvailability = new DataAvailabilityDto
                {
                    HasEnrollments = true,
                    HasCompletedSessions = true,
                    HasEvaluatedSessions = true
                }
            }
        );
    }

    // ????????? GET /api/ai/health ?????????

    [Fact]
    public void HealthCheck_ReturnsHealthyStatus()
    {
        // Arrange
        var contextBuilder = MockContextBuilder();
        var streamingService = MockStreamingService();
        var controller = new AiController(contextBuilder.Object, streamingService.Object);
        AttachAnonymous(controller);

        // Act
        var result = controller.HealthCheck();

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value!;
        var responseType = response.GetType();
        responseType.GetProperty("status")!.GetValue(response).Should().Be("healthy");
        responseType.GetProperty("service")!.GetValue(response).Should().Be("ai-chat");
        responseType.GetProperty("version")!.GetValue(response).Should().Be("2.0.0");
    }

    [Fact]
    public void HealthCheck_DoesNotRequireAuthentication()
    {
        // Arrange
        var contextBuilder = MockContextBuilder();
        var streamingService = MockStreamingService();
        var controller = new AiController(contextBuilder.Object, streamingService.Object);
        
        // No user attached - anonymous
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        // Act
        var result = controller.HealthCheck();

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
    }

    // ????????? POST /api/ai/chat/stream (Positive scenarios with mocks) ?????????

    [Fact]
    public async Task StreamChat_ValidStudent_CallsContextBuilderAndStreamingService()
    {
        // Arrange
        var mockContext = CreateMockContextResponse();
        var contextBuilder = MockContextBuilder(mockContext);
        var streamingService = MockStreamingService();

        var controller = new AiController(contextBuilder.Object, streamingService.Object);
        
        // Create a mock HTTP context with response body
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Student"),
            new Claim(ClaimTypes.NameIdentifier, "student-1")
        }, "mock");
        httpContext.User = new ClaimsPrincipal(identity);
        
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new ChatRequest
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "user", Content = "Cum m? pot preg?ti mai bine?" }
            },
            HistorySessions = 5,
            Language = "ro"
        };

        // Act
        await controller.StreamChat(request);

        // Assert
        contextBuilder.Verify(x => x.BuildStudentContextAsync(
            "student-1",
            5,
            "ro",
            It.IsAny<CancellationToken>()), Times.Once);

        streamingService.Verify(x => x.StreamToClientAsync(
            It.IsAny<List<object>>(),
            It.IsAny<HttpResponse>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StreamChat_WithDefaultParameters_UsesDefaults()
    {
        // Arrange
        var mockContext = CreateMockContextResponse();
        var contextBuilder = MockContextBuilder(mockContext);
        var streamingService = MockStreamingService();

        var controller = new AiController(contextBuilder.Object, streamingService.Object);
        
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Student"),
            new Claim(ClaimTypes.NameIdentifier, "student-2")
        }, "mock");
        httpContext.User = new ClaimsPrincipal(identity);
        
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // Request without optional parameters
        var request = new ChatRequest
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "user", Content = "Ce gre?eli fac des?" }
            }
            // HistorySessions and Language are null, should use defaults
        };

        // Act
        await controller.StreamChat(request);

        // Assert - defaults are 5 and "ro"
        contextBuilder.Verify(x => x.BuildStudentContextAsync(
            "student-2",
            5,  // Default value
            "ro",  // Default value
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StreamChat_WithCustomHistorySessions_PassesCorrectValue()
    {
        // Arrange
        var mockContext = CreateMockContextResponse();
        var contextBuilder = MockContextBuilder(mockContext);
        var streamingService = MockStreamingService();

        var controller = new AiController(contextBuilder.Object, streamingService.Object);
        
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Student"),
            new Claim(ClaimTypes.NameIdentifier, "student-3")
        }, "mock");
        httpContext.User = new ClaimsPrincipal(identity);
        
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new ChatRequest
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "user", Content = "Test message" }
            },
            HistorySessions = 10,
            Language = "en"
        };

        // Act
        await controller.StreamChat(request);

        // Assert
        contextBuilder.Verify(x => x.BuildStudentContextAsync(
            "student-3",
            10,
            "en",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StreamChat_WithConversationHistory_IncludesMessagesInRequest()
    {
        // Arrange
        var mockContext = CreateMockContextResponse();
        var contextBuilder = MockContextBuilder(mockContext);
        
        List<object>? capturedMessages = null;
        var streamingService = new Mock<IAiStreamingService>();
        streamingService.Setup(x => x.StreamToClientAsync(
                It.IsAny<List<object>>(),
                It.IsAny<HttpResponse>(),
                It.IsAny<CancellationToken>()))
            .Callback<List<object>, HttpResponse, CancellationToken>((msgs, _, _) => capturedMessages = msgs)
            .Returns(Task.CompletedTask);

        var controller = new AiController(contextBuilder.Object, streamingService.Object);
        
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "Student"),
            new Claim(ClaimTypes.NameIdentifier, "student-4")
        }, "mock");
        httpContext.User = new ClaimsPrincipal(identity);
        
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var request = new ChatRequest
        {
            Messages = new List<ChatMessage>
            {
                new ChatMessage { Role = "user", Content = "Prima întrebare" },
                new ChatMessage { Role = "assistant", Content = "R?spuns" },
                new ChatMessage { Role = "user", Content = "A doua întrebare" }
            }
        };

        // Act
        await controller.StreamChat(request);

        // Assert
        capturedMessages.Should().NotBeNull();
        // 2 system messages + 3 conversation messages = 5 total
        capturedMessages!.Count.Should().Be(5);
    }
}
