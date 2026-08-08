using System.Diagnostics.CodeAnalysis;

using Fushi.Core.Errors;
using Fushi.Core.Exceptions;

namespace Fushi.Core.Results;

/// <summary>
/// The outcome of an operation that produces a value of type
/// <typeparamref name="T"/> on success, or a described
/// <see cref="Errors.Error"/> on failure.
/// </summary>
/// <remarks>
/// Deliberately a <see langword="class"/> rather than a
/// <see langword="struct"/>, unlike the non-generic <see cref="Result"/>. A
/// struct is reachable through <c>default</c>, which would produce an instance
/// claiming success while holding a null value — a failure mode that no amount
/// of discipline at the call site can rule out. A reference type makes that
/// state unrepresentable, and the nullable analyser catches the null case
/// instead.
/// </remarks>
/// <typeparam name="T">The type of the value produced on success.</typeparam>
/// <seealso cref="Result"/>
public sealed class Result<T> : IResult
{
    private readonly T? _value;

    private Result(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _value = value;
        Error = Error.None;
        IsSuccess = true;
    }

    private Result(Error error)
    {
        if (error.IsNone)
        {
            throw new ArgumentException(
                "A failed result requires a concrete error. Use Success(value) instead.",
                nameof(error));
        }

        Error = error;
        IsSuccess = false;
    }

    /// <summary>
    /// Gets a value indicating whether the operation succeeded.
    /// </summary>
    [MemberNotNullWhen(true, nameof(_value))]
    public bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    [MemberNotNullWhen(false, nameof(_value))]
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
    /// Gets the value produced by a successful operation.
    /// </summary>
    /// <value>The produced value, which is never <see langword="null"/>.</value>
    /// <exception cref="ResultAccessException">
    /// The operation failed, so no value exists. Check
    /// <see cref="IsSuccess"/> first, or use
    /// <see cref="Fushi.Core.Extensions.ResultExtensions"/> to work with the
    /// value without unwrapping it by hand.
    /// </exception>
    public T Value => IsSuccess ? _value : throw new ResultAccessException(Error);

    /// <summary>
    /// Creates a successful result carrying the given value.
    /// </summary>
    /// <param name="value">The produced value. Must not be <see langword="null"/>.</param>
    /// <returns>A result in the success state.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="value"/> is <see langword="null"/>. A successful result
    /// with no value should be a non-generic <see cref="Result"/> instead.
    /// </exception>
    public static Result<T> Success(T value) => new(value);

    /// <summary>
    /// Creates a failed result carrying the given error.
    /// </summary>
    /// <param name="error">The failure. Must not be <see cref="Errors.Error.None"/>.</param>
    /// <returns>A result in the failure state.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="error"/> is <see cref="Errors.Error.None"/>.
    /// </exception>
    public static Result<T> Failure(Error error) => new(error);

    /// <summary>
    /// Attempts to read the produced value without throwing.
    /// </summary>
    /// <param name="value">
    /// When this method returns <see langword="true"/>, the produced value;
    /// otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the operation succeeded; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public bool TryGetValue([NotNullWhen(true)] out T? value)
    {
        value = _value;
        return IsSuccess;
    }

    /// <summary>
    /// Wraps a value in a successful result.
    /// </summary>
    /// <param name="value">The produced value.</param>
    /// <returns>A result in the success state.</returns>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>
    /// Wraps an error in a failed result.
    /// </summary>
    /// <param name="error">The failure to wrap.</param>
    /// <returns>A result in the failure state.</returns>
    public static implicit operator Result<T>(Error error) => Failure(error);

    /// <summary>
    /// Discards the produced value, keeping only whether the operation
    /// succeeded and why it did not.
    /// </summary>
    /// <param name="result">The result to narrow.</param>
    /// <returns>
    /// A non-generic <see cref="Result"/> in the same state.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="result"/> is <see langword="null"/>.
    /// </exception>
    public static implicit operator Result(Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    /// <summary>
    /// Wraps a value in a successful result.
    /// </summary>
    /// <remarks>
    /// The named alternative to the implicit conversion from
    /// <typeparamref name="T"/>.
    /// </remarks>
    /// <param name="value">The produced value.</param>
    /// <returns>A result in the success state.</returns>
    public static Result<T> FromValue(T value) => Success(value);

    /// <summary>
    /// Wraps an error in a failed result.
    /// </summary>
    /// <remarks>
    /// The named alternative to the implicit conversion from
    /// <see cref="Errors.Error"/>.
    /// </remarks>
    /// <param name="error">The failure to wrap.</param>
    /// <returns>A result in the failure state.</returns>
    public static Result<T> FromError(Error error) => Failure(error);

    /// <summary>
    /// Discards the produced value, keeping only the success state.
    /// </summary>
    /// <remarks>
    /// The named alternative to the implicit conversion to
    /// <see cref="Result"/>.
    /// </remarks>
    /// <returns>A non-generic <see cref="Result"/> in the same state.</returns>
    public Result ToResult() => IsSuccess ? Result.Success() : Result.Failure(Error);

    /// <inheritdoc/>
    public override string ToString() => IsSuccess ? $"Success({_value})" : $"Failure({Error})";
}
