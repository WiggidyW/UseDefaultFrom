using Microsoft.CodeAnalysis;

namespace UseDefaultFrom;

public static class SymbolExt
{
    public static string AccessibilityString<TThis>(this TThis symbol)
        where TThis : ISymbol
    {
        string accessibilityWithName = symbol.ToDisplayString(
            new SymbolDisplayFormat(
                memberOptions: SymbolDisplayMemberOptions.IncludeAccessibility
            )
        );
        int finalSpaceIndex = accessibilityWithName.LastIndexOf(' ');
        return finalSpaceIndex == -1
            ? string.Empty
            : accessibilityWithName.Substring(0, finalSpaceIndex);
    }
}

public static class PropertySymbolExt
{
    // Does not include 'get' or 'set' or any trailing ';'
    public static string PartialPropertyDeclaration<TThis>(this TThis symbol)
        where TThis : IPropertySymbol
    {
        string modifiersWithNameString = symbol.ToDisplayString(
            new SymbolDisplayFormat(
                memberOptions: SymbolDisplayMemberOptions.IncludeModifiers
                    | SymbolDisplayMemberOptions.IncludeAccessibility
                    | SymbolDisplayMemberOptions.IncludeExplicitInterface
                    | SymbolDisplayMemberOptions.IncludeParameters
                    | SymbolDisplayMemberOptions.IncludeRef,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
            )
        );
        int finalSpaceIndex = modifiersWithNameString.LastIndexOf(' ');
        string modifiersString =
            finalSpaceIndex == -1
                ? string.Empty
                : modifiersWithNameString.Substring(0, finalSpaceIndex);
        return $"{modifiersString} partial {symbol.Type} {symbol.Name}";
    }
}

public static class NamedTypeSymbolExt
{
    // Does not include accessibility or the opening '{' of the declaration
    public static string PartialDeclaration<TThis>(this TThis symbol)
        where TThis : INamedTypeSymbol
    {
        string declarationString = symbol.ToDisplayString(
            new SymbolDisplayFormat(
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters
                    | SymbolDisplayGenericsOptions.IncludeVariance,
                kindOptions: SymbolDisplayKindOptions.IncludeTypeKeyword
            )
        );
        string typeParameterConstraintsString = symbol.ToDisplayString(
            new SymbolDisplayFormat(
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters
                    | SymbolDisplayGenericsOptions.IncludeTypeConstraints,
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces
            )
        );
        int whereIndex = typeParameterConstraintsString.IndexOf("where");
        return whereIndex == -1
            ? $"partial {declarationString}"
            : $"partial {declarationString} {typeParameterConstraintsString.Substring(whereIndex)}";
    }
}
