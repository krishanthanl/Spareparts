using System.Net;
using Api.Observability;
using Application.Identity;
using Microsoft.Extensions.Configuration;
using Sentry;

namespace Backend.UnitTests;

[Collection(nameof(SentryDeliveryTests))]
public sealed class SentryTests
{
    [Fact]
    public void Sentry_is_off_without_a_dsn()
    {
        Assert.False(SentrySetup.IsEnabled(new ConfigurationBuilder().Build()));
        Assert.False(SentrySetup.IsEnabled(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Sentry:Dsn"] = " " }).Build()));
        Assert.True(SentrySetup.IsEnabled(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Sentry:Dsn"] = "https://k@o1.ingest.sentry.io/1" }).Build()));
    }

    [Theory]
    [InlineData("https://abc123@o1.ingest.sentry.io/456", true)]
    [InlineData("https://abc123@o1.ingest.us.sentry.io/456/", true)]
    [InlineData("https://o1.ingest.sentry.io/456", false)] // no public key: the Azure crash
    [InlineData("https://sentry.io/organizations/shop/projects/api/", false)]
    [InlineData("abc123", false)]
    [InlineData("ftp://abc@host/1", false)]
    [InlineData("https://abc@host", false)] // no project id
    public void Only_well_formed_dsns_turn_sentry_on(string dsn, bool valid) => Assert.Equal(valid, SentrySetup.IsValidDsn(dsn));

    [Fact]
    public async Task An_invalid_dsn_does_not_stop_the_api_starting()
    {
        Environment.SetEnvironmentVariable("Sentry__Dsn", "https://o1.ingest.sentry.io/456");
        try
        {
            await using var factory = new AuthFactory();
            var response = await factory.CreateClient().GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Sentry__Dsn", null);
        }
    }

    [Theory]
    [InlineData(typeof(Application.Common.NotFoundException))]
    [InlineData(typeof(Application.Common.ConflictException))]
    [InlineData(typeof(Application.Common.ForbiddenException))]
    [InlineData(typeof(OperationCanceledException))]
    public void Expected_outcomes_are_never_sent(Type type)
    {
        var exception = type == typeof(OperationCanceledException) ? new OperationCanceledException() : (Exception)Activator.CreateInstance(type, "x")!;
        Assert.Null(SentrySetup.Scrub(new SentryEvent(exception), new SentryHint()));
        Assert.Null(SentrySetup.Scrub(new SentryEvent(new FluentValidation.ValidationException("bad")), new SentryHint()));
    }

    [Fact]
    public void Unexpected_errors_are_sent_without_personal_data()
    {
        var e = new SentryEvent(new InvalidOperationException("boom"));
        e.Request.Cookies = "session=secret";
        e.Request.QueryString = "?email=sam@example.com";
        e.Request.Data = new { Name = "Sam" };
        e.Request.Headers["Authorization"] = "Bearer secret";
        e.User = new SentryUser { Id = "u1", Email = "sam@example.com", IpAddress = "203.0.113.5", Username = "sam" };
        e.ServerName = "web-1";

        var sent = SentrySetup.Scrub(e, new SentryHint());

        Assert.NotNull(sent);
        Assert.Null(sent.Request.Cookies);
        Assert.Null(sent.Request.QueryString);
        Assert.Null(sent.Request.Data);
        Assert.Empty(sent.Request.Headers);
        Assert.Equal("u1", sent.User.Id);
        Assert.Null(sent.User.Email);
        Assert.Null(sent.User.IpAddress);
        Assert.Null(sent.User.Username);
        Assert.Null(sent.ServerName);
    }

    [Fact]
    public async Task Test_endpoint_needs_an_admin_and_explains_when_sentry_is_off()
    {
        await using var factory = new AuthFactory();
        var cashier = factory.ClientWith(factory.TokenFor(PermissionKeys.SalesInvoice));
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.PostAsync("/api/diagnostics/sentry-test", null)).StatusCode);

        var admin = factory.ClientWith(factory.TokenFor(PermissionKeys.RolesManage));
        var response = await admin.PostAsync("/api/diagnostics/sentry-test", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Sentry__Dsn", await response.Content.ReadAsStringAsync());
    }
}

/// <summary>Sentry is process-wide, so this runs on its own, with a local listener standing in for sentry.io.</summary>
[CollectionDefinition(nameof(SentryDeliveryTests), DisableParallelization = true)]
public sealed class SentryDeliveryCollection;

[Collection(nameof(SentryDeliveryTests))]
public sealed class SentryDeliveryTests
{
    [Fact]
    public async Task A_request_failure_reaches_sentry_tagged_and_without_the_token()
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var received = new List<string>();
        var receiving = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { return; }
                Stream body = ctx.Request.InputStream;
                if (ctx.Request.Headers["Content-Encoding"] == "gzip") body = new System.IO.Compression.GZipStream(body, System.IO.Compression.CompressionMode.Decompress);
                using (var reader = new StreamReader(body)) { var text = await reader.ReadToEndAsync(); lock (received) received.Add(text); }
                ctx.Response.StatusCode = 200;
                ctx.Response.Close();
            }
        });

        // Same route as an Azure app setting: environment variables are read before startup decides to enable Sentry.
        Environment.SetEnvironmentVariable("Sentry__Dsn", $"http://publickey@127.0.0.1:{port}/1");
        Environment.SetEnvironmentVariable("Sentry__Environment", "test");
        try
        {
            await using var factory = new AuthFactory();
            var token = factory.TokenFor(PermissionKeys.RolesManage);
            var response = await factory.ClientWith(token).PostAsync("/api/diagnostics/sentry-test", null);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var correlationId = problem.RootElement.GetProperty("correlationId").GetString()!;
            await SentrySdk.FlushAsync(TimeSpan.FromSeconds(5));

            string envelope;
            lock (received) envelope = string.Join("\n", received);
            Assert.Contains("SentryTestException", envelope);
            Assert.Contains(correlationId, envelope);
            Assert.DoesNotContain(token, envelope);
            Assert.DoesNotContain("Bearer ey", envelope);
            Assert.Contains("\"environment\":\"test\"", envelope);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Sentry__Dsn", null);
            Environment.SetEnvironmentVariable("Sentry__Environment", null);
        }
        listener.Stop();
        await receiving;
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
