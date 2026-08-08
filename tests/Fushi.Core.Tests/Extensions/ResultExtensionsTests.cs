using System.Globalization;

using Fushi.Core.Errors;
using Fushi.Core.Extensions;
using Fushi.Core.Results;

namespace Fushi.Core.Tests.Extensions;

/// <summary>
/// Covers <see cref="ResultExtensions"/>: that each step transforms a success,
/// and, more importantly, that a failure short-circuits the chain without
/// running the continuation and reaches the end with its original error intact.
/// </summary>
public sealed class ResultExtensionsTests
{
    private static readonly Error Closed = Error.Conflict("Cycle.NotOpen", "That cycle is not accepting votes.");

    private static readonly Error TooShort = Error.Validation("Code.TooShort", "A code is six characters.");

    [Fact]
    public void MapTransformsTheValueOfASuccess()
    {
        Result<string> mapped = Result<int>.Success(6).Map(value => new string('x', value));

        mapped.IsSuccess.ShouldBeTrue();
        mapped.Value.ShouldBe("xxxxxx");
    }

    // Not merely "the result is a failure": the point of short-circuiting is that
    // the continuation never runs, so a step that touches a database or sends a
    // message cannot fire after an earlier step has already failed.
    [Fact]
    public void MapDoesNotRunItsSelectorOnAFailure()
    {
        bool ran = false;

        Result<string> mapped = Result<int>.Failure(Closed).Map(value =>
        {
            ran = true;
            return Render(value);
        });

        ran.ShouldBeFalse();
        mapped.IsFailure.ShouldBeTrue();
        mapped.Error.ShouldBe(Closed);
    }

    [Fact]
    public void BindRunsTheNextStepOnASuccessAndCarriesItsFailureThrough()
    {
        Result<int>.Success(4).Bind(value => Result<int>.Success(value * 2)).Value.ShouldBe(8);

        Result<int> failed = Result<int>.Success(4).Bind(_ => Result<int>.Failure(TooShort));

        failed.IsFailure.ShouldBeTrue();
        failed.Error.ShouldBe(TooShort);
    }

    [Fact]
    public void BindDoesNotRunTheNextStepOnAFailure()
    {
        bool ran = false;

        Result<string> bound = Result<int>.Failure(Closed).Bind(_ =>
        {
            ran = true;
            return Result<string>.Success("unreachable");
        });

        ran.ShouldBeFalse();
        bound.Error.ShouldBe(Closed);
    }

    [Fact]
    public void BindOnANonGenericResultRunsTheNextStepOnlyOnSuccess()
    {
        bool ran = false;

        Result.Success().Bind(() => Result.Failure(TooShort)).Error.ShouldBe(TooShort);

        Result bound = Result.Failure(Closed).Bind(() =>
        {
            ran = true;
            return Result.Success();
        });

        ran.ShouldBeFalse();
        bound.Error.ShouldBe(Closed);
    }

    [Fact]
    public void EnsureKeepsAValueThatSatisfiesTheCondition()
    {
        Result<int> ensured = Result<int>.Success(10).Ensure(value => value > 5, TooShort);

        ensured.IsSuccess.ShouldBeTrue();
        ensured.Value.ShouldBe(10);
    }

    [Fact]
    public void EnsureFailsAValueThatDoesNotSatisfyTheCondition()
    {
        Result<int> ensured = Result<int>.Success(1).Ensure(value => value > 5, TooShort);

        ensured.IsFailure.ShouldBeTrue();
        ensured.Error.ShouldBe(TooShort);
    }

    // A failure keeps the error it already had rather than being relabelled with
    // the one the guard would have produced, so the first thing that went wrong is
    // still what the caller sees.
    [Fact]
    public void EnsureDoesNotEvaluateItsPredicateOnAFailure()
    {
        bool ran = false;

        Result<int> ensured = Result<int>.Failure(Closed).Ensure(
            _ =>
            {
                ran = true;
                return false;
            },
            TooShort);

        ran.ShouldBeFalse();
        ensured.Error.ShouldBe(Closed);
    }

    [Fact]
    public void TapRunsItsSideEffectOnASuccessAndPassesTheResultThrough()
    {
        int observed = 0;

        Result<int> tapped = Result<int>.Success(7).Tap(value => observed = value);

        observed.ShouldBe(7);
        tapped.Value.ShouldBe(7);
    }

    [Fact]
    public void TapDoesNotRunItsSideEffectOnAFailure()
    {
        bool ran = false;

        Result<int> tapped = Result<int>.Failure(Closed).Tap(_ => ran = true);

        ran.ShouldBeFalse();
        tapped.Error.ShouldBe(Closed);
    }

    [Fact]
    public void ValueOrReadsTheValueOfASuccessAndSubstitutesOnFailure()
    {
        Result<string>.Success("real").ValueOr("fallback").ShouldBe("real");
        Result<string>.Failure(Closed).ValueOr("fallback").ShouldBe("fallback");
    }

    [Fact]
    public void MatchRunsExactlyOneBranch()
    {
        Result<int>.Success(3).Match(value => $"ok:{value}", error => $"no:{error.Code}").ShouldBe("ok:3");

        Result<int>.Failure(Closed)
            .Match(value => $"ok:{value}", error => $"no:{error.Code}")
            .ShouldBe("no:Cycle.NotOpen");
    }

    [Fact]
    public void OnSuccessAndOnFailureRunOnlyForTheirOwnState()
    {
        int successes = 0;
        int failures = 0;

        _ = Result.Success().OnSuccess(() => successes++).OnFailure(_ => failures++);
        _ = Result.Failure(Closed).OnSuccess(() => successes++).OnFailure(_ => failures++);

        successes.ShouldBe(1);
        failures.ShouldBe(1);
    }

    // The whole point of the chain: one failing link and everything after it is
    // skipped, with the original error arriving at the end unchanged.
    [Fact]
    public void AFailureEarlyInAChainSkipsEveryLaterStep()
    {
        List<string> ran = [];

        Result<string> outcome = Result<int>.Success(4)
            .Ensure(value => value > 100, TooShort)
            .Tap(_ => ran.Add("tap"))
            .Bind(value =>
            {
                ran.Add("bind");
                return Result<int>.Success(value);
            })
            .Map(value =>
            {
                ran.Add("map");
                return Render(value);
            });

        ran.ShouldBeEmpty();
        outcome.Error.ShouldBe(TooShort);
    }

    [Fact]
    public async Task TheAsynchronousStepsShortCircuitTheSameWay()
    {
        bool ran = false;

        Result<string> outcome = await Task.FromResult(Result<int>.Failure(Closed))
            .Ensure(_ => true, TooShort)
            .BindAsync(_ =>
            {
                ran = true;
                return Task.FromResult(Result<int>.Success(1));
            })
            .MapAsync(Render);

        ran.ShouldBeFalse();
        outcome.Error.ShouldBe(Closed);
    }

    [Fact]
    public async Task TheAsynchronousStepsRunInOrderOnASuccess()
    {
        string outcome = await Task.FromResult(Result<int>.Success(3))
            .BindAsync(value => Task.FromResult(Result<int>.Success(value * 3)))
            .MatchAsync(value => $"ok:{value}", error => error.Code);

        outcome.ShouldBe("ok:9");
    }

    [Fact]
    public void EveryStepRejectsANullContinuation()
    {
        _ = Should.Throw<ArgumentNullException>(
            () => Result<int>.Success(1).Map((Func<int, int>)null!));
        _ = Should.Throw<ArgumentNullException>(
            () => Result<int>.Success(1).Bind((Func<int, Result<int>>)null!));
        _ = Should.Throw<ArgumentNullException>(() => Result<int>.Success(1).Ensure(null!, TooShort));
        _ = Should.Throw<ArgumentNullException>(() => Result<int>.Success(1).Tap(null!));
        _ = Should.Throw<ArgumentNullException>(() => Result.Success().Bind(null!));
    }

    private static string Render(int value) => value.ToString(CultureInfo.InvariantCulture);
}
