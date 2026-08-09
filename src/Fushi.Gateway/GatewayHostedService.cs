using Discord;
using Discord.WebSocket;
using Fushi.Gateway.Logging;
using Fushi.Gateway.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fushi.Gateway;

/// <summary>
/// Owns the socket client's connection for the lifetime of the host.
/// </summary>
/// <remarks>
/// There is no reconnect loop here, and there must not be one. Discord.Net
/// reconnects internally: it backs off, resumes the session where it can, and
/// starts a fresh one where it cannot, all inside
/// <see cref="DiscordSocketClient.StartAsync"/>. A supervising loop layered on top
/// of that does not add resilience, it competes with it — two things both trying
/// to reconnect produce identify-rate-limit bans, which Discord applies to the
/// whole application rather than the connection that earned them. Disconnects and
/// reconnects are logged so the behaviour is visible, and nothing more is done
/// about them.
/// <br/>
/// The service also bridges the library's own log stream onto
/// <see cref="ILogger"/>, so that gateway diagnostics land in the same sink as
/// everything else instead of on the console alone.
/// </remarks>
internal sealed class GatewayHostedService : BackgroundService
{
    private readonly DiscordSocketClient _client;
    private readonly GatewayReadiness _readiness;
    private readonly DiscordOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<GatewayHostedService> _logger;

    /// <summary>
    /// When the connection was last lost, as a <see cref="TimeProvider"/>
    /// timestamp, or zero when the client has never been disconnected.
    /// </summary>
    /// <remarks>
    /// Written from Discord.Net's event dispatch and read from it again, so it
    /// goes through <see cref="Interlocked"/> rather than a plain assignment. Zero
    /// doubles as the "no outage in progress" marker, which is what distinguishes
    /// the first connection of the process from a reconnection.
    /// </remarks>
    private long _disconnectedAt;

    /// <summary>
    /// Initialises the service.
    /// </summary>
    /// <param name="client">The socket client to connect and disconnect.</param>
    /// <param name="readiness">The signal raised once Discord reports ready.</param>
    /// <param name="options">The connection settings, already validated.</param>
    /// <param name="clock">
    /// Supplies the timestamps an outage is measured with.
    /// <see cref="TimeProvider"/> rather than <c>Stopwatch</c> so a test can drive
    /// the clock instead of waiting on it.
    /// </param>
    /// <param name="logger">The logger to write to.</param>
    /// <exception cref="ArgumentNullException">
    /// Any argument is <see langword="null"/>.
    /// </exception>
    public GatewayHostedService(
        DiscordSocketClient client,
        GatewayReadiness readiness,
        IOptions<DiscordOptions> options,
        TimeProvider clock,
        ILogger<GatewayHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _client = client;
        _readiness = readiness;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Logs in, starts the receive loop, and stays out of the way until the host
    /// stops.
    /// </summary>
    /// <remarks>
    /// The infinite delay is the point of the method rather than a placeholder.
    /// Once <see cref="DiscordSocketClient.StartAsync"/> returns, the library is
    /// running its own connection on its own threads, and this task exists only to
    /// keep a handle on the host's shutdown token so the client can be logged out
    /// when it is cancelled.
    /// </remarks>
    /// <param name="stoppingToken">Cancelled when the host begins shutting down.</param>
    /// <returns>A task that completes when the client has been logged out.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Subscribe();

        try
        {
            GatewayLog.Connecting(_logger);

            await _client.LoginAsync(TokenType.Bot, _options.Token);
            await _client.StartAsync();

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // The ordinary way this method ends. Rethrown rather than swallowed so
            // the host sees a cancelled task and not a completed one, which is how
            // it tells an orderly stop from a service that quietly gave up.
            throw;
        }
        catch (Exception exception)
        {
            // The outermost boundary of the service, and the one place a bare
            // catch is right: whatever went wrong, it has to be logged with the
            // token nowhere near the message before the host is allowed to tear
            // everything down.
            GatewayLog.LoginFailed(_logger, exception);
            throw;
        }
        finally
        {
            Unsubscribe();

            // Nobody may be left waiting on a readiness signal that is never
            // coming, whether the process is stopping normally or because login
            // failed.
            _readiness.Abandon();

            await ShutDownAsync();
        }
    }

    /// <summary>
    /// Stops and logs out the client, reporting rather than raising any failure.
    /// </summary>
    /// <remarks>
    /// Runs from a <see langword="finally"/> block, which is why it cannot be
    /// allowed to throw. An exception raised while unwinding replaces the one that
    /// started the unwinding, so a hiccup closing a socket would erase the
    /// cancellation — or the login failure — that is the actual reason the process
    /// is stopping.
    /// </remarks>
    /// <returns>A task that completes once the attempt has been made.</returns>
    private async Task ShutDownAsync()
    {
        try
        {
            await _client.StopAsync();
            await _client.LogoutAsync();

            GatewayLog.ShutDown(_logger);
        }
#pragma warning disable CA1031 // Catching everything is the requirement here, not
        // an oversight: this runs while unwinding, and any exception it lets
        // escape replaces the cancellation or login failure that is the real
        // reason the process is stopping. The suppression is scoped to this one
        // handler rather than the file.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            GatewayLog.ShutDownFailed(_logger, exception);
        }
    }

    private void Subscribe()
    {
        _client.Log += OnLogAsync;
        _client.Ready += OnReadyAsync;
        _client.Connected += OnConnectedAsync;
        _client.Disconnected += OnDisconnectedAsync;
    }

    private void Unsubscribe()
    {
        _client.Log -= OnLogAsync;
        _client.Ready -= OnReadyAsync;
        _client.Connected -= OnConnectedAsync;
        _client.Disconnected -= OnDisconnectedAsync;
    }

    private Task OnLogAsync(LogMessage message)
    {
        // Translated into a local rather than inline, because an argument that is
        // a method call is evaluated whether or not the level is enabled, and the
        // library raises this event for every gateway frame.
        var level = DiscordClientFactory.ToLogLevel(message.Severity);

        GatewayLog.Library(
            _logger,
            level,
            message.Source ?? "Discord",
            message.Message ?? string.Empty,
            message.Exception);

        return Task.CompletedTask;
    }

    private Task OnReadyAsync()
    {
        // Signalled before logging, so that anything blocked on readiness is
        // released at the earliest correct moment rather than behind a log write.
        _readiness.Signal();

        SocketSelfUser? self = _client.CurrentUser;
        GatewayLog.Ready(
            _logger,
            self?.Username ?? "unknown",
            self?.Id ?? 0UL,
            _client.Guilds.Count);

        return Task.CompletedTask;
    }

    private Task OnConnectedAsync()
    {
        long disconnectedAt = Interlocked.Exchange(ref _disconnectedAt, 0L);
        if (disconnectedAt == 0L)
        {
            // The first connection of the process. Reported by the Ready handler
            // once the caches are populated, which is the moment that matters.
            return Task.CompletedTask;
        }

        TimeSpan outage = _clock.GetElapsedTime(disconnectedAt);
        if (outage.TotalSeconds > _options.ReconnectBackoffCeilingSeconds)
        {
            GatewayLog.ReconnectedSlowly(
                _logger,
                outage.TotalSeconds,
                _options.ReconnectBackoffCeilingSeconds);
        }
        else
        {
            GatewayLog.Reconnected(_logger, outage.TotalSeconds);
        }

        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(Exception exception)
    {
        Interlocked.Exchange(ref _disconnectedAt, _clock.GetTimestamp());

        GatewayLog.Disconnected(
            _logger,
            exception?.Message ?? "no reason given",
            exception);

        return Task.CompletedTask;
    }
}
