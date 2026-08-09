using Fushi.Core.Utilities;

namespace Fushi.Core.Tests.Utilities;

/// <summary>
/// Covers <see cref="MentionUtility"/>: the markup built for users, roles,
/// channels, commands, and timestamps, the parsers that read it back including
/// the legacy nickname form, and the refusal to read malformed input.
/// </summary>
public sealed class MentionUtilityTests
{
    [Fact]
    public void EachKindOfMentionGetsItsOwnSigil()
    {
        MentionUtility.User(123uL).ShouldBe("<@123>");
        MentionUtility.Role(123uL).ShouldBe("<@&123>");
        MentionUtility.Channel(123uL).ShouldBe("<#123>");
        MentionUtility.Command("submission view", 123uL).ShouldBe("</submission view:123>");
    }

    [Fact]
    public void ATimestampCarriesUnixSecondsAndTheStyleSuffix()
    {
        var instant = DateTimeOffset.FromUnixTimeSeconds(1_745_160_000L);

        MentionUtility.Timestamp(instant, TimestampStyle.Relative).ShouldBe("<t:1745160000:R>");
        MentionUtility.Timestamp(instant).ShouldBe("<t:1745160000:f>");
    }

    [Theory]
    [InlineData(TimestampStyle.ShortTime, "t")]
    [InlineData(TimestampStyle.LongTime, "T")]
    [InlineData(TimestampStyle.ShortDate, "d")]
    [InlineData(TimestampStyle.LongDate, "D")]
    [InlineData(TimestampStyle.ShortDateTime, "f")]
    [InlineData(TimestampStyle.LongDateTime, "F")]
    [InlineData(TimestampStyle.Relative, "R")]
    public void EveryTimestampStyleHasItsOwnSuffix(TimestampStyle style, string expected) => MentionUtility.Timestamp(DateTimeOffset.UnixEpoch, style).ShouldBe($"<t:0:{expected}>");

    [Fact]
    public void AMentionRoundTripsThroughItsOwnParser()
    {
        const ulong id = 175928847299117063uL;

        MentionUtility.TryParseUser(MentionUtility.User(id), out ulong userId).ShouldBeTrue();
        MentionUtility.TryParseRole(MentionUtility.Role(id), out ulong roleId).ShouldBeTrue();
        MentionUtility.TryParseChannel(MentionUtility.Channel(id), out ulong channelId).ShouldBeTrue();

        userId.ShouldBe(id);
        roleId.ShouldBe(id);
        channelId.ShouldBe(id);
    }

    // Discord stopped emitting the nickname form years ago, but it still occurs in
    // stored message content, so refusing it would silently drop old data.
    [Fact]
    public void TheLegacyNicknameFormIsStillAccepted()
    {
        MentionUtility.TryParseUser("<@!123>", out ulong userId).ShouldBeTrue();

        userId.ShouldBe(123uL);
    }

    [Fact]
    public void SurroundingWhiteSpaceIsIgnored()
    {
        MentionUtility.TryParseUser("  <@123>\n", out ulong userId).ShouldBeTrue();

        userId.ShouldBe(123uL);
    }

    // The sigils overlap by one character, so each parser has to reject the other
    // kinds rather than reading whatever digits it can find.
    [Theory]
    [InlineData("<@&123>")]
    [InlineData("<#123>")]
    [InlineData("123")]
    public void TheUserParserRejectsEverythingThatIsNotAUserMention(string value)
    {
        MentionUtility.TryParseUser(value, out ulong userId).ShouldBeFalse();

        userId.ShouldBe(0uL);
    }

    [Theory]
    [InlineData("<@123>")]
    [InlineData("<#123>")]
    [InlineData("<@!123>")]
    public void TheRoleParserRejectsEverythingThatIsNotARoleMention(string value)
    {
        MentionUtility.TryParseRole(value, out ulong roleId).ShouldBeFalse();

        roleId.ShouldBe(0uL);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<@>")]
    [InlineData("<@")]
    [InlineData("@123>")]
    [InlineData("<@abc>")]
    [InlineData("<@12a3>")]
    [InlineData("<@ 123>")]
    [InlineData("<@-1>")]
    [InlineData("<@+123>")]
    [InlineData("<@0>")]
    [InlineData("<@99999999999999999999>")]
    public void MalformedInputIsRejectedWithoutThrowing(string value)
    {
        MentionUtility.TryParseUser(value, out ulong userId).ShouldBeFalse();

        userId.ShouldBe(0uL);
    }

    [Fact]
    public void ReadingAnyIdentifierAcceptsEveryMentionFormAndABareNumber()
    {
        MentionUtility.TryParseAny("<@123>", out ulong fromUser).ShouldBeTrue();
        MentionUtility.TryParseAny("<@!123>", out ulong fromNickname).ShouldBeTrue();
        MentionUtility.TryParseAny("<@&123>", out ulong fromRole).ShouldBeTrue();
        MentionUtility.TryParseAny("<#123>", out ulong fromChannel).ShouldBeTrue();
        MentionUtility.TryParseAny("  123 ", out ulong fromBareNumber).ShouldBeTrue();

        fromUser.ShouldBe(123uL);
        fromNickname.ShouldBe(123uL);
        fromRole.ShouldBe(123uL);
        fromChannel.ShouldBe(123uL);
        fromBareNumber.ShouldBe(123uL);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not an id")]
    [InlineData("<@>")]
    [InlineData("0")]
    public void ReadingAnyIdentifierStillRejectsWhatIsNotOne(string value)
    {
        MentionUtility.TryParseAny(value, out ulong snowflake).ShouldBeFalse();

        snowflake.ShouldBe(0uL);
    }

    [Fact]
    public void ACommandLinkNeedsAName()
    {
        _ = Should.Throw<ArgumentException>(() => MentionUtility.Command("   ", 123uL));
        _ = Should.Throw<ArgumentException>(() => MentionUtility.Command(null!, 123uL));
    }
}
