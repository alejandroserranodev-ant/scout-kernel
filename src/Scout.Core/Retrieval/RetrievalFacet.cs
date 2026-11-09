namespace Scout.Core.Retrieval;

/// <summary>
/// A compact attribute returned with a card so the caller does not have to open the detail to use it.
/// </summary>
/// <param name="Name">The attribute name.</param>
/// <param name="Value">The attribute value.</param>
public readonly record struct RetrievalFacet(string Name, string Value);
