namespace Scout.Core.Intelligence;

/// <summary>
/// Provides well-known contract kinds while allowing future compilers to emit additional string kinds.
/// </summary>
/// <remarks>
/// A contract kind describes why a symbol behaves as a stability boundary. It is an inference derived from
/// observed consumers, never an assertion that the symbol was intentionally designed as a public API.
/// </remarks>
public static class KnownContractKinds
{
    /// <summary>Identifies a symbol that other symbols implement.</summary>
    public const string Abstraction = "abstraction";

    /// <summary>Identifies a symbol that other symbols inherit from.</summary>
    public const string InheritanceRoot = "inheritance-root";

    /// <summary>Identifies a callable consumed from outside its declaring type.</summary>
    public const string CallableSurface = "callable-surface";

    /// <summary>Identifies a type consumed by several independent symbols.</summary>
    public const string SharedType = "shared-type";

    /// <summary>Identifies a value declaration, such as a constant or a design token, consumed by several symbols.</summary>
    public const string SharedValue = "shared-value";

    /// <summary>Identifies a view component rendered by other components, whose props form the boundary.</summary>
    public const string ComponentSurface = "component-surface";
}
