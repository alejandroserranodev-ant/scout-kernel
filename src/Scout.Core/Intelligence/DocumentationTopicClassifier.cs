using Scout.Core.Text;

namespace Scout.Core.Intelligence;

/// <summary>
/// Matches the title of a documentation section against the vocabulary each topic is usually written in.
/// </summary>
/// <remarks>
/// <para>
/// The match is lexical and nothing more: a title that names a topic is reported for that topic, and the
/// section body is never consulted. That is why the result is stated intent to go and read, not a fact.
/// </para>
/// <para>
/// Titles are folded to unaccented lowercase words first, so the vocabulary is written once per word and
/// serves "Convenciones" and "convenciones" alike. English and Spanish are covered because they are the
/// languages the documentation Scout is pointed at is written in; a topic with no entry for a language
/// simply goes unmatched in it.
/// </para>
/// </remarks>
internal static class DocumentationTopicClassifier
{
    // A word matches when it begins with a stem, so one stem serves its singular, plural and inflections.
    // Stems that are short enough to begin unrelated words are listed as whole words instead.
    private static readonly (string Topic, string[] Stems, string[] Words)[] Vocabulary =
    [
        (KnownDocumentationTopics.Architecture,
            ["architect", "arquitect", "design", "diseno", "estructura", "structure", "module", "modulo", "component", "layer", "capas"],
            []),
        (KnownDocumentationTopics.Conventions,
            ["convention", "convenc", "style", "estilo", "naming", "nomenclatur", "standard", "estandar", "guideline", "pattern", "patron", "practice", "practica", "rule", "regla"],
            []),
        (KnownDocumentationTopics.Contracts,
            ["contract", "contrato", "endpoint", "interface", "interfaz", "schema", "esquema"],
            ["api", "apis"]),
        (KnownDocumentationTopics.Concerns,
            ["security", "segur", "privacy", "privacidad", "performance", "rendimiento", "risk", "riesgo", "limitation", "limitacion", "debt", "deuda", "constraint", "restriccion", "observab", "accessib", "accesib"],
            []),
        (KnownDocumentationTopics.Flows,
            ["flow", "flujo", "workflow", "process", "proceso", "pipeline", "lifecycle", "ciclo", "journey", "sequence", "secuencia"],
            []),
        (KnownDocumentationTopics.Decisions,
            ["decision", "rationale", "tradeoff", "trade-off"],
            ["adr", "adrs"]),
        (KnownDocumentationTopics.Operations,
            ["deploy", "despliegue", "setup", "install", "instalac", "configur", "environment", "entorno", "runbook", "release", "migrat", "migrac", "operat", "operac"],
            []),
        (KnownDocumentationTopics.Testing,
            ["test", "prueba", "verific", "coverage", "cobertura"],
            ["qa"])
    ];

    /// <summary>
    /// Finds the topics a section title names.
    /// </summary>
    /// <param name="title">The section title.</param>
    /// <returns>The matching topics in reporting order, or none when the title names no topic.</returns>
    internal static IReadOnlyList<string> Classify(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        var words = SplitWords(DiacriticFolding.Fold(title));
        if (words.Length == 0)
        {
            return [];
        }

        return
        [
            .. Vocabulary
                .Where(entry => words.Any(word =>
                    entry.Words.Contains(word, StringComparer.Ordinal) ||
                    entry.Stems.Any(stem => word.StartsWith(stem, StringComparison.Ordinal))))
                .Select(entry => entry.Topic)
        ];
    }

    private static string[] SplitWords(string text)
    {
        var separated = text
            .ToLowerInvariant()
            .Select(static character => char.IsAsciiLetterOrDigit(character) || character == '-' ? character : ' ')
            .ToArray();
        return new string(separated).Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
