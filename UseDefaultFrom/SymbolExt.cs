using Microsoft.CodeAnalysis;

namespace UseDefaultFrom;

internal static class PropertySymbolExt
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

internal static class NamedTypeSymbolExt
{
    // Does not include accessibility or the opening '{' of the declaration
    public static string PartialTypeDeclaration<TThis>(this TThis symbol)
        where TThis : INamedTypeSymbol
    {
        string partialDeclarationString =
            "partial "
            + symbol.ToDisplayString(
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
            ? partialDeclarationString
            : $"{partialDeclarationString} {typeParameterConstraintsString.Substring(whereIndex)}";
    }
}
