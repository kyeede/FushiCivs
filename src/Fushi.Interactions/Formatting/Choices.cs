using System.Globalization;

namespace Fushi.Interactions.Formatting;

/// <summary>
/// The fixed sets of values the configuration panels offer.
/// </summary>
/// <remarks>
/// Every number a guild can configure is chosen from a list here rather than
/// typed. Typing a number invites the two failures a select menu cannot have:
/// a value outside what the rules accept, and a value that is accepted but
/// meant something else — <c>60</c> where a ratio was wanted, or <c>0.6</c>
/// where a percentage was.
/// <br/>
/// The lists are deliberately shorter than the range the domain allows. A
/// threshold of 63% is representable and would work, but nobody has ever wanted
/// one, and offering every whole percent would bury the four values people
/// actually pick.
/// </remarks>
internal static class Choices
{
    /// <summary>
    /// The most options Discord accepts in one select menu.
    /// </summary>
    public const int PAGE_SIZE = 25;

    /// <summary>
    /// The approval thresholds offered, as whole percentages.
    /// </summary>
    public static ReadOnlySpan<int> Thresholds =>
        [50, 55, 60, 66, 70, 75, 80, 90, 100];

    /// <summary>
    /// The quorum sizes offered.
    /// </summary>
    public static ReadOnlySpan<int> Quorums =>
        [1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 15, 20];

    /// <summary>
    /// The minutes past the hour offered, in five-minute steps.
    /// </summary>
    public static ReadOnlySpan<int> Minutes =>
        [0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55];

    // Enumerating the platform's time zone database is not free, and the answer
    // cannot change while the process is running.
    private static readonly string[] AllZones = LoadZones();

    private static readonly string[] AllRegions =
        [.. AllZones
            .Select(RegionOf)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    /// <summary>
    /// Gets the regions time zones are grouped under.
    /// </summary>
    public static IReadOnlyList<string> Regions => AllRegions;

    /// <summary>
    /// Describes a threshold in the two forms people think in.
    /// </summary>
    /// <param name="percent">The whole percentage.</param>
    /// <returns>A label such as <c>60% — three in five must approve</c>.</returns>
    public static string ThresholdLabel(int percent) => percent switch
    {
        50 => "50% — a simple majority",
        60 => "60% — three in five (default)",
        66 => "66% — two thirds",
        75 => "75% — three quarters",
        100 => "100% — unanimous",
        _ => string.Create(CultureInfo.InvariantCulture, $"{percent}%"),
    };

    /// <summary>
    /// Describes a quorum size.
    /// </summary>
    /// <param name="quorum">The number of votes required.</param>
    /// <returns>A label naming the count.</returns>
    public static string QuorumLabel(int quorum) => quorum switch
    {
        1 => "1 vote",
        3 => "3 votes (default)",
        _ => string.Create(CultureInfo.InvariantCulture, $"{quorum} votes"),
    };

    /// <summary>
    /// Renders an hour as it appears on a twenty-four hour clock.
    /// </summary>
    /// <param name="hour">The hour, from zero to twenty-three.</param>
    /// <returns>A label such as <c>09</c>.</returns>
    public static string HourLabel(int hour) =>
        hour.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Renders a minute past the hour.
    /// </summary>
    /// <param name="minute">The minute, from zero to fifty-nine.</param>
    /// <returns>A label such as <c>30</c>.</returns>
    public static string MinuteLabel(int minute) =>
        minute.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets the zones belonging to a region, one page at a time.
    /// </summary>
    /// <param name="region">The region to read.</param>
    /// <param name="page">The zero-based page.</param>
    /// <returns>At most <see cref="PAGE_SIZE"/> identifiers, in order.</returns>
    public static IReadOnlyList<string> ZonePage(string region, int page) =>
        [.. AllZones
            .Where(id => RegionOf(id).Equals(region, StringComparison.Ordinal))
            .Skip(page * PAGE_SIZE)
            .Take(PAGE_SIZE)];

    /// <summary>
    /// Counts the pages a region's zones fill.
    /// </summary>
    /// <param name="region">The region to measure.</param>
    /// <returns>At least one, so an empty region still renders.</returns>
    public static int PageCount(string region)
    {
        int total = AllZones.Count(id => RegionOf(id).Equals(region, StringComparison.Ordinal));

        return Math.Max(1, (total + PAGE_SIZE - 1) / PAGE_SIZE);
    }

    /// <summary>
    /// Finds the region a zone identifier belongs to.
    /// </summary>
    /// <param name="timeZoneId">The identifier to inspect.</param>
    /// <returns>The part before the slash, or the identifier itself.</returns>
    public static string RegionOf(string timeZoneId)
    {
        int slash = timeZoneId.IndexOf('/', StringComparison.Ordinal);

        return slash < 0 ? timeZoneId : timeZoneId[..slash];
    }

    /// <summary>
    /// Strips the region from a zone identifier for display inside its own list.
    /// </summary>
    /// <param name="timeZoneId">The identifier to shorten.</param>
    /// <returns>The part after the first slash, or the identifier itself.</returns>
    public static string ZoneLabel(string timeZoneId)
    {
        int slash = timeZoneId.IndexOf('/', StringComparison.Ordinal);

        return slash < 0 ? timeZoneId : timeZoneId[(slash + 1)..].Replace('_', ' ');
    }

    /// <summary>
    /// Reads the zones this host can resolve, named the way the domain names them.
    /// </summary>
    /// <remarks>
    /// A schedule is stored as an IANA identifier so that "10:00 in Berlin" stays
    /// 10:00 in Berlin across a daylight saving change. Windows reports its zones
    /// under its own names, so those are translated; on a platform that already
    /// uses IANA names the translation declines and the original is kept. Reading
    /// the list from the host rather than hard-coding it means anything offered is
    /// by construction something the host can resolve.
    /// </remarks>
    /// <returns>The identifiers, in order and without duplicates.</returns>
    private static string[] LoadZones() =>
        [.. TimeZoneInfo.GetSystemTimeZones()
            .Select(zone => TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out string? iana)
                ? iana
                : zone.Id)
            .Where(id => id.Contains('/', StringComparison.Ordinal)
                || id.Equals("UTC", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
}
