using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using DriveFlow_CRM_API.Models;
using DriveFlow_CRM_API.Models.DTOs;
using DriveFlow_CRM_API.Services;

// Alias to resolve ambiguity with System.IO.File
using StudentFile = DriveFlow_CRM_API.Models.File;

namespace DriveFlow.Tests.TestInfrastructure;

/// <summary>
/// Custom <see cref="WebApplicationFactory{TEntryPoint}"/> for integration/system tests.
/// Replaces the real database with an in-memory EF Core database and
/// external services with fake implementations.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Seeded test data includes:</strong>
/// <list type="bullet">
///   <item>Roles: SuperAdmin, SchoolAdmin, Instructor, Student</item>
///   <item>Users for each role with known passwords (Test123!)</item>
///   <item>Two auto schools (AutoSchoolA, AutoSchoolB) for scoping tests</item>
///   <item>Geography: County, City, Address</item>
///   <item>Licenses (B, A, C, etc.)</item>
///   <item>TeachingCategories per school</item>
///   <item>Vehicles per school</item>
///   <item>Files (student enrollments)</item>
///   <item>Requests for Request flow testing</item>
///   <item>InstructorAvailability slots</item>
///   <item>Appointments for SessionForm flow</item>
///   <item>ExamForms and ExamItems</item>
///   <item>SessionForms with sample mistakes</item>
/// </list>
/// </para>
/// </remarks>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Unique database name to ensure test isolation.
    /// </summary>
    private readonly string _dbName = $"DriveFlowTest_{Guid.NewGuid()}";

    /// <summary>
    /// Static constructor to set environment variables before any test runs.
    /// This is necessary because Program.cs checks for DB connection early.
    /// </summary>
    static CustomWebApplicationFactory()
    {
        // Set a dummy connection string that passes the initial validation.
        // The actual DbContext will be replaced with InMemory in ConfigureServices.
        Environment.SetEnvironmentVariable("DB_CONNECTION_URI", 
            "mysql://test:test@localhost:3306/testdb");
        
        // Set JWT_KEY for authentication to work
        Environment.SetEnvironmentVariable("JWT_KEY", 
            "ThisIsAVeryLongSecretKeyForTestingPurposesOnly123456789");
    }

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Add configuration overrides before services are configured
        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Add test-specific configuration
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=testdb;User=test;Password=test;",
                ["Jwt:Key"] = "ThisIsAVeryLongSecretKeyForTestingPurposesOnly123456789",
                ["Jwt:Issuer"] = "DriveFlowTest",
                ["Jwt:Audience"] = "DriveFlowTestAudience",
                ["Jwt:AccessExpiresMinutes"] = "60",
                ["Jwt:RefreshExpiresDays"] = "7"
            });
        });

        builder.ConfigureServices(services =>
        {
            // ?????????????????????????????????????????????????????????????????????
            // 1. Remove the real DbContext registration and replace with InMemory
            // ?????????????????????????????????????????????????????????????????????
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (dbDescriptor != null)
                services.Remove(dbDescriptor);

            // Remove any other DbContext-related descriptors
            services.RemoveAll<ApplicationDbContext>();

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseInMemoryDatabase(_dbName);
                options.EnableSensitiveDataLogging();
                // Configure the InMemory provider to ignore transaction operations
                // since it doesn't support real transactions
                options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
            });

            // ?????????????????????????????????????????????????????????????????????
            // 2. Replace external services with fakes
            // ?????????????????????????????????????????????????????????????????????
            services.RemoveAll<IAiStreamingService>();
            services.AddScoped<IAiStreamingService, FakeAiStreamingService>();

            services.RemoveAll<IAiContextBuilder>();
            services.AddScoped<IAiContextBuilder, FakeAiContextBuilder>();

            // ?????????????????????????????????????????????????????????????????????
            // 3. Disable rate limiting for tests by setting very high limits
            // ?????????????????????????????????????????????????????????????????????
            services.Configure<AspNetCoreRateLimit.IpRateLimitOptions>(opt =>
            {
                opt.EnableEndpointRateLimiting = false;
                opt.GeneralRules = new List<AspNetCoreRateLimit.RateLimitRule>
                {
                    new AspNetCoreRateLimit.RateLimitRule
                    {
                        Endpoint = "*",
                        Limit = 10000,  // Very high limit for testing
                        Period = "1m"
                    }
                };
            });

            // ?????????????????????????????????????????????????????????????????????
            // 4. Build a temporary service provider to seed the database
            // ?????????????????????????????????????????????????????????????????????
            var sp = services.BuildServiceProvider();

            using var scope = sp.CreateScope();
            var scopedServices = scope.ServiceProvider;
            var db = scopedServices.GetRequiredService<ApplicationDbContext>();

            // Ensure the database is created
            db.Database.EnsureCreated();

            // Guard: ensure we're in Testing environment
            var env = scopedServices.GetRequiredService<IWebHostEnvironment>();
            if (!env.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"CustomWebApplicationFactory must run in 'Testing' environment. " +
                    $"Current environment: '{env.EnvironmentName}'.");
            }

            // Seed the database with deterministic test data
            SeedTestData(scopedServices, db);
        });
    }

    /// <summary>
    /// Seeds the in-memory database with deterministic test data.
    /// </summary>
    private static void SeedTestData(IServiceProvider services, ApplicationDbContext db)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        // ?????????????????????????????????????????????????????????????????????
        // Roles: SuperAdmin, SchoolAdmin, Instructor, Student
        // ?????????????????????????????????????????????????????????????????????
        var roles = new[] { "SuperAdmin", "SchoolAdmin", "Instructor", "Student" };
        foreach (var roleName in roles)
        {
            if (!roleManager.RoleExistsAsync(roleName).GetAwaiter().GetResult())
            {
                roleManager.CreateAsync(new IdentityRole(roleName)).GetAwaiter().GetResult();
            }
        }

        // ?????????????????????????????????????????????????????????????????????
        // Counties
        // ?????????????????????????????????????????????????????????????????????
        var countyCluj = new County { CountyId = 1, Name = "Cluj", Abbreviation = "CJ" };
        var countyBucuresti = new County { CountyId = 2, Name = "Bucuresti", Abbreviation = "B" };
        db.Counties.AddRange(countyCluj, countyBucuresti);

        // ?????????????????????????????????????????????????????????????????????
        // Cities
        // ?????????????????????????????????????????????????????????????????????
        var cityCluj = new City { CityId = 1, Name = "Cluj-Napoca", CountyId = 1 };
        var cityBucuresti = new City { CityId = 2, Name = "Bucuresti", CountyId = 2 };
        db.Cities.AddRange(cityCluj, cityBucuresti);

        // ?????????????????????????????????????????????????????????????????????
        // Addresses
        // ?????????????????????????????????????????????????????????????????????
        var addressA = new Address
        {
            AddressId = 1,
            StreetName = "Strada Aviatorilor",
            AddressNumber = "10",
            Postcode = "400001",
            CityId = 1
        };
        var addressB = new Address
        {
            AddressId = 2,
            StreetName = "Bulevardul Unirii",
            AddressNumber = "25",
            Postcode = "010001",
            CityId = 2
        };
        db.Addresses.AddRange(addressA, addressB);

        // ?????????????????????????????????????????????????????????????????????
        // Licenses
        // ?????????????????????????????????????????????????????????????????????
        var licenseB = new License { LicenseId = 1, Type = "B" };
        var licenseA = new License { LicenseId = 2, Type = "A" };
        var licenseC = new License { LicenseId = 3, Type = "C" };
        var licenseBE = new License { LicenseId = 4, Type = "BE" };
        db.Licenses.AddRange(licenseB, licenseA, licenseC, licenseBE);

        // ?????????????????????????????????????????????????????????????????????
        // AutoSchools: AutoSchoolA (Id=1), AutoSchoolB (Id=2)
        // ?????????????????????????????????????????????????????????????????????
        var autoSchoolA = new AutoSchool
        {
            AutoSchoolId = 1,
            Name = "AutoSchoolA",
            Description = "Test driving school A in Cluj",
            Email = "schoola@test.com",
            PhoneNumber = "0740000001",
            WebSite = "https://schoola.test",
            Status = AutoSchoolStatus.Active,
            AddressId = 1
        };

        var autoSchoolB = new AutoSchool
        {
            AutoSchoolId = 2,
            Name = "AutoSchoolB",
            Description = "Test driving school B in Bucuresti",
            Email = "schoolb@test.com",
            PhoneNumber = "0740000002",
            WebSite = "https://schoolb.test",
            Status = AutoSchoolStatus.Active,
            AddressId = 2
        };

        db.AutoSchools.AddRange(autoSchoolA, autoSchoolB);

        // ?????????????????????????????????????????????????????????????????????
        // TeachingCategories (per school)
        // ?????????????????????????????????????????????????????????????????????
        var teachingCatA1 = new TeachingCategory
        {
            TeachingCategoryId = 1,
            Code = "B",
            SessionCost = 150m,
            SessionDuration = 90,
            ScholarshipPrice = 2500m,
            MinDrivingLessonsReq = 30,
            AutoSchoolId = 1,
            LicenseId = 1
        };
        var teachingCatA2 = new TeachingCategory
        {
            TeachingCategoryId = 2,
            Code = "A",
            SessionCost = 120m,
            SessionDuration = 60,
            ScholarshipPrice = 2000m,
            MinDrivingLessonsReq = 20,
            AutoSchoolId = 1,
            LicenseId = 2
        };
        var teachingCatB1 = new TeachingCategory
        {
            TeachingCategoryId = 3,
            Code = "B",
            SessionCost = 140m,
            SessionDuration = 90,
            ScholarshipPrice = 2400m,
            MinDrivingLessonsReq = 30,
            AutoSchoolId = 2,
            LicenseId = 1
        };
        var teachingCatB2 = new TeachingCategory
        {
            TeachingCategoryId = 4,
            Code = "C",
            SessionCost = 200m,
            SessionDuration = 120,
            ScholarshipPrice = 3500m,
            MinDrivingLessonsReq = 40,
            AutoSchoolId = 2,
            LicenseId = 3
        };
        db.TeachingCategories.AddRange(teachingCatA1, teachingCatA2, teachingCatB1, teachingCatB2);

        // ?????????????????????????????????????????????????????????????????????
        // Vehicles (per school)
        // ?????????????????????????????????????????????????????????????????????
        var vehicleA1 = new Vehicle
        {
            VehicleId = 1,
            LicensePlateNumber = "CJ-01-TST",
            TransmissionType = TransmissionType.MANUAL,
            Brand = "Dacia",
            Model = "Logan",
            YearOfProduction = 2022,
            Color = "White",
            FuelType = TipCombustibil.BENZINA,
            EngineSizeLiters = 1.2m,
            PowertrainType = TipPropulsie.COMBUSTIBIL,
            AutoSchoolId = 1,
            LicenseId = 1
        };
        var vehicleA2 = new Vehicle
        {
            VehicleId = 2,
            LicensePlateNumber = "CJ-02-TST",
            TransmissionType = TransmissionType.AUTOMATIC,
            Brand = "Honda",
            Model = "CBR600",
            YearOfProduction = 2021,
            Color = "Red",
            FuelType = TipCombustibil.BENZINA,
            EngineSizeLiters = 0.6m,
            PowertrainType = TipPropulsie.COMBUSTIBIL,
            AutoSchoolId = 1,
            LicenseId = 2
        };
        var vehicleB1 = new Vehicle
        {
            VehicleId = 3,
            LicensePlateNumber = "B-01-TST",
            TransmissionType = TransmissionType.MANUAL,
            Brand = "Ford",
            Model = "Focus",
            YearOfProduction = 2023,
            Color = "Blue",
            FuelType = TipCombustibil.MOTORINA,
            EngineSizeLiters = 1.5m,
            PowertrainType = TipPropulsie.COMBUSTIBIL,
            AutoSchoolId = 2,
            LicenseId = 1
        };
        var vehicleB2 = new Vehicle
        {
            VehicleId = 4,
            LicensePlateNumber = "B-02-TST",
            TransmissionType = TransmissionType.MANUAL,
            Brand = "MAN",
            Model = "TGX",
            YearOfProduction = 2020,
            Color = "White",
            FuelType = TipCombustibil.MOTORINA,
            EngineSizeLiters = 12.0m,
            PowertrainType = TipPropulsie.COMBUSTIBIL,
            AutoSchoolId = 2,
            LicenseId = 3
        };
        db.Vehicles.AddRange(vehicleA1, vehicleA2, vehicleB1, vehicleB2);

        // ?????????????????????????????????????????????????????????????????????
        // ExamForms (one per license)
        // ?????????????????????????????????????????????????????????????????????
        var examFormB = new ExamForm { FormId = 1, LicenseId = 1, MaxPoints = 21 };
        var examFormA = new ExamForm { FormId = 2, LicenseId = 2, MaxPoints = 21 };
        var examFormC = new ExamForm { FormId = 3, LicenseId = 3, MaxPoints = 21 };
        db.ExamForms.AddRange(examFormB, examFormA, examFormC);

        // ?????????????????????????????????????????????????????????????????????
        // ExamItems (sample items for license B form)
        // ?????????????????????????????????????????????????????????????????????
        var examItems = new[]
        {
            new ExamItem { ItemId = 1, FormId = 1, Description = "Neasigurarea la schimbarea directiei de mers", PenaltyPoints = 9, OrderIndex = 1 },
            new ExamItem { ItemId = 2, FormId = 1, Description = "Nerespectarea semnificatiei indicatoarelor", PenaltyPoints = 6, OrderIndex = 2 },
            new ExamItem { ItemId = 3, FormId = 1, Description = "Nesemnalizarea schimbarii directiei de mers", PenaltyPoints = 6, OrderIndex = 3 },
            new ExamItem { ItemId = 4, FormId = 1, Description = "Nerespectarea regulilor de depasire", PenaltyPoints = 21, OrderIndex = 4 },
            new ExamItem { ItemId = 5, FormId = 1, Description = "Neacordarea prioritatii pietonilor", PenaltyPoints = 21, OrderIndex = 5 },
            new ExamItem { ItemId = 6, FormId = 1, Description = "Depasirea vitezei legale", PenaltyPoints = 9, OrderIndex = 6 },
            new ExamItem { ItemId = 7, FormId = 1, Description = "Executarea incorecta a parcarii", PenaltyPoints = 5, OrderIndex = 7 },
            new ExamItem { ItemId = 8, FormId = 1, Description = "Folosirea incorecta a luminilor", PenaltyPoints = 3, OrderIndex = 8 },
            // Items for license A form
            new ExamItem { ItemId = 9, FormId = 2, Description = "Nesincronizarea comenzilor", PenaltyPoints = 6, OrderIndex = 1 },
            new ExamItem { ItemId = 10, FormId = 2, Description = "Nementinerea directiei de mers", PenaltyPoints = 9, OrderIndex = 2 },
            new ExamItem { ItemId = 11, FormId = 2, Description = "Executarea nereglementara a virajelor", PenaltyPoints = 6, OrderIndex = 3 },
            // Items for license C form
            new ExamItem { ItemId = 12, FormId = 3, Description = "Neverificarea incarcaturii vehiculului", PenaltyPoints = 9, OrderIndex = 1 },
            new ExamItem { ItemId = 13, FormId = 3, Description = "Manevrarea incorecta la spatii restranse", PenaltyPoints = 6, OrderIndex = 2 },
            new ExamItem { ItemId = 14, FormId = 3, Description = "Nepastrarea distantei de siguranta", PenaltyPoints = 9, OrderIndex = 3 }
        };
        db.ExamItems.AddRange(examItems);

        db.SaveChanges();

        // ?????????????????????????????????????????????????????????????????????
        // Users: SuperAdmin (no school), SchoolAdmins, Instructors, Students
        // ?????????????????????????????????????????????????????????????????????
        var testPassword = "Test123!";

        // SuperAdmin
        var superAdmin = CreateUser(userManager, "admin@test.com", "Super", "Admin", null, "SuperAdmin", testPassword);

        // SchoolAdmin for School A
        var schoolAdminA = CreateUser(userManager, "schooladmin@test.com", "School", "AdminA", 1, "SchoolAdmin", testPassword);

        // SchoolAdmin for School B
        var schoolAdminB = CreateUser(userManager, "schooladminb@test.com", "School", "AdminB", 2, "SchoolAdmin", testPassword);

        // Instructors for School A
        var instructorA1 = CreateUser(userManager, "instructor@test.com", "Instructor", "OneA", 1, "Instructor", testPassword);
        var instructorA2 = CreateUser(userManager, "instructor2a@test.com", "Instructor", "TwoA", 1, "Instructor", testPassword);

        // Instructors for School B
        var instructorB1 = CreateUser(userManager, "instructorb@test.com", "Instructor", "OneB", 2, "Instructor", testPassword);

        // Students for School A
        var studentA1 = CreateUser(userManager, "student@test.com", "Student", "OneA", 1, "Student", testPassword, "1900101010001");
        var studentA2 = CreateUser(userManager, "student2a@test.com", "Student", "TwoA", 1, "Student", testPassword, "1900101010002");

        // Students for School B
        var studentB1 = CreateUser(userManager, "studentb@test.com", "Student", "OneB", 2, "Student", testPassword, "1900101010003");

        // ?????????????????????????????????????????????????????????????????????
        // ApplicationUserTeachingCategory (instructor-category assignments)
        // ?????????????????????????????????????????????????????????????????????
        var userTeachingCategories = new[]
        {
            new ApplicationUserTeachingCategory { ApplicationUserTeachingCategoryId = 1, UserId = instructorA1!.Id, TeachingCategoryId = 1 },
            new ApplicationUserTeachingCategory { ApplicationUserTeachingCategoryId = 2, UserId = instructorA1.Id, TeachingCategoryId = 2 },
            new ApplicationUserTeachingCategory { ApplicationUserTeachingCategoryId = 3, UserId = instructorA2!.Id, TeachingCategoryId = 1 },
            new ApplicationUserTeachingCategory { ApplicationUserTeachingCategoryId = 4, UserId = instructorB1!.Id, TeachingCategoryId = 3 },
            new ApplicationUserTeachingCategory { ApplicationUserTeachingCategoryId = 5, UserId = instructorB1.Id, TeachingCategoryId = 4 },
            new ApplicationUserTeachingCategory { ApplicationUserTeachingCategoryId = 6, UserId = studentA1!.Id, TeachingCategoryId = 1 },
            new ApplicationUserTeachingCategory { ApplicationUserTeachingCategoryId = 7, UserId = studentA2!.Id, TeachingCategoryId = 1 },
            new ApplicationUserTeachingCategory { ApplicationUserTeachingCategoryId = 8, UserId = studentB1!.Id, TeachingCategoryId = 3 }
        };
        db.ApplicationUserTeachingCategories.AddRange(userTeachingCategories);

        // ?????????????????????????????????????????????????????????????????????
        // Files (student enrollments) - using StudentFile alias
        // ?????????????????????????????????????????????????????????????????????
        var fileA1 = new StudentFile
        {
            FileId = 1,
            ScholarshipStartDate = DateTime.Today.AddMonths(-2),
            CriminalRecordExpiryDate = DateTime.Today.AddMonths(10),
            MedicalRecordExpiryDate = DateTime.Today.AddMonths(6),
            Status = FileStatus.APPROVED,
            StudentId = studentA1.Id,
            InstructorId = instructorA1.Id,
            TeachingCategoryId = 1,
            VehicleId = 1
        };
        var fileA2 = new StudentFile
        {
            FileId = 2,
            ScholarshipStartDate = DateTime.Today.AddMonths(-1),
            CriminalRecordExpiryDate = DateTime.Today.AddMonths(11),
            MedicalRecordExpiryDate = DateTime.Today.AddMonths(5),
            Status = FileStatus.APPROVED,
            StudentId = studentA2.Id,
            InstructorId = instructorA2.Id,
            TeachingCategoryId = 1,
            VehicleId = 1
        };
        var fileB1 = new StudentFile
        {
            FileId = 3,
            ScholarshipStartDate = DateTime.Today.AddMonths(-3),
            CriminalRecordExpiryDate = DateTime.Today.AddMonths(9),
            MedicalRecordExpiryDate = DateTime.Today.AddMonths(4),
            Status = FileStatus.APPROVED,
            StudentId = studentB1.Id,
            InstructorId = instructorB1.Id,
            TeachingCategoryId = 3,
            VehicleId = 3
        };
        db.Files.AddRange(fileA1, fileA2, fileB1);

        // ?????????????????????????????????????????????????????????????????????
        // Payments
        // ?????????????????????????????????????????????????????????????????????
        var payments = new[]
        {
            new Payment { PaymentId = 1, ScholarshipBasePayment = true, SessionsPayed = 10, FileId = 1 },
            new Payment { PaymentId = 2, ScholarshipBasePayment = false, SessionsPayed = 5, FileId = 2 },
            new Payment { PaymentId = 3, ScholarshipBasePayment = true, SessionsPayed = 15, FileId = 3 }
        };
        db.Payments.AddRange(payments);

        // ?????????????????????????????????????????????????????????????????????
        // Requests (contact requests for enrollment)
        // ?????????????????????????????????????????????????????????????????????
        var requests = new[]
        {
            new Request { RequestId = 1, FirstName = "Alex", LastName = "Popescu", PhoneNumber = "0711000001", DrivingCategory = "B", Status = "Pending", RequestDate = DateTime.UtcNow.AddDays(-5), AutoSchoolId = 1 },
            new Request { RequestId = 2, FirstName = "Maria", LastName = "Ionescu", PhoneNumber = "0711000002", DrivingCategory = "B", Status = "Approved", RequestDate = DateTime.UtcNow.AddDays(-3), AutoSchoolId = 1 },
            new Request { RequestId = 3, FirstName = "Radu", LastName = "Marinescu", PhoneNumber = "0711000003", DrivingCategory = "C", Status = "Pending", RequestDate = DateTime.UtcNow.AddDays(-1), AutoSchoolId = 2 },
            new Request { RequestId = 4, FirstName = "Ana", LastName = "Toma", PhoneNumber = "0711000004", DrivingCategory = "B", Status = "Rejected", RequestDate = DateTime.UtcNow.AddDays(-7), AutoSchoolId = 2 }
        };
        db.Requests.AddRange(requests);

        // ?????????????????????????????????????????????????????????????????????
        // InstructorAvailability (availability slots)
        // ?????????????????????????????????????????????????????????????????????
        var availabilities = new List<InstructorAvailability>();
        var availId = 1;
        
        // Instructor A1 availability for next 5 days
        for (var day = 0; day < 5; day++)
        {
            availabilities.Add(new InstructorAvailability
            {
                IntervalId = availId++,
                Date = DateTime.Today.AddDays(day),
                StartHour = new TimeSpan(9, 0, 0),
                EndHour = new TimeSpan(12, 0, 0),
                InstructorId = instructorA1.Id
            });
            availabilities.Add(new InstructorAvailability
            {
                IntervalId = availId++,
                Date = DateTime.Today.AddDays(day),
                StartHour = new TimeSpan(14, 0, 0),
                EndHour = new TimeSpan(17, 0, 0),
                InstructorId = instructorA1.Id
            });
        }

        // Instructor A2 availability
        for (var day = 0; day < 3; day++)
        {
            availabilities.Add(new InstructorAvailability
            {
                IntervalId = availId++,
                Date = DateTime.Today.AddDays(day),
                StartHour = new TimeSpan(10, 0, 0),
                EndHour = new TimeSpan(14, 0, 0),
                InstructorId = instructorA2.Id
            });
        }

        // Instructor B1 availability
        for (var day = 1; day < 4; day++)
        {
            availabilities.Add(new InstructorAvailability
            {
                IntervalId = availId++,
                Date = DateTime.Today.AddDays(day),
                StartHour = new TimeSpan(8, 0, 0),
                EndHour = new TimeSpan(16, 0, 0),
                InstructorId = instructorB1.Id
            });
        }
        db.InstructorAvailabilities.AddRange(availabilities);

        // ?????????????????????????????????????????????????????????????????????
        // Appointments (scheduled driving lessons)
        // ?????????????????????????????????????????????????????????????????????
        var appointments = new[]
        {
            new Appointment { AppointmentId = 1, Date = DateTime.Today.AddDays(-2), StartHour = new TimeSpan(9, 0, 0), EndHour = new TimeSpan(10, 30, 0), FileId = 1 },
            new Appointment { AppointmentId = 2, Date = DateTime.Today.AddDays(-1), StartHour = new TimeSpan(14, 0, 0), EndHour = new TimeSpan(15, 30, 0), FileId = 1 },
            new Appointment { AppointmentId = 3, Date = DateTime.Today, StartHour = new TimeSpan(10, 0, 0), EndHour = new TimeSpan(11, 30, 0), FileId = 1 },
            new Appointment { AppointmentId = 4, Date = DateTime.Today.AddDays(-3), StartHour = new TimeSpan(11, 0, 0), EndHour = new TimeSpan(12, 30, 0), FileId = 2 },
            new Appointment { AppointmentId = 5, Date = DateTime.Today.AddDays(-1), StartHour = new TimeSpan(9, 0, 0), EndHour = new TimeSpan(10, 30, 0), FileId = 3 },
            new Appointment { AppointmentId = 6, Date = DateTime.Today.AddDays(1), StartHour = new TimeSpan(8, 0, 0), EndHour = new TimeSpan(10, 0, 0), FileId = 3 }
        };
        db.Appointments.AddRange(appointments);

        // ?????????????????????????????????????????????????????????????????????
        // SessionForms (evaluation forms for completed appointments)
        // ?????????????????????????????????????????????????????????????????????
        var sessionForms = new[]
        {
            new SessionForm
            {
                SessionFormId = 1,
                AppointmentId = 1,
                FormId = 1,
                MistakesJson = "[{\"id_item\":1,\"count\":1},{\"id_item\":8,\"count\":2}]",
                CreatedAt = DateTime.Today.AddDays(-2).AddHours(9),
                FinalizedAt = DateTime.Today.AddDays(-2).AddHours(10),
                TotalPoints = 15,
                Result = "PASSED"
            },
            new SessionForm
            {
                SessionFormId = 2,
                AppointmentId = 2,
                FormId = 1,
                MistakesJson = "[{\"id_item\":2,\"count\":1},{\"id_item\":3,\"count\":1}]",
                CreatedAt = DateTime.Today.AddDays(-1).AddHours(14),
                FinalizedAt = DateTime.Today.AddDays(-1).AddHours(15),
                TotalPoints = 12,
                Result = "PASSED"
            },
            new SessionForm
            {
                SessionFormId = 3,
                AppointmentId = 4,
                FormId = 1,
                MistakesJson = "[{\"id_item\":4,\"count\":1}]",
                CreatedAt = DateTime.Today.AddDays(-3).AddHours(11),
                FinalizedAt = DateTime.Today.AddDays(-3).AddHours(12),
                TotalPoints = 21,
                Result = "FAILED"
            },
            new SessionForm
            {
                SessionFormId = 4,
                AppointmentId = 5,
                FormId = 3,
                MistakesJson = "[{\"id_item\":12,\"count\":1},{\"id_item\":14,\"count\":1}]",
                CreatedAt = DateTime.Today.AddDays(-1).AddHours(9),
                FinalizedAt = DateTime.Today.AddDays(-1).AddHours(10),
                TotalPoints = 18,
                Result = "PASSED"
            }
        };
        db.SessionForms.AddRange(sessionForms);

        db.SaveChanges();
    }

    /// <summary>
    /// Helper to create a user with the specified role.
    /// </summary>
    private static ApplicationUser? CreateUser(
        UserManager<ApplicationUser> userManager,
        string email,
        string firstName,
        string lastName,
        int? autoSchoolId,
        string role,
        string password,
        string? cnp = null)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            FirstName = firstName,
            LastName = lastName,
            AutoSchoolId = autoSchoolId,
            Cnp = cnp
        };

        var result = userManager.CreateAsync(user, password).GetAwaiter().GetResult();
        if (result.Succeeded)
        {
            userManager.AddToRoleAsync(user, role).GetAwaiter().GetResult();
            return user;
        }

        return null;
    }
}

/// <summary>
/// Fake implementation of <see cref="IAiStreamingService"/> for testing.
/// Always returns success without calling external APIs.
/// </summary>
internal sealed class FakeAiStreamingService : IAiStreamingService
{
    public Task StreamToClientAsync(
        List<object> messages,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        // Return a simple success response without calling external services
        return response.WriteAsync("event: done\ndata:\n\n", cancellationToken);
    }
}

/// <summary>
/// Fake implementation of <see cref="IAiContextBuilder"/> for testing.
/// Returns a minimal context without actual database queries.
/// </summary>
internal sealed class FakeAiContextBuilder : IAiContextBuilder
{
    public Task<AiStudentContextResponse?> BuildStudentContextAsync(
        string studentId,
        int historySessions = 5,
        string language = "ro",
        CancellationToken cancellationToken = default)
    {
        // Return a minimal fake response using the record constructor
        var context = new StudentContextDto
        {
            Student = new StudentSummaryDto
            {
                FullName = "Test Student",
                Email = "student@test.com",
                SchoolName = "AutoSchoolA",
                TotalEnrollments = 1,
                TotalCompletedSessions = 0,
                FirstSessionDate = null,
                LastSessionDate = null
            },
            Categories = new List<CategoryProgressDto>(),
            OverallProgress = new OverallProgressDto
            {
                TotalSessions = 0,
                TotalEvaluatedSessions = 0,
                OverallPassRate = null,
                AveragePenaltyPoints = null,
                OverallTrend = "insufficient_data",
                CategoriesImproving = 0,
                CategoriesDeclining = 0,
                TotalDistinctMistakes = 0,
                ImprovementAreas = new List<string>()
            },
            CommonMistakes = new List<MistakeSummaryDto>(),
            StrongSkills = new List<string>(),
            SkillsNeedingImprovement = new List<string>(),
            LatestSessionHighlights = new List<SessionHighlightDto>(),
            CoachingNotes = new List<string> { "Fake context for testing" },
            DataAvailability = new DataAvailabilityDto
            {
                HasEnrollments = false,
                HasCompletedSessions = false,
                HasEvaluatedSessions = false,
                CategoriesWithoutSessions = new List<string>(),
                CategoriesWithIncompleteData = new List<string>(),
                Warnings = new List<string> { "This is a fake response for testing" }
            }
        };

        var response = new AiStudentContextResponse(
            GeneratedAt: DateTime.UtcNow,
            SystemPrompt: "Fake system prompt for testing",
            Context: context
        );

        return Task.FromResult<AiStudentContextResponse?>(response);
    }
}
