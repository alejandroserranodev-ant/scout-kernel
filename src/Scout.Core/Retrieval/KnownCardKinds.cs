namespace Scout.Core.Retrieval;

/// <summary>
/// The kinds of knowledge a Scout Kernel can be retrieved by.
/// </summary>
public static class KnownCardKinds
{
    /// <summary>A stability boundary: what breaks when this changes shape.</summary>
    public const string Contract = "contract";

    /// <summary>An inferred module: where a concern lives.</summary>
    public const string Module = "module";

    /// <summary>A declared symbol: the navigation target itself.</summary>
    public const string Symbol = "symbol";

    /// <summary>A titled documentation section: stated intent to read, not an observed fact.</summary>
    public const string Documentation = "documentation";

    /// <summary>A piece of wording observed in a declaration: the way to reach code from the words it shows.</summary>
    public const string Text = "text";

    /// <summary>A source file as a whole: what it declares, reads and shows, so a question reaches the file to open.</summary>
    public const string File = "file";
}
