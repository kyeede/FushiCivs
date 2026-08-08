using Fushi.Core.Errors;

namespace Fushi.Core.Exceptions;

/// <summary>
/// Base type for exceptions raised by Fushi that carry a structured
/// <see cref="Errors.Error"/> alongside the message.
/// </summary>
/// <remarks>
/// Fushi has two failure channels and this is the narrower one. Anything a user
/// could plausibly cause travels as a <see cref="Fushi.Core.Results.Result"/>;
/// exceptions are for conditions the program cannot continue through, and exist
/// mainly so that the outermost handler has something typed to catch and can
/// still report a proper error code rather than a bare stack trace.
/// </remarks>
public class FushiException : Exception
{
    /// <summary>
    /// Initialises the exception from a structured error.
    /// </summary>
    /// <param name="error">
    /// The failure being raised. Must not be <see cref="Errors.Error.None"/>,
    /// since an exception without a failure has nothing to report.
    /// </param>
    /// <param name="innerException">
    /// The underlying cause, when this exception is wrapping one.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="error"/> is <see cref="Errors.Error.None"/>.
    /// </exception>
    public FushiException(Error error, Exception? innerException = null)
        : base(Describe(error), innerException)
    {
        if (error.IsNone)
        {
            throw new ArgumentException("An exception requires a concrete error.", nameof(error));
        }

        Error = error;
    }

    /// <summary>
    /// Initialises the exception from a message alone, classifying it as
    /// <see cref="ErrorType.Unexpected"/>.
    /// </summary>
    /// <param name="message">The human-readable explanation.</param>
    /// <param name="innerException">
    /// The underlying cause, when this exception is wrapping one.
    /// </param>
    public FushiException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = Error.Unexpected("Fushi.Unexpected", message);
    }

    /// <summary>
    /// Gets the structured failure this exception reports.
    /// </summary>
    public Error Error { get; }

    private static string Describe(Error error) => error.IsNone ? "An error occurred." : error.Description;
}
