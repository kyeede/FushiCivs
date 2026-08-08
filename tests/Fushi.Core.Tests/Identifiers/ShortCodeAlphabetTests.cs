using Fushi.Core.Identifiers;

namespace Fushi.Core.Tests.Identifiers;

/// <summary>
/// Covers <see cref="ShortCodeAlphabet"/>: the Crockford Base32 symbol set, the
/// confusable-letter folding applied on input, and the value/symbol mapping.
/// </summary>
public sealed class ShortCodeAlphabetTests
{
    [Fact]
    public void SymbolSetIsCrockfordBase32()
    {
        new string(ShortCodeAlphabet.Symbols).ShouldBe("0123456789ABCDEFGHJKMNPQRSTVWXYZ");
        ShortCodeAlphabet.Symbols.Length.ShouldBe(ShortCodeAlphabet.RADIX);
    }

    [Theory]
    [InlineData('I')]
    [InlineData('L')]
    [InlineData('O')]
    [InlineData('U')]
    public void SymbolSetExcludesTheConfusableAndUnfortunateLetters(char excluded)
    {
        ShortCodeAlphabet.Symbols.Contains(excluded).ShouldBeFalse();
    }

    [Fact]
    public void EverySymbolMapsBackToItsOwnIndex()
    {
        for (int value = 0; value < ShortCodeAlphabet.RADIX; value++)
        {
            ShortCodeAlphabet.ValueOf(ShortCodeAlphabet.SymbolFor(value)).ShouldBe(value);
        }
    }

    [Theory]
    [InlineData('i', '1')]
    [InlineData('I', '1')]
    [InlineData('l', '1')]
    [InlineData('L', '1')]
    [InlineData('o', '0')]
    [InlineData('O', '0')]
    [InlineData('a', 'A')]
    [InlineData('z', 'Z')]
    [InlineData('7', '7')]
    [InlineData('!', '!')]
    public void NormaliseFoldsConfusablesAndUpperCasesLetters(char typed, char expected)
    {
        ShortCodeAlphabet.Normalise(typed).ShouldBe(expected);
    }

    [Theory]
    [InlineData('I', 1)]
    [InlineData('l', 1)]
    [InlineData('O', 0)]
    [InlineData('o', 0)]
    [InlineData('a', 10)]
    [InlineData('Z', 31)]
    public void ValueOfDecodesFoldedAndLowerCaseCharacters(char typed, int expected)
    {
        ShortCodeAlphabet.ValueOf(typed).ShouldBe(expected);
    }

    [Theory]
    [InlineData('U')]
    [InlineData('u')]
    [InlineData('-')]
    [InlineData(' ')]
    [InlineData('\u00e9')]
    public void ValueOfRejectsCharactersOutsideTheAlphabet(char typed)
    {
        ShortCodeAlphabet.ValueOf(typed).ShouldBe(-1);
        ShortCodeAlphabet.Contains(typed).ShouldBeFalse();
    }

    [Theory]
    [InlineData('0')]
    [InlineData('9')]
    [InlineData('A')]
    [InlineData('z')]
    [InlineData('I')]
    [InlineData('O')]
    public void ContainsAcceptsAnythingThatCanBeDecoded(char typed)
    {
        ShortCodeAlphabet.Contains(typed).ShouldBeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(ShortCodeAlphabet.RADIX)]
    [InlineData(int.MaxValue)]
    public void SymbolForRejectsValuesOutsideTheRadix(int value)
    {
        _ = Should.Throw<ArgumentOutOfRangeException>(() => ShortCodeAlphabet.SymbolFor(value));
    }
}
