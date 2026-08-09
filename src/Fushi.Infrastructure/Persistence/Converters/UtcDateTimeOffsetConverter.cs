using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Fushi.Infrastructure.Persistence.Converters;

/// <summary>
/// Writes every <see cref="DateTimeOffset"/> as UTC before it reaches PostgreSQL.
/// </summary>
/// <remarks>
/// Npgsql rejects non-zero offsets for <c>timestamp with time zone</c>. Domain
/// code is supposed to produce UTC already (see
/// <c>CycleSchedule</c> resolution), but a converter here is the last line of
/// defence: a single accidental local-offset value must not take down a cycle
/// open. Reads are left alone — values coming back from the store are already UTC.
/// </remarks>
internal sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, DateTimeOffset>
{
    /// <summary>
    /// Initialises the converter.
    /// </summary>
    public UtcDateTimeOffsetConverter()
        : base(
            value => value.ToUniversalTime(),
            value => new DateTimeOffset(DateTime.SpecifyKind(value.UtcDateTime, DateTimeKind.Utc)))
    {
    }
}

/// <summary>
/// Nullable counterpart of <see cref="UtcDateTimeOffsetConverter"/>.
/// </summary>
internal sealed class NullableUtcDateTimeOffsetConverter
    : ValueConverter<DateTimeOffset?, DateTimeOffset?>
{
    /// <summary>
    /// Initialises the converter.
    /// </summary>
    public NullableUtcDateTimeOffsetConverter()
        : base(
            value => value.HasValue ? value.GetValueOrDefault().ToUniversalTime() : null,
            value => value.HasValue
                ? new DateTimeOffset(
                    DateTime.SpecifyKind(value.GetValueOrDefault().UtcDateTime, DateTimeKind.Utc))
                : null)
    {
    }
}
