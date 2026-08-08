namespace Fushi.Core.Identifiers;

/// <summary>
/// The symbol set used to render and read a <see cref="ShortCode"/>.
/// </summary>
/// <remarks>
/// This is Crockford's Base32 alphabet: the digits <c>0</c>–<c>9</c> followed by
/// the letters <c>A</c>–<c>Z</c> with <c>I</c>, <c>L</c>, <c>O</c>, and
/// <c>U</c> removed. The first three are dropped because they are visually
/// confusable with <c>1</c> and <c>0</c> in most fonts, and <c>U</c> is dropped
/// so that a randomly generated code is far less likely to spell something
/// unfortunate.
/// <br/>
/// Because the confusable letters are excluded from output rather than merely
/// discouraged, they can be reinterpreted unambiguously on input:
/// <see cref="Normalise"/> folds <c>I</c> and <c>L</c> to <c>1</c> and
/// <c>O</c> to <c>0</c>. A user who mistypes what they read off the screen
/// still lands on the right submission.
/// </remarks>
/// <seealso href="https://www.crockford.com/base32.html">Crockford Base32</seealso>
public static class ShortCodeAlphabet
{
    /// <summary>
    /// The number of distinct symbols, and therefore the numeric base.
    /// </summary>
    public const int RADIX = 32;

    /// <summary>
    /// The encoding symbols, ordered so that the index of a character is its
    /// numeric value.
    /// </summary>
    /// <value>
    /// The 32 characters <c>0123456789ABCDEFGHJKMNPQRSTVWXYZ</c>.
    /// </value>
    public static ReadOnlySpan<char> Symbols => "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>
    /// Converts a character to the value it encodes, applying the confusable
    /// letter folding described on this type.
    /// </summary>
    /// <param name="symbol">
    /// The character to interpret. Case is ignored.
    /// </param>
    /// <returns>
    /// The value in the range <c>0</c> to <c>31</c>, or <c>-1</c> when the
    /// character is not part of the alphabet and is not a recognised
    /// substitute for one.
    /// </returns>
    public static int ValueOf(char symbol)
    {
        char folded = Normalise(symbol);

        int index = Symbols.IndexOf(folded);
        return index;
    }

    /// <summary>
    /// Converts a value to the character that encodes it.
    /// </summary>
    /// <param name="value">A value in the range <c>0</c> to <c>31</c>.</param>
    /// <returns>The encoding character.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="value"/> is negative or is not less than
    /// <see cref="RADIX"/>.
    /// </exception>
    public static char SymbolFor(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(value, RADIX);

        return Symbols[value];
    }

    /// <summary>
    /// Maps a character to its canonical alphabet form, upper-casing it and
    /// folding the confusable letters onto the digits they resemble.
    /// </summary>
    /// <param name="symbol">The character to fold.</param>
    /// <returns>
    /// The canonical character, which is still outside the alphabet when the
    /// input was not a recognised symbol.
    /// </returns>
    public static char Normalise(char symbol) =>
        symbol switch
        {
            'i' or 'I' or 'l' or 'L' => '1',
            'o' or 'O' => '0',
            >= 'a' and <= 'z' => (char)(symbol - 32),
            _ => symbol,
        };

    /// <summary>
    /// Determines whether a character is a symbol of this alphabet, or folds
    /// onto one.
    /// </summary>
    /// <param name="symbol">The character to test. Case is ignored.</param>
    /// <returns>
    /// <see langword="true"/> when the character can be decoded; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public static bool Contains(char symbol) => ValueOf(symbol) >= 0;
}
