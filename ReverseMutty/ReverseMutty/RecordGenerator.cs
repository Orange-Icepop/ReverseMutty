// Copyright (c) 2020-2024 Atypical Consulting SRL. All rights reserved.
// Atypical Consulting SRL licenses this file to you under the Apache 2.0 license.
// See the LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using ReverseMutty.Abstractions;
using ReverseMutty.Models;
using ReverseMutty.Templates;

namespace ReverseMutty;

/// <summary>
/// A generator that creates mutable records.
/// </summary>
[Generator]
public class RecordGenerator : BaseSourceGenerator
{
    /// <inheritdoc />
    public override void GenerateCode(SourceProductionContext context, ImmutableArray<INamedTypeSymbol> classTypes)
    {
        if (classTypes.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (INamedTypeSymbol classType in classTypes)
        {
            ClassTokens recordTokens = new(classType);
            string recordName = recordTokens.ClassName;
            string? namespaceName = recordTokens.NamespaceName;

            // Generate mutable wrapper
            string mutableWrapperSource = new ImmutableWrapperTemplate(recordTokens).GenerateCode();
            string mutableFileName = (namespaceName is not null)
                ? $"{namespaceName}.Mutable{recordName}.g.cs"
                : $"Mutable{recordName}.g.cs";
            AddSource(context, mutableFileName, mutableWrapperSource);
        }
    }
}
