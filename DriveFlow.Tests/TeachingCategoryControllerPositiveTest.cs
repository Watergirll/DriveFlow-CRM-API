using System.Security.Claims;
using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using DriveFlow_CRM_API.Controllers;
using DriveFlow_CRM_API.Models;

namespace DriveFlow.Tests.Controllers;

/// <summary>
/// Positive-path unit tests for <see cref="TeachingCategoryController"/>:
/// CRUD for teaching categories.
/// EF Core runs in-memory; UserManager is mocked.
/// </summary>
public sealed class TeachingCategoryControllerPositiveTest
{
    // ????????? helpers ?????????
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"TeachingCategory_Pos_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Mock<UserManager<ApplicationUser>> MockUserManager(ApplicationDbContext db, ApplicationUser? callerUser = null)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var mgr = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        if (callerUser != null)
        {
            mgr.Setup(x => x.GetUserId(It.IsAny<ClaimsPrincipal>()))
               .Returns(callerUser.Id);
            
            // Return db.Users directly - EF Core in-memory provider already supports async operations
            mgr.SetupGet(x => x.Users)
               .Returns(db.Users);
        }

        return mgr;
    }

    private static void AttachSchoolAdmin(ControllerBase controller, string adminId, int schoolId)
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

    private static void AttachSuperAdmin(ControllerBase controller, string adminId)
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "SuperAdmin"),
            new Claim(ClaimTypes.NameIdentifier, adminId)
        }, "mock");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    // ????????? GET /api/TeachingCategory/get/{schoolId} ?????????

    [Fact]
    public async Task GetTeachingCategories_SchoolAdmin_ReturnsCategories()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);

        db.Licenses.Add(new DriveFlow_CRM_API.Models.License { LicenseId = 1, Type = "B" });
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School1" });
        db.TeachingCategories.Add(new TeachingCategory
        {
            TeachingCategoryId = 1,
            Code = "B",
            LicenseId = 1,
            SessionCost = 100,
            SessionDuration = 60,
            ScholarshipPrice = 2000,
            MinDrivingLessonsReq = 30,
            AutoSchoolId = 1
        });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db, admin);
        var controller = new TeachingCategoryController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id, 1);

        // Act
        var result = await controller.GetTeachingCategories(1);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var categories = okResult.Value.Should().BeAssignableTo<List<TeachingCategoryResponseDto>>().Subject;
        categories.Should().HaveCount(1);
        categories[0].SessionCost.Should().Be(100);
        categories[0].LicenseType.Should().Be("B");
    }

    // ????????? POST /api/TeachingCategory/create/{schoolId} ?????????

    [Fact]
    public async Task CreateTeachingCategory_ValidData_Returns201AndPersists()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        db.Licenses.Add(new DriveFlow_CRM_API.Models.License { LicenseId = 1, Type = "B" });
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School1" });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db, admin);
        var controller = new TeachingCategoryController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id, 1);

        var dto = new TeachingCategoryCreateDto
        {
            LicenseId = 1,
            SessionCost = 120,
            SessionDuration = 90,
            ScholarshipPrice = 2500,
            MinDrivingLessonsReq = 25
        };

        // Act
        var result = await controller.CreateTeachingCategory(1, dto);

        // Assert
        result.Should().BeOfType<CreatedResult>();
        db.TeachingCategories.Should().ContainSingle(tc => tc.SessionCost == 120 && tc.AutoSchoolId == 1);
    }

    // ????????? PUT /api/TeachingCategory/update/{schoolId}/{teachingCategoryId} ?????????

    [Fact]
    public async Task UpdateTeachingCategory_ValidData_Returns200AndUpdates()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        db.Licenses.Add(new DriveFlow_CRM_API.Models.License { LicenseId = 1, Type = "B" });
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School1" });
        db.TeachingCategories.Add(new TeachingCategory
        {
            TeachingCategoryId = 100,
            Code = "B",
            LicenseId = 1,
            SessionCost = 100,
            SessionDuration = 60,
            AutoSchoolId = 1
        });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db, admin);
        var controller = new TeachingCategoryController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id, 1);

        var dto = new TeachingCategoryUpdateDto
        {
            LicenseId = 1,
            SessionCost = 150,
            SessionDuration = 90,
            ScholarshipPrice = 3000,
            MinDrivingLessonsReq = 35
        };

        // Act
        var result = await controller.UpdateTeachingCategory(1, 100, dto);

        // Assert
        result.Should().BeOfType<OkObjectResult>();
        var category = await db.TeachingCategories.FindAsync(100);
        category!.SessionCost.Should().Be(150);
        category.SessionDuration.Should().Be(90);
    }

    // ????????? DELETE /api/TeachingCategory/delete/{schoolId}/{teachingCategoryId} ?????????

    [Fact]
    public async Task DeleteTeachingCategory_ExistingCategory_Returns204AndRemoves()
    {
        // Arrange
        await using var db = CreateInMemoryDb();

        var admin = new ApplicationUser { Id = "admin-1", AutoSchoolId = 1 };
        db.Users.Add(admin);
        db.AutoSchools.Add(new AutoSchool { AutoSchoolId = 1, Name = "School1" });
        db.TeachingCategories.Add(new TeachingCategory
        {
            TeachingCategoryId = 200,
            Code = "B",
            AutoSchoolId = 1
        });
        await db.SaveChangesAsync();

        var userMgr = MockUserManager(db, admin);
        var controller = new TeachingCategoryController(db, userMgr.Object);
        AttachSchoolAdmin(controller, admin.Id, 1);

        // Act
        var result = await controller.DeleteTeachingCategory(1, 200);

        // Assert
        result.Should().BeOfType<NoContentResult>();
        db.TeachingCategories.Should().BeEmpty();
    }
}

// Helper extension for mocking IQueryable - no longer needed but kept for potential other uses
public static class MockDbSetExtensions
{
    public static Mock<DbSet<T>> BuildMockDbSet<T>(this IQueryable<T> source) where T : class
    {
        var mockSet = new Mock<DbSet<T>>();
        mockSet.As<IAsyncEnumerable<T>>()
               .Setup(m => m.GetAsyncEnumerator(It.IsAny<CancellationToken>()))
               .Returns(new TestAsyncEnumerator<T>(source.GetEnumerator()));
        mockSet.As<IQueryable<T>>()
               .Setup(m => m.Provider)
               .Returns(new TestAsyncQueryProvider<T>(source.Provider));
        mockSet.As<IQueryable<T>>().Setup(m => m.Expression).Returns(source.Expression);
        mockSet.As<IQueryable<T>>().Setup(m => m.ElementType).Returns(source.ElementType);
        mockSet.As<IQueryable<T>>().Setup(m => m.GetEnumerator()).Returns(source.GetEnumerator());
        return mockSet;
    }
}

internal class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
{
    private readonly IQueryProvider _inner;

    internal TestAsyncQueryProvider(IQueryProvider inner)
    {
        _inner = inner;
    }

    public IQueryable CreateQuery(Expression expression)
    {
        return new TestAsyncEnumerable<TEntity>(expression);
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        return new TestAsyncEnumerable<TElement>(expression);
    }

    public object? Execute(Expression expression)
    {
        return _inner.Execute(expression);
    }

    public TResult Execute<TResult>(Expression expression)
    {
        return _inner.Execute<TResult>(expression);
    }

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
    {
        var expectedResultType = typeof(TResult).GetGenericArguments()[0];
        var executionResult = typeof(IQueryProvider)
            .GetMethod(
                name: nameof(IQueryProvider.Execute),
                genericParameterCount: 1,
                types: new[] { typeof(Expression) })!
            .MakeGenericMethod(expectedResultType)
            .Invoke(this, new[] { expression });

        return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!
            .MakeGenericMethod(expectedResultType)
            .Invoke(null, new[] { executionResult })!;
    }
}

internal class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
{
    public TestAsyncEnumerable(IEnumerable<T> enumerable)
        : base(enumerable)
    { }

    public TestAsyncEnumerable(Expression expression)
        : base(expression)
    { }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        return new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
    }

    IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
}

internal class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
{
    private readonly IEnumerator<T> _inner;

    public TestAsyncEnumerator(IEnumerator<T> inner)
    {
        _inner = inner;
    }

    public T Current => _inner.Current;

    public ValueTask<bool> MoveNextAsync()
    {
        return ValueTask.FromResult(_inner.MoveNext());
    }

    public ValueTask DisposeAsync()
    {
        _inner.Dispose();
        return ValueTask.CompletedTask;
    }
}
