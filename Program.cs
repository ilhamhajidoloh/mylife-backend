using System.Text;
using System.Text.Json.Serialization;
using back_mylife.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.IdentityModel.Tokens;

// โหลดไฟล์ .env ถ้ามีอยู่
DotNetEnv.Env.Load();

// อนุญาตให้ใช้ DateTime Kind = Unspecified กับ PostgreSQL timestamp
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// JWT Configuration from Environment Variables
builder.Configuration["Jwt:Key"] = Environment.GetEnvironmentVariable("JWT_KEY") ?? builder.Configuration["Jwt:Key"];
builder.Configuration["Jwt:Issuer"] = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? builder.Configuration["Jwt:Issuer"];
builder.Configuration["Jwt:Audience"] = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? builder.Configuration["Jwt:Audience"];
builder.Configuration["Jwt:ExpiryDays"] = Environment.GetEnvironmentVariable("JWT_EXPIRY_DAYS") ?? builder.Configuration["Jwt:ExpiryDays"] ?? "7";

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
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
