using Microsoft.CodeAnalysis;

namespace UML.Core.SourceGenerators;

internal static class ConfigDiagnostics
{
    private const string Category = "UML.Configuration";

    public static readonly DiagnosticDescriptor DuplicateKey = new(
        "UMLCFG001",
        "Duplicate config key",
        "Config key '{0}' is declared more than once in the same layer",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor NotPartial = new(
        "UMLCFG002",
        "Config declaration must be partial",
        "Config declaration '{0}' requires the property, its type, and all containing types to be partial and accessible",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor NotStatic = new(
        "UMLCFG003",
        "Config property must be static",
        "Config property '{0}' must be static",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor InvalidDefault = new(
        "UMLCFG004",
        "Unsupported config default value",
        "Config property '{0}' has an unsupported default value",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor InvalidKey = new(
        "UMLCFG005",
        "Invalid config key",
        "Config key '{0}' is invalid",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor TypeMismatch = new(
        "UMLCFG006",
        "Config type mismatch",
        "Config property '{0}' type does not match the attribute type argument",
        Category,
        DiagnosticSeverity.Error,
        true);
}
