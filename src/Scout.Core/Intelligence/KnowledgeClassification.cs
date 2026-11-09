namespace Scout.Core.Intelligence;

/// <summary>
/// Distinguishes observed facts from derived interpretations and proposed guidance.
/// </summary>
public enum KnowledgeClassification
{
    /// <summary>Knowledge directly established by structured analysis.</summary>
    Fact,

    /// <summary>Knowledge derived from one or more facts.</summary>
    Inference,

    /// <summary>Guidance proposed from facts or inferences.</summary>
    Recommendation
}
