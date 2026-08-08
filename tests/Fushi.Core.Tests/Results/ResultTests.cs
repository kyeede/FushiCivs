using Fushi.Core.Errors;
using Fushi.Core.Results;

namespace Fushi.Core.Tests.Results;

/// <summary>
/// Covers the non-generic <see cref="Result"/>: its two states, the refusal to
/// represent a failure without an error, matching, and the conversion from an
/// <see cref="Error"/>.
/// </summary>
public sealed class ResultTests
{
    [Fact]
    public void ASuccessCarriesNoError()
    {
        Result result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBe(Error.None);
        result.Error.IsNone.ShouldBeTrue();
    }

    // Success is the default value so that the common path allocates nothing,
    // which also means an unassigned Result must not read as a failure.
    [Fact]
    public void TheDefaultValueIsASuccess()
    {
        Result result = default;

        result.IsSuccess.ShouldBeTrue();
        result.ShouldBe(Result.Success());
    }

    [Fact]
    public void AFailureCarriesTheErrorItWasGiven()
    {
        Error error = Error.Conflict("Cycle.NotOpen", "That cycle is not accepting votes.");

        Result result = Result.Failure(error);

        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
        result.Error.Code.ShouldBe("Cycle.NotOpen");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    // A failure with nothing to report is a contradiction, so the type refuses to
    // represent one rather than producing a value that claims both states.
    [Fact]
    public void AFailureCannotBeBuiltFromTheAbsenceOfAnError()
    {
        _ = Should.Throw<ArgumentException>(() => Result.Failure(Error.None));
        _ = Should.Throw<ArgumentException>(() => Result.FromError(default));
    }

    [Fact]
    public void AnErrorConvertsImplicitlyToAFailure()
    {
        Error error = Error.NotFound("Submission.NotFound", "No submission has that code.");

        Result result = error;

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
        Result.FromError(error).ShouldBe(result);
    }

    [Fact]
    public void MatchRunsTheSuccessBranchForASuccess()
    {
        bool failureRan = false;

        string matched = Result.Success().Match(() => "ok", _ =>
        {
            failureRan = true;
            return "failed";
        });

        matched.ShouldBe("ok");
        failureRan.ShouldBeFalse();
    }

    [Fact]
    public void MatchRunsTheFailureBranchWithTheErrorForAFailure()
    {
        Error error = Error.Validation("Vote.Invalid", "That is not a choice.");
        bool successRan = false;

        string matched = Result.Failure(error).Match(
            () =>
            {
                successRan = true;
                return "ok";
            },
            failure => failure.Code);

        matched.ShouldBe("Vote.Invalid");
        successRan.ShouldBeFalse();
    }

    [Fact]
    public void MatchRejectsANullBranch()
    {
        _ = Should.Throw<ArgumentNullException>(() => Result.Success().Match(null!, _ => 0));
        _ = Should.Throw<ArgumentNullException>(() => Result.Success().Match(() => 0, null!));
    }

    [Fact]
    public void TheInterfaceReportsTheSameStateAsTheConcreteMembers()
    {
        Error error = Error.Unexpected("Db.Unavailable", "The database did not answer.");

        IResult success = AsInterface(Result.Success());
        IResult failure = AsInterface(Result.Failure(error));

        success.IsSuccess.ShouldBeTrue();
        success.IsFailure.ShouldBeFalse();
        success.Error.ShouldBe(Error.None);

        failure.IsSuccess.ShouldBeFalse();
        failure.IsFailure.ShouldBeTrue();
        failure.Error.ShouldBe(error);
    }

    [Fact]
    public void TwoResultsInTheSameStateAreEqual()
    {
        Error error = Error.Forbidden("Vote.NotPermitted", "You may not vote here.");

        Result.Success().ShouldBe(Result.Success());
        Result.Failure(error).ShouldBe(Result.Failure(error));
        Result.Failure(error).ShouldNotBe(Result.Success());
    }

    private static IResult AsInterface(IResult result) => result;
}
