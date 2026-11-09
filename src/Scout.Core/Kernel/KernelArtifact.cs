using System.Text;

namespace Scout.Core.Kernel;

/// <summary>
/// One published kernel file. Content is held as bytes so a generated database is published through exactly
/// the same atomic path as a rendered document.
/// </summary>
internal sealed class KernelArtifact
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    private KernelArtifact(string relativePath, ReadOnlyMemory<byte> content)
    {
        RelativePath = relativePath;
        Content = content;
    }

    internal string RelativePath { get; }

    internal ReadOnlyMemory<byte> Content { get; }

    internal static KernelArtifact Text(string relativePath, string content) =>
        new(relativePath, Utf8WithoutBom.GetBytes(content));

    internal static KernelArtifact Binary(string relativePath, ReadOnlyMemory<byte> content) =>
        new(relativePath, content);
}
