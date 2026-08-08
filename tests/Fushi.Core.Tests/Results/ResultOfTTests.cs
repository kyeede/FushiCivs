using Fushi.Core.Errors;
using Fushi.Core.Exceptions;
using Fushi.Core.Results;

namespace Fushi.Core.Tests.Results;

/// <summary>
/// Covers <see cref="Result{T}"/>: reading the value on each state, the
/// exception raised for reaching past a failure, the conversions to and from a
/// value, an error, and the non-generic <see cref="Result"/>.
/// </summary>
public sealed class ResultOfTTests
{
    [Fact]
    public void ASuccessCarriesItsValueAndNoError()
    {
        Result<string> result = Result<string>.Success("7K4M2P");

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Value.ShouldBe("7K4M2P");
        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void AFailureCarriesItsErrorAndNoValue()
    {
        Error error = Error.NotFound("Submission.NotFound", "No submission has that code.");

        Result<string> result = Result<string>.Failure(error);

        result.IsSuccess.ShouldBeFalse();
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
    }

    // Reading past a failure is a bug at the call site rather than a runtime
    // condition, so the exception carries the original error and is deliberately
    // not a domain exception that a catch-all could swallow.
    [Fact]
    public void ReadingTheValueOfAFailureThrowsCarryingTheOriginalError()
    {
        Error error = Error.Conflict("Cycle.Closed", "That cycle has already closed.");
        Result<int> result = Result<int>.Failure(error);

        ResultAccessException thrown = Should.Throw<ResultAccessException>(() => _ = result.Value);

        thrown.Error.ShouldBe(error);
        thrown.ShouldBeAssignableTo<InvalidOperationException>();
        thrown.ShouldNotBeAssignableTo<FushiException>();
    }

    [Fact]
    public void TryGetValueHandsBackTheValueOfASuccess()
    {
        Result<int> result = Result<int>.Success(42);

        result.TryGetValue(out int value).ShouldBeTrue();
        value.ShouldBe(42);
    }

    [Fact]
    public void TryGetValueReportsFailureWithoutThrowing()
    {
        Result<string> result = Result<string>.Failure(Error.Validation("Code.Invalid", "That is not a code."));

        result.TryGetValue(out string? value).ShouldBeFalse();
        value.ShouldBeNull();
    }

    [Fact]
    public void AValueConvertsImplicitlyToASuccess()
    {
        Result<int> result = 7;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(7);
        Result<int>.FromValue(7).Value.ShouldBe(7);
    }

    [Fact]
    public void AnErrorConvertsImplicitlyToAFailure()
    {
        Error error = Error.Forbidden("Vote.NotPermitted", "You may not vote here.");

        Result<int> result = error;

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
        Result<int>.FromError(error).Error.ShouldBe(error);
    }

    // A successful result with no value is what the non-generic Result is for, so
    // the generic one refuses to represent it.
    [Fact]
    public void ASuccessCannotBeBuiltFromANullValue()
    {
        _ = Should.Throw<ArgumentNullException>(() => Result<string>.Success(null!));
    }

    [Fact]
    public void AFailureCannotBeBuiltFromTheAbsenceOfAnError()
    {
        _ = Should.Throw<ArgumentException>(() => Result<string>.Failure(Error.None));
    }

    [Fact]
    public void NarrowingKeepsTheStateAndDiscardsTheValue()
    {
        Error error = Error.Unexpected("Db.Unavailable", "The database did not answer.");

        Result fromSuccess = Result<string>.Success("kept");
        Result fromFailure = Result<string>.Failure(error);

        fromSuccess.IsSuccess.ShouldBeTrue();
        fromFailure.IsFailure.ShouldBeTrue();
        fromFailure.Error.ShouldBe(error);

        Result<string>.Success("kept").ToResult().ShouldBe(fromSuccess);
        Result<string>.Failure(error).ToResult().ShouldBe(fromFailure);
    }

    [Fact]
    public void TheInterfaceReportsTheSameStateAsTheConcreteMembers()
    {
        Error error = Error.Conflict("Vote.Duplicate", "You have already voted.");

        IResult success = AsInterface(Result<int>.Success(1));
        IResult failure = AsInterface(Result<int>.Failure(error));

        success.IsSuccess.ShouldBeTrue();
        success.IsFailure.ShouldBeFalse();
        success.Error.ShouldBe(Error.None);

        failure.IsSuccess.ShouldBeFalse();
        failure.IsFailure.ShouldBeTrue();
        failure.Error.ShouldBe(error);
    }

    private static IResult AsInterface(IResult result) => result;
}
