namespace Fushi.Core.Errors;

/// <summary>
/// A described, categorised failure returned from an operation that is expected
/// to be able to fail.
/// </summary>
/// <remarks>
/// Expected failures — a closed cycle, a duplicate vote, an unknown code — are
/// ordinary outcomes of a correctly working system, so they travel as values
/// inside a <see cref="Fushi.Core.Results.Result"/> rather than as exceptions.
/// Exceptions stay reserved for conditions that indicate the program itself is
/// wrong.
/// <br/>
/// <see cref="Code"/> is a stable machine-readable key in
/// <c>Area.Reason</c> form, safe to switch on and to use as a localisation key.
/// <see cref="Description"/> is prose for a human and may be reworded freely.
/// </remarks>
/// <example>
/// <code>
/// public static Error NotOpen(ShortCode code) => Error.Conflict(
///     "Cycle.NotOpen",
///     $"Cycle {code} is not currently accepting votes.");
/// </code>
/// </example>
public readonly record struct Error
{
    private Error(string code, string description, ErrorType type)
    {
        Code = code;
        Description = description;
        Type = type;
    }

    /// <summary>
    /// Gets the absence of an error, used by successful results.
    /// </summary>
    /// <value>
    /// The default value of the type, so that a zeroed <see cref="Error"/> and
    /// <see cref="None"/> are indistinguishable.
    /// </value>
    public static Error None => default;

    /// <summary>
    /// Gets the stable machine-readable identifier for this failure.
    /// </summary>
    /// <value>
    /// A dotted key such as <c>Submission.NotFound</c>, or an empty string for
    /// <see cref="None"/>.
    /// </value>
    public string Code => field ?? string.Empty;

    /// <summary>
    /// Gets the human-readable explanation of the failure.
    /// </summary>
    /// <value>
    /// A complete sentence suitable for showing to a user, or an empty string
    /// for <see cref="None"/>.
    /// </value>
    public string Description => field ?? string.Empty;

    /// <summary>
    /// Gets the category this failure belongs to.
    /// </summary>
    public ErrorType Type { get; }

    /// <summary>
    /// Gets a value indicating whether this instance represents the absence of
    /// a failure.
    /// </summary>
    public bool IsNone => Type == ErrorType.None;

    /// <summary>
    /// Creates a <see cref="ErrorType.Validation"/> error for input the caller
    /// can correct.
    /// </summary>
    /// <param name="code">The stable machine-readable key.</param>
    /// <param name="description">The human-readable explanation.</param>
    /// <returns>The constructed error.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="code"/> or <paramref name="description"/> is empty or
    /// consists only of white space.
    /// </exception>
    public static Error Validation(string code, string description)
        => Create(code, description, ErrorType.Validation);

    /// <summary>
    /// Creates a <see cref="ErrorType.NotFound"/> error for an entity that does
    /// not exist or is not visible.
    /// </summary>
    /// <param name="code">The stable machine-readable key.</param>
    /// <param name="description">The human-readable explanation.</param>
    /// <returns>The constructed error.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="code"/> or <paramref name="description"/> is empty or
    /// consists only of white space.
    /// </exception>
    public static Error NotFound(string code, string description)
        => Create(code, description, ErrorType.NotFound);

    /// <summary>
    /// Creates a <see cref="ErrorType.Conflict"/> error for a request that
    /// contradicts the current state.
    /// </summary>
    /// <param name="code">The stable machine-readable key.</param>
    /// <param name="description">The human-readable explanation.</param>
    /// <returns>The constructed error.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="code"/> or <paramref name="description"/> is empty or
    /// consists only of white space.
    /// </exception>
    public static Error Conflict(string code, string description)
        => Create(code, description, ErrorType.Conflict);

    /// <summary>
    /// Creates a <see cref="ErrorType.Forbidden"/> error for a caller that
    /// lacks the required permission.
    /// </summary>
    /// <param name="code">The stable machine-readable key.</param>
    /// <param name="description">The human-readable explanation.</param>
    /// <returns>The constructed error.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="code"/> or <paramref name="description"/> is empty or
    /// consists only of white space.
    /// </exception>
    public static Error Forbidden(string code, string description)
        => Create(code, description, ErrorType.Forbidden);

    /// <summary>
    /// Creates an <see cref="ErrorType.Unexpected"/> error for a broken
    /// invariant or a failed dependency.
    /// </summary>
    /// <param name="code">The stable machine-readable key.</param>
    /// <param name="description">The human-readable explanation.</param>
    /// <returns>The constructed error.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="code"/> or <paramref name="description"/> is empty or
    /// consists only of white space.
    /// </exception>
    public static Error Unexpected(string code, string description)
        => Create(code, description, ErrorType.Unexpected);

    private static Error Create(string code, string description, ErrorType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new Error(code, description, type);
    }

    /// <inheritdoc/>
    public override string ToString() => IsNone ? nameof(None) : $"{Code}: {Description}";
}
