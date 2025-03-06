using System;
using Godot;

namespace GDModelExporter.Examples;

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