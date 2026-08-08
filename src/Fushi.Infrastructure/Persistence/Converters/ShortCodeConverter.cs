using Fushi.Core.Identifiers;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Fushi.Infrastructure.Persistence.Converters;

/// <summary>
/// Stores a <see cref="ShortCode"/> as the six-character text a user would type.
/// </summary>
/// <remarks>
/// A <see cref="ShortCode"/> is really a 30-bit integer, and storing it as one
/// would be four bytes instead of six and marginally faster to compare. It is
/// stored as text anyway, for a reason that outweighs both: somebody will
/// eventually query this database by hand to answer a question about a
/// submission, and they will have the code from Discord, not its integer value.
/// A column they can paste a code into is worth more than two bytes a row.
/// <br/>
/// The text form is canonical — always upper case, always six characters — so the
/// index is a plain equality index with no need for case-insensitive collation.
/// Folding of the confusable characters happens in
/// <see cref="ShortCode.TryParse(string, out ShortCode)"/> before a value ever
/// reaches this converter, so the database never sees an <c>I</c> where a
/// <c>1</c> was meant.
/// </remarks>
public sealed class ShortCodeConverter : ValueConverter<ShortCode, string>
{
    /// <summary>
    /// Initialises the converter.
    /// </summary>
    public ShortCodeConverter()
        : base(
            code => code.ToString(),
            text => ShortCode.Parse(text, null))
    {
    }
}

/// <summary>
/// Stores a nullable <see cref="ShortCode"/> as text, mapping an absent code to
/// <see langword="null"/>.
/// </summary>
/// <remarks>
/// Needed separately because a converter over <c>ShortCode</c> does not
/// automatically apply to <c>ShortCode?</c> when the null must survive the round
/// trip rather than collapsing to <see cref="ShortCode.Empty"/>. The audit trail
/// depends on that distinction: an entry about a guild's configuration has no
/// subject code at all, which is not the same as having an empty one.
/// </remarks>
public sealed class NullableShortCodeConverter : ValueConverter<ShortCode?, string?>
{
    /// <summary>
    /// Initialises the converter.
    /// </summary>
    public NullableShortCodeConverter()
        : base(
            code => code.HasValue ? code.Value.ToString() : null,
            text => text == null ? null : ShortCode.Parse(text, null))
    {
    }
}
