using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Fushi.Infrastructure.Persistence.Converters;

/// <summary>
/// Stores a Discord snowflake in a PostgreSQL <c>bigint</c>.
/// </summary>
/// <remarks>
/// PostgreSQL has no unsigned 64-bit integer, and Npgsql will refuse to map
/// <see cref="ulong"/> without being told what to do with it. The alternatives
/// are a <c>numeric</c> column, which is variable-width and slow to index, text,
/// which is worse, or reinterpreting the bits as a signed 64-bit value. The last
/// is what this does.
/// <br/>
/// The conversion is exact and lossless in both directions, because it changes
/// nothing but the interpretation of the top bit. Snowflakes will not reach that
/// bit for around another seventy million years, so in practice every value stored
/// is positive and reads naturally in a SQL client. The <c>unchecked</c>
/// conversion is there to keep the mapping total rather than because the range is
/// expected to be used.
/// <br/>
/// Applied to every <see cref="ulong"/> in the model at once through
/// <c>ConfigureConventions</c>, rather than named on each property. There are
/// dozens of snowflake columns and one forgotten attribute would surface as a
/// provider error at startup — or worse, only for the entity nobody tested.
/// </remarks>
public sealed class SnowflakeConverter : ValueConverter<ulong, long>
{
    /// <summary>
    /// Initialises the converter.
    /// </summary>
    public SnowflakeConverter()
        : base(
            snowflake => unchecked((long)snowflake),
            stored => unchecked((ulong)stored))
    {
    }
}
