using System.Numerics;

namespace Content.Client._KS14.Rift;

[RegisterComponent]
public sealed partial class KsRiftComponent : Component
{
    [DataField]
    public Box2 BoundingBox = new(-32f, -32f, 32f, 32f);

    [DataField]
    public Vector2 RenderOffset = new(0, -5);

    [DataField]
    public Angle RotationOffset = new();
}
