using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Fushi.Core.Identifiers;

/// <summary>
/// A six-character public reference code, short enough for someone to read off
/// a screen and type into a command.
/// </summary>
/// <remarks>
/// Every entity a user can address keeps two identities. The
/// <see cref="System.Guid"/> primary key is what the database and the code join
/// on, and it never appears in the interface. The short code is what a person
/// works with: <c>/submission view code:7K4M2P</c> is something a moderator can
/// relay over voice chat, which <c>3f2504e0-4f89-11d3-9a0c-0305e82c3301</c> is
/// not.
/// <br/>
/// The value is thirty bits rendered in <see cref="ShortCodeAlphabet"/>, giving
/// 1,073,741,824 possibilities. That number is large but not unlimited, and the
/// relevant figure is not exhaustion but collision: by the birthday bound, a
/// pool this size reaches a fifty percent chance of one repeat at roughly
/// 38,000 codes. Codes are therefore scoped and enforced unique per guild by a
/// database index, and generation retries on conflict rather than trusting
/// randomness to be enough on its own.
/// <br/>
/// The zero value is reserved to mean "no code assigned" so that
/// <c>default(ShortCode)</c> is recognisably empty rather than a legitimate
/// looking <c>000000</c>. <see cref="New"/> never produces it.
/// </remarks>
/// <example>
/// <code>
/// ShortCode code = ShortCode.New();
/// Console.WriteLine(code);                       // e.g. 7K4M2P
///
/// // Confusable characters are folded, so a misread still resolves.
/// ShortCode.Parse("7k4m2p") == code;             // true, case is ignored
/// ShortCode.Parse("7K4M2P") == ShortCode.Parse("7K4M2P");
/// </code>
/// </example>
public readonly struct ShortCode
    : IEquatable<ShortCode>,
        IComparable<ShortCode>,
        ISpanFormattable,
        ISpanParsable<ShortCode>
{
    /// <summary>
    /// The number of characters in the rendered form.
    /// </summary>
    public const int LENGTH = 6;

    /// <summary>
    /// The number of distinct codes, including the reserved empty value.
    /// </summary>
    /// <remarks>
    /// Equal to <see cref="ShortCodeAlphabet.RADIX"/> raised to
    /// <see cref="LENGTH"/>, which is exactly 2^30.
    /// </remarks>
    public const uint CARDINALITY = 1u << 30;

    private const uint MASK = CARDINALITY - 1u;

    private readonly uint _value;

    private ShortCode(uint value)
    {
        _value = value & MASK;
    }

    /// <summary>
    /// Gets the reserved value meaning that no code has been assigned.
    /// </summary>
    public static ShortCode Empty => default;

    /// <summary>
    /// Gets a value indicating whether this is the reserved empty code.
    /// </summary>
    public bool IsEmpty => _value == 0u;

    /// <summary>
    /// Generates a new code using a cryptographically secure random source.
    /// </summary>
    /// <remarks>
    /// The alphabet size is a power of two, so masking raw random bits down to
    /// thirty selects each code with exactly equal probability. A modulo
    /// reduction against a non-power-of-two alphabet would skew towards the
    /// lower symbols; nothing here needs rejection sampling to avoid that.
    /// <br/>
    /// A cryptographic source rather than <see cref="System.Random"/> because
    /// codes address moderation records. A predictable sequence would let
    /// someone enumerate submissions that were never shared with them.
    /// </remarks>
    /// <returns>A new code, never <see cref="Empty"/>.</returns>
    public static ShortCode New()
    {
        Span<byte> buffer = stackalloc byte[sizeof(uint)];

        uint value;
        do
        {
            RandomNumberGenerator.Fill(buffer);
            value = BinaryPrimitives.ReadUInt32LittleEndian(buffer) & MASK;
        } while (value == 0u);

        return new ShortCode(value);
    }

    /// <summary>
    /// Reconstructs a code from the numeric value returned by
    /// <see cref="ToUInt32"/>.
    /// </summary>
    /// <param name="value">
    /// The numeric value. Bits above the low thirty are discarded.
    /// </param>
    /// <returns>The reconstructed code.</returns>
    public static ShortCode FromUInt32(uint value) => new(value);

    /// <summary>
    /// Returns the numeric value behind this code.
    /// </summary>
    /// <remarks>
    /// Useful for a storage provider that prefers a fixed-width integer column
    /// over the six-character text form. The default mapping stores the text,
    /// which keeps ad-hoc database queries legible.
    /// </remarks>
    /// <returns>A value below <see cref="CARDINALITY"/>.</returns>
    public uint ToUInt32() => _value;

    /// <summary>
    /// Parses a code, accepting any casing and ignoring hyphens, underscores,
    /// and white space.
    /// </summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="provider">
    /// Unused. The format is invariant, so no culture affects it.
    /// </param>
    /// <returns>The parsed code.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="s"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="FormatException">
    /// <paramref name="s"/> does not contain exactly <see cref="LENGTH"/>
    /// alphabet characters.
    /// </exception>
    public static ShortCode Parse(string s, IFormatProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(s);

        return Parse(s.AsSpan(), provider);
    }

    /// <inheritdoc cref="Parse(string, IFormatProvider?)"/>
    public static ShortCode Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null) =>
        TryParse(s, provider, out ShortCode code)
            ? code
            : throw new FormatException(
                $"'{s}' is not a valid short code. A code is {LENGTH} characters from "
                    + $"the alphabet {ShortCodeAlphabet.Symbols}."
            );

    /// <summary>
    /// Attempts to parse a code, accepting any casing and ignoring hyphens,
    /// underscores, and white space.
    /// </summary>
    /// <remarks>
    /// This is the entry point for anything a user typed. It is deliberately
    /// forgiving about presentation — <c>7k4-m2p</c> and <c>7K4M2P</c> both
    /// parse — while staying strict about the code itself, so a wrong code is
    /// still rejected rather than silently coerced into a different one.
    /// </remarks>
    /// <param name="s">The text to parse.</param>
    /// <param name="provider">
    /// Unused. The format is invariant, so no culture affects it.
    /// </param>
    /// <param name="result">
    /// When this method returns <see langword="true"/>, the parsed code;
    /// otherwise <see cref="Empty"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the text was a valid code; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ShortCode result)
    {
        _ = provider;
        result = Empty;

        uint accumulated = 0u;
        int consumed = 0;

        foreach (char character in s)
        {
            if (character is '-' or '_' or ' ' or '\t')
            {
                continue;
            }

            int symbol = ShortCodeAlphabet.ValueOf(character);
            if (symbol < 0)
            {
                return false;
            }

            if (++consumed > LENGTH)
            {
                return false;
            }

            accumulated = (accumulated * ShortCodeAlphabet.RADIX) + (uint)symbol;
        }

        if (consumed != LENGTH)
        {
            return false;
        }

        result = new ShortCode(accumulated);
        return true;
    }

    /// <inheritdoc cref="TryParse(ReadOnlySpan{char}, IFormatProvider?, out ShortCode)"/>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ShortCode result)
    {
        if (s is null)
        {
            result = Empty;
            return false;
        }

        return TryParse(s.AsSpan(), provider, out result);
    }

    /// <inheritdoc cref="TryParse(ReadOnlySpan{char}, IFormatProvider?, out ShortCode)"/>
    public static bool TryParse([NotNullWhen(true)] string? s, out ShortCode result) =>
        TryParse(s, provider: null, out result);

    /// <summary>
    /// Renders the code as <see cref="LENGTH"/> characters.
    /// </summary>
    /// <returns>The rendered code, such as <c>7K4M2P</c>.</returns>
    public override string ToString() => ToString(format: null, formatProvider: null);

    /// <summary>
    /// Renders the code as <see cref="LENGTH"/> characters.
    /// </summary>
    /// <param name="format">
    /// <c>G</c> or <see langword="null"/> for the canonical upper-case form, or
    /// <c>L</c> for a lower-case form suitable for a URL path segment.
    /// </param>
    /// <param name="formatProvider">
    /// Unused. The format is invariant, so no culture affects it.
    /// </param>
    /// <returns>The rendered code.</returns>
    /// <exception cref="FormatException">
    /// <paramref name="format"/> is not a recognised specifier.
    /// </exception>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        Span<char> buffer = stackalloc char[LENGTH];
        if (!TryFormat(buffer, out int written, format, formatProvider))
        {
            throw new FormatException($"'{format}' is not a supported short code format.");
        }

        return new string(buffer[..written]);
    }

    /// <summary>
    /// Writes the rendered code into the destination span without allocating.
    /// </summary>
    /// <param name="destination">
    /// The span to write into. Must hold at least <see cref="LENGTH"/>
    /// characters.
    /// </param>
    /// <param name="charsWritten">
    /// When this method returns <see langword="true"/>, the number of
    /// characters written, which is always <see cref="LENGTH"/>.
    /// </param>
    /// <param name="format">
    /// <c>G</c> or empty for the canonical upper-case form, or <c>L</c> for
    /// lower case.
    /// </param>
    /// <param name="provider">
    /// Unused. The format is invariant, so no culture affects it.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the code was written; <see langword="false"/>
    /// when <paramref name="destination"/> was too small or the format was not
    /// recognised.
    /// </returns>
    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default,
        IFormatProvider? provider = null
    )
    {
        _ = provider;
        charsWritten = 0;

        bool lowercase;
        if (format.IsEmpty || format is "G" or "g")
        {
            lowercase = false;
        }
        else if (format is "L" or "l")
        {
            lowercase = true;
        }
        else
        {
            return false;
        }

        if (destination.Length < LENGTH)
        {
            return false;
        }

        uint remaining = _value;
        for (int position = LENGTH - 1; position >= 0; position--)
        {
            char symbol = ShortCodeAlphabet.SymbolFor((int)(remaining % ShortCodeAlphabet.RADIX));
            destination[position] = lowercase ? char.ToLowerInvariant(symbol) : symbol;
            remaining /= ShortCodeAlphabet.RADIX;
        }

        charsWritten = LENGTH;
        return true;
    }

    /// <inheritdoc/>
    public bool Equals(ShortCode other) => _value == other._value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ShortCode other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _value.GetHashCode();

    /// <inheritdoc/>
    /// <remarks>
    /// Orders by numeric value, which is the same as ordinal order of the
    /// rendered form because the alphabet is itself ordered by value.
    /// </remarks>
    public int CompareTo(ShortCode other) => _value.CompareTo(other._value);

    /// <summary>Determines whether two codes are the same.</summary>
    /// <param name="left">The first code.</param>
    /// <param name="right">The second code.</param>
    /// <returns><see langword="true"/> when they are equal.</returns>
    public static bool operator ==(ShortCode left, ShortCode right) => left.Equals(right);

    /// <summary>Determines whether two codes differ.</summary>
    /// <param name="left">The first code.</param>
    /// <param name="right">The second code.</param>
    /// <returns><see langword="true"/> when they are not equal.</returns>
    public static bool operator !=(ShortCode left, ShortCode right) => !left.Equals(right);

    /// <summary>Determines whether one code sorts before another.</summary>
    /// <param name="left">The first code.</param>
    /// <param name="right">The second code.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> sorts first.</returns>
    public static bool operator <(ShortCode left, ShortCode right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether one code sorts before another or equals it.</summary>
    /// <param name="left">The first code.</param>
    /// <param name="right">The second code.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> does not sort after.</returns>
    public static bool operator <=(ShortCode left, ShortCode right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether one code sorts after another.</summary>
    /// <param name="left">The first code.</param>
    /// <param name="right">The second code.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> sorts last.</returns>
    public static bool operator >(ShortCode left, ShortCode right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether one code sorts after another or equals it.</summary>
    /// <param name="left">The first code.</param>
    /// <param name="right">The second code.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> does not sort first.</returns>
    public static bool operator >=(ShortCode left, ShortCode right) => left.CompareTo(right) >= 0;
}
