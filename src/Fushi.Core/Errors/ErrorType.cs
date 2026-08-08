namespace Fushi.Core.Errors;

/// <summary>
/// Classifies why an operation failed, independently of what specifically went
/// wrong.
/// </summary>
/// <remarks>
/// The category drives presentation: the Discord layer maps it to an embed
/// colour and to whether the reply is ephemeral, so a user never sees a
/// validation complaint styled like an internal fault. Keeping that decision on
/// a small closed set means adding a new error never requires touching the
/// presentation switch.
/// </remarks>
public enum ErrorType
{
    /// <summary>
    /// No failure. Reserved for <see cref="Error.None"/>.
    /// </summary>
    None = 0,

    /// <summary>
    /// The request was malformed or self-contradictory. The caller can fix it
    /// by supplying different input.
    /// </summary>
    Validation = 1,

    /// <summary>
    /// The addressed entity does not exist, or is not visible to the caller.
    /// </summary>
    NotFound = 2,

    /// <summary>
    /// The request was well-formed but conflicts with the current state, such
    /// as voting on a cycle that has already closed.
    /// </summary>
    Conflict = 3,

    /// <summary>
    /// The caller is known but lacks the permission the request requires.
    /// </summary>
    Forbidden = 4,

    /// <summary>
    /// A dependency failed or an invariant broke. The caller cannot correct
    /// this by changing the request.
    /// </summary>
    Unexpected = 5,
}
