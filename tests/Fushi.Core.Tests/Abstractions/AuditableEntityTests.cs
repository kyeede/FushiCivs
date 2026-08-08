using Fushi.Core.Abstractions;

namespace Fushi.Core.Tests.Abstractions;

/// <summary>
/// Covers <see cref="AuditableEntity{TId}"/>: the stamps written by the three
/// mark methods, the timeline they refuse to record, and the round trip from
/// deleted back to live.
/// </summary>
public sealed class AuditableEntityTests
{
    private static readonly DateTimeOffset Created = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANewEntityCarriesOnlyItsCreationStamp()
    {
        Sample sample = New();

        sample.CreatedAt.ShouldBe(Created);
        sample.CreatedBy.ShouldBe(7uL);
        sample.UpdatedAt.ShouldBeNull();
        sample.UpdatedBy.ShouldBeNull();
        sample.DeletedAt.ShouldBeNull();
        sample.DeletedBy.ShouldBeNull();
        sample.IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public void MarkingAnUpdateRecordsWhoChangedItAndWhen()
    {
        Sample sample = New();

        sample.MarkUpdated(Created.AddHours(1), updatedBy: 99uL);

        sample.UpdatedAt.ShouldBe(Created.AddHours(1));
        sample.UpdatedBy.ShouldBe(99uL);
    }

    [Fact]
    public void AnEntityCanBeUpdatedAtTheVeryInstantItWasCreated()
    {
        Sample sample = New();

        sample.MarkUpdated(Created, updatedBy: 99uL);

        sample.UpdatedAt.ShouldBe(Created);
    }

    // A stamp that predates creation is not a late clock, it is a bug, and
    // accepting it would put an unexplainable row in the audit trail.
    [Theory]
    [InlineData(-1)]
    [InlineData(-3600)]
    public void AnEntityCannotBeChangedBeforeItExisted(int secondsBeforeCreation)
    {
        Sample sample = New();
        DateTimeOffset impossible = Created.AddSeconds(secondsBeforeCreation);

        _ = Should.Throw<ArgumentOutOfRangeException>(() => sample.MarkUpdated(impossible, 1uL));
        _ = Should.Throw<ArgumentOutOfRangeException>(() => sample.MarkDeleted(impossible, 1uL));
    }

    [Fact]
    public void MarkingADeletionRecordsWhoRemovedItAndWhen()
    {
        Sample sample = New();

        sample.MarkDeleted(Created.AddDays(1), deletedBy: 42uL);

        sample.IsDeleted.ShouldBeTrue();
        sample.DeletedAt.ShouldBe(Created.AddDays(1));
        sample.DeletedBy.ShouldBe(42uL);
    }

    // A retried command must not overwrite the original stamp, or the audit trail
    // would name whoever pressed the button twice rather than whoever deleted it.
    [Fact]
    public void DeletingAnAlreadyDeletedEntityKeepsTheOriginalStamp()
    {
        Sample sample = New();
        sample.MarkDeleted(Created.AddDays(1), deletedBy: 42uL);

        sample.MarkDeleted(Created.AddDays(2), deletedBy: 43uL);

        sample.DeletedAt.ShouldBe(Created.AddDays(1));
        sample.DeletedBy.ShouldBe(42uL);
    }

    [Fact]
    public void RestoringClearsTheDeletionAndRecordsAModification()
    {
        Sample sample = New();
        sample.MarkDeleted(Created.AddDays(1), deletedBy: 42uL);

        sample.MarkRestored(Created.AddDays(2), restoredBy: 43uL);

        sample.IsDeleted.ShouldBeFalse();
        sample.DeletedAt.ShouldBeNull();
        sample.DeletedBy.ShouldBeNull();
        sample.UpdatedAt.ShouldBe(Created.AddDays(2));
        sample.UpdatedBy.ShouldBe(43uL);
    }

    [Fact]
    public void RestoringSomethingThatWasNeverDeletedChangesNothing()
    {
        Sample sample = New();

        sample.MarkRestored(Created.AddDays(1), restoredBy: 43uL);

        sample.IsDeleted.ShouldBeFalse();
        sample.UpdatedAt.ShouldBeNull();
    }

    [Fact]
    public void AnEntityCanBeDeletedAndRestoredMoreThanOnce()
    {
        Sample sample = New();

        sample.MarkDeleted(Created.AddDays(1), 42uL);
        sample.MarkRestored(Created.AddDays(2), 43uL);
        sample.MarkDeleted(Created.AddDays(3), 44uL);

        sample.IsDeleted.ShouldBeTrue();
        sample.DeletedAt.ShouldBe(Created.AddDays(3));
        sample.DeletedBy.ShouldBe(44uL);
    }

    private static Sample New() => new(Guid.NewGuid(), Created, 7uL);

    private sealed class Sample : AuditableEntity<Guid>
    {
        public Sample(Guid id, DateTimeOffset createdAt, ulong createdBy)
            : base(id, createdAt, createdBy)
        {
        }
    }
}
