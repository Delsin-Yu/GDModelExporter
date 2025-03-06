# GDModelExporter

The `GDModelExport` project utilize the power of Source Generator to dynamically generates `GodotSharp` compatible property bindings (`_GetPropertyList`, `_Get`, and `_Set`) implementation, which partially enables simplex use of custom data models.

## Usage

You may check the [example project](./GDModelExporter.Examples/) for a complete example of how to use the `GDModelExporter` project.

First, reference the `GDModelExporter` project in your Godot project.

```xml
<ItemGroup>
    <ProjectReference
            Include="..\GDModelExporter\GDModelExporter.csproj"
            OutputItemType="Analyzer"
            ReferenceOutputAssembly="true"/>
</ItemGroup>
```

Consider the following C# data model:

```csharp
public record TestDataModel(
    float FloatValue,
    Vector2 Vector2Value,
    Transform2D Transform2DValue,
    Projection ProjectionValue,
    Color ColorValue,
    byte[] PackedByteArray,
    long[] PackedInt64Array,
    Vector3[] PackedVector3Array,
    DayOfWeek Enum,
    StringSplitOptions FlagsEnum,
    Godot.Collections.Array<Vector2I> UnpackedVector2IArray,
    Godot.Collections.Array<Resource> UnpackedResourcesArray,
    Godot.Collections.Dictionary<int, Texture2D> Dictionary
);
```

This data model can be used in a `Node` type as follows:

```csharp
using Godot;

namespace GDModelExporter.Examples;

// Add the GenerateExportModel and the Tool attribute to the class
[GenerateExportModel, Tool]
public partial class TestExportNode : Node
{
    // Add the ExportModel attribute to the field you wish to bind to
    [ExportModel] private TestDataModel? _testDataModel;

    public override void _Ready()
    {
        // Call the generated InitializeProperties method in runtime code for initialization
        InitializeProperties();
        
        // the TestDataModel property is now available
        GD.Print(TestDataModel.FloatValue);
        GD.Print(TestDataModel.Vector2Value);
        GD.Print(TestDataModel.Transform2DValue);
        GD.Print(TestDataModel.ProjectionValue);
        GD.Print(TestDataModel.ColorValue);
        GD.Print(TestDataModel.PackedByteArray);
        GD.Print(TestDataModel.PackedInt64Array);
        GD.Print(TestDataModel.PackedVector3Array);
        GD.Print(TestDataModel.Enum);
        GD.Print(TestDataModel.FlagsEnum);
        GD.Print(TestDataModel.UnpackedVector2IArray);
        GD.Print(TestDataModel.UnpackedResourcesArray);
        GD.Print(TestDataModel.Dictionary);
    }

    // Editor will call _Get on a script with Tool attribute, redirect the call to the generated GetProperty method
    public override Variant _Get(StringName property) => GetProperty(property);

    // Editor will call _Set on a script with Tool attribute, redirect the call to the generated SetProperty method
    public override bool _Set(StringName property, Variant value) => SetProperty(property, value);

    // Editor will call _GetPropertyList on a script with Tool attribute, redirect the call to the generated GetPropertyList method
    public override Godot.Collections.Array<Godot.Collections.Dictionary> _GetPropertyList()
    {
        var list = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        PopulatePropertyList(list);
        return list;
    }

    // Make sure to call the generated DisposeValues method when the node gets freed
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        DisposeValues();
    }
}
```

## Limitations

As C# is a compiled language, you ***MUST*** build the project before you entering any scene with node that uses the `GDModelExporter` generated code, otherwise the data will loss at the time you save that scene.
The `GDModelExporter` is not a full replacement for the `Export` attribute, it only provides a way to bind data models to Godot nodes. You are still recomeneded to use the `Export` attribute for simple properties.