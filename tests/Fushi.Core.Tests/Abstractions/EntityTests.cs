using Fushi.Core.Abstractions;

namespace Fushi.Core.Tests.Abstractions;

/// <summary>
/// Covers <see cref="Entity{TId}"/>: identity comparison across instances, the
/// refusal to treat two different types as the same entity, and the treatment
/// of an entity that has not been persisted yet.
/// </summary>
public sealed class EntityTests
{
    // The whole point of comparing by identity: a detached copy loaded from a
    // second query is still the same row, even though the two objects share
    // nothing else.
    [Fact]
    public void TwoInstancesWithTheSameIdentifierAreTheSameEntity()
    {
        var id = Guid.NewGuid();

        Sample first = new(id);
        Sample second = new(id);

        first.Equals(second).ShouldBeTrue();
        (first == second).ShouldBeTrue();
        (first != second).ShouldBeFalse();
        first.GetHashCode().ShouldBe(second.GetHashCode());
    }

    [Fact]
    public void TwoInstancesWithDifferentIdentifiersAreDifferentEntities()
    {
        Sample first = new(Guid.NewGuid());
        Sample second = new(Guid.NewGuid());

        first.Equals(second).ShouldBeFalse();
        (first != second).ShouldBeTrue();
    }

    // Identifiers are only unique within a table, so a submission and a vote that
    // happen to share a value must not compare equal.
    [Fact]
    public void TwoTypesSharingAnIdentifierAreStillDifferentEntities()
    {
        var id = Guid.NewGuid();

        Sample sample = new(id);
        Other other = new(id);

        sample.Equals(other).ShouldBeFalse();
        other.Equals(sample).ShouldBeFalse();
    }

    // A default identifier means "not saved yet", and two unsaved objects are not
    // the same row simply because neither has been given a number.
    [Fact]
    public void UnpersistedEntitiesAreDistinctFromEverythingIncludingEachOther()
    {
        Sample first = new();
        Sample second = new();

        first.Equals(second).ShouldBeFalse();
        first.Equals(first).ShouldBeTrue();
    }

    [Fact]
    public void NothingIsEqualToNull()
    {
        Sample sample = new(Guid.NewGuid());
        Sample? absent = Absent();

        sample.Equals(absent).ShouldBeFalse();
        (sample == absent).ShouldBeFalse();
        (absent == sample).ShouldBeFalse();
        (sample != absent).ShouldBeTrue();
    }

    [Fact]
    public void TwoAbsentEntitiesAreEqual() => (Absent() == Absent()).ShouldBeTrue();

    [Fact]
    public void ComparingAgainstAnUnrelatedObjectIsFalseRatherThanAnError() => new Sample(Guid.NewGuid()).Equals("not an entity").ShouldBeFalse();

    [Fact]
    public void AnEntityCannotBeCreatedWithoutAnIdentifier() => _ = Should.Throw<ArgumentException>(() => new Sample(Guid.Empty));

    private static Sample? Absent() => null;

    private sealed class Sample : Entity<Guid>
    {
        public Sample(Guid id)
            : base(id)
        {
        }

        public Sample()
        {
        }
    }

    private sealed class Other : Entity<Guid>
    {
        public Other(Guid id)
            : base(id)
        {
        }
    }
}
