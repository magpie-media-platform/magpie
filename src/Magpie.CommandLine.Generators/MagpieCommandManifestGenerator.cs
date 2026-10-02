using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Magpie.CommandLine.Generators;

/// <summary>
/// Generates the command manifest for the Magpie command line API.
/// </summary>
[Generator]
public sealed class MagpieCommandManifestGenerator : IIncrementalGenerator
{
    private enum BodyKind
    {
        None,
        OptionalJson,
        RequiredJson,
        File
    }

    private sealed class CommandEntry(
        IMethodSymbol methodSymbol,
        string group,
        string command,
        string methodExpression,
        string routeTemplate,
        string? description,
        string groupDescription,
        IReadOnlyList<ParameterDescriptor> pathParameters,
        IReadOnlyList<ParameterDescriptor> queryParameters,
        BodyKind bodyKind,
        string? fileFieldName,
        bool blobResult)
    {
        public IMethodSymbol MethodSymbol { get; } = methodSymbol;

        public string Group { get; } = group;

        public string Command { get; } = command;

        public string MethodExpression { get; } = methodExpression;

        public string RouteTemplate { get; } = routeTemplate;

        public string? Description { get; } = description;

        public string GroupDescription { get; } = groupDescription;

        public IReadOnlyList<ParameterDescriptor> PathParameters { get; } = pathParameters;

        public IReadOnlyList<ParameterDescriptor> QueryParameters { get; } = queryParameters;

        public BodyKind BodyKind { get; } = bodyKind;

        public string? FileFieldName { get; } = fileFieldName;

        public bool BlobResult { get; } = blobResult;
    }

    private sealed class ParameterDescriptor(string name, string cliName, bool isCollection)
    {
        public string Name { get; } = name;

        public string CliName { get; } = cliName;

        public bool IsCollection { get; } = isCollection;
    }

    [SuppressMessage("Ordering", "SA1201", Justification = "Nested types come first in this codebase.")]
    private static readonly DiagnosticDescriptor MissingRouteParameter = new(
        "MCLI001",
        "CLI route parameter is missing",
        "CLI command '{0} {1}' route parameter '{2}' has no matching handler parameter",
        "Magpie.CommandLine",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MultipleBodyParameters = new(
        "MCLI002",
        "CLI command has multiple body parameters",
        "CLI command '{0} {1}' has more than one body parameter",
        "Magpie.CommandLine",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor AmbiguousParameter = new(
        "MCLI003",
        "CLI parameter binding is ambiguous",
        "CLI command '{0} {1}' parameter '{2}' needs an explicit binding attribute",
        "Magpie.CommandLine",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateCommand = new(
        "MCLI004",
        "CLI command is duplicated",
        "CLI command '{0} {1}' is declared more than once",
        "Magpie.CommandLine",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedMethod = new(
        "MCLI005",
        "CLI command uses an unsupported HTTP method",
        "CLI command '{0} {1}' uses unsupported HTTP method '{2}'",
        "Magpie.CommandLine",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ConflictingOption = new(
        "MCLI006",
        "CLI option conflicts with a reserved option",
        "CLI command '{0} {1}' option '{2}' conflicts with a reserved option",
        "Magpie.CommandLine",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor RouteMismatch = new(
        "MCLI007",
        "CLI route does not match an endpoint",
        "CLI handler '{0}' declares a method and route the group does not map",
        "Magpie.CommandLine",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnreadableRouteShape = new(
        "MCLI008",
        "CLI route validation is off for a group",
        "CLI group '{0}' declares a name or a route the generator cannot read, so route checks are off",
        "Magpie.CommandLine",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private const string ApiAssemblyName = "Magpie.Api";
    private const string AuthenticatedUserName = "Magpie.Sdk.Api.AuthenticatedUser";
    private const string CommandAttributeName = "Magpie.Api.MagpieCommandAttribute";
    private const string CommandGroupAttributeName = "Magpie.Api.MagpieCommandGroupAttribute";
    private const string BlobResultAttributeName = "Magpie.Api.MagpieBlobCommandResultAttribute";
    private const string FromBodyAttributeName = "Microsoft.AspNetCore.Mvc.FromBodyAttribute";
    private const string FromFormAttributeName = "Microsoft.AspNetCore.Mvc.FromFormAttribute";
    private const string FromQueryAttributeName = "Microsoft.AspNetCore.Mvc.FromQueryAttribute";
    private const string FromServicesAttributeName = "Microsoft.AspNetCore.Mvc.FromServicesAttribute";
    private const string FormFileName = "Microsoft.AspNetCore.Http.IFormFile";
    private const string HttpContextName = "Microsoft.AspNetCore.Http.HttpContext";
    private const string HttpRequestName = "Microsoft.AspNetCore.Http.HttpRequest";
    private const string HttpResponseName = "Microsoft.AspNetCore.Http.HttpResponse";

    private static void Execute(SourceProductionContext context, IAssemblySymbol? apiAssembly)
    {
        if (apiAssembly is null)
        {
            return;
        }

        var entries = new List<CommandEntry>();
        foreach (var groupSymbol in GetAllTypes(apiAssembly.GlobalNamespace)
            .Where(type => HasAttribute(type, CommandGroupAttributeName))
            .OrderBy(symbol => symbol.ToDisplayString(), StringComparer.Ordinal))
        {
            if (!TryGetCommandGroup(groupSymbol, out var group, out var routePrefix, out var groupDescription))
            {
                continue;
            }

            foreach (var method in groupSymbol.GetMembers().OfType<IMethodSymbol>()
                .OrderBy(symbol => symbol.Name, StringComparer.Ordinal))
            {
                if (TryCreateEntry(
                    context,
                    method,
                    group,
                    routePrefix,
                    groupDescription,
                    out var entry) && entry is not null)
                {
                    entries.Add(entry);
                }
            }
        }

        ReportDuplicateCommands(context, entries);
        context.AddSource("MagpieGeneratedCommandManifest.g.cs", SourceText.From(BuildSource(entries), Encoding.UTF8));
    }

    private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol namespaceSymbol)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            if (member is INamespaceSymbol nestedNamespace)
            {
                foreach (var type in GetAllTypes(nestedNamespace))
                {
                    yield return type;
                }
            }
            else if (member is INamedTypeSymbol type)
            {
                yield return type;
            }
        }
    }

    private static bool TryGetCommandGroup(
        INamedTypeSymbol groupSymbol,
        out string group,
        out string routePrefix,
        out string groupDescription)
    {
        group = string.Empty;
        routePrefix = string.Empty;
        groupDescription = string.Empty;

        var attribute = FindAttribute(groupSymbol, CommandGroupAttributeName);
        if (attribute is null ||
            !TryGetStringConstructorArgument(attribute, 0, out group) ||
            !TryGetStringConstructorArgument(attribute, 1, out routePrefix))
        {
            return false;
        }

        groupDescription = TryGetStringConstructorArgument(attribute, 2, out var description)
            ? description
            : string.Empty;
        return true;
    }

    private static bool TryCreateEntry(
        SourceProductionContext context,
        IMethodSymbol method,
        string group,
        string routePrefix,
        string groupDescription,
        out CommandEntry? entry)
    {
        entry = null;
        var attribute = FindAttribute(method, CommandAttributeName);
        if (attribute is null || !TryGetStringConstructorArgument(attribute, 0, out var command))
        {
            return false;
        }

        var verb = TryGetStringConstructorArgument(attribute, 1, out var declaredVerb) ? declaredVerb : "GET";
        if (!TryGetMethodExpression(verb, out var methodExpression))
        {
            context.ReportDiagnostic(Diagnostic.Create(UnsupportedMethod, GetLocation(method), group, command, verb));
            return false;
        }

        var routeTemplate = TryGetStringConstructorArgument(attribute, 2, out var template)
            ? template
            : string.Empty;
        var description = TryGetNullableStringConstructorArgument(attribute, 3);
        var route = CombineRoute("api/" + routePrefix.TrimStart('/'), routeTemplate);
        var routeParameterNames = ParseRouteParameterNames(route);
        var pathParameters = BuildPathParameters(context, method, group, command, routeParameterNames);
        if (pathParameters is null)
        {
            return false;
        }

        var queryParameters = new List<ParameterDescriptor>();
        var bodyKind = BodyKind.None;
        string? fileFieldName = null;
        var valid = true;
        foreach (var parameter in method.Parameters)
        {
            if (routeParameterNames.Contains(parameter.Name, StringComparer.OrdinalIgnoreCase) ||
                ShouldSkipParameter(parameter))
            {
                continue;
            }

            if (HasAttribute(parameter, FromBodyAttributeName))
            {
                if (bodyKind != BodyKind.None)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        MultipleBodyParameters,
                        GetLocation(method),
                        group,
                        command));
                    valid = false;
                    continue;
                }

                bodyKind = IsNullable(parameter) ? BodyKind.OptionalJson : BodyKind.RequiredJson;
                continue;
            }

            var fromForm = FindAttribute(parameter, FromFormAttributeName);
            if (fromForm is not null && IsType(parameter.Type, FormFileName))
            {
                if (bodyKind != BodyKind.None)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        MultipleBodyParameters,
                        GetLocation(method),
                        group,
                        command));
                    valid = false;
                    continue;
                }

                bodyKind = BodyKind.File;
                fileFieldName = TryGetNamedStringArgument(fromForm, "Name", out var formName)
                    ? formName
                    : parameter.Name;
                continue;
            }

            if (TryCreateQueryParameter(parameter, out var queryParameter) && queryParameter is not null)
            {
                if (IsReservedOption(queryParameter.CliName))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        ConflictingOption,
                        GetLocation(method),
                        group,
                        command,
                        queryParameter.CliName));
                    valid = false;
                }

                queryParameters.Add(queryParameter);
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                AmbiguousParameter,
                GetLocation(method),
                group,
                command,
                parameter.Name));
            valid = false;
        }

        if (!valid)
        {
            return false;
        }

        entry = new(
            method,
            group,
            command,
            methodExpression,
            route,
            description,
            groupDescription,
            pathParameters,
            queryParameters,
            bodyKind,
            fileFieldName,
            HasAttribute(method, BlobResultAttributeName));
        return true;
    }

    private static IReadOnlyList<ParameterDescriptor>? BuildPathParameters(
        SourceProductionContext context,
        IMethodSymbol method,
        string group,
        string command,
        IReadOnlyList<string> routeParameterNames)
    {
        var parameters = new List<ParameterDescriptor>(routeParameterNames.Count);
        var valid = true;
        foreach (var routeParameterName in routeParameterNames)
        {
            if (!method.Parameters.Any(parameter =>
                string.Equals(parameter.Name, routeParameterName, StringComparison.OrdinalIgnoreCase)))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MissingRouteParameter,
                    GetLocation(method),
                    group,
                    command,
                    routeParameterName));
                valid = false;
                continue;
            }

            parameters.Add(new ParameterDescriptor(
                routeParameterName,
                ToKebabCase(routeParameterName),
                isCollection: false));
        }

        return valid ? parameters : null;
    }

    private static bool TryCreateQueryParameter(
        IParameterSymbol parameter,
        out ParameterDescriptor? descriptor)
    {
        descriptor = null;
        var fromQuery = FindAttribute(parameter, FromQueryAttributeName);
        var isCollection = IsScalarCollection(parameter.Type);
        if (!IsScalar(parameter.Type) && !isCollection)
        {
            return false;
        }

        var wireName = fromQuery is not null && TryGetNamedStringArgument(fromQuery, "Name", out var queryName)
            ? queryName
            : parameter.Name;
        descriptor = new(wireName, ToOptionName(parameter.Name), isCollection);
        return true;
    }

    private static bool ShouldSkipParameter(IParameterSymbol parameter)
    {
        return HasAttribute(parameter, FromServicesAttributeName) ||
            IsType(parameter.Type, "System.Threading.CancellationToken") ||
            IsType(parameter.Type, AuthenticatedUserName) ||
            IsType(parameter.Type, HttpContextName) ||
            IsType(parameter.Type, HttpRequestName) ||
            IsType(parameter.Type, HttpResponseName);
    }

    private static bool IsNullable(IParameterSymbol parameter)
    {
        if (parameter.Type is INamedTypeSymbol namedType &&
            GetFullMetadataName(namedType.OriginalDefinition) == "System.Nullable`1")
        {
            return true;
        }

        return !parameter.Type.IsValueType && parameter.NullableAnnotation == NullableAnnotation.Annotated;
    }

    private static bool IsScalarCollection(ITypeSymbol type)
    {
        if (IsType(type, "System.String"))
        {
            return false;
        }

        if (type is IArrayTypeSymbol arrayType)
        {
            return IsScalar(arrayType.ElementType);
        }

        if (type is INamedTypeSymbol namedType &&
            GetFullMetadataName(namedType.OriginalDefinition) == "System.Collections.Generic.IEnumerable`1")
        {
            return IsScalar(namedType.TypeArguments[0]);
        }

        foreach (var interfaceType in type.AllInterfaces)
        {
            if (GetFullMetadataName(interfaceType.OriginalDefinition) == "System.Collections.Generic.IEnumerable`1")
            {
                return IsScalar(interfaceType.TypeArguments[0]);
            }
        }

        return false;
    }

    private static bool IsScalar(ITypeSymbol type)
    {
        type = UnwrapNullable(type);
        return type.SpecialType is SpecialType.System_String
                or SpecialType.System_Boolean
                or SpecialType.System_Byte
                or SpecialType.System_SByte
                or SpecialType.System_Int16
                or SpecialType.System_UInt16
                or SpecialType.System_Int32
                or SpecialType.System_UInt32
                or SpecialType.System_Int64
                or SpecialType.System_UInt64
                or SpecialType.System_Single
                or SpecialType.System_Double
                or SpecialType.System_Decimal ||
            type.TypeKind == TypeKind.Enum ||
            IsType(type, "System.Guid") ||
            IsType(type, "System.DateTime") ||
            IsType(type, "System.DateTimeOffset") ||
            IsType(type, "System.DateOnly") ||
            IsType(type, "System.TimeOnly") ||
            IsType(type, "System.TimeSpan") ||
            IsType(type, "System.Uri");
    }

    private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType &&
            GetFullMetadataName(namedType.OriginalDefinition) == "System.Nullable`1"
                ? namedType.TypeArguments[0]
                : type;
    }

    private static bool IsType(ITypeSymbol type, string metadataName)
    {
        return GetFullMetadataName(type) == metadataName;
    }

    private static AttributeData? FindAttribute(ISymbol symbol, string attributeName)
    {
        return symbol.GetAttributes().FirstOrDefault(attribute =>
            attribute.AttributeClass is not null &&
            GetFullMetadataName(attribute.AttributeClass) == attributeName);
    }

    private static bool HasAttribute(ISymbol symbol, string attributeName)
    {
        return FindAttribute(symbol, attributeName) is not null;
    }

    private static bool TryGetStringConstructorArgument(AttributeData attribute, int index, out string value)
    {
        value = string.Empty;
        if (attribute.ConstructorArguments.Length <= index ||
            attribute.ConstructorArguments[index].Value is not string text)
        {
            return false;
        }

        value = text;
        return true;
    }

    private static string? TryGetNullableStringConstructorArgument(AttributeData attribute, int index)
    {
        return attribute.ConstructorArguments.Length > index
            ? attribute.ConstructorArguments[index].Value as string
            : null;
    }

    private static bool TryGetNamedStringArgument(AttributeData attribute, string name, out string value)
    {
        value = string.Empty;
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is string text && !string.IsNullOrWhiteSpace(text))
            {
                value = text;
                return true;
            }
        }

        return false;
    }

    private static string CombineRoute(string routePrefix, string routeTemplate)
    {
        return string.IsNullOrWhiteSpace(routeTemplate)
            ? routePrefix.Trim('/')
            : routePrefix.TrimEnd('/') + "/" + routeTemplate.TrimStart('/');
    }

    private static IReadOnlyList<string> ParseRouteParameterNames(string route)
    {
        var parameters = new List<string>();
        for (var index = 0; index < route.Length; index++)
        {
            if (route[index] != '{')
            {
                continue;
            }

            var end = route.IndexOf('}', index + 1);
            if (end < 0)
            {
                break;
            }

            var parameter = route.Substring(index + 1, end - index - 1).TrimStart('*');
            var constraint = parameter.IndexOf(':');
            if (constraint >= 0)
            {
                parameter = parameter.Substring(0, constraint);
            }

            if (parameter.Length > 0)
            {
                parameters.Add(parameter);
            }

            index = end;
        }

        return parameters;
    }

    private static bool TryGetMethodExpression(string method, out string expression)
    {
        expression = method.ToUpperInvariant() switch
        {
            "DELETE" => "HttpMethod.Delete",
            "GET" => "HttpMethod.Get",
            "PATCH" => "HttpMethod.Patch",
            "POST" => "HttpMethod.Post",
            "PUT" => "HttpMethod.Put",
            _ => string.Empty
        };
        return expression.Length > 0;
    }

    private static void ReportDuplicateCommands(SourceProductionContext context, IReadOnlyList<CommandEntry> entries)
    {
        foreach (var duplicate in entries
            .GroupBy(entry => entry.Group + "\0" + entry.Command, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            foreach (var entry in duplicate)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateCommand,
                    GetLocation(entry.MethodSymbol),
                    entry.Group,
                    entry.Command));
            }
        }
    }

    private static Location GetLocation(ISymbol symbol)
    {
        return symbol.Locations.FirstOrDefault() ?? Location.None;
    }

    private static string GetFullMetadataName(ISymbol? symbol)
    {
        if (symbol is null)
        {
            return string.Empty;
        }

        var parts = new Stack<string>();
        for (var current = symbol; current is not null; current = current.ContainingSymbol)
        {
            if (current is INamespaceSymbol { IsGlobalNamespace: true })
            {
                break;
            }

            parts.Push(current.MetadataName);
        }

        return string.Join(".", parts);
    }

    private static bool IsReservedOption(string optionName)
    {
        return optionName is "--api-key" or "--data" or "--file" or "--json" or "--output" or "--url";
    }

    private static string ToOptionName(string name)
    {
        return "--" + ToKebabCase(name);
    }

    private static string ToKebabCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var current = name[index];
            if (current is '_' or '-' or ' ' or '/')
            {
                if (builder.Length > 0 && builder[builder.Length - 1] != '-')
                {
                    builder.Append('-');
                }

                continue;
            }

            if (char.IsUpper(current) &&
                builder.Length > 0 &&
                builder[builder.Length - 1] != '-' &&
                (index + 1 == name.Length || !char.IsUpper(name[index + 1])))
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }

    private static string BuildSource(IReadOnlyList<CommandEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using System.Net.Http;");
        builder.AppendLine();
        builder.AppendLine("using Magpie.CommandLine.Descriptors;");
        builder.AppendLine();
        builder.AppendLine("using Microsoft.AspNetCore.Routing.Patterns;");
        builder.AppendLine();
        builder.AppendLine("namespace Magpie.CommandLine;");
        builder.AppendLine();
        builder.AppendLine("internal static class MagpieGeneratedCommandManifest");
        builder.AppendLine("{");
        builder.AppendLine("    internal static IReadOnlyList<MagpieCommandManifestEntry> Entries { get; } =");
        builder.AppendLine("    [");
        for (var index = 0; index < entries.Count; index++)
        {
            AppendEntry(builder, entries[index], index == entries.Count - 1);
        }

        builder.AppendLine("    ];");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendEntry(StringBuilder builder, CommandEntry entry, bool isLast)
    {
        builder.AppendLine("        new MagpieCommandManifestEntry(");
        builder.AppendLine("            " + ToLiteral(entry.Group) + ",");
        builder.AppendLine("            " + ToLiteral(entry.Command) + ",");
        builder.AppendLine("            " + entry.MethodExpression + ",");
        builder.AppendLine("            RoutePatternFactory.Parse(" + ToLiteral(entry.RouteTemplate) + "),");
        builder.AppendLine("            " + ToNullableLiteral(entry.Description) + ",");
        builder.AppendLine("            " + ToLiteral(entry.GroupDescription) + ",");
        AppendParameters(builder, entry.PathParameters, "PathParameters");
        builder.AppendLine(",");
        AppendParameters(builder, entry.QueryParameters, "QueryParameters");
        builder.AppendLine(",");
        builder.AppendLine("            MagpieCommandBodyKind." + entry.BodyKind + ",");
        builder.AppendLine("            " + ToNullableLiteral(entry.FileFieldName) + ",");
        builder.AppendLine("            BlobResult: " + ToBoolean(entry.BlobResult));
        builder.Append("        )");
        builder.AppendLine(isLast ? string.Empty : ",");
    }

    private static void AppendParameters(
        StringBuilder builder,
        IReadOnlyList<ParameterDescriptor> parameters,
        string label)
    {
        builder.AppendLine("            " + label + ":");
        builder.AppendLine("            [");
        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];
            builder.Append("                new MagpieCommandParameterDescriptor(");
            builder.Append(ToLiteral(parameter.Name));
            builder.Append(", ");
            builder.Append(ToLiteral(parameter.CliName));
            builder.Append(", ");
            builder.Append(ToBoolean(parameter.IsCollection));
            builder.AppendLine(index == parameters.Count - 1 ? ")" : "),");
        }

        builder.Append("            ]");
    }

    private static string ToLiteral(string value)
    {
        return SymbolDisplay.FormatLiteral(value, quote: true);
    }

    private static string ToNullableLiteral(string? value)
    {
        return value is null ? "null" : ToLiteral(value);
    }

    private static string ToBoolean(bool value)
    {
        return value ? "true" : "false";
    }

    /// <summary>
    /// Initializes command manifest source generation.
    /// </summary>
    /// <param name="context">Generator initialization context</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var apiAssembly = context.CompilationProvider
            .Select(static (compilation, _) => compilation.SourceModule.ReferencedAssemblySymbols
                .FirstOrDefault(assembly => assembly.Name == ApiAssemblyName))
            .WithComparer(SymbolEqualityComparer.Default)
            .WithTrackingName("ApiAssembly");
        context.RegisterSourceOutput(apiAssembly, Execute);

        var routeDiagnostics = context.SyntaxProvider.ForAttributeWithMetadataName(
            CommandGroupAttributeName,
            static (node, _) => node is TypeDeclarationSyntax,
            static (syntax, _) => ValidateMappedRoutes(syntax));
        context.RegisterSourceOutput(
            routeDiagnostics,
            static (output, diagnostics) =>
            {
                foreach (var diagnostic in diagnostics)
                {
                    output.ReportDiagnostic(diagnostic);
                }
            });
    }

    private static IReadOnlyList<Diagnostic> ValidateMappedRoutes(GeneratorAttributeSyntaxContext syntax)
    {
        var group = (INamedTypeSymbol)syntax.TargetSymbol;
        var declaration = (TypeDeclarationSyntax)syntax.TargetNode;
        if (!TryGetCommandGroup(group, out _, out var prefix, out _))
        {
            return [];
        }

        var name = declaration.Members.OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault(property => property.Identifier.ValueText == "Name")?.ExpressionBody?.Expression;
        if (name is null || syntax.SemanticModel.GetConstantValue(name).Value is not string mappedPrefix)
        {
            return [Diagnostic.Create(UnreadableRouteShape, declaration.Identifier.GetLocation(), group.Name)];
        }

        if (prefix != mappedPrefix)
        {
            return [];
        }

        var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax member ||
                !member.Name.Identifier.ValueText.StartsWith("Map", StringComparison.Ordinal))
            {
                continue;
            }

            if (member.Name.Identifier.ValueText is not
                    ("MapGet" or "MapPost" or "MapPut" or "MapPatch" or "MapDelete") ||
                invocation.ArgumentList.Arguments.Count == 0 ||
                syntax.SemanticModel.GetConstantValue(invocation.ArgumentList.Arguments[0].Expression).Value
                    is not string route)
            {
                return [Diagnostic.Create(UnreadableRouteShape, invocation.GetLocation(), group.Name)];
            }

            mapped.Add(member.Name.Identifier.ValueText + " " + route);
        }

        if (mapped.Count == 0)
        {
            return [];
        }

        var diagnostics = new List<Diagnostic>();
        foreach (var method in group.GetMembers().OfType<IMethodSymbol>())
        {
            var attribute = FindAttribute(method, CommandAttributeName);
            if (attribute is null)
            {
                continue;
            }

            var verb = TryGetStringConstructorArgument(attribute, 1, out var declaredVerb) ? declaredVerb : "GET";
            var route = TryGetStringConstructorArgument(attribute, 2, out var declaredRoute) ? declaredRoute : "";
            if (!mapped.Contains("Map" + verb + " " + route))
            {
                diagnostics.Add(Diagnostic.Create(RouteMismatch, GetLocation(method), method.Name));
            }
        }

        return diagnostics;
    }
}
