using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ReverseMutty;

[Generator]
public class ReverseMuttyGenerator : IIncrementalGenerator
{
    private const string GenerateImmutableAttrName = "GenerateImmutableAttribute";
    private const string InImmutableAttrName = "InImmutableAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 生成标记特性
        context.RegisterPostInitializationOutput(static ctx =>
        {
            ctx.AddSource("GenerateImmutableAttribute.g.cs", SourceText.From(AttributesSource.GenerateImmutable, Encoding.UTF8));
            ctx.AddSource("InImmutableAttribute.g.cs", SourceText.From(AttributesSource.InImmutable, Encoding.UTF8));
        });

        // 查找所有标记了 [GenerateImmutable] 的类
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsSyntaxTarget(node),
                transform: static (ctx, _) => GetSemanticTarget(ctx))
            .Where(static symbol => symbol is not null)
            .Select(static (symbol, _) => symbol!);

        // 合并编译单元并生成代码
        var compilationAndClasses = context.CompilationProvider.Combine(classDeclarations.Collect());
        context.RegisterSourceOutput(compilationAndClasses,
            static (spc, source) => Execute(source.Left, source.Right, spc));
    }

    private static bool IsSyntaxTarget(SyntaxNode node)
        => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 };

    private static INamedTypeSymbol? GetSemanticTarget(GeneratorSyntaxContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;
        var model = context.SemanticModel;
        if (model.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol classSymbol) return null;

        var attr = classSymbol.GetAttributes().FirstOrDefault(ad =>
            ad.AttributeClass?.Name == GenerateImmutableAttrName ||
            ad.AttributeClass?.ToDisplayString().EndsWith(GenerateImmutableAttrName) == true);
        return attr is not null ? classSymbol : null;
    }

    private static void Execute(Compilation compilation, ImmutableArray<INamedTypeSymbol> classes, SourceProductionContext context)
    {
        if (classes.IsDefaultOrEmpty) return;

        var listType = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
        var dictType = compilation.GetTypeByMetadataName("System.Collections.Generic.Dictionary`2");
        // 不检查 null，在 ConvertType 中处理

        foreach (var classSymbol in classes.Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default))
        {
            var result = GenerateImmutableClass(classSymbol, compilation, listType, dictType, context.CancellationToken);
            if (!string.IsNullOrEmpty(result))
            {
                context.AddSource($"{classSymbol.Name}Immutable.g.cs", SourceText.From(result, Encoding.UTF8));
            }
        }
    }

    private static string GenerateImmutableClass(INamedTypeSymbol classSymbol, Compilation compilation,
        INamedTypeSymbol? listType, INamedTypeSymbol? dictType, System.Threading.CancellationToken cancellationToken)
    {
        var namespaceName = classSymbol.ContainingNamespace?.ToDisplayString() ?? "";
        var className = classSymbol.Name;

        // 处理泛型参数
        string typeParameters = "";
        string typeParametersConstraint = "";
        if (classSymbol.TypeParameters.Length > 0)
        {
            typeParameters = "<" + string.Join(", ", classSymbol.TypeParameters.Select(tp => tp.Name)) + ">";
            // 收集约束
            var constraints = new List<string>();
            foreach (var tp in classSymbol.TypeParameters)
            {
                var constraintList = new List<string>();
                if (tp.HasReferenceTypeConstraint)
                    constraintList.Add("class");
                else if (tp.HasValueTypeConstraint)
                    constraintList.Add("struct");
                constraintList.AddRange(tp.ConstraintTypes.Select(c => c.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                if (tp.HasConstructorConstraint)
                    constraintList.Add("new()");

                if (constraintList.Any())
                    constraints.Add($"where {tp.Name} : {string.Join(", ", constraintList)}");
            }
            if (constraints.Any())
                typeParametersConstraint = " " + string.Join(" ", constraints);
        }

        var immutableClassName = $"Immutable{className}{typeParameters}";

        var properties = new List<PropertyInfo>();
        var methods = new List<string>();

        // 收集属性
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is IPropertySymbol prop && IsPublicAutoProperty(prop))
            {
                var propType = ConvertType(prop.Type, listType, dictType, compilation, classSymbol);
                var defaultValue = GetDefaultValue(prop, cancellationToken);
                properties.Add(new PropertyInfo(prop.Name, propType, defaultValue));
            }
        }

        // 收集标记了 [InImmutable] 的方法
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.Ordinary } method)
            {
                var attr = method.GetAttributes().FirstOrDefault(ad =>
                    ad.AttributeClass?.Name == InImmutableAttrName ||
                    ad.AttributeClass?.ToDisplayString().EndsWith(InImmutableAttrName) == true);
                if (attr is not null)
                {
                    var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
                    if (syntaxRef?.GetSyntax(cancellationToken) is MethodDeclarationSyntax methodSyntax)
                    {
                        // 移除特性列表（避免重复），并添加警告注释
                        var withoutAttr = methodSyntax.WithAttributeLists(default);
                        methods.Add($@"
        // 注意：复制自原类 {className}，请确保方法体可编译（可能需调整对原类私有成员的访问）
        {withoutAttr.ToFullString().Trim()}");
                    }
                }
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        if (!string.IsNullOrEmpty(namespaceName))
        {
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
        }

        // 生成不可变 Record
        sb.AppendLine($"    public record {immutableClassName}{typeParametersConstraint}");
        sb.AppendLine("    {");

        // 属性
        foreach (var prop in properties)
        {
            sb.Append($"        public {prop.Type} {prop.Name} {{ get; init; }}");
            if (!string.IsNullOrEmpty(prop.DefaultValue))
                sb.Append($" = {prop.DefaultValue};");
            sb.AppendLine();
        }

        // 方法
        if (methods.Any())
        {
            sb.AppendLine();
            foreach (var method in methods)
            {
                sb.AppendLine(method);
            }
        }

        // ToMutable 方法
        sb.AppendLine();
        sb.AppendLine($"        public {className}{typeParameters} ToMutable()");
        sb.AppendLine("        {");
        sb.Append($"            return new {className}{typeParameters}");
        if (properties.Any())
        {
            sb.AppendLine();
            sb.AppendLine("            {");
            foreach (var prop in properties)
            {
                sb.AppendLine($"                {prop.Name} = this.{prop.Name},");
            }
            sb.Append("            };");
        }
        else
        {
            sb.Append("();");
        }
        sb.AppendLine();
        sb.AppendLine("        }");
        sb.AppendLine("    }");

        // 扩展方法 ToImmutable
        sb.AppendLine();
        sb.AppendLine($"    public static class {className}ImmutableExtensions");
        sb.AppendLine("    {");
        sb.AppendLine($"        public static {immutableClassName} ToImmutable(this {className}{typeParameters} source)");
        sb.AppendLine("        {");
        sb.Append($"            return new {immutableClassName}");
        if (properties.Any())
        {
            sb.AppendLine();
            sb.AppendLine("            {");
            foreach (var prop in properties)
            {
                sb.AppendLine($"                {prop.Name} = source.{prop.Name},");
            }
            sb.Append("            };");
        }
        else
        {
            sb.Append("();");
        }
        sb.AppendLine();
        sb.AppendLine("        }");
        sb.AppendLine("    }");

        if (!string.IsNullOrEmpty(namespaceName))
            sb.AppendLine("}");

        return sb.ToString();
    }

    private static bool IsPublicAutoProperty(IPropertySymbol prop)
    {
        return prop is
        {
            DeclaredAccessibility: Accessibility.Public,
            GetMethod.DeclaredAccessibility: Accessibility.Public,
            SetMethod.DeclaredAccessibility: Accessibility.Public
        };
    }

    private static string? GetDefaultValue(IPropertySymbol prop, System.Threading.CancellationToken cancellationToken)
    {
        var syntaxRef = prop.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef?.GetSyntax(cancellationToken) is PropertyDeclarationSyntax propSyntax)
        {
            return propSyntax.Initializer?.Value?.ToString();
        }
        return null;
    }

    private static string ConvertType(ITypeSymbol type, INamedTypeSymbol? listType, INamedTypeSymbol? dictType,
        Compilation compilation, INamedTypeSymbol currentClass)
    {
        // 处理泛型参数（如 T）
        if (type is ITypeParameterSymbol tp)
            return tp.Name;

        if (type is INamedTypeSymbol named)
        {
            // 检查 List<T>
            if (listType != null && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, listType))
            {
                var elementType = named.TypeArguments[0];
                return $"global::System.Collections.Immutable.ImmutableList<{ConvertType(elementType, listType, dictType, compilation, currentClass)}>";
            }
            // 检查 Dictionary<TKey,TValue>
            if (dictType != null && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, dictType))
            {
                var keyType = named.TypeArguments[0];
                var valueType = named.TypeArguments[1];
                return $"global::System.Collections.Immutable.ImmutableDictionary<{ConvertType(keyType, listType, dictType, compilation, currentClass)}, {ConvertType(valueType, listType, dictType, compilation, currentClass)}>";
            }
            // 其他命名类型：递归转换类型参数（如 List<List<T>> 中的内层 T）
            if (named.TypeArguments.Length > 0)
            {
                var convertedArgs = named.TypeArguments.Select(arg => ConvertType(arg, listType, dictType, compilation, currentClass));
                return $"{named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Split('[')[0]}<{string.Join(", ", convertedArgs)}>";
            }
        }

        // 默认返回完全限定名
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private record struct PropertyInfo(string Name, string Type, string? DefaultValue);
}

internal static class AttributesSource
{
    public const string GenerateImmutable = @"
using System;
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class GenerateImmutableAttribute : Attribute { }
";

    public const string InImmutable = @"
using System;
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class InImmutableAttribute : Attribute { }
";
}