using Fushi.Core.Errors;

namespace Fushi.Core.Results;

/// <summary>
/// The parts of a result that do not depend on the type of value it carries.
/// </summary>
/// <remarks>
/// Exists so that generic code can inspect a result without knowing whether it
/// is a <see cref="Result"/> or a <see cref="Result{T}"/>. The pipeline needs
/// exactly this: its behaviours are generic over the response type, and the
/// unit-of-work stage has to ask "did this succeed" before deciding whether to
/// commit.
/// <br/>
/// Reaching this interface from a <see cref="Result"/> boxes it, since that type
/// is a struct. Prefer the concrete type wherever it is known; the boxing is
/// worth accepting only in the generic code that has no alternative.
/// </remarks>
public interface IResult
{
    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    bool IsFailure { get; }

    /// <summary>
    /// Gets the failure this result carries.
    /// </summary>
    /// <value>
    /// The error describing the failure, or <see cref="Errors.Error.None"/> when
    /// the operation succeeded.
    /// </value>
    Error Error { get; }
}
