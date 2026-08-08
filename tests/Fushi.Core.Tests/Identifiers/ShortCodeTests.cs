using System.Globalization;

using Fushi.Core.Identifiers;

namespace Fushi.Core.Tests.Identifiers;

/// <summary>
/// Covers <see cref="ShortCode"/>: generation, the six-character rendered form,
/// the deliberately forgiving parser, the reserved empty value, numeric
/// conversion, and ordering.
/// </summary>
public sealed class ShortCodeTests
{
    private const int SAMPLE_SIZE = 4_000;

    [Fact]
    public void GeneratedCodeRendersAsSixCharactersAndParsesBackToAnEqualValue()
    {
        ShortCode code = ShortCode.New();

        string rendered = Render(code);

        rendered.Length.ShouldBe(ShortCode.LENGTH);
        Parse(rendered).ShouldBe(code);
    }

    [Fact]
    public void GeneratedCodeIsNeverTheReservedEmptyValue()
    {
        for (int attempt = 0; attempt < 1_000; attempt++)
        {
            ShortCode.New().IsEmpty.ShouldBeFalse();
        }
    }

    [Fact]
    public void GeneratedCodesUseOnlyAlphabetSymbols()
    {
        for (int attempt = 0; attempt < 1_000; attempt++)
        {
            foreach (char symbol in Render(ShortCode.New()))
            {
                ShortCodeAlphabet.Symbols.Contains(symbol).ShouldBeTrue();
            }
        }
    }

    [Fact]
    public void GeneratedCodesNeverContainTheExcludedConfusableLetters()
    {
        for (int attempt = 0; attempt < 1_000; attempt++)
        {
            Render(ShortCode.New()).IndexOfAny(['I', 'L', 'O', 'U']).ShouldBe(-1);
        }
    }

    // Not a test of the randomness itself, only that generation spans the code
    // space rather than degenerating. A single repeat is tolerated because the
    // birthday bound over 2^30 values makes one possible at this sample size;
    // two would mean the generator is not drawing uniformly.
    [Fact]
    public void GenerationSpreadsAcrossTheCodeSpaceWithoutRepeating()
    {
        var codes = new HashSet<ShortCode>(SAMPLE_SIZE);
        var leadingSymbols = new HashSet<char>();
        int aboveMidpoint = 0;

        for (int attempt = 0; attempt < SAMPLE_SIZE; attempt++)
        {
            ShortCode code = ShortCode.New();

            code.ToUInt32().ShouldBeLessThan(ShortCode.CARDINALITY);
            _ = codes.Add(code);
            _ = leadingSymbols.Add(Render(code)[0]);

            if (code.ToUInt32() >= ShortCode.CARDINALITY / 2u)
            {
                aboveMidpoint++;
            }
        }

        codes.Count.ShouldBeGreaterThanOrEqualTo(SAMPLE_SIZE - 1);
        leadingSymbols.Count.ShouldBeGreaterThan(1);
        aboveMidpoint.ShouldBeInRange(SAMPLE_SIZE / 4, SAMPLE_SIZE * 3 / 4);
    }

    [Theory]
    [InlineData("1I1L11", "111111")]
    [InlineData("O0OO00", "000000")]
    [InlineData("iiiiii", "111111")]
    public void ParsingFoldsConfusableLettersOntoTheDigitsTheyResemble(string typed, string canonical)
    {
        Parse(typed).ShouldBe(Parse(canonical));
    }

    [Fact]
    public void ParsingIgnoresCase()
    {
        Parse("7k4m2p").ShouldBe(Parse("7K4M2P"));
    }

    [Theory]
    [InlineData("7K4-M2P")]
    [InlineData("7K4_M2P")]
    [InlineData(" 7K4 M2P ")]
    [InlineData("7K4\tM2P")]
    public void ParsingIgnoresSeparatorsAndWhiteSpace(string typed)
    {
        Parse(typed).ShouldBe(Parse("7K4M2P"));
    }

    [Fact]
    public void RenderingRoundTripsThroughParsingForEveryAlphabetSymbol()
    {
        for (int value = 0; value < ShortCodeAlphabet.RADIX; value++)
        {
            string rendered = new(ShortCodeAlphabet.SymbolFor(value), ShortCode.LENGTH);

            Render(Parse(rendered)).ShouldBe(rendered);
        }
    }

    [Fact]
    public void DefaultValueIsTheReservedEmptyCode()
    {
        ShortCode code = default;

        code.ShouldBe(ShortCode.Empty);
        code.IsEmpty.ShouldBeTrue();
        Render(code).ShouldBe("000000");
    }

    [Theory]
    [InlineData("")]
    [InlineData("     ")]
    [InlineData("7K4M2")]
    [InlineData("7K4M2PQ")]
    [InlineData("7K4M2U")]
    [InlineData("7K4M2!")]
    [InlineData("------")]
    public void TryParseRejectsMalformedInputWithoutThrowing(string typed)
    {
        bool parsed = ShortCode.TryParse(typed, CultureInfo.InvariantCulture, out ShortCode result);

        parsed.ShouldBeFalse();
        result.ShouldBe(ShortCode.Empty);
    }

    [Fact]
    public void TryParseRejectsNullWithoutThrowing()
    {
        bool parsed = ShortCode.TryParse(null, CultureInfo.InvariantCulture, out ShortCode result);

        parsed.ShouldBeFalse();
        result.ShouldBe(ShortCode.Empty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("7K4M2")]
    [InlineData("7K4M2PQ")]
    [InlineData("7K4M2U")]
    public void ParseThrowsOnTheInputTryParseRejects(string typed)
    {
        _ = Should.Throw<FormatException>(() => Parse(typed));
    }

    [Fact]
    public void ParseThrowsOnNull()
    {
        _ = Should.Throw<ArgumentNullException>(() => Parse(null!));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(31u)]
    [InlineData(1_000_000u)]
    [InlineData(ShortCode.CARDINALITY - 1u)]
    public void NumericConversionRoundTrips(uint value)
    {
        ShortCode.FromUInt32(value).ToUInt32().ShouldBe(value);
    }

    // Thirty bits is the whole code space, so anything above it is masked rather
    // than rejected: FromUInt32 reconstructs a stored value, it does not validate
    // an untrusted one.
    [Theory]
    [InlineData(ShortCode.CARDINALITY, 0u)]
    [InlineData(ShortCode.CARDINALITY + 5u, 5u)]
    [InlineData(uint.MaxValue, ShortCode.CARDINALITY - 1u)]
    public void NumericConversionMasksValuesAboveTheCodeSpace(uint value, uint expected)
    {
        ShortCode.FromUInt32(value).ToUInt32().ShouldBe(expected);
    }

    [Fact]
    public void TryFormatWritesIntoAnExactlySizedSpan()
    {
        ShortCode code = Parse("7K4M2P");
        Span<char> destination = stackalloc char[ShortCode.LENGTH];

        bool formatted = code.TryFormat(destination, out int written, "G", CultureInfo.InvariantCulture);

        formatted.ShouldBeTrue();
        written.ShouldBe(ShortCode.LENGTH);
        new string(destination).ShouldBe("7K4M2P");
    }

    [Fact]
    public void TryFormatFailsWithoutWritingIntoATooSmallSpan()
    {
        ShortCode code = Parse("7K4M2P");
        Span<char> destination = stackalloc char[ShortCode.LENGTH - 1];
        destination.Fill('.');

        bool formatted = code.TryFormat(destination, out int written, "G", CultureInfo.InvariantCulture);

        formatted.ShouldBeFalse();
        written.ShouldBe(0);
        new string(destination).ShouldBe(".....");
    }

    [Fact]
    public void TryFormatRejectsAnUnknownFormatSpecifier()
    {
        ShortCode code = Parse("7K4M2P");
        Span<char> destination = stackalloc char[ShortCode.LENGTH];

        bool formatted = code.TryFormat(destination, out int written, "X", CultureInfo.InvariantCulture);

        formatted.ShouldBeFalse();
        written.ShouldBe(0);
    }

    [Theory]
    [InlineData(null, "7K4M2P")]
    [InlineData("G", "7K4M2P")]
    [InlineData("g", "7K4M2P")]
    [InlineData("L", "7k4m2p")]
    [InlineData("l", "7k4m2p")]
    public void FormatSpecifiersSelectTheCasing(string? format, string expected)
    {
        ShortCode code = Parse("7K4M2P");

        code.ToString(format, CultureInfo.InvariantCulture).ShouldBe(expected);
    }

    [Fact]
    public void ToStringThrowsOnAnUnknownFormatSpecifier()
    {
        ShortCode code = Parse("7K4M2P");

        _ = Should.Throw<FormatException>(() => code.ToString("X", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void EqualCodesShareAHashCodeAndCompareAsEquivalent()
    {
        ShortCode left = Parse("7K4M2P");
        ShortCode right = Parse("7k4-m2p");

        left.Equals(right).ShouldBeTrue();
        left.Equals((object)right).ShouldBeTrue();
        left.GetHashCode().ShouldBe(right.GetHashCode());
        left.CompareTo(right).ShouldBe(0);
    }

    [Fact]
    public void ACodeIsNotEqualToAValueOfAnotherType()
    {
        Parse("7K4M2P").Equals("7K4M2P").ShouldBeFalse();
    }

    [Theory]
    [InlineData(0u, 1u)]
    [InlineData(1u, 2u)]
    [InlineData(100u, 1_000u)]
    [InlineData(ShortCode.CARDINALITY - 2u, ShortCode.CARDINALITY - 1u)]
    public void ComparisonOperatorsAgreeWithCompareTo(uint lower, uint higher)
    {
        ShortCode low = ShortCode.FromUInt32(lower);
        ShortCode high = ShortCode.FromUInt32(higher);

        low.CompareTo(high).ShouldBeLessThan(0);
        (low < high).ShouldBeTrue();
        (low <= high).ShouldBeTrue();
        (low > high).ShouldBeFalse();
        (low >= high).ShouldBeFalse();
        (low == high).ShouldBeFalse();
        (low != high).ShouldBeTrue();
    }

    [Fact]
    public void ComparisonOperatorsTreatEqualCodesAsNeitherLowerNorHigher()
    {
        ShortCode left = ShortCode.FromUInt32(42u);
        ShortCode right = ShortCode.FromUInt32(42u);

        (left < right).ShouldBeFalse();
        (left > right).ShouldBeFalse();
        (left <= right).ShouldBeTrue();
        (left >= right).ShouldBeTrue();
        (left == right).ShouldBeTrue();
        (left != right).ShouldBeFalse();
    }

    // The alphabet is ordered by value, so sorting by the numeric value and
    // sorting the rendered text ordinally have to agree.
    [Fact]
    public void OrderingMatchesTheOrdinalOrderOfTheRenderedForm()
    {
        List<ShortCode> codes =
        [
            .. Enumerable.Range(0, 200).Select(step => ShortCode.FromUInt32(unchecked((uint)step * 7_919u))),
        ];

        List<string> byValue = [.. codes.Order().Select(Render)];
        List<string> byText = [.. codes.Select(Render).Order(StringComparer.Ordinal)];

        byValue.ShouldBe(byText);
    }

    private static ShortCode Parse(string text) => ShortCode.Parse(text, CultureInfo.InvariantCulture);

    private static string Render(ShortCode code) => code.ToString(format: null, CultureInfo.InvariantCulture);
}
