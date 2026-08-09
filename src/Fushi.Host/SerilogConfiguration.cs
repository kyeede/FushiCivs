using System.Globalization;

using Serilog;
using Serilog.Events;

namespace Fushi.Host;

/// <summary>
/// Builds the logging pipeline.
/// </summary>
/// <remarks>
/// Configuration first, code second. Everything here is a default that
/// <c>appsettings.json</c> or an environment variable can override, which is what
/// lets an operator turn one category up to debug without a rebuild. The code
/// exists so that a host with no logging configuration at all still writes
/// something readable to the console rather than nothing.
/// </remarks>
internal static class SerilogConfiguration
{
    // The source context is included because a bot's log is read by category — the
    // scheduler, the gateway, one handler — far more often than it is read top to
    // bottom.
    private const string CONSOLE_TEMPLATE =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Configures the logger for a running host.
    /// </summary>
    /// <param name="context">The host being built, for its configuration.</param>
    /// <param name="services">The container, so sinks can resolve dependencies.</param>
    /// <param name="configuration">The logger configuration to fill in.</param>
    public static void Configure(
        HostBuilderContext context,
        IServiceProvider services,
        LoggerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(configuration);

        // Invariant rather than the machine's locale. A log read by two people on
        // differently configured machines should not render a timestamp or a count
        // two different ways.
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .WriteTo.Console(
                outputTemplate: CONSOLE_TEMPLATE,
                formatProvider: CultureInfo.InvariantCulture);

        // Discord.Net logs every heartbeat and every cached entity at debug through
        // the adapter in the gateway. Useful when diagnosing a connection, and
        // overwhelming the rest of the time, so its floor is raised here rather
        // than the whole application's being lowered.
        configuration.MinimumLevel.Override("Discord", LogEventLevel.Information);

        // EF Core writes the text of every command it executes at information,
        // which on a bot means a paragraph per vote. The queries are still
        // available by turning this category down deliberately.
        configuration.MinimumLevel.Override(
            "Microsoft.EntityFrameworkCore.Database.Command",
            LogEventLevel.Warning);

        // The health endpoints are polled every few seconds by whatever is
        // supervising the process. Logging each request would make the log a record
        // of the probe rather than of the bot.
        configuration.MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning);
    }
}
