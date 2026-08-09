using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Fushi.Host;

/// <summary>
/// Wires OpenTelemetry into the host.
/// </summary>
public static class TelemetryExtensions
{
    /// <summary>
    /// Adds runtime metrics and tracing, exporting them only when a collector has
    /// been configured.
    /// </summary>
    /// <remarks>
    /// Instrumentation is always collected, even with nowhere to send it. The cost
    /// of collecting is paid by the runtime either way, and having the counters in
    /// process means a diagnostic session can read them without somebody first
    /// standing up a collector.
    /// <br/>
    /// The endpoint is read from <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, which the
    /// OpenTelemetry SDK reads for itself as well. It is checked here only to
    /// decide whether to attach an exporter at all: an exporter pointed at nothing
    /// retries on a timer and fills the log with connection failures.
    /// </remarks>
    /// <param name="services">The container to add to.</param>
    /// <param name="configuration">The configuration to read the endpoint from.</param>
    /// <returns>
    /// <paramref name="services"/>, so registration can be chained.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configuration"/> is
    /// <see langword="null"/>.
    /// </exception>
    public static IServiceCollection AddTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string serviceName = configuration["OTEL_SERVICE_NAME"] ?? "fushi";
        bool exporting = !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithMetrics(metrics =>
            {
                metrics.AddRuntimeInstrumentation();

                if (exporting)
                {
                    metrics.AddOtlpExporter();
                }
            })
            .WithTracing(tracing =>
            {
                if (exporting)
                {
                    tracing.AddOtlpExporter();
                }
            });

        return services;
    }
}
