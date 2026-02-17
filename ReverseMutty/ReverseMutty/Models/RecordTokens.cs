// Copyright (c) 2020-2024 Atypical Consulting SRL. All rights reserved.
// Atypical Consulting SRL licenses this file to you under the Apache 2.0 license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ReverseMutty.Models;

/// <summary>
/// Represents some string tokens that will be used to generate a record.
/// </summary>
public class ClassTokens
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClassTokens"/> class.
    /// </summary>
    /// <param name="classSymbol">The record symbol.</param>
    public ClassTokens(INamedTypeSymbol classSymbol)
    {
        ClassName = classSymbol.Name;

        NamespaceName = (classSymbol.ContainingNamespace.IsGlobalNamespace)
            ? null
            : classSymbol.ContainingNamespace.ToString();

        Properties = classSymbol
            .GetMembers()
            .OfType<IPropertySymbol>()
            .Where(static p =>
                p is
                {
                    IsReadOnly: false,
                    IsImplicitlyDeclared: false,
                    DeclaredAccessibility: Accessibility.Public
                })
            .Select(static p => new PropertyModel(p))
            .ToImmutableArray();
    }

    /// <summary>
    /// Gets the name of the class.
    /// </summary>
    public string ClassName { get; }

    /// <summary>
    /// Gets the name of the immutable record.
    /// </summary>
    public string ImmutableRecordName => $"Immutable{ClassName}";

    /// <summary>
    /// Gets the namespace of the class.
    /// </summary>
    public string? NamespaceName { get; }

    /// <summary>
    /// Gets the properties of the class.
    /// </summary>
    public ImmutableArray<PropertyModel> Properties { get; }
}
