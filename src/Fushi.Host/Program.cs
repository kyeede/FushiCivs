using Fushi.Application;
using Fushi.Gateway;
using Fushi.Host;
using Fushi.Host.Health;
using Fushi.Host.Options;
using Fushi.Host.Scheduling;
using Fushi.Infrastructure;
using Fushi.Interactions;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;

using Serilog;

// The composition root, and the only file in the solution that is allowed to know
// about every layer at once. Everything below is wiring: which implementation
// answers which abstraction, and what runs on a timer. No rule about voting,
// cycles, or submissions is decided here — if one ever appears in this file, it
// belongs in Fushi.Application or Fushi.Core instead.

// Before the builder, because DOTNET_ENVIRONMENT decides which appsettings file
// is layered on and that is read while the builder is constructed.
DotEnv.Load();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog(SerilogConfiguration.Configure);

builder.Services.AddOptions<SchedulerOptions>()
    .Bind(builder.Configuration.GetSection(SchedulerOptions.SECTION))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Order is presentation-independent — the container resolves lazily — but it is
// written inside-out on purpose, so the file reads as the architecture does.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddGateway(builder.Configuration);
builder.Services.AddInteractions(builder.Configuration);

// Registration first: the other two work through guilds that already have a row,
// and this is what puts one there. Ordering is presentational — all three start
// together and each waits for the gateway itself — but it reads in the order the
// data comes into existence.
builder.Services.AddHostedService<GuildRegistrar>();
builder.Services.AddHostedService<CycleScheduler>();
builder.Services.AddHostedService<IntakeSweeper>();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: [Probes.READY])
    .AddCheck<GatewayHealthCheck>("gateway", tags: [Probes.READY]);

builder.Services.AddTelemetry(builder.Configuration);

WebApplication app = builder.Build();

// Liveness answers "is this process running and able to respond at all", so it
// deliberately runs no checks. A dependency being down is a readiness question,
// and answering it here would get the process restarted for something a restart
// cannot fix.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains(Probes.READY),
});

await app.MigrateIfConfiguredAsync();

await app.RunAsync();

/// <summary>
/// The tags that select which health checks answer which probe.
/// </summary>
internal static class Probes
{
    /// <summary>
    /// Marks a check as part of the readiness probe.
    /// </summary>
    public const string READY = "ready";
}
