using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace UseDefaultFrom;

#pragma warning disable RS1035 // Do not use APIs banned for analyzers

[Generator]
// Target is the class that has the attributes, and is reusing defaults
// Source is the class that has the defaults
public class UseDefaultFromGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
        // Register a syntax receiver to collect candidate nodes
        context.RegisterForSyntaxNotifications(
            () => new UseDefaultFromSyntaxReceiver()
        );
    }

    public void Execute(GeneratorExecutionContext context)
    {
        // Retrieve the syntax receiver
        UseDefaultFromSyntaxReceiver receiver = (UseDefaultFromSyntaxReceiver)(
            context.SyntaxContextReceiver ?? throw new Exception("Unreachable")
        );

        // Group properties by their containing class
        IEnumerable<
            IGrouping<ISymbol?, (IPropertySymbol, AttributeData)>
        > targetPropertiesGrouped = receiver.CandidateProperties.GroupBy(
            p => p.Item1.ContainingType,
            SymbolEqualityComparer.Default
        );

        // Process each property with [UseDefaultFrom]
        foreach (
            IGrouping<
                ISymbol?,
                (IPropertySymbol, AttributeData)
            > targetProperties in targetPropertiesGrouped
        )
        {
            INamedTypeSymbol? targetType = (INamedTypeSymbol)(
                targetProperties.Key ?? throw new Exception("Unreachable")
            );

            (string fileName, SourceText fileContent) = GenerateForTarget(
                context,
                targetType,
                targetProperties
            );

            // Add the generated source code to the compilation
            context.AddSource(fileName, fileContent);
        }
    }

    // recursively search the inheritance hierarchy for the property
    private IPropertySymbol? GetProperty(
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
            return GetProperty(type.BaseType, propertyName);
        }
        else
        {
            return null;
        }
    }

    private (string fileName, SourceText fileContent) GenerateForTarget(
        GeneratorExecutionContext context,
        INamedTypeSymbol targetType,
        IEnumerable<(IPropertySymbol, AttributeData)> targetProperties
    )
    {
        StringBuilder sourceTextBuilder = new();
        sourceTextBuilder.AppendLine("#nullable enable");
        sourceTextBuilder.AppendLine(
            $"namespace {targetType.ContainingNamespace}"
        );
        sourceTextBuilder.AppendLine("{");
        sourceTextBuilder.AppendLine($"    {targetType.PartialDeclaration()}");
        sourceTextBuilder.AppendLine("    {");

        // Iterate through each target property that has the [UseDefaultFrom] attribute
        foreach (
            (
                IPropertySymbol targetProperty,
                AttributeData targetPropertyAttribute
            ) in targetProperties
        )
        {
            // Retrieve the type of the source property's owner
            INamedTypeSymbol sourceType = (INamedTypeSymbol)
                targetPropertyAttribute.AttributeClass!.TypeArguments[0];

            // Retrieve the source property
            string? sourcePropertyName =
                targetPropertyAttribute.ConstructorArguments[0].Value as string;
            IPropertySymbol? sourceProperty = GetProperty(
                sourceType,
                sourcePropertyName!
            );
            if (sourceProperty == null)
            {
                throw new ArgumentException(
                    $"The property '{sourcePropertyName}' does not exist on '{sourceType.Name}'."
                );
            }

            // Create a private field that we can set the default to
            string defaultValue = GetDefaultPropertyValue(
                context,
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

        // Remove final trailing newline
        sourceTextBuilder.Length -= 2;

        // Close brackets
        sourceTextBuilder.AppendLine("    }");
        sourceTextBuilder.AppendLine("}");

        // Write to a file for debugging
        File.WriteAllText(
            $"C:\\Users\\rigglesr\\Desktop\\Txt\\{targetType.Name}Defaults.g.cs",
            sourceTextBuilder.ToString()
        );

        // Add the generated source code to the compilation
        return (
            $"{targetType.Name}Defaults.g.cs",
            SourceText.From(sourceTextBuilder.ToString(), Encoding.UTF8)
        );
    }

    private string GetDefaultPropertyValue(
        GeneratorExecutionContext context,
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
            return ParseValueExpression(context, initializer.Value);
        }
    }

    private string ParseValueExpression(
        GeneratorExecutionContext context,
        ExpressionSyntax expression
    )
    {
        SemanticModel semanticModel = context.Compilation.GetSemanticModel(
            expression.SyntaxTree
        );
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
                ParseValueExpression(context, argument.Expression)
            );
            invokeBuilder.Append(", ");
        }

        // Remove trailing ', '
        invokeBuilder.Length -= 2;

        // Finish the invocation
        invokeBuilder.Append(")");
        return invokeBuilder.ToString();
    }

    // Syntax Receiver to collect properties with [UseDefaultFrom] attribute
    class UseDefaultFromSyntaxReceiver : ISyntaxContextReceiver
    {
        public List<(
            IPropertySymbol,
            AttributeData
        )> CandidateProperties { get; } = new();

        public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
        {
            // Look for properties with [UseDefaultFrom] attribute
            if (
                context.Node is PropertyDeclarationSyntax propertyDeclaration
                && context.SemanticModel.GetDeclaredSymbol(propertyDeclaration)
                    is IPropertySymbol propertySymbol
            )
            {
                AttributeData? useDefaultFromAttribute = propertySymbol
                    .GetAttributes()
                    .FirstOrDefault(attr =>
                        attr.AttributeClass?.IsGenericType == true
                        && attr.AttributeClass.Name.StartsWith(
                            "UseDefaultFromAttribute"
                        )
                    );
                if (useDefaultFromAttribute != null)
                {
                    CandidateProperties.Add(
                        (propertySymbol, useDefaultFromAttribute)
                    );
                }
            }
        }
    }
}

#pragma warning restore RS1035 // Do not use APIs banned for analyzers
