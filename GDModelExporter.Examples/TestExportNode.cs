using Godot;

namespace GDModelExporter.Examples;

[GenerateExportModel, Tool]
public partial class TestExportNode : Node
{
    [ExportModel] private TestDataModel? _testDataModel;

    public override void _Ready()
    {
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

    public override Variant _Get(StringName property) => GetProperty(property);

    public override bool _Set(StringName property, Variant value) => SetProperty(property, value);

    public override Godot.Collections.Array<Godot.Collections.Dictionary> _GetPropertyList()
    {
        var list = new Godot.Collections.Array<Godot.Collections.Dictionary>();
        PopulatePropertyList(list);
        return list;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        DisposeValues();
    }
}