using Fushi.Core.Utilities;

namespace Fushi.Core.Tests.Utilities;

/// <summary>
/// Covers <see cref="SnowflakeUtility"/>: the timestamp Discord embeds in the
/// top bits of an identifier, the synthetic snowflake built from an instant,
/// and the plausibility range check.
/// </summary>
public sealed class SnowflakeUtilityTests
{
    // A real identifier, checked against the value Discord's own documentation
    // uses as its worked example. If the epoch or the shift is ever "tidied", this
    // is the test that notices.
    [Fact]
    public void AKnownDiscordSnowflakeDecodesToItsDocumentedInstant()
    {
        DateTimeOffset timestamp = SnowflakeUtility.ToTimestamp(175928847299117063uL);

        timestamp.ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(1462015105796L));
        timestamp.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void TheEpochIsTheFirstMillisecondOfTwentyFifteen()
    {
        SnowflakeUtility.Epoch.ShouldBe(new DateTimeOffset(2015, 1, 1, 0, 0, 0, TimeSpan.Zero));
        SnowflakeUtility.DISCORD_EPOCH_MILLISECONDS.ShouldBe(1_420_070_400_000L);
    }

    [Fact]
    public void TheSmallestNonZeroSnowflakeDecodesToTheEpochItself()
    {
        SnowflakeUtility.ToTimestamp(1uL << SnowflakeUtility.TIMESTAMP_SHIFT)
            .ShouldBe(SnowflakeUtility.Epoch.AddMilliseconds(1));

        SnowflakeUtility.ToTimestamp(1uL).ShouldBe(SnowflakeUtility.Epoch);
    }

    [Theory]
    [InlineData(2015, 1, 1)]
    [InlineData(2020, 6, 15)]
    [InlineData(2026, 8, 8)]
    [InlineData(2099, 12, 31)]
    public void AnInstantSurvivesTheRoundTripThroughASyntheticSnowflake(int year, int month, int day)
    {
        DateTimeOffset instant = new(year, month, day, 13, 45, 30, TimeSpan.Zero);

        SnowflakeUtility.ToTimestamp(SnowflakeUtility.FromTimestamp(instant)).ShouldBe(instant);
    }

    // The low 22 bits are zeroed, which is what makes the value sort immediately
    // before every real identifier from the same millisecond — exactly what
    // Discord's before/after pagination needs.
    [Fact]
    public void ASyntheticSnowflakeCarriesNoWorkerOrIncrementBits()
    {
        ulong synthetic = SnowflakeUtility.FromTimestamp(new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero));

        (synthetic & ((1uL << SnowflakeUtility.TIMESTAMP_SHIFT) - 1)).ShouldBe(0uL);
    }

    [Fact]
    public void AnInstantIsReadInAbsoluteTermsRatherThanByItsWallClock()
    {
        DateTimeOffset utc = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset shifted = new(2026, 8, 8, 14, 0, 0, TimeSpan.FromHours(2));

        SnowflakeUtility.FromTimestamp(shifted).ShouldBe(SnowflakeUtility.FromTimestamp(utc));
    }

    // Zero is not a real identifier, and decoding it would silently answer "made
    // on the first day of 2015" rather than "this is not an identifier".
    [Fact]
    public void ZeroIsRejectedRatherThanDecodedToTheEpoch()
    {
        _ = Should.Throw<ArgumentOutOfRangeException>(() => SnowflakeUtility.ToTimestamp(0uL));

        SnowflakeUtility.TryToTimestamp(0uL, out DateTimeOffset timestamp).ShouldBeFalse();
        timestamp.ShouldBe(DateTimeOffset.MinValue);
    }

    [Fact]
    public void TryToTimestampAgreesWithToTimestampForARealIdentifier()
    {
        SnowflakeUtility.TryToTimestamp(175928847299117063uL, out DateTimeOffset timestamp).ShouldBeTrue();

        timestamp.ShouldBe(SnowflakeUtility.ToTimestamp(175928847299117063uL));
    }

    [Fact]
    public void AnInstantBeforeTheEpochCannotBeEncoded()
    {
        _ = Should.Throw<ArgumentOutOfRangeException>(
            () => SnowflakeUtility.FromTimestamp(SnowflakeUtility.Epoch.AddMilliseconds(-1)));
    }

    [Fact]
    public void AnIdentifierFromTheFutureIsNotPlausible()
    {
        DateTimeOffset now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

        SnowflakeUtility.IsPlausible(SnowflakeUtility.FromTimestamp(now.AddDays(-1)), now).ShouldBeTrue();
        SnowflakeUtility.IsPlausible(SnowflakeUtility.FromTimestamp(now), now).ShouldBeTrue();
        SnowflakeUtility.IsPlausible(SnowflakeUtility.FromTimestamp(now.AddDays(1)), now).ShouldBeFalse();
        SnowflakeUtility.IsPlausible(0uL, now).ShouldBeFalse();
    }
}
