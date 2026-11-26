using GameHub.Core.Text;

namespace GameHub.Core.Tests;

public class TextTests
{
    [Theory]
    [InlineData("9 dez. 2020", "2020-12-09")]
    [InlineData("10 de dezembro de 2020", "2020-12-10")]
    [InlineData("Dec 10, 2020", "2020-12-10")]
    [InlineData("2020-12-10", "2020-12-10")]
    [InlineData("Q4 2026", "2026")]
    [InlineData("Em breve", null)]
    public void Dates_are_normalized(string input, string? expected) => Assert.Equal(expected, DateParsing.ToIso(input));

    [Theory]
    [InlineData("The Witcher 3: Wild Hunt", "witcher 3 wild hunt")]
    [InlineData("Final Fantasy VII Remake", "final fantasy 7 remake")]
    [InlineData("Pokémon™ Edição Definitiva", "pokemon")]
    public void Titles_are_normalized(string input, string expected) => Assert.Equal(expected, TitleNormalizer.Normalize(input));

    [Fact]
    public void Similarity_ranks_exact_matches_first()
    {
        Assert.Equal(1, TitleNormalizer.Similarity("Hades", "HADES"));
        Assert.True(TitleNormalizer.Similarity("Hades", "Hades II") > TitleNormalizer.Similarity("Hades", "Hollow Knight"));
    }

    [Fact]
    public void Html_is_converted_to_readable_text()
    {
        Assert.Equal("Título\nLinha &\n\n• item", HtmlText.ToPlain("<h1>Título</h1><p>Linha &amp;</p><br><ul><li>item</li></ul>"));
        Assert.Equal("Uma frase.", HtmlText.Summarize("Uma frase. Outra frase bem mais longa que passa do limite.", 15));
    }
}
