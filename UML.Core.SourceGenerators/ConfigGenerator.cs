using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UML.Core.SourceGenerators;

[Generator(LanguageNames.CSharp)]
public sealed class ConfigGenerator : IIncrementalGenerator
{
    private const string AttributeName = "UML.Core.Configuration.ConfigItemAttribute`1";

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeName,
                static (node, _) => node is PropertyDeclarationSyntax,
                static (ctx, _) => CreateCandidate(ctx))
            .Collect();

        context.RegisterSourceOutput(candidates, static (spc, items) => Emit(spc, items));
    }

    private static Candidate CreateCandidate(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not IPropertySymbol property)
            return Candidate.Empty;

        var containingType = property.ContainingType;
        if (containingType is null)
            return Candidate.Empty;

        var attribute = context.Attributes.FirstOrDefault();
        if (attribute is null)
            return Candidate.Empty;

        var attributeClass = attribute.AttributeClass;
        if (attributeClass is null)
            return Candidate.Empty;

        var location = property.Locations.FirstOrDefault() ?? Location.None;
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        if (!property.IsPartialDefinition)
            diagnostics.Add(Diagnostic.Create(ConfigDiagnostics.NotPartial, location, property.Name));

        if (!property.IsStatic)
            diagnostics.Add(Diagnostic.Create(ConfigDiagnostics.NotStatic, location, property.Name));

        if (!IsPartialChain(containingType) || !IsAccessible(containingType))
            diagnostics.Add(Diagnostic.Create(ConfigDiagnostics.NotPartial, location, containingType.Name));

        var arguments = attribute.ConstructorArguments;
        if (arguments.Length < 2 || arguments[0].Value is not string key || string.IsNullOrWhiteSpace(key))
        {
            diagnostics.Add(Diagnostic.Create(ConfigDiagnostics.InvalidKey, location, property.Name));
            return new Candidate(null, diagnostics.ToImmutable());
        }

        if (!IsValidKey(key))
            diagnostics.Add(Diagnostic.Create(ConfigDiagnostics.InvalidKey, location, key));

        var attributeType = attributeClass.TypeArguments.FirstOrDefault();
        if (attributeType is null || !SymbolEqualityComparer.Default.Equals(attributeType, property.Type))
            diagnostics.Add(Diagnostic.Create(ConfigDiagnostics.TypeMismatch, location, property.Name));

        string defaultExpression;
        try
        {
            defaultExpression = RenderDefault(arguments[1]);
        }
        catch
        {
            diagnostics.Add(Diagnostic.Create(ConfigDiagnostics.InvalidDefault, location, property.Name));
            return new Candidate(null, diagnostics.ToImmutable());
        }

        var layerValue = arguments.Length > 2 && arguments[2].Value is int rawLayer ? rawLayer : 1;
        if (layerValue is < 0 or > 2)
        {
            diagnostics.Add(Diagnostic.Create(ConfigDiagnostics.InvalidDefault, location, property.Name));
            return new Candidate(null, diagnostics.ToImmutable());
        }

        if (diagnostics.Count > 0)
            return new Candidate(null, diagnostics.ToImmutable());

        var model = new ConfigModel(
            property.Name,
            key,
            defaultExpression,
            RenderLayer(layerValue),
            property.DeclaredAccessibility,
            property.SetMethod is not null,
            property.SetMethod?.IsInitOnly == true,
            containingType,
            property.Type,
            location);

        return new Candidate(model, ImmutableArray<Diagnostic>.Empty);
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<Candidate> candidates)
    {
        foreach (var candidate in candidates)
        foreach (var diagnostic in candidate.Diagnostics)
            context.ReportDiagnostic(diagnostic);

        var models = candidates
            .Where(candidate => candidate.Model is not null)
            .Select(candidate => candidate.Model!)
            .ToArray();

        foreach (var group in models
                     .GroupBy(model => (model.LayerExpression, model.Key))
                     .Where(group => group.Count() > 1))
        foreach (var model in group)
            context.ReportDiagnostic(Diagnostic.Create(
                ConfigDiagnostics.DuplicateKey,
                model.Location ?? Location.None,
                model.Key));

        foreach (var group in models.GroupBy(
                     model => model.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            context.AddSource(
                $"{Sanitize(group.Key)}.g.cs",
                EmitType(group.First().ContainingType, group.ToArray()));

        context.AddSource("ConfigSchema.g.cs", EmitSchema(models));
    }

    private static string EmitType(INamedTypeSymbol type, IReadOnlyList<ConfigModel> models)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");

        var namespaceName = GetNamespace(type);
        if (!string.IsNullOrEmpty(namespaceName))
        {
            builder.Append("namespace ").Append(namespaceName).AppendLine(";");
            builder.AppendLine();
        }

        var chain = new Stack<INamedTypeSymbol>();
        for (var current = type; current is not null; current = current.ContainingType)
            chain.Push(current);

        var indent = 0;
        foreach (var current in chain)
        {
            AppendIndent(builder, indent);
            builder.AppendLine(TypeDeclaration(current));
            AppendIndent(builder, indent);
            builder.AppendLine("{");
            indent++;
        }

        foreach (var model in models.OrderBy(model => model.PropertyName, StringComparer.Ordinal))
        {
            var typeName = model.PropertyType.ToDisplayString(TypeFormat);
            var fieldName = model.PropertyName + "Key";

            AppendIndent(builder, indent);
            builder.Append("public static readonly global::UML.Core.Configuration.ConfigKey<")
                .Append(typeName)
                .Append("> ")
                .Append(fieldName)
                .Append(" = new(")
                .Append(SymbolDisplay.FormatLiteral(model.Key, true))
                .Append(", ")
                .Append(model.DefaultExpression)
                .Append(", ")
                .Append(model.LayerExpression)
                .AppendLine(");");

            builder.AppendLine();
            AppendIndent(builder, indent);
            builder.Append(AccessibilityText(model.Accessibility))
                .Append(" static partial ")
                .Append(typeName)
                .Append(' ')
                .AppendLine(model.PropertyName);
            AppendIndent(builder, indent);
            builder.AppendLine("{");
            AppendIndent(builder, indent + 1);
            builder.Append("get => global::UML.Core.Configuration.ConfigService.Current.Get(")
                .Append(fieldName)
                .AppendLine(");");

            if (model.HasSetter)
            {
                AppendIndent(builder, indent + 1);
                builder.Append(model.IsInit ? "init" : "set")
                    .Append(" => global::UML.Core.Configuration.ConfigService.Current.Set(")
                    .Append(fieldName)
                    .AppendLine(", value);");
            }

            AppendIndent(builder, indent);
            builder.AppendLine("}");
            builder.AppendLine();
        }

        for (var index = indent - 1; index >= 0; index--)
        {
            AppendIndent(builder, index);
            builder.AppendLine("}");
        }

        return builder.ToString();
    }

    private static string EmitSchema(IReadOnlyList<ConfigModel> models)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("namespace UML.Core.Configuration;");
        builder.AppendLine();
        builder.AppendLine("internal static partial class ConfigSchema");
        builder.AppendLine("{");
        builder.AppendLine("    public static readonly global::System.Collections.Generic.IReadOnlyList<global::UML.Core.Configuration.IConfigKey> All = new global::UML.Core.Configuration.IConfigKey[]");
        builder.AppendLine("    {");

        foreach (var model in models
                     .OrderBy(model => GetNamespace(model.ContainingType), StringComparer.Ordinal)
                     .ThenBy(
                         model => model.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                         StringComparer.Ordinal)
                     .ThenBy(model => model.PropertyName, StringComparer.Ordinal))
        {
            builder.Append("        ")
                .Append(model.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Append('.')
                .Append(model.PropertyName)
                .AppendLine("Key,");
        }

        builder.AppendLine("    };");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string TypeDeclaration(INamedTypeSymbol type)
    {
        var builder = new StringBuilder();
        builder.Append(AccessibilityText(type.DeclaredAccessibility));
        if (type.IsStatic)
        {
            builder.Append(" static");
        }
        else
        {
            if (type.TypeKind == TypeKind.Class && type.IsAbstract) builder.Append(" abstract");
            if (type.TypeKind == TypeKind.Class && type.IsSealed) builder.Append(" sealed");
            if (type.TypeKind == TypeKind.Struct && type.IsReadOnly) builder.Append(" readonly");
        }

        return builder
            .Append(" partial ")
            .Append(TypeKeyword(type))
            .Append(' ')
            .Append(type.Name)
            .ToString();
    }

    private static string TypeKeyword(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Struct)
            return type.IsRecord ? "record struct" : "struct";
        if (type.TypeKind == TypeKind.Class)
            return type.IsRecord ? "record" : "class";
        return "class";
    }

    private static string AccessibilityText(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Private => "private",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => "internal"
    };

    private static string RenderLayer(int layer) => layer switch
    {
        0 => "global::UML.Core.Configuration.ConfigLayer.Global",
        2 => "global::UML.Core.Configuration.ConfigLayer.Instance",
        _ => "global::UML.Core.Configuration.ConfigLayer.Local"
    };

    private static string RenderDefault(TypedConstant constant)
    {
        if (constant.IsNull)
            return "default!";

        return constant.Kind switch
        {
            TypedConstantKind.Type =>
                $"typeof({((ITypeSymbol)constant.Value!).ToDisplayString(TypeFormat)})",
            TypedConstantKind.Enum =>
                $"({constant.Type!.ToDisplayString(TypeFormat)}){constant.Value}",
            TypedConstantKind.Array => RenderArray(constant),
            _ => constant.ToCSharpString()
        };
    }

    private static string RenderArray(TypedConstant constant)
    {
        var arrayType = (IArrayTypeSymbol)constant.Type!;
        var elementType = arrayType.ElementType.ToDisplayString(TypeFormat);
        var values = string.Join(", ", constant.Values.Select(RenderDefault));
        return $"new {elementType}[] {{ {values} }}";
    }

    private static bool IsValidKey(string key)
    {
        foreach (var character in key)
            if (!char.IsLetterOrDigit(character) && character is not ('.' or '_' or '-'))
                return false;
        return true;
    }

    private static bool IsPartialChain(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
            if (!current.DeclaringSyntaxReferences.Any(reference =>
                    reference.GetSyntax() is TypeDeclarationSyntax syntax &&
                    syntax.Modifiers.Any(SyntaxKind.PartialKeyword)))
                return false;
        return true;
    }

    private static bool IsAccessible(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
                return false;
        return true;
    }

    private static string GetNamespace(INamedTypeSymbol type) =>
        type.ContainingNamespace?.IsGlobalNamespace == true
            ? string.Empty
            : type.ContainingNamespace!.ToDisplayString();

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Replace("global::", string.Empty))
            builder.Append(char.IsLetterOrDigit(character) || character is '.' or '_' or '-'
                ? character
                : '_');
        return builder.ToString();
    }

    private static void AppendIndent(StringBuilder builder, int indent) =>
        builder.Append(' ', indent * 4);

    private sealed record ConfigModel(
        string PropertyName,
        string Key,
        string DefaultExpression,
        string LayerExpression,
        Accessibility Accessibility,
        bool HasSetter,
        bool IsInit,
        INamedTypeSymbol ContainingType,
        ITypeSymbol PropertyType,
        Location? Location);

    private sealed record Candidate(ConfigModel? Model, ImmutableArray<Diagnostic> Diagnostics)
    {
        public static readonly Candidate Empty = new(null, ImmutableArray<Diagnostic>.Empty);
    }
}
