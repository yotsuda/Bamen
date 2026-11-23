namespace Bamen.Core.Cameras;

/// <summary>A point or direction in the dance's world, in metres: y up, the audience toward +z (as Shosa has it).</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 Up = new(0, 1, 0);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public Vec3 Cross(Vec3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public double Length => Math.Sqrt(Dot(this));
    public Vec3 Normalized() => Length < 1e-12 ? Zero : this * (1 / Length);

    /// <summary>On the floor: y taken out.</summary>
    public Vec3 Flat => new(X, 0, Z);

    // Not the record's own: it would print Flat, whose Flat it would print, and so on.
    public override string ToString() => FormattableString.Invariant($"({X:0.###}, {Y:0.###}, {Z:0.###})");
}
