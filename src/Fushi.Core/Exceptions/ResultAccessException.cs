using Fushi.Core.Errors;

namespace Fushi.Core.Exceptions;

/// <summary>
/// Thrown when the value of a failed result is read.
/// </summary>
/// <remarks>
/// This exception always indicates a bug at the call site rather than a runtime
/// condition: the caller reached for a value that a failed result never had.
/// It is deliberately distinct from <see cref="FushiException"/> so that a
/// catch-all around domain failures cannot accidentally swallow a programming
/// mistake and turn it into a silent wrong answer.
/// </remarks>
/// <seealso cref="Fushi.Core.Results.Result{T}.Value"/>
public sealed class ResultAccessException : InvalidOperationException
{
    /// <summary>
    /// Initialises the exception for the failure that was masked by the access.
    /// </summary>
    /// <param name="error">The error carried by the failed result.</param>
    public ResultAccessException(Error error)
        : base($"Cannot read the value of a failed result. The result failed with '{error}'.")
    {
        Error = error;
    }

    /// <summary>
    /// Gets the error carried by the result whose value was read.
    /// </summary>
    /// <remarks>
    /// Preserved so that a top-level handler can still report the original
    /// failure rather than only the access violation that surfaced it.
    /// </remarks>
    public Error Error { get; }
}
