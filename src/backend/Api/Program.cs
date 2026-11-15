using System.Diagnostics;
using Api.Configuration;
using Api.Errors;
using Api.Health;
using Api.Identity;
using Api.Observability;
using Api.Validation;
using Application.Common;
using FluentValidation;
using Infrastructure.Persistence;
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
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
    context.ProblemDetails.Extensions["correlationId"] = context.HttpContext.Items["CorrelationId"];
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddControllers(options => options.Filters.Add<FluentValidationFilter>());
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
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();
app.Run();

public partial class Program;
