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
/// A generator that creates extension methods for mutable records.
/// </summary>
[Generator]
public class RecordExtensionsGenerator : BaseSourceGenerator
{
    /// <inheritdoc />
    public override void GenerateCode(SourceProductionContext context, ImmutableArray<INamedTypeSymbol> recordTypes)
    {
        if (recordTypes.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (INamedTypeSymbol record in recordTypes)
        {
            ClassTokens recordTokens = new(record);
            string recordName = recordTokens.ClassName;
            string? namespaceName = recordTokens.NamespaceName;

            // Generate extension methods
            string mutableExtensionSource = new MutableExtensionsTemplate(recordTokens).GenerateCode();
            string extensionFileName = (namespaceName is not null)
                ? $"{namespaceName}.Extensions{recordName}.g.cs"
                : $"Extensions{recordName}.g.cs";
            AddSource(context, extensionFileName, mutableExtensionSource);
        }
    }
}
