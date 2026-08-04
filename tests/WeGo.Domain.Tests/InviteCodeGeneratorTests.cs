using WeGo.Domain.Trips;

namespace WeGo.Domain.Tests;

public sealed class InviteCodeGeneratorTests
{
    [Fact]
    public void Generate_produces_a_code_of_the_specified_length()
    {
        InviteCodeGenerator.Generate().Should().HaveLength(InviteCodeGenerator.Length);
    }

    [Fact]
    public void Generate_only_uses_the_unambiguous_alphabet()
    {
        for (var i = 0; i < 200; i++)
        {
            InviteCodeGenerator.Generate().ToCharArray()
                .Should().OnlyContain(c => InviteCodeGenerator.Alphabet.Contains(c));
        }
    }

    [Fact]
    public void Alphabet_excludes_the_characters_that_are_easy_to_misread()
    {
        InviteCodeGenerator.Alphabet.Should().NotContain("I");
        InviteCodeGenerator.Alphabet.Should().NotContain("L");
        InviteCodeGenerator.Alphabet.Should().NotContain("O");
        InviteCodeGenerator.Alphabet.Should().NotContain("U");
        InviteCodeGenerator.Alphabet.Should().NotContain("0");
        InviteCodeGenerator.Alphabet.Should().NotContain("1");
    }

    [Fact]
    public void Generate_does_not_repeat_itself_across_many_draws()
    {
        var codes = Enumerable.Range(0, 500).Select(_ => InviteCodeGenerator.Generate()).ToList();

        codes.Distinct().Should().HaveCount(codes.Count);
    }

    [Fact]
    public void Generate_rejects_out_of_range_bytes_instead_of_folding_them()
    {
        // 250 is above the rejection limit (240) and must be discarded; the next
        // byte, 0, is the one that decides the character. A '%' fold would have
        // produced Alphabet[250 % 30] instead.
        var queue = new Queue<byte>([250, 0, 1, 2, 3, 4, 5, 6, 7, 8]);

        var code = InviteCodeGenerator.Generate(buffer => buffer[0] = queue.Dequeue());

        code[0].Should().Be(InviteCodeGenerator.Alphabet[0]);
        code[1].Should().Be(InviteCodeGenerator.Alphabet[1]);
    }

    [Fact]
    public void Generate_maps_bytes_below_the_limit_straight_onto_the_alphabet()
    {
        var queue = new Queue<byte>([0, 1, 2, 3, 4, 5, 6, 7]);

        var code = InviteCodeGenerator.Generate(buffer => buffer[0] = queue.Dequeue());

        code.Should().Be(InviteCodeGenerator.Alphabet.Substring(0, 8));
    }

    [Theory]
    [InlineData("abcdefgh", "ABCDEFGH")]
    [InlineData("  AbCdEfGh  ", "ABCDEFGH")]
    public void Normalize_upper_cases_and_trims(string input, string expected)
    {
        InviteCodeGenerator.Normalize(input).Should().Be(expected);
    }
}
