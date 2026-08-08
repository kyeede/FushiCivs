namespace Fushi.Gateway;

/// <summary>
/// Reports whether the bot has finished connecting to Discord, and lets a caller
/// wait until it has.
/// </summary>
/// <remarks>
/// Logging in returns long before the bot is usable. Discord sends the guild,
/// channel, and role data over the gateway after the handshake, and until the
/// <c>Ready</c> frame arrives the socket cache is empty — so a component that
/// starts work the moment the host is up would see a bot in no guilds at all.
/// <br/>
/// Anything that has to touch Discord during startup, such as registering slash
/// commands, waits on this first. Everything else can ignore it, because a
/// command arriving from Discord implies the connection is already up.
/// </remarks>
public interface IGatewayReadiness
{
    /// <summary>
    /// Gets a value indicating whether the gateway has reported itself ready.
    /// </summary>
    /// <value>
    /// <see langword="true"/> once the <c>Ready</c> frame has been seen. It stays
    /// <see langword="true"/> across a later disconnect, because the caches
    /// survive one and Discord.Net resumes the session rather than starting over.
    /// </value>
    bool IsReady { get; }

    /// <summary>
    /// Waits until the gateway reports itself ready.
    /// </summary>
    /// <remarks>
    /// Returns immediately when <see cref="IsReady"/> is already
    /// <see langword="true"/>. There is no timeout parameter on purpose: a caller
    /// that wants one supplies a cancelled-after token, which composes with host
    /// shutdown instead of racing it.
    /// </remarks>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>A task that completes when the bot is connected and populated.</returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was cancelled, or the gateway gave up
    /// on connecting. The second case is a cancellation rather than a fault
    /// because the failure has already been logged and rethrown where it
    /// happened; repeating it here would only give every waiter its own copy of
    /// the same stack trace.
    /// </exception>
    Task WaitForReadyAsync(CancellationToken cancellationToken = default);
}
