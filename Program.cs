using System.Text;
using System.Text.Json.Serialization;
using back_mylife.Data;
using back_mylife.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Oracle.ManagedDataAccess.Client;

// Load environment variables from .env file if it exists.
// .env is a local fallback only: variables already present in the process
// environment (including empty ones) take precedence and are never overwritten.
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
            if (Environment.GetEnvironmentVariable(key) == null)
            {
                Environment.SetEnvironmentVariable(key, parts[1].Trim());
            }
        }
    }
}

var builder = WebApplication.CreateBuilder(args);

builder.Configuration["Jwt:Key"] = Environment.GetEnvironmentVariable("JWT_KEY") ?? builder.Configuration["Jwt:Key"];
builder.Configuration["Jwt:Issuer"] = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? builder.Configuration["Jwt:Issuer"];
builder.Configuration["Jwt:Audience"] = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? builder.Configuration["Jwt:Audience"];
builder.Configuration["Google:ClientId"] = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID") ?? builder.Configuration["Google:ClientId"];
builder.Configuration["Google:ClientSecret"] = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET") ?? builder.Configuration["Google:ClientSecret"];
builder.Configuration["Service:ApiKey"] = Environment.GetEnvironmentVariable("SERVICE_API_KEY") ?? builder.Configuration["Service:ApiKey"];

builder.Configuration["OCI:AccessKey"] = Environment.GetEnvironmentVariable("OCI_S3_ACCESS_KEY") ?? Environment.GetEnvironmentVariable("OCI_ACCESS_KEY") ?? builder.Configuration["OCI:AccessKey"];
builder.Configuration["OCI:SecretKey"] = Environment.GetEnvironmentVariable("OCI_S3_SECRET_KEY") ?? Environment.GetEnvironmentVariable("OCI_SECRET_KEY") ?? builder.Configuration["OCI:SecretKey"];
builder.Configuration["OCI:Region"] = Environment.GetEnvironmentVariable("OCI_REGION") ?? builder.Configuration["OCI:Region"] ?? "ap-singapore-1";
builder.Configuration["OCI:Namespace"] = Environment.GetEnvironmentVariable("OCI_NAMESPACE") ?? builder.Configuration["OCI:Namespace"];
builder.Configuration["OCI:BucketName"] = Environment.GetEnvironmentVariable("OCI_BUCKET_NAME") ?? builder.Configuration["OCI:BucketName"] ?? "mylife-profile-bucket";
builder.Configuration["OCI:PublicUrlBase"] = Environment.GetEnvironmentVariable("OCI_PUBLIC_URL_BASE") ?? builder.Configuration["OCI:PublicUrlBase"];

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
    });
builder.Services.AddOpenApi();

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

var credentialConnectionString = Environment.GetEnvironmentVariable("ORACLE_CONNECTION_STRING")
    ?? throw new InvalidOperationException("ORACLE_CONNECTION_STRING must be configured.");

var oracleTlsHost = Environment.GetEnvironmentVariable("ORACLE_TLS_HOST")
    ?? builder.Configuration["OracleTls:Host"]
    ?? throw new InvalidOperationException("Oracle TLS host must be configured.");
var oracleTlsServiceName = Environment.GetEnvironmentVariable("ORACLE_TLS_SERVICE_NAME")
    ?? builder.Configuration["OracleTls:ServiceName"]
    ?? throw new InvalidOperationException("Oracle TLS service name must be configured.");

OracleConnectionStringBuilder credentialBuilder;
try
{
    credentialBuilder = new OracleConnectionStringBuilder(credentialConnectionString);
}
catch (Exception exception)
{
    throw new InvalidOperationException("ORACLE_CONNECTION_STRING is invalid.", exception);
}

if (string.IsNullOrWhiteSpace(credentialBuilder.UserID) || string.IsNullOrWhiteSpace(credentialBuilder.Password))
{
    throw new InvalidOperationException("ORACLE_CONNECTION_STRING must contain a user ID and password.");
}

var directDataSource = $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCPS)(PORT=1521)(HOST={oracleTlsHost}))(CONNECT_DATA=(SERVICE_NAME={oracleTlsServiceName}))(SECURITY=(SSL_SERVER_DN_MATCH=yes)))";
var connectionString = new OracleConnectionStringBuilder
{
    UserID = credentialBuilder.UserID,
    Password = credentialBuilder.Password,
    DataSource = directDataSource
}.ConnectionString;

// Oracle Managed Data Access uses the operating-system certificate store for TCPS.
// No downloaded Autonomous Database wallet or local network configuration is required.
OracleConfiguration.WalletLocation = "system";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseOracle(connectionString));

builder.Services.AddHttpClient();
builder.Services.AddScoped<GoogleCalendarService>();
builder.Services.AddScoped<ClassReminderService>();
builder.Services.AddScoped<ActivityReminderService>();
builder.Services.AddScoped<OracleObjectStorageService>();
builder.Services.AddScoped<EmailSenderService>();
builder.Services.AddScoped<UserEmailNotificationService>();

var enableBackendReminderWorker = string.Equals(
    Environment.GetEnvironmentVariable("ENABLE_BACKEND_REMINDER_WORKER") ?? builder.Configuration["Reminders:EnableBackgroundWorker"],
    "true",
    StringComparison.OrdinalIgnoreCase);

if (enableBackendReminderWorker)
{
    builder.Services.AddHostedService<ClassReminderBackgroundService>();
}

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
