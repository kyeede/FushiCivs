using Fushi.Core.Errors;
using Fushi.Core.Results;

namespace Fushi.Core.Extensions;

/// <summary>
/// Composition helpers that let a chain of fallible steps be written as a
/// single expression.
/// </summary>
/// <remarks>
/// Without these, every step in a handler needs its own <c>if (result.IsFailure)
/// return result.Error;</c>, and the actual work disappears between the checks.
/// <c>Bind</c> and its neighbours carry the failure through for you: once a
/// step fails, the later steps do not run and the original error reaches the
/// caller unchanged.
/// <br/>
/// The asynchronous overloads accept a <see cref="Task{TResult}"/> as the
/// receiver so that a chain can be awaited once at the end rather than at every
/// link.
/// </remarks>
/// <example>
/// <code>
/// return await FindCycle(command.Code, token)
///     .Ensure(cycle => cycle.IsOpen(now), CycleErrors.NotOpen)
///     .BindAsync(cycle => RecordVote(cycle, command.VoterId, token))
///     .MapAsync(vote => vote.ToModel());
/// </code>
/// </example>
public static class ResultExtensions
{
    extension(Result result)
    {
        /// <summary>
        /// Runs an action when the result succeeded, passing the result
        /// through either way.
        /// </summary>
        /// <param name="action">The action to run on success.</param>
        /// <returns>The original result.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="action"/> is <see langword="null"/>.
        /// </exception>
        public Result OnSuccess(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (result.IsSuccess)
            {
                action();
            }

            return result;
        }

        /// <summary>
        /// Runs an action when the result failed, passing the result through
        /// either way.
        /// </summary>
        /// <param name="action">The action to run with the error.</param>
        /// <returns>The original result.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="action"/> is <see langword="null"/>.
        /// </exception>
        public Result OnFailure(Action<Error> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            if (result.IsFailure)
            {
                action(result.Error);
            }

            return result;
        }

        /// <summary>
        /// Runs the next fallible step only when this one succeeded.
        /// </summary>
        /// <param name="next">The step to run on success.</param>
        /// <returns>
        /// The result of <paramref name="next"/>, or this result's failure.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="next"/> is <see langword="null"/>.
        /// </exception>
        public Result Bind(Func<Result> next)
        {
            ArgumentNullException.ThrowIfNull(next);

            return result.IsSuccess ? next() : result;
        }
    }

    extension<T>(Result<T> result)
    {
        /// <summary>
        /// Collapses both states into a single value by applying whichever
        /// function matches the state this result is in.
        /// </summary>
        /// <typeparam name="TOut">The type both branches produce.</typeparam>
        /// <param name="onSuccess">Invoked with the value on success.</param>
        /// <param name="onFailure">Invoked with the error on failure.</param>
        /// <returns>The value produced by the branch that ran.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="onSuccess"/> or <paramref name="onFailure"/> is
        /// <see langword="null"/>, or the result itself is
        /// <see langword="null"/>.
        /// </exception>
        public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(onSuccess);
            ArgumentNullException.ThrowIfNull(onFailure);

            return result.IsSuccess ? onSuccess(result.Value) : onFailure(result.Error);
        }

        /// <summary>
        /// Transforms the value of a successful result, leaving a failure
        /// untouched.
        /// </summary>
        /// <typeparam name="TOut">The transformed value type.</typeparam>
        /// <param name="selector">
        /// The transformation, which cannot itself fail.
        /// </param>
        /// <returns>
        /// A result holding the transformed value, or the original failure.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="selector"/> is <see langword="null"/>, or the result
        /// itself is <see langword="null"/>.
        /// </exception>
        public Result<TOut> Map<TOut>(Func<T, TOut> selector)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(selector);

            return result.IsSuccess
                ? Result<TOut>.Success(selector(result.Value))
                : Result<TOut>.Failure(result.Error);
        }

        /// <summary>
        /// Runs the next fallible step on the value, only when this step
        /// succeeded.
        /// </summary>
        /// <remarks>
        /// The difference from <c>Map</c> is that the step itself can fail:
        /// use <c>Map</c> for a pure transformation and <c>Bind</c> when the
        /// next step returns its own result.
        /// </remarks>
        /// <typeparam name="TOut">The value type the next step produces.</typeparam>
        /// <param name="next">The step to run on success.</param>
        /// <returns>
        /// The result of <paramref name="next"/>, or the original failure.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="next"/> is <see langword="null"/>, or the result
        /// itself is <see langword="null"/>.
        /// </exception>
        public Result<TOut> Bind<TOut>(Func<T, Result<TOut>> next)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(next);

            return result.IsSuccess ? next(result.Value) : Result<TOut>.Failure(result.Error);
        }

        /// <summary>
        /// Fails an otherwise successful result when its value does not satisfy
        /// a condition.
        /// </summary>
        /// <param name="predicate">The condition the value must satisfy.</param>
        /// <param name="error">The failure to produce when it does not.</param>
        /// <returns>
        /// The original result when the condition holds; otherwise a failure
        /// carrying <paramref name="error"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="predicate"/> is <see langword="null"/>, or the
        /// result itself is <see langword="null"/>.
        /// </exception>
        public Result<T> Ensure(Func<T, bool> predicate, Error error)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(predicate);

            if (result.IsFailure)
            {
                return result;
            }

            return predicate(result.Value) ? result : Result<T>.Failure(error);
        }

        /// <summary>
        /// Runs a side effect on the value of a successful result, passing the
        /// result through either way.
        /// </summary>
        /// <param name="action">The side effect to run.</param>
        /// <returns>The original result.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="action"/> is <see langword="null"/>, or the result
        /// itself is <see langword="null"/>.
        /// </exception>
        public Result<T> Tap(Action<T> action)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(action);

            if (result.IsSuccess)
            {
                action(result.Value);
            }

            return result;
        }

        /// <summary>
        /// Reads the value, substituting a fallback when the result failed.
        /// </summary>
        /// <param name="fallback">The value to use on failure.</param>
        /// <returns>
        /// The produced value, or <paramref name="fallback"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// The result is <see langword="null"/>.
        /// </exception>
        public T ValueOr(T fallback)
        {
            ArgumentNullException.ThrowIfNull(result);

            return result.IsSuccess ? result.Value : fallback;
        }
    }

    extension(Task<Result> task)
    {
        /// <summary>
        /// Awaits the result, then runs the next fallible step when it
        /// succeeded.
        /// </summary>
        /// <param name="next">The step to run on success.</param>
        /// <returns>
        /// The result of <paramref name="next"/>, or the awaited failure.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="task"/> or <paramref name="next"/> is
        /// <see langword="null"/>.
        /// </exception>
        public async Task<Result> BindAsync(Func<Task<Result>> next)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(next);

            Result result = await task;
            return result.IsSuccess ? await next() : result;
        }
    }

    extension<T>(Task<Result<T>> task)
    {
        /// <summary>
        /// Awaits the result, then collapses both states into a single value.
        /// </summary>
        /// <typeparam name="TOut">The type both branches produce.</typeparam>
        /// <param name="onSuccess">Invoked with the value on success.</param>
        /// <param name="onFailure">Invoked with the error on failure.</param>
        /// <returns>The value produced by the branch that ran.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="task"/>, <paramref name="onSuccess"/>, or
        /// <paramref name="onFailure"/> is <see langword="null"/>.
        /// </exception>
        public async Task<TOut> MatchAsync<TOut>(
            Func<T, TOut> onSuccess,
            Func<Error, TOut> onFailure)
        {
            ArgumentNullException.ThrowIfNull(task);

            Result<T> result = await task;
            return result.Match(onSuccess, onFailure);
        }

        /// <summary>
        /// Awaits the result, then transforms a successful value.
        /// </summary>
        /// <typeparam name="TOut">The transformed value type.</typeparam>
        /// <param name="selector">The transformation, which cannot fail.</param>
        /// <returns>
        /// A result holding the transformed value, or the awaited failure.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="task"/> or <paramref name="selector"/> is
        /// <see langword="null"/>.
        /// </exception>
        public async Task<Result<TOut>> MapAsync<TOut>(Func<T, TOut> selector)
        {
            ArgumentNullException.ThrowIfNull(task);

            Result<T> result = await task;
            return result.Map(selector);
        }

        /// <summary>
        /// Awaits the result, then runs the next fallible step on its value.
        /// </summary>
        /// <typeparam name="TOut">The value type the next step produces.</typeparam>
        /// <param name="next">The step to run on success.</param>
        /// <returns>
        /// The result of <paramref name="next"/>, or the awaited failure.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="task"/> or <paramref name="next"/> is
        /// <see langword="null"/>.
        /// </exception>
        public async Task<Result<TOut>> BindAsync<TOut>(Func<T, Task<Result<TOut>>> next)
        {
            ArgumentNullException.ThrowIfNull(task);
            ArgumentNullException.ThrowIfNull(next);

            Result<T> result = await task;
            return result.IsSuccess ? await next(result.Value) : Result<TOut>.Failure(result.Error);
        }

        /// <summary>
        /// Awaits the result, then fails it when the value does not satisfy a
        /// condition.
        /// </summary>
        /// <param name="predicate">The condition the value must satisfy.</param>
        /// <param name="error">The failure to produce when it does not.</param>
        /// <returns>
        /// The awaited result when the condition holds; otherwise a failure
        /// carrying <paramref name="error"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="task"/> or <paramref name="predicate"/> is
        /// <see langword="null"/>.
        /// </exception>
        public async Task<Result<T>> Ensure(Func<T, bool> predicate, Error error)
        {
            ArgumentNullException.ThrowIfNull(task);

            Result<T> result = await task;
            return result.Ensure(predicate, error);
        }
    }
}
