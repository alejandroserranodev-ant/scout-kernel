namespace Scout.Core.Retrieval;

/// <summary>
/// A vocabulary entry: the dimension a term occupies and how much observing it is worth.
/// </summary>
/// <param name="Term">The lowercase retrieval term.</param>
/// <param name="Dimension">The dimension the term occupies.</param>
/// <param name="InverseDocumentFrequency">How rare, and therefore how informative, the term is.</param>
public readonly record struct EmbeddingTerm(string Term, int Dimension, double InverseDocumentFrequency);
