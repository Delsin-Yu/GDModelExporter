using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Godot.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace GDModelExporter;

[Generator(LanguageNames.CSharp)]
public class Generator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context) { }

    public void Execute(GeneratorExecutionContext context)
    {
        var godotClasses = context.Compilation.SyntaxTrees.SelectMany(
                tree =>
                    tree.GetRoot().DescendantNodes()
                        .OfType<ClassDeclarationSyntax>()
                        .SelectGodotScriptClasses(context.Compilation)
                        // Report and skip non-partial classes
                        .Where(
                            x =>
                            {
                                if (!x.cds.IsPartial()) return false;
                                return !x.cds.IsNested() || x.cds.AreAllOuterTypesPartial(out _);
                            }
                        )
                        .Select(x => x.symbol)
            )
            .Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default)
            .ToArray();

        GenerateAttributes(context);
        
        if (godotClasses.Length <= 0) return;
        
        foreach (var typeSymbol in godotClasses)
        {
            var attributes = typeSymbol.GetAttributes();

            var found = false;

            foreach (var attributeData in attributes)
            {
                if (attributeData.AttributeClass?.Name is not (ClassAttributeShort or ClassAttribute)) continue;
                found = true;
                break;
            }

            if(!found) continue;

            GenerateExportCodeForType(typeSymbol, context);
        }
    }
    
    private const string Namespace = "GDModelExporter";
    private const string ClassAttribute = "GenerateExportModelAttribute";
    private const string ClassAttributeShort = "GenerateExportModel";
    private const string FieldAttribute = "ExportModelAttribute";
    private const string FieldAttributeShort = "ExportModel";

    private static void GenerateAttributes(GeneratorExecutionContext context)
    {
        context.AddSource(
            "Attributes.g.cs",
            $$"""
            using System;
            using Godot;
            
            namespace {{Namespace}}
            {
                /// <summary>
                /// Instructs the source code generator to generate export code for fields marked with <see cref=\"ExportModelAttribute\"/> in the current type
                /// </summary>
                [AttributeUsage(AttributeTargets.Class)]
                public class {{ClassAttribute}} : Attribute;
                
                /// <summary>
                /// Instructions the source code generator to generate export code for the current field
                /// </summary>
                [AttributeUsage(AttributeTargets.Field)]
                public class {{FieldAttribute}} : Attribute;
                
                /// <summary>
                /// Instructs the source code generator to use the export configuration for a specific property
                /// </summary>
                [AttributeUsage(AttributeTargets.Parameter)]
                public class ExportConfigAttribute(PropertyHint propertyHint, string hintString = "") : Attribute
                {
                    /// <summary>
                    /// The <see cref="PropertyHint"/> to use
                    /// </summary>
                    public PropertyHint PropertyHint { get; } = propertyHint;
                
                    /// <summary>
                    /// The hint string to use
                    /// </summary>
                    public string HintString { get; } = hintString;
                }
            }
            """
        );
    }
    
    private static void GenerateExportCodeForType(INamedTypeSymbol symbol, GeneratorExecutionContext context)
    {
        var source = new StringBuilder();

        source.AppendLine(
            """
            using System;
            using System.Collections.Generic;
            using Godot;

            #nullable enable

            """
        );

        var namespaceSymbol = symbol.ContainingNamespace;
        var classNs = namespaceSymbol is { IsGlobalNamespace: false }
            ? namespaceSymbol.FullQualifiedNameOmitGlobal()
            : string.Empty;
        var hasNamespace = classNs.Length != 0;
        var isInnerClass = symbol.ContainingType != null;
        var uniqueHint = symbol.FullQualifiedNameOmitGlobal().SanitizeQualifiedNameForUniqueHint()
                         + "_ScriptExportModels.generated";

        var propertyBuilder = new StringBuilder();
        var stringNameBuilder = new StringBuilder();

        if (hasNamespace)
        {
            source.Append("namespace ");
            source.Append(classNs);
            source.Append(";\n\n");
        }

        var indentationLevel = 1;

        if (isInnerClass)
        {
            var containingType = symbol.ContainingType;
            AppendPartialContainingTypeDeclarations(containingType);

            void AppendPartialContainingTypeDeclarations(INamedTypeSymbol? containingTypeSymbol)
            {
                if (containingTypeSymbol == null)
                    return;

                indentationLevel++;
                AppendPartialContainingTypeDeclarations(containingTypeSymbol.ContainingType);

                source.Append("partial ");
                source.Append(containingTypeSymbol.GetDeclarationKeyword());
                source.Append(" ");
                source.Append(containingTypeSymbol.NameWithTypeParameters());
                source.Append("\n{\n");
            }
        }

        var ci = new string(' ', 4 * (indentationLevel - 1));
        source
            .Append(ci).Append("partial class ").Append(symbol.NameWithTypeParameters()).Append(" : ISerializationListener").Append('\n')
            .Append(ci).Append("{\n");


        var members = symbol.GetMembers();
        var valid = false;
        var mi = new string(' ', 4 * indentationLevel);
        var mii = new string(' ', 4 * (indentationLevel + 1));
        var miii = new string(' ', 4 * (indentationLevel + 2));
        
        
        source
            .AppendLine(
                $$"""
                  {{mi}}private void PopulatePropertyList(Godot.Collections.Array<Godot.Collections.Dictionary> propertyList)
                  {{mi}}{
                  """
            );

        var dictionary = new Dictionary<string, (IFieldSymbol Model, IParameterSymbol Parameter)>();

        var fields = members.OfType<IFieldSymbol>().ToArray();

        foreach (var field in fields)
        {
            if (field
                .GetAttributes()
                .All(data => data.AttributeClass?.Name is not (FieldAttribute or FieldAttributeShort))) continue;

            var parameterizedCtors = field.Type.GetMembers().OfType<IMethodSymbol>().Where(x => x.MethodKind == MethodKind.Constructor).FirstOrDefault(x => x.Parameters.Length > 0);

            if(parameterizedCtors is null) continue;
            
            valid = true;

            var pascalFieldName = SnakeCaseToPascalCase(field.Name);

            source.AppendLine($"#region {pascalFieldName}");
            
            var notNullTypeName = field.Type.ToString().Replace("?", string.Empty);
            propertyBuilder
                .Append(mi).AppendLine($"/// <summary>The runtime initialized <see cref=\"{notNullTypeName}\"/> property</summary>")
                .Append(mi).AppendLine($"public {notNullTypeName} {pascalFieldName} => {field.Name}!;")
                .AppendLine();

            Append(
                VariantType.Nil,
                pascalFieldName,
                PropertyHint.None,
                "",
                PropertyUsageFlags.Group,
                false
            );
            
            foreach (var parameterSymbol in parameterizedCtors.Parameters)
            {

                GetInfo(
                    parameterSymbol.Type,
                    out var type,
                    out var hint,
                    out var hintString,
                    out var isError
                );

                dictionary.Add(parameterSymbol.Name, (field, parameterSymbol));
                stringNameBuilder.Append(mi).AppendLine(
                    $"""private static readonly StringName {parameterSymbol.Name}_StringNameCache = "{parameterSymbol.Name}";"""
                );
                
                var exportConfig = parameterSymbol.GetAttributes()
                    .FirstOrDefault(data => data.AttributeClass?.Name == "ExportConfigAttribute");

                if (exportConfig != null)
                {
                    var arguments = exportConfig.ConstructorArguments;
                    var value1 = arguments[0].Value;
                    if (value1 != null) hint = (PropertyHint)(long)value1;
                    var value2 = arguments[1].Value;
                    if (value2 != null) hintString = (string)value2;
                }

                Append(
                    type,
                    parameterSymbol.Name,
                    hint,
                    hintString,
                    PropertyUsageFlags.Default | PropertyUsageFlags.ScriptVariable,
                    isError
                );
            }

            source
                .AppendLine("#endregion")
                .AppendLine();

            continue;

            void Append(
                VariantType type,
                string name,
                PropertyHint hint,
                string hintString,
                PropertyUsageFlags usageFlags,
                bool isError)
            {
                if (isError)
                {
                    source.AppendLine($"#error {hintString} {name}");
                    return;
                }

                source
                    .Append(mii)
                    .AppendLine(
                        $$"""
                          propertyList.Add(new() { { "type", (int)Variant.Type.{{type}} }, { "name", "{{name}}" }, { "hint", (int)PropertyHint.{{hint}} }, { "hint_string", "{{hintString}}" }, { "usage", (int)({{ParseFlags("PropertyUsageFlags.", usageFlags.ToString())}}) } });
                          """
                    );
            }
        }

        if (!valid) return;

        source
            .AppendLine(
                $$"""
                  {{mi}}}

                  """
            );

        source
            .Append(mi).AppendLine("private readonly Dictionary<StringName, Variant> _propertyStorage = new()")
            .Append(mi).AppendLine("{");

        foreach (var data in dictionary)
            source.AppendLine(
                $$"""
                  {{mii}}{ {{data.Key}}_StringNameCache, default },    
                  """
            );
        
        source
            .Append(mi).AppendLine("};").AppendLine();
        
        source
            .AppendLine(
                $$"""
                  {{mi}}private Variant GetProperty(StringName propertyName)
                  {{mi}}{
                  {{mii}}if (_propertyStorage.TryGetValue(propertyName, out var property))
                  {{miii}}return property;
                  {{mii}}return default;
                  {{mi}}}
                  
                  """
            );

        source
            .AppendLine(
                $$"""
                  {{mi}}private bool SetProperty(StringName propertyName, Variant value)
                  {{mi}}{
                  {{mii}}ref var propertyValue = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrNullRef(_propertyStorage, propertyName);
                  {{mii}}if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref propertyValue)) return false;
                  {{mii}}propertyValue = value;
                  {{mii}}return true;
                  {{mi}}}
                  
                  """
            );

        source.AppendLine(
            $$"""
              {{mi}}private void InitializeProperties()
              {{mi}}{
              #if TOOLS
              {{mii}}if (Engine.IsEditorHint()) return;
              #endif
              """
        );

        foreach (var fieldSymbol in fields)
        {
            var fieldName = fieldSymbol.Name;
            var fieldType = fieldSymbol.Type;
            source.AppendLine($"{mii}{fieldName} = new(");
            
            var constructor = fieldType.GetMembers().OfType<IMethodSymbol>().First(x => x.MethodKind == MethodKind.Constructor);
            var isFirst = true;
            foreach (var parameterSymbol in constructor.Parameters)
            {
                if (isFirst) isFirst = false;
                else source.AppendLine(", ");
                source.Append($"{miii}_propertyStorage[{parameterSymbol.Name}_StringNameCache].As<{parameterSymbol.Type}>()");
            }

            
            source.AppendLine().AppendLine($"{mii});");
        }
        
        source.AppendLine(
            $$"""
              {{mi}}}
              {{mi}}
              """
        );

        source.AppendLine(
            $$"""
              {{mi}}/// <inheritdoc/>
              {{mi}}public void OnBeforeSerialize()
              {{mi}}{
              {{mii}}foreach (var (propertyName, propertyValue) in _propertyStorage) 
              {{miii}}SetMeta(propertyName, propertyValue);
              {{mi}}}
              
              {{mi}}/// <inheritdoc/>
              {{mi}}public void OnAfterDeserialize()
              {{mi}}{
              {{mii}}foreach (var key in _propertyStorage.Keys)
              {{mii}}{
              {{miii}}if (!HasMeta(key)) continue; 
              {{miii}}_propertyStorage[key] = GetMeta(key);
              {{miii}}RemoveMeta(key);
              {{mii}}}
              {{mi}}}
              """
        );


        source.AppendLine(
            $$"""
              {{mi}}private void DisposeValues()
              {{mi}}{
              {{mii}}Variant property;
              """
              );
        
        foreach (var fieldSymbol in fields)
        {
            var fieldType = fieldSymbol.Type;
            
            var constructor = fieldType.GetMembers().OfType<IMethodSymbol>().First(x => x.MethodKind == MethodKind.Constructor);
            foreach (var parameterSymbol in constructor.Parameters)
            {
                bool isGodotObject;
                var currentType = parameterSymbol.Type;
                do
                {
                    isGodotObject = currentType.Name == "GodotObject";
                    currentType = currentType.BaseType;
                } while (currentType != null && !isGodotObject);

                if(!isGodotObject) continue;
                
                source.AppendLine($"{mii}if (_propertyStorage.Remove({parameterSymbol.Name}_StringNameCache, out property)) property.AsGodotObject()?.Dispose();");
            }
        }
        
        source.AppendLine(
            $$"""
              {{mii}}_propertyStorage.Clear();
              {{mi}}}
              """
        );
        
        source.Append(propertyBuilder).AppendLine();
        source.Append(stringNameBuilder).AppendLine();

        source.Append(ci);
        source.Append("}\n"); // partial class

        if (isInnerClass)
        {
            var containingType = symbol.ContainingType;

            while (containingType != null)
            {
                source.Append("}\n"); // outer class

                containingType = containingType.ContainingType;
            }
        }

        context.AddSource(uniqueHint, SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private static void GetInfo(
        ITypeSymbol symbol,
        out VariantType type,
        out PropertyHint hint,
        out string hintString,
        out bool isError)
    {
        hint = PropertyHint.None;
        hintString = "";
        isError = false;

        switch (symbol.Name)
        {
            case nameof(Boolean):
                type = VariantType.Bool;
                break;
            case nameof(Char):
            case nameof(SByte):
            case nameof(Int16):
            case nameof(Int32):
            case nameof(Int64):
            case nameof(Byte):
            case nameof(UInt16):
            case nameof(UInt32):
            case nameof(UInt64):
                type = VariantType.Int;
                break;
            case nameof(Single):
            case nameof(Double):
                type = VariantType.Float;
                break;
            case nameof(String):
                type = VariantType.String;
                break;
            case "Vector2":
                type = VariantType.Vector2;
                break;
            case "Vector2I":
                type = VariantType.Vector2I;
                break;
            case "Rect2":
                type = VariantType.Rect2;
                break;
            case "Rect2I":
                type = VariantType.Rect2I;
                break;
            case "Transform2D":
                type = VariantType.Transform2D;
                break;
            case "Vector3":
                type = VariantType.Vector3;
                break;
            case "Vector3I":
                type = VariantType.Vector3I;
                break;
            case "Basis":
                type = VariantType.Basis;
                break;
            case "Quaternion":
                type = VariantType.Quaternion;
                break;
            case "Transform3D":
                type = VariantType.Transform3D;
                break;
            case "Vector4":
                type = VariantType.Vector4;
                break;
            case "Vector4I":
                type = VariantType.Vector4I;
                break;
            case "Projection":
                type = VariantType.Projection;
                break;
            case "Aabb":
                type = VariantType.Aabb;
                break;
            case "Color":
                type = VariantType.Color;
                break;
            case "Plane":
                type = VariantType.Plane;
                break;
            case "Variant":
                type = VariantType.Nil;
                break;
            default:
                type = VariantType.Nil;
                hintString = $"Not Supported Type: {symbol.Name}";
                isError = true;

                switch (symbol.TypeKind)
                {
                    case TypeKind.Array:
                        var arraySymbol = (IArrayTypeSymbol)symbol;
                        if (arraySymbol.Rank > 1) break;
                        var arrayType = arraySymbol.ElementType;

                        switch (arrayType.Name)
                        {
                            case nameof(Byte):
                                type = VariantType.PackedByteArray;
                                isError = false;
                                hintString = "";
                                break;
                            case nameof(Int32):
                                type = VariantType.PackedInt32Array;
                                isError = false;
                                hintString = "";
                                break;
                            case nameof(Int64):
                                type = VariantType.PackedInt64Array;
                                isError = false;
                                hintString = "";
                                break;
                            case nameof(Single):
                                type = VariantType.PackedFloat32Array;
                                isError = false;
                                hintString = "";
                                break;
                            case nameof(Double):
                                type = VariantType.PackedFloat64Array;
                                isError = false;
                                hintString = "";
                                break;
                            case nameof(String):
                                type = VariantType.PackedStringArray;
                                isError = false;
                                hintString = "";
                                break;
                            case "Vector2":
                                type = VariantType.PackedVector2Array;
                                isError = false;
                                hintString = "";
                                break;
                            case "Vector3":
                                type = VariantType.PackedVector3Array;
                                isError = false;
                                hintString = "";
                                break;
                            case "Color":
                                type = VariantType.PackedColorArray;
                                isError = false;
                                hintString = "";
                                break;
                            case "Vector4":
                                type = VariantType.PackedVector4Array;
                                isError = false;
                                hintString = "";
                                break;
                        }

                        break;
                    case TypeKind.Enum:
                        type = VariantType.Int;
                        hint = symbol
                            .GetAttributes()
                            .Any(x => x.AttributeClass?.Name == "FlagsAttribute")
                            ? PropertyHint.Flags
                            : PropertyHint.Enum;
                        hintString =
                            string.Join(
                                ",",
                                symbol
                                    .GetMembers()
                                    .OfType<IFieldSymbol>()
                                    .Where(x => x.IsConst)
                                    .Select(x => $"{x.Name}:{x.ConstantValue}")
                            );
                        isError = false;
                        break;
                    case TypeKind.Class:
                        if (HasBaseType(symbol, "Node"))
                        {
                            type = VariantType.Object;
                            hint = PropertyHint.NodeType;
                            isError = false;
                            hintString = symbol.Name;
                        }
                        
                        else if (HasBaseType(symbol, "Dictionary"))
                        {
                            type = VariantType.Dictionary;

                            if (symbol.OriginalDefinition.FullQualifiedNameOmitGlobal() == "Godot.Collections.Dictionary<TKey, TValue>" && symbol is INamedTypeSymbol namedTypeSymbol)
                            {
                                hint = PropertyHint.TypeString;
                                var innerTypeKey = namedTypeSymbol.TypeArguments[0];
                                var innerTypeValue = namedTypeSymbol.TypeArguments[1];
                                GetInfo(innerTypeKey, out var innerKeyVariantType, out var innerKeyVariantHint, out var innerKeyHintString, out var innerKeyHintError);
                                GetInfo(innerTypeValue, out var innerValueVariantType, out var innerValueVariantHint, out var innerValueHintString, out var innerValueHintError);
                                if (!innerKeyHintError && !innerValueHintError)
                                {
                                    hintString = $"{(int)innerKeyVariantType}/{(int)innerKeyVariantHint}:{innerKeyHintString};{(int)innerValueVariantType}/{(int)innerValueVariantHint}:{innerValueHintString}";
                                    isError = false;  
                                }
                                else
                                {
                                    hintString = $"Not Supported Type: {symbol.Name}, Key ({innerTypeKey.Name}) Match: {innerKeyHintError}, Value ({innerTypeValue.Name}) Match: {innerValueHintError}";
                                }
                            }
                            else
                            {
                                hint = PropertyHint.None;
                                isError = false;
                                hintString = symbol.Name;
                            }
                        }
                        
                        else if (HasBaseType(symbol, "Array"))
                        {
                            
                            type = VariantType.Array;
                            if (symbol.OriginalDefinition.FullQualifiedNameOmitGlobal() == "Godot.Collections.Array<T>" && symbol is INamedTypeSymbol namedTypeSymbol)
                            {
                                hint = PropertyHint.TypeString;
                                var innerType = namedTypeSymbol.TypeArguments[0];
                                GetInfo(innerType, out var innerVariantType, out var innerVariantHint, out var innerHintString, out var error);
                                if (!error)
                                {
                                    hintString = $"{(int)innerVariantType}/{(int)innerVariantHint}:{innerHintString}";
                                    isError = false;  
                                }
                            }
                            else
                            {
                                hint = PropertyHint.None;
                                isError = false;
                                hintString = symbol.Name;
                            }
                        }

                        else if (HasBaseType(symbol, "Resource"))
                        {
                            type = VariantType.Object;
                            hint = PropertyHint.ResourceType;
                            isError = false;
                            hintString = symbol.Name;
                        }

                        break;
                }

                break;
        }
    }

    public static bool HasBaseType(ITypeSymbol symbol, string typeName)
    {
        while (true)
        {
            if (symbol.Name == typeName) return true;
            if (symbol.BaseType == null) return false;
            symbol = symbol.BaseType;
        }
    }

    private static readonly Regex SnakeToPascalRegex = new("(^|_)([a-z])", RegexOptions.Compiled);

    private static string SnakeCaseToPascalCase(string input) => string.IsNullOrEmpty(input)
        ? input
        : SnakeToPascalRegex
            .Replace(input, m => m.Groups[2].Value.ToUpperInvariant());

    private static readonly string[] SplitArgs = [", "];

    private static string ParseFlags(string header, string flags)
    {
        return string.Join(" | ", flags.Split(SplitArgs, StringSplitOptions.RemoveEmptyEntries).Select(x => header + x));
    }
}