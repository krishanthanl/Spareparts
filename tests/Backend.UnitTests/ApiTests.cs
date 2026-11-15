using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Configuration;
using Microsoft.AspNetCore.Mvc;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.UnitTests;

public sealed class ApiTests
{
    [Fact]
    public async Task Liveness_is_independent_of_database_and_readiness_fails_safely()
    {
        await using var factory = new FoundationFactory();
        var client = factory.CreateClient();
        var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.True(live.Headers.Contains("X-Correlation-ID"));
        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.DoesNotContain("Password", await ready.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("http://localhost:5173", true)]
    [InlineData("https://untrusted.example", false)]
    public async Task Cors_only_allows_configured_origins(string origin, bool allowed)
    {
        await using var factory = new FoundationFactory();
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/health/live");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        var response = await client.SendAsync(request);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Errors_are_ProblemDetails_without_exception_secrets()
    {
        await using var factory = new FoundationFactory();
        var client = factory.CreateClient();
        var response = await client.GetAsync("/test/error");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("do-not-expose", text);
        Assert.Contains("traceId", text);
        var missing = await client.GetAsync("/not-found");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Validation_errors_use_field_errors()
    {
        await using var factory = new FoundationFactory();
        var response = await factory.CreateClient().PostAsJsonAsync("/test/validation", new { Name = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty("Name", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("http://localhost:5173/path")]
    [InlineData("javascript:alert(1)")]
    public void Cors_configuration_rejects_unsafe_origins(string origin) => Assert.False(CorsSettings.IsValidOrigin(origin));

    [Theory]
    [InlineData("")]
    [InlineData("not a connection")]
    public void Connection_validation_fails_without_echoing_input(string connection) => Assert.False(DatabaseSettings.IsValid(connection));
}

public sealed class FoundationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Timeout=1;Pooling=false",
            ["Cors:Origins:0"] = "http://localhost:5173",
            ["Cors:Origins:1"] = "http://localhost:5174"
        }));
        builder.ConfigureServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(TestController).Assembly);
            services.AddSingleton<IValidator<TestInput>, TestInputValidator>();
        });
    }
}

[ApiController]
[Route("test")]
public sealed class TestController : ControllerBase
{
    [HttpGet("error")]
    public IActionResult Error() => throw new InvalidOperationException("do-not-expose");
    [HttpPost("validation")]
    public IActionResult ValidateInput(TestInput input) => Ok();
}
public sealed record TestInput(string Name);
public sealed class TestInputValidator : AbstractValidator<TestInput>
{
    public TestInputValidator() => RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
}