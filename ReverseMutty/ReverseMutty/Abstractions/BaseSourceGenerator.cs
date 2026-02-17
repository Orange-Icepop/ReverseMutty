// Copyright (c) 2020-2024 Atypical Consulting SRL. All rights reserved.
// Atypical Consulting SRL licenses this file to you under the Apache 2.0 license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ReverseMutty.Abstractions;

/// <summary>
/// The base source generator for incremental generation of records with the MutableGenerationAttribute.
/// </summary>
public abstract class BaseSourceGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Create a provider that finds all classes with the ImmutableGenerationAttribute
        IncrementalValueProvider<ImmutableArray<INamedTypeSymbol>> recordTypesWithAttribute = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (syntaxNode, _) => CouldBeImmutableGenerationAttribute(syntaxNode),
                transform: static (ctx, _) => GetClassTypeWithAttribute(ctx)!)
            .Where(static type => type is not null) // Filter out nulls
            .Collect(); // Collect all relevant types

        // Register the generation action
        context.RegisterSourceOutput(recordTypesWithAttribute, GenerateCode);
    }

    /// <summary>
    /// Generates the source code for the given record types.
    /// </summary>
    /// <param name="context">The source production context.</param>
    /// <param name="classTypes">The record types to generate code for.</param>
    public abstract void GenerateCode(SourceProductionContext context, ImmutableArray<INamedTypeSymbol> classTypes);

    /// <summary>
    /// Adds the source code to the context.
    /// </summary>
    /// <param name="context">The source production context.</param>
    /// <param name="name">The name of the source file.</param>
    /// <param name="source">The source code.</param>
    protected static void AddSource(in SourceProductionContext context, string name, string source)
    {
        context.AddSource(name, SourceText.From(source, Encoding.UTF8));
    }

    private static bool CouldBeImmutableGenerationAttribute(SyntaxNode syntaxNode)
    {
        if (syntaxNode is not AttributeSyntax attribute)
        {
            return false;
        }

        string? name = ExtractName(attribute.Name);
        return name is "ImmutableGeneration" or "ImmutableGenerationAttribute";
    }

    private static INamedTypeSymbol? GetClassTypeWithAttribute(in GeneratorSyntaxContext context)
    {
        var attributeSyntax = (AttributeSyntax)context.Node;

        // Check if the attribute is applied to a record declaration
        if (attributeSyntax.Parent?.Parent is not ClassDeclarationSyntax classDeclarationSyntax)
        {
            return null;
        }

        // Get the semantic model and check the type symbol
        INamedTypeSymbol? type = context.SemanticModel.GetDeclaredSymbol(classDeclarationSyntax);

        // Check if the type symbol has the MutableGenerationAttribute
        return (type is null || !HasImmutableGenerationAttribute(type)) ? null : type;
    }

    private static string? ExtractName(NameSyntax? name)
    {
        return name switch
        {
            SimpleNameSyntax ins => ins.Identifier.Text,
            QualifiedNameSyntax qns => qns.Right.Identifier.Text,
            _ => null
        };
    }

    private static bool HasImmutableGenerationAttribute(ISymbol type)
    {
        return type.GetAttributes().Any(static a =>
            a.AttributeClass is
            {
                Name: "ImmutableGenerationAttribute",
                ContainingNamespace:
                {
                    Name: "ReverseMutty",
                    ContainingNamespace.IsGlobalNamespace: true
                }
            });
    }
}
