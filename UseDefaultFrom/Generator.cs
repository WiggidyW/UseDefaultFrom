using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace UseDefaultFrom;

internal class TargetTypeWithUDFProperties
{
    public readonly INamedTypeSymbol TargetType;

    // [UseDefaultFrom] properties on the target type
    public readonly IEnumerable<UDFProperty> UDFProperties;

    // SemanticModel for the target type
    public readonly SemanticModel SemanticModel;

    public TargetTypeWithUDFProperties(
        INamedTypeSymbol targetType,
        IEnumerable<UDFProperty> udfProperties,
        SemanticModel semanticModel
    )
    {
        TargetType = targetType;
        UDFProperties = udfProperties;
        SemanticModel = semanticModel;
    }
}

internal class UDFProperty
{
    public readonly IPropertySymbol TargetProperty;
    public readonly INamedTypeSymbol SourceType;
    public readonly IPropertySymbol SourceProperty;

    public UDFProperty(
        IPropertySymbol targetProperty,
        INamedTypeSymbol sourceType,
        IPropertySymbol sourceProperty
    )
    {
        TargetProperty = targetProperty;
        SourceType = sourceType;
        SourceProperty = sourceProperty;
    }
}

[Generator]
// Target is the class that has the attributes, and is reusing defaults
// Source is the class that has the defaults
public class UseDefaultFromGenerator : IIncrementalGenerator
{
    void IIncrementalGenerator.Initialize(
        IncrementalGeneratorInitializationContext context
    )
    {
        // For each TypeDeclaration Node,
        // Select properties with [UseDefaultFrom] attribute
        IncrementalValuesProvider<TargetTypeWithUDFProperties> propertiesWithAttribute =
            context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (context, _) =>
                {
                    INamedTypeSymbol targetType =
                        context.SemanticModel.GetDeclaredSymbol(
                            (TypeDeclarationSyntax)context.Node
                        )!;
                    IEnumerable<UDFProperty> udfProperties = GetUDFProperties(
                        targetType
                    );
                    return new TargetTypeWithUDFProperties(
                        targetType,
                        udfProperties,
                        context.SemanticModel
                    );
                }
            );

        // Create a source output for each target type with [UseDefaultFrom] properties
        // Generate a partial class with default values for each property
        context.RegisterSourceOutput(
            propertiesWithAttribute,
            static (context, property) =>
            {
                (string FileName, SourceText FileContent)? generatedFile =
                    GenerateForTarget(
                        property.TargetType,
                        property.UDFProperties,
                        property.SemanticModel
                    );
                if (generatedFile != null)
                // TargetType has properties with [UseDefaultFrom]
                {
                    context.AddSource(
                        generatedFile.Value.FileName,
                        generatedFile.Value.FileContent
                    );
                }
            }
        );
    }

    // Returns all properties marked with [UseDefaultFrom] on the target type
    private static IEnumerable<UDFProperty> GetUDFProperties(
        INamedTypeSymbol targetType
    )
    {
        foreach (
            IPropertySymbol targetProperty in targetType
                .GetMembers()
                .OfType<IPropertySymbol>()
        )
        {
            AttributeData? attributeData = targetProperty
                .GetAttributes()
                .FirstOrDefault(
                    (AttributeData attribute) =>
                        attribute.AttributeClass?.Name
                        == "UseDefaultFromAttribute"
                );
            if (attributeData == null)
            // It's not a property with [UseDefaultFrom]
            {
                continue;
            }

            // The source type is the single type parameter of the attribute
            INamedTypeSymbol sourceType = (INamedTypeSymbol)
                attributeData.AttributeClass!.TypeArguments[0];

            // The source property is the single constructor argument of the attribute
            string sourcePropertyName = (string)
                attributeData.ConstructorArguments[0].Value!;
            IPropertySymbol sourceProperty =
                GetPropertyByName(sourceType, sourcePropertyName)
                ?? throw new ArgumentException(
                    $"The property '{sourcePropertyName}' does not exist on '{sourceType.Name}'."
                );

            yield return new UDFProperty(
                targetProperty,
                sourceType,
                sourceProperty
            );
        }
    }

    // recursively search the inheritance hierarchy for the property with the specified name
    private static IPropertySymbol? GetPropertyByName(
        INamedTypeSymbol type,
        string? propertyName
    )
    {
        IPropertySymbol? property = type.GetMembers()
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p => p.Name == propertyName);
        if (property != null)
        {
            return property;
        }
        else if (type.BaseType != null)
        {
            return GetPropertyByName(type.BaseType, propertyName);
        }
        else
        {
            return null;
        }
    }

    // Generate a partial class with default values for each property
    // If there are no properties with [UseDefaultFrom], return null
    private static (string FileName, SourceText FileContent)? GenerateForTarget(
        INamedTypeSymbol targetType,
        IEnumerable<UDFProperty> udfProperties,
        SemanticModel semanticModel
    )
    {
        StringBuilder sourceTextBuilder = new();
        sourceTextBuilder.AppendLine("#nullable enable");
        sourceTextBuilder.AppendLine(
            $"namespace {targetType.ContainingNamespace}"
        );
        sourceTextBuilder.AppendLine("{");
        sourceTextBuilder.AppendLine(
            $"    {targetType.PartialTypeDeclaration()}"
        );
        sourceTextBuilder.AppendLine("    {");

        // Iterate through each target property that has the [UseDefaultFrom] attribute
        bool hasAnyProperties = false;
        foreach (UDFProperty udfProperty in udfProperties)
        {
            IPropertySymbol sourceProperty = udfProperty.SourceProperty;
            IPropertySymbol targetProperty = udfProperty.TargetProperty;
            hasAnyProperties = true;

            // Create a private field that we can set the default to
            string defaultValue = GetDefaultPropertyValue(
                semanticModel,
                sourceProperty
            );
            sourceTextBuilder.AppendLine(
                $"        private {targetProperty.Type} __{targetProperty.Name} = {defaultValue};"
            );

            // Create the implementation of the partial property, which wraps the private field
            sourceTextBuilder.AppendLine(
                $"        {targetProperty.PartialPropertyDeclaration()}"
            );
            sourceTextBuilder.AppendLine("        {");
            sourceTextBuilder.AppendLine(
                $"            get => __{targetProperty.Name};"
            );
            sourceTextBuilder.AppendLine(
                $"            set => __{targetProperty.Name} = value;"
            );
            sourceTextBuilder.AppendLine("        }");

            // Add a trailing newline to separate properties
            sourceTextBuilder.AppendLine();
        }

        if (!hasAnyProperties)
        // No point in generating a partial class with nothing in it
        {
            return null;
        }

        // Remove final trailing newline
        sourceTextBuilder.Length -= 2;

        // Close brackets
        sourceTextBuilder.AppendLine("    }");
        sourceTextBuilder.AppendLine("}");

        // Add the generated source code to the compilation
        return (
            $"{targetType.Name}Defaults.g.cs",
            SourceText.From(sourceTextBuilder.ToString(), Encoding.UTF8)
        );
    }

    private static string GetDefaultPropertyValue(
        SemanticModel semanticModel,
        IPropertySymbol property
    )
    {
        // Attempt to retrieve the initializer expression from the syntax tree
        SyntaxReference? syntaxReference =
            property.DeclaringSyntaxReferences.FirstOrDefault();
        PropertyDeclarationSyntax? propertySyntax = (PropertyDeclarationSyntax?)
            syntaxReference?.GetSyntax();
        EqualsValueClauseSyntax? initializer = propertySyntax?.Initializer;
        if (initializer == null)
        // Default value is implicitly set, meaning it's the default value for the type
        {
            return "default";
        }
        else
        // Default value is an expression, parse it
        {
            return ParseValueExpression(semanticModel, initializer.Value);
        }
    }

    private static string ParseValueExpression(
        SemanticModel semanticModel,
        ExpressionSyntax expression
    )
    {
        SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(expression);
        ISymbol? symbol = symbolInfo.Symbol;
        if (symbol == null)
        // Expression is a raw literal like a string or an integer
        {
            return expression.ToString();
        }

        StringBuilder invokeBuilder = new();
        ArgumentListSyntax? argumentList;
        if (expression is ObjectCreationExpressionSyntax constructorExpression)
        // Expression is a constructor invocation
        {
            TypeSyntax typeSyntax = constructorExpression.Type;
            ISymbol typeSymbol = semanticModel
                .GetSymbolInfo(typeSyntax)
                .Symbol!;
            invokeBuilder.Append($"new {typeSymbol.ToDisplayString()}(");
            argumentList = constructorExpression.ArgumentList;
        }
        else if (expression is InvocationExpressionSyntax invocationExpression)
        // Expression is a method invocation
        {
            ExpressionSyntax invocationExpressionExpression =
                invocationExpression.Expression;
            ISymbol invocationExpressionSymbol = semanticModel
                .GetSymbolInfo(invocationExpressionExpression)
                .Symbol!;
            invokeBuilder.Append(
                $"{invocationExpressionSymbol.ToDisplayString()}("
            );
            argumentList = invocationExpression.ArgumentList;
        }
        else
        // Expression is a reference to some field or property
        {
            return symbol.ToDisplayString();
        }

        // Append arguments to the invocation
        foreach (ArgumentSyntax argument in argumentList?.Arguments ?? [])
        {
            if (argument.NameColon != null)
            {
                invokeBuilder.Append($"{argument.NameColon.Name}: ");
            }
            invokeBuilder.Append(
                ParseValueExpression(semanticModel, argument.Expression)
            );
            invokeBuilder.Append(", ");
        }

        // Remove trailing ', '
        invokeBuilder.Length -= 2;

        // Finish the invocation
        invokeBuilder.Append(")");
        return invokeBuilder.ToString();
    }
}
