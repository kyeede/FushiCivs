using Fushi.Core.Errors;

namespace Fushi.Core.Results;

/// <summary>
/// The outcome of an operation that either succeeds or fails with a described
/// <see cref="Errors.Error"/>, and that produces no value on success.
/// </summary>
/// <remarks>
/// Returning a result instead of throwing makes failure part of the method
/// signature. A caller cannot forget that an operation can fail, because the
/// only way to reach the success path is to acknowledge the other one.
/// <br/>
/// The type is a <see langword="struct"/> and a successful result is the
/// default value, so the common path allocates nothing.
/// </remarks>
/// <seealso cref="Result{T}"/>
public readonly record struct Result : IResult
{
    private Result(Error error)
    {
        if (error.IsNone)
        {
            throw new ArgumentException(
                "A failed result requires a concrete error. Use Success() instead.",
                nameof(error)
            );
        }

        Error = error;
    }

    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    public bool IsSuccess => Error.IsNone;

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the failure this result carries.
    /// </summary>
    /// <value>
    /// The error describing the failure, or <see cref="Errors.Error.None"/>
    /// when the operation succeeded.
    /// </value>
    public Error Error { get; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <returns>A result in the success state.</returns>
    public static Result Success() => default;

    /// <summary>
    /// Creates a failed result carrying the given error.
    /// </summary>
    /// <param name="error">The failure. Must not be <see cref="Errors.Error.None"/>.</param>
    /// <returns>A result in the failure state.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="error"/> is <see cref="Errors.Error.None"/>.
    /// </exception>
    public static Result Failure(Error error) => new(error);

    /// <summary>
    /// Collapses both states into a single value by applying whichever function
    /// matches the state this result is in.
    /// </summary>
    /// <typeparam name="TOut">The type both branches produce.</typeparam>
    /// <param name="onSuccess">Invoked when the operation succeeded.</param>
    /// <param name="onFailure">
    /// Invoked with the error when the operation failed.
    /// </param>
    /// <returns>The value produced by the branch that ran.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="onSuccess"/> or <paramref name="onFailure"/> is
    /// <see langword="null"/>.
    /// </exception>
    public TOut Match<TOut>(Func<TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess ? onSuccess() : onFailure(Error);
    }

    /// <summary>
    /// Creates a failed result from an error, so that a method returning
    /// <see cref="Result"/> can <c>return</c> an error directly.
    /// </summary>
    /// <param name="error">The failure to wrap.</param>
    /// <returns>A result in the failure state.</returns>
    public static implicit operator Result(Error error) => Failure(error);

    /// <summary>
    /// Creates a failed result from an error.
    /// </summary>
    /// <remarks>
    /// The named alternative to the implicit conversion, provided because an
    /// implicit operator alone is not discoverable from every language.
    /// </remarks>
    /// <param name="error">The failure to wrap.</param>
    /// <returns>A result in the failure state.</returns>
    public static Result FromError(Error error) => Failure(error);

    /// <inheritdoc/>
    public override string ToString() => IsSuccess ? "Success" : $"Failure({Error})";
}
