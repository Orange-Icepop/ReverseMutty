// Copyright (c) 2020-2024 Atypical Consulting SRL. All rights reserved.
// Atypical Consulting SRL licenses this file to you under the Apache 2.0 license.
// See the LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Mutty.Templates;

namespace Mutty;

/// <summary>
/// A generator that creates mutable wrappers for records.
/// </summary>
[Generator]
public class Attributes : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Inject the marker attribute into the user's compilation.
        context.RegisterPostInitializationOutput(ctx =>
        {
            string attributeSource = new MutableGenerationAttributeTemplate().GenerateCode();
            const string fileName = "Mutty.MutableGenerationAttribute.g.cs";
            ctx.AddSource(fileName, attributeSource);
        });
    }
}
