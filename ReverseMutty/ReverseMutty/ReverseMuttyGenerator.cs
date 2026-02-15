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

        // 语法快速筛选：类声明且包含特性列表
        private static bool IsSyntaxTarget(SyntaxNode node)
            => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 };

        // 语义转换：获取应用了 [GenerateImmutable] 的类符号
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

        // 主执行逻辑
        private static void Execute(Compilation compilation, ImmutableArray<INamedTypeSymbol> classes, SourceProductionContext context)
        {
            if (classes.IsDefaultOrEmpty) return;

            // 获取常用类型符号，用于类型比较
            var listType = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
            var dictType = compilation.GetTypeByMetadataName("System.Collections.Generic.Dictionary`2");
            var immutableListType = compilation.GetTypeByMetadataName("System.Collections.Immutable.ImmutableList");
            var immutableDictType = compilation.GetTypeByMetadataName("System.Collections.Immutable.ImmutableDictionary");

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
            var immutableClassName = $"Immutable{className}";

            var properties = new List<PropertyInfo>();
            var methods = new List<string>();

            // 收集属性
            foreach (var member in classSymbol.GetMembers())
            {
                if (member is IPropertySymbol prop && IsPublicAutoProperty(prop))
                {
                    var propType = ConvertType(prop.Type, listType, dictType, compilation);
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
                        // 获取方法声明的完整文本
                        var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
                        if (syntaxRef?.GetSyntax(cancellationToken) is MethodDeclarationSyntax methodSyntax)
                        {
                            // 移除特性列表，避免重复
                            var withoutAttr = methodSyntax.WithAttributeLists(default);
                            methods.Add(withoutAttr.ToFullString());
                        }
                    }
                }
            }

            // 构建代码
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine($"namespace {namespaceName}");
                sb.AppendLine("{");
            }

            // 生成不可变 Record
            sb.AppendLine($"    public record {immutableClassName}");
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
                    sb.AppendLine($"        {method.Trim()}");
                }
            }

            // ToMutable 方法
            sb.AppendLine();
            sb.AppendLine($"        public {className} ToMutable()");
            sb.AppendLine("        {");
            sb.Append($"            return new {className}");
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
            sb.AppendLine($"        public static {immutableClassName} ToImmutable(this {className} source)");
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

        // 判断是否为 public 自动属性（有 get; set;）
        private static bool IsPublicAutoProperty(IPropertySymbol prop)
        {
            return prop is
            {
                DeclaredAccessibility: Accessibility.Public, 
                GetMethod.DeclaredAccessibility: Accessibility.Public,
                SetMethod.DeclaredAccessibility: Accessibility.Public
            };
        }

        // 获取属性的默认值表达式（如果有）
        private static string? GetDefaultValue(IPropertySymbol prop, System.Threading.CancellationToken cancellationToken)
        {
            var syntaxRef = prop.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef?.GetSyntax(cancellationToken) is PropertyDeclarationSyntax propSyntax)
            {
                var initializer = propSyntax.Initializer?.Value?.ToString();
                return initializer;
            }
            return null;
        }

        // 类型转换：List<T> → ImmutableList<T>；Dictionary<TKey,TValue> → ImmutableDictionary<TKey,TValue>
        private static string ConvertType(ITypeSymbol type, INamedTypeSymbol? listType, INamedTypeSymbol? dictType, Compilation compilation)
        {
            if (type is INamedTypeSymbol named)
            {
                // 检查 List<T>
                if (listType != null && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, listType))
                {
                    var elementType = named.TypeArguments[0];
                    return $"global::System.Collections.Immutable.ImmutableList<{ConvertType(elementType, listType, dictType, compilation)}>";
                }
                // 检查 Dictionary<TKey,TValue>
                if (dictType != null && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, dictType))
                {
                    var keyType = named.TypeArguments[0];
                    var valueType = named.TypeArguments[1];
                    return $"global::System.Collections.Immutable.ImmutableDictionary<{ConvertType(keyType, listType, dictType, compilation)}, {ConvertType(valueType, listType, dictType, compilation)}>";
                }
            }
            // 其他类型返回完全限定名
            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private record struct PropertyInfo(string Name, string Type, string? DefaultValue);
    }

    // 内嵌特性源码
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