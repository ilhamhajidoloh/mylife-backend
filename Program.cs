using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using back_mylife.Data;
using back_mylife.Services;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// Load environment variables from .env file if it exists
var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envPath))
{
    foreach (var line in File.ReadAllLines(envPath))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
        var parts = trimmed.Split('=', 2);
        if (parts.Length == 2)
        {
            var key = parts[0].Trim();
            var value = parts[1].Trim();
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}

var builder = WebApplication.CreateBuilder(args);

// Inject secrets from environment variables (populated from .env)
builder.Configuration["Jwt:Key"] = Environment.GetEnvironmentVariable("JWT_KEY") ?? builder.Configuration["Jwt:Key"];
builder.Configuration["Jwt:Issuer"] = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? builder.Configuration["Jwt:Issuer"];
builder.Configuration["Jwt:Audience"] = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? builder.Configuration["Jwt:Audience"];
builder.Configuration["Google:ClientId"] = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? builder.Configuration["Google:ClientId"];
builder.Configuration["Google:ClientSecret"] = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET") ?? builder.Configuration["Google:ClientSecret"];

// Service API key สำหรับ endpoint ที่เรียกโดยบริการภายนอก (เช่น LINE bot worker) แทน user JWT
builder.Configuration["Service:ApiKey"] = Environment.GetEnvironmentVariable("SERVICE_API_KEY") ?? builder.Configuration["Service:ApiKey"];

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
    });
builder.Services.AddOpenApi();

// JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
    });
builder.Services.AddAuthorization();

// Connection String configuration (อ่านจาก .env หรือ appsettings.json)
var connString = Environment.GetEnvironmentVariable("DEFAULT_CONNECTION") 
    ?? builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Host=localhost;Database=mylife_db;Username=postgres;Password=postgres";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connString));

builder.Services.AddHttpClient();
builder.Services.AddScoped<GoogleCalendarService>();
builder.Services.AddScoped<ClassReminderService>();
builder.Services.AddScoped<ActivityReminderService>();
builder.Services.AddHostedService<ClassReminderBackgroundService>();

// Enable CORS for Flutter app development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Auto-migrate or ensure database and tables created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        var databaseCreator = db.Database.GetService<IDatabaseCreator>() as RelationalDatabaseCreator;
        if (databaseCreator != null)
        {
            if (!databaseCreator.Exists())
            {
                databaseCreator.Create();
            }
            try
            {
                databaseCreator.CreateTables();
                app.Logger.LogInformation("Database tables created successfully.");
            }
            catch (Exception ex)
            {
                app.Logger.LogInformation("CreateTables skipped or tables already exist: " + ex.Message);
            }
        }

        // CockroachDB does not support PostgreSQL's procedural DO blocks.  Use
        // idempotent DDL that is supported by both CockroachDB and PostgreSQL
        // so deployments can upgrade databases created before Recurrence existed.
        await db.Database.ExecuteSqlRawAsync(@"
ALTER TABLE IF EXISTS ""TodoItems""
ADD COLUMN IF NOT EXISTS ""Recurrence"" integer NOT NULL DEFAULT 0;");

        await db.Database.ExecuteSqlRawAsync(@"
ALTER TABLE IF EXISTS ""Activities""
ADD COLUMN IF NOT EXISTS ""Recurrence"" integer NOT NULL DEFAULT 0;");

        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ""TodoCompletions"" (
    ""Id"" uuid NOT NULL PRIMARY KEY,
    ""TodoItemId"" uuid NOT NULL REFERENCES ""TodoItems"" (""Id"") ON DELETE CASCADE,
    ""CompletedDate"" timestamp without time zone NOT NULL,
    ""IsCompleted"" boolean NOT NULL DEFAULT false
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TodoCompletions_TodoItemId_CompletedDate""
ON ""TodoCompletions"" (""TodoItemId"", ""CompletedDate"");");

        // Todo: description/status/priority/reminder tracking (migrated away from Supabase).
        await db.Database.ExecuteSqlRawAsync(@"
ALTER TABLE IF EXISTS ""TodoItems""
ADD COLUMN IF NOT EXISTS ""Description"" text NULL,
ADD COLUMN IF NOT EXISTS ""Status"" integer NOT NULL DEFAULT 0,
ADD COLUMN IF NOT EXISTS ""Priority"" integer NOT NULL DEFAULT 1,
ADD COLUMN IF NOT EXISTS ""ReminderSentAt"" timestamp without time zone NULL;");

        // Activity: reminder + Google Calendar sync tracking (migrated away from Supabase).
        await db.Database.ExecuteSqlRawAsync(@"
ALTER TABLE IF EXISTS ""Activities""
ADD COLUMN IF NOT EXISTS ""ReminderMinutes"" integer NULL,
ADD COLUMN IF NOT EXISTS ""ReminderSentAt"" timestamp without time zone NULL,
ADD COLUMN IF NOT EXISTS ""GoogleEventId"" text NULL;");

        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ""GoogleCalendarConnections"" (
    ""Id"" uuid NOT NULL PRIMARY KEY,
    ""UserId"" uuid NOT NULL REFERENCES ""Users"" (""Id"") ON DELETE CASCADE,
    ""AccessToken"" text NOT NULL,
    ""RefreshToken"" text NOT NULL,
    ""TokenExpiresAt"" timestamp without time zone NOT NULL,
    ""CreatedAt"" timestamp without time zone NOT NULL,
    ""UpdatedAt"" timestamp without time zone NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_GoogleCalendarConnections_UserId""
ON ""GoogleCalendarConnections"" (""UserId"");");

        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ""LineConnections"" (
    ""Id"" uuid NOT NULL PRIMARY KEY,
    ""UserId"" uuid NOT NULL REFERENCES ""Users"" (""Id"") ON DELETE CASCADE,
    ""LineUserId"" text NOT NULL,
    ""NotificationsEnabled"" boolean NOT NULL DEFAULT true,
    ""ConnectedAt"" timestamp without time zone NOT NULL,
    ""SessionStateJson"" text NULL,
    ""SessionExpiresAt"" timestamp without time zone NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LineConnections_UserId""
ON ""LineConnections"" (""UserId"");
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LineConnections_LineUserId""
ON ""LineConnections"" (""LineUserId"");");

        // Add class reminder columns to LineConnections
        await db.Database.ExecuteSqlRawAsync(@"
ALTER TABLE IF EXISTS ""LineConnections""
ADD COLUMN IF NOT EXISTS ""ClassRemindersEnabled"" boolean NOT NULL DEFAULT false,
ADD COLUMN IF NOT EXISTS ""ClassReminderMinutes"" integer NOT NULL DEFAULT 15;");

        // Create ClassRemindersSent table
        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ""ClassRemindersSent"" (
    ""Id"" uuid NOT NULL PRIMARY KEY,
    ""UserId"" uuid NOT NULL REFERENCES ""Users"" (""Id"") ON DELETE CASCADE,
    ""CourseId"" uuid NOT NULL REFERENCES ""Courses"" (""Id"") ON DELETE CASCADE,
    ""ClassDate"" timestamp without time zone NOT NULL,
    ""SentAt"" timestamp without time zone NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ClassRemindersSent_UserId_CourseId_ClassDate""
ON ""ClassRemindersSent"" (""UserId"", ""CourseId"", ""ClassDate"");");

        // Multi-Book Finance Support
        await db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ""FinanceBooks"" (
    ""Id"" uuid NOT NULL PRIMARY KEY,
    ""UserId"" uuid NOT NULL REFERENCES ""Users"" (""Id"") ON DELETE CASCADE,
    ""Name"" text NOT NULL DEFAULT 'สมุดหลัก',
    ""Icon"" text NOT NULL DEFAULT '🏠',
    ""Color"" text NOT NULL DEFAULT '#8b5cf6',
    ""IsDefault"" boolean NOT NULL DEFAULT false,
    ""CreatedAt"" timestamp without time zone NOT NULL DEFAULT (now() at time zone 'utc')
);
CREATE INDEX IF NOT EXISTS ""IX_FinanceBooks_UserId""
ON ""FinanceBooks"" (""UserId"");");

        await db.Database.ExecuteSqlRawAsync(@"
ALTER TABLE IF EXISTS ""FinanceTransactions""
ADD COLUMN IF NOT EXISTS ""BookId"" uuid NULL REFERENCES ""FinanceBooks"" (""Id"") ON DELETE SET NULL;");

        await db.Database.ExecuteSqlRawAsync(@"
ALTER TABLE IF EXISTS ""RecurringExpenses""
ADD COLUMN IF NOT EXISTS ""BookId"" uuid NULL REFERENCES ""FinanceBooks"" (""Id"") ON DELETE SET NULL;");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not automatically create/migrate DB. Make sure Postgres / CockroachDB is running.");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowAll");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/", () => Results.Ok(new { message = "MyLife API is running!", status = "Healthy" }));
app.MapControllers();

app.Run();
