using System.Diagnostics;
using Api.Configuration;
using Api.Errors;
using Api.Health;
using Api.Identity;
using Api.Observability;
using Api.Validation;
using System.Threading.RateLimiting;
using Api.Controllers;
using Application.Identity;
using Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Application.Common;
using FluentValidation;
using Infrastructure;
using Infrastructure.Persistence;
using Infrastructure.Storage;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSerilog(logging => logging.MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogEventLevel.Fatal)
    .Enrich.FromLogContext().WriteTo.Console());
builder.Services.AddOptions<DatabaseSettings>().BindConfiguration("ConnectionStrings")
    .Validate(x => DatabaseSettings.IsValid(x.DefaultConnection), "A valid database connection is required; configure ConnectionStrings:DefaultConnection using secrets or environment variables.")
    .Validate(x => builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") || (DatabaseSettings.IsValid(x.DefaultConnection) && new Npgsql.NpgsqlConnectionStringBuilder(x.DefaultConnection).SslMode == Npgsql.SslMode.VerifyFull),
        "Production database connections require SSL Mode=VerifyFull.")
    .ValidateOnStart();
builder.Services.AddOptions<CorsSettings>().BindConfiguration("Cors")
    .Validate(x => x.Origins.Length > 0 && x.Origins.All(CorsSettings.IsValidOrigin), "Configure exact HTTP(S) frontend origins without paths, trailing slashes or wildcards.")
    .ValidateOnStart();
builder.Services.AddCors();
builder.Services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>()
    .Configure<IOptions<CorsSettings>>((options, settings) => options.AddDefaultPolicy(policy => policy
        .WithOrigins(settings.Value.Origins).WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
        .WithHeaders("Content-Type", "Authorization", "X-Correlation-ID", "X-CSRF-Token", "Idempotency-Key")
        .WithExposedHeaders("X-Correlation-ID").AllowCredentials()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentActor, CurrentActor>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ShopTime>();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddDbContext<AppDbContext>((services, options) => options
    .UseNpgsql(services.GetRequiredService<IOptions<DatabaseSettings>>().Value.DefaultConnection,
        postgres => postgres.CommandTimeout(15))
    .AddInterceptors(services.GetRequiredService<AuditSaveChangesInterceptor>()));
var storageSettings = builder.Configuration.GetSection("Storage").Get<StorageSettings>() ?? new StorageSettings();
storageSettings.LocalRoot = Path.GetFullPath(storageSettings.LocalRoot, builder.Environment.ContentRootPath);
builder.Services.AddCatalogInfrastructure(storageSettings);
builder.Services.AddSingleton(builder.Configuration.GetSection(Application.Sales.ShopSettings.SectionName).Get<Application.Sales.ShopSettings>() ?? new Application.Sales.ShopSettings());
var allowEphemeralKey = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
var ephemeralKey = JwtSettings.EphemeralKey();
builder.Services.AddOptions<JwtSettings>().BindConfiguration("Jwt")
    // Development without a configured key: sign-ins end whenever the API restarts.
    .PostConfigure(x => { if (!JwtSettings.IsValidKey(x.SigningKey) && allowEphemeralKey) x.SigningKey = ephemeralKey; })
    .Validate(x => JwtSettings.IsValidKey(x.SigningKey),
        $"Configure Jwt:SigningKey (at least {JwtSettings.MinKeyBytes} bytes) using secrets or environment variables.")
    .ValidateOnStart();
builder.Services.AddSingleton(services => services.GetRequiredService<IOptions<JwtSettings>>().Value);
builder.Services.AddSingleton<IAccessTokenIssuer, AccessTokenIssuer>();
var sessionSettings = builder.Configuration.GetSection("Auth").Get<SessionSettings>() ?? new SessionSettings();
builder.Services.AddIdentityInfrastructure(sessionSettings);
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<IdentitySeedService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<JwtSettings>((options, settings) => JwtSetup.Configure(options, settings));
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddAuthorizationBuilder().AddPermissionPolicies();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthController.LoginRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, ct) =>
        await Results.Problem(statusCode: 429, title: "Too many requests.", detail: "Too many attempts. Wait a minute and try again.").ExecuteAsync(context.HttpContext);
});
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
    context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.Items["CorrelationId"];
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddControllers(options => options.Filters.Add<FluentValidationFilter>())
    // Enums travel as names ("Posted", "Grn"); numbers are still accepted on input.
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddValidatorsFromAssemblyContaining<ICurrentActor>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"]);
var app = builder.Build();
app.UseMiddleware<CorrelationMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages(async status =>
{
    await Results.Problem(statusCode: status.HttpContext.Response.StatusCode).ExecuteAsync(status.HttpContext);
});
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseCors();
Directory.CreateDirectory(storageSettings.LocalRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(storageSettings.LocalRoot),
    RequestPath = storageSettings.PublicPath,
    ServeUnknownFileTypes = false,
    OnPrepareResponse = context =>
    {
        // Stored file names are random and never reused, so they can be cached for a long time.
        context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        context.Context.Response.Headers.XContentTypeOptions = "nosniff";
    },
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();
app.Run();

public partial class Program;
