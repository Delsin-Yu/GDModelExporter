using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Godot.SourceGenerators
{
    internal static class ExtensionMethods
    {
        public static bool InheritsFrom(this ITypeSymbol? symbol, string assemblyName, string typeFullName)
        {
            while (symbol != null)
            {
                if (symbol.ContainingAssembly?.Name == assemblyName &&
                    symbol.FullQualifiedNameOmitGlobal() == typeFullName)
                {
                    return true;
                }

                symbol = symbol.BaseType;
            }

            return false;
        }

        private static bool TryGetGodotScriptClass(
            this ClassDeclarationSyntax cds, Compilation compilation,
            out INamedTypeSymbol? symbol
        )
        {
            var sm = compilation.GetSemanticModel(cds.SyntaxTree);

            var classTypeSymbol = sm.GetDeclaredSymbol(cds);

            if (classTypeSymbol?.BaseType == null
                || !classTypeSymbol.BaseType.InheritsFrom("GodotSharp", "Godot.GodotObject"))
            {
                symbol = null;
                return false;
            }

            symbol = classTypeSymbol;
            return true;
        }

        public static IEnumerable<(ClassDeclarationSyntax cds, INamedTypeSymbol symbol)> SelectGodotScriptClasses(
            this IEnumerable<ClassDeclarationSyntax> source,
            Compilation compilation
        )
        {
            foreach (var cds in source)
            {
                if (cds.TryGetGodotScriptClass(compilation, out var symbol))
                    yield return (cds, symbol!);
            }
        }

        public static bool IsNested(this TypeDeclarationSyntax cds)
            => cds.Parent is TypeDeclarationSyntax;

        public static bool IsPartial(this TypeDeclarationSyntax cds)
            => cds.Modifiers.Any(SyntaxKind.PartialKeyword);

        public static bool AreAllOuterTypesPartial(
            this TypeDeclarationSyntax cds,
            out TypeDeclarationSyntax? typeMissingPartial
        )
        {
            SyntaxNode? outerSyntaxNode = cds.Parent;

            while (outerSyntaxNode is TypeDeclarationSyntax outerTypeDeclSyntax)
            {
                if (!outerTypeDeclSyntax.IsPartial())
                {
                    typeMissingPartial = outerTypeDeclSyntax;
                    return false;
                }

                outerSyntaxNode = outerSyntaxNode.Parent;
            }

            typeMissingPartial = null;
            return true;
        }

        public static string GetDeclarationKeyword(this INamedTypeSymbol namedTypeSymbol)
        {
            string? keyword = namedTypeSymbol.DeclaringSyntaxReferences
                .OfType<TypeDeclarationSyntax>().FirstOrDefault()?
                .Keyword.Text;

            return keyword ?? namedTypeSymbol.TypeKind switch
            {
                TypeKind.Interface => "interface",
                TypeKind.Struct => "struct",
                _ => "class"
            };
        }

        public static string NameWithTypeParameters(this INamedTypeSymbol symbol)
        {
            return symbol.IsGenericType ?
                string.Concat(symbol.Name, "<", string.Join(", ", symbol.TypeParameters), ">") :
                symbol.Name;
        }

        private static SymbolDisplayFormat FullyQualifiedFormatOmitGlobal { get; } =
            SymbolDisplayFormat.FullyQualifiedFormat
                .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted);

        private static SymbolDisplayFormat FullyQualifiedFormatIncludeGlobal { get; } =
            SymbolDisplayFormat.FullyQualifiedFormat
                .WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Included);

        public static string FullQualifiedNameOmitGlobal(this ITypeSymbol symbol)
            => symbol.ToDisplayString(NullableFlowState.NotNull, FullyQualifiedFormatOmitGlobal);

        public static string FullQualifiedNameOmitGlobal(this INamespaceSymbol namespaceSymbol)
            => namespaceSymbol.ToDisplayString(FullyQualifiedFormatOmitGlobal);

        public static string FullQualifiedNameIncludeGlobal(this ITypeSymbol symbol)
            => symbol.ToDisplayString(NullableFlowState.NotNull, FullyQualifiedFormatIncludeGlobal);
        

        public static string SanitizeQualifiedNameForUniqueHint(this string qualifiedName)
            => qualifiedName
                // AddSource() doesn't support angle brackets
                .Replace("<", "(Of ")
                .Replace(">", ")");
    }
}
