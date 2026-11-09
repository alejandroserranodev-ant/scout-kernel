using Scout.Core.Retrieval;

namespace Scout.Tests.Retrieval;

public sealed class IdentifierTokenizerTests
{
    [Fact]
    public void SplitsCamelCaseAndKeepsTheWholeIdentifier()
    {
        var tokens = IdentifierTokenizer.Tokenize("formatTotal").ToArray();

        Assert.Contains("format", tokens);
        Assert.Contains("total", tokens);
        Assert.Contains("formattotal", tokens);
    }

    [Fact]
    public void SplitsPathsAndKebabCase()
    {
        var tokens = IdentifierTokenizer.Tokenize("src/invoice-store.ts").ToArray();

        Assert.Contains("src", tokens);
        Assert.Contains("invoice", tokens);
        Assert.Contains("store", tokens);
    }

    [Fact]
    public void FoldsAccentedLettersSoDocumentationMeetsAnUnaccentedQuery()
    {
        Assert.Equal(["activacion", "paso"], IdentifierTokenizer.Tokenize("Activación paso").ToArray());
        Assert.Equal(IdentifierTokenizer.Tokenize("configuracion"), IdentifierTokenizer.Tokenize("Configuración"));
    }

    [Fact]
    public void KeepsAcronymsTogether()
    {
        var tokens = IdentifierTokenizer.Tokenize("HTTPClient").ToArray();

        Assert.Contains("http", tokens);
        Assert.Contains("client", tokens);
    }

    [Fact]
    public void DoesNotEmitAWholeIdentifierThatHasASinglePart()
    {
        var tokens = IdentifierTokenizer.Tokenize("invoice").ToArray();

        Assert.Equal(["invoice"], tokens);
    }
}
