using Fushi.Core.Errors;

using Microsoft.Extensions.Logging;

namespace Fushi.Application.Logging;

/// <summary>
/// Log messages emitted by the request pipeline.
/// </summary>
/// <remarks>
/// Every log call in this project goes through a partial method like these. The
/// <see cref="LoggerMessageAttribute"/> source generator turns each one into a
/// strongly typed, pre-compiled write with no boxing of the arguments and no
/// format string parsed at run time. It also means a message cannot be logged
/// with the wrong number or type of arguments, because the call site is checked
/// by the compiler rather than at the moment the line executes.
/// <br/>
/// Event identifiers are grouped by feature and never reused, so a dashboard
/// built on them keeps working across releases. The pipeline owns 1000 to 1099.
/// </remarks>
internal static partial class PipelineLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Debug,
        Message = "Dispatching {RequestName}")]
    public static partial void Dispatching(ILogger logger, string requestName);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "{RequestName} succeeded in {ElapsedMilliseconds} ms")]
    public static partial void Succeeded(
        ILogger logger,
        string requestName,
        long elapsedMilliseconds);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "{RequestName} failed in {ElapsedMilliseconds} ms with {ErrorCode}: {ErrorDescription}")]
    public static partial void Failed(
        ILogger logger,
        string requestName,
        long elapsedMilliseconds,
        string errorCode,
        string errorDescription);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Error,
        Message = "{RequestName} threw after {ElapsedMilliseconds} ms")]
    public static partial void Faulted(
        ILogger logger,
        string requestName,
        long elapsedMilliseconds,
        Exception exception);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Debug,
        Message = "{RequestName} rejected by validation: {Failures}")]
    public static partial void ValidationRejected(
        ILogger logger,
        string requestName,
        string failures);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Debug,
        Message = "Committed {RowCount} row(s) for {RequestName}")]
    public static partial void Committed(ILogger logger, int rowCount, string requestName);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Debug,
        Message = "{RequestName} returned a failure, so nothing was committed")]
    public static partial void RolledBack(ILogger logger, string requestName);

    /// <summary>
    /// Writes the outcome of a request at the level its error category deserves.
    /// </summary>
    /// <remarks>
    /// A user mistyping a code is not an incident and must not be logged as one;
    /// a broken invariant is, and must not be lost among them. Routing on
    /// <see cref="ErrorType"/> keeps that judgement in one place rather than
    /// leaving each behaviour to guess.
    /// </remarks>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="requestName">The request's type name.</param>
    /// <param name="elapsedMilliseconds">How long handling took.</param>
    /// <param name="error">The failure to report.</param>
    public static void Outcome(
        ILogger logger,
        string requestName,
        long elapsedMilliseconds,
        Error error)
    {
        if (error.IsNone)
        {
            Succeeded(logger, requestName, elapsedMilliseconds);
            return;
        }

        Failed(logger, requestName, elapsedMilliseconds, error.Code, error.Description);
    }
}
