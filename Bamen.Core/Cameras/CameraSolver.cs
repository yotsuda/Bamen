using System.Globalization;
using Bamen.Core.Dances;
using Bamen.Core.Semantics;
using Bamen.Core.Syntax;

namespace Bamen.Core.Cameras;

/// <summary>
/// A shot's camera, solved: where it stands, which way it looks and through what lens. Fixed for the
/// whole shot in M0. <see cref="Right"/>, <see cref="Up"/> and <see cref="Forward"/> are the camera's
/// own directions (it looks along Forward); the picture is 16:9.
/// </summary>
public sealed record SolvedCamera(
    BoundShot Shot,
    Vec3 Position,
    Vec3 Forward,
    Vec3 Up,
    Vec3 Right,
    double FocalLength,
    double Pitch,
    Vec3 Facing,
    Vec3 Aim,
    int FirstFrame,
    int LastFrame)
{
    /// <summary>The picture is 16:9: a full-frame sensor (36 mm wide) cut to 36 x 20.25 mm.</summary>
    public const double SensorWidth = 36, SensorHeight = 20.25, Aspect = SensorWidth / SensorHeight;

    public double HorizontalFov => 2 * Math.Atan(SensorWidth / 2 / FocalLength) * 180 / Math.PI;
    public double VerticalFov => 2 * Math.Atan(SensorHeight / 2 / FocalLength) * 180 / Math.PI;

    /// <summary>
    /// Where a point falls in the picture: x from -1 (left edge) to 1 (right), y from -1 (bottom) to 1
    /// (top), and its depth in front of the camera in metres (not in the picture when 0 or less).
    /// </summary>
    public (double X, double Y, double Depth) Project(Vec3 p)
    {
        var d = p - Position;
        double depth = d.Dot(Forward);
        if (depth <= 1e-6)
            return (0, 0, depth);
        return (d.Dot(Right) / depth * (FocalLength / (SensorWidth / 2)), d.Dot(Up) / depth * (FocalLength / (SensorHeight / 2)), depth);
    }
}

/// <summary>
/// Solves the fixed camera of each shot (DESIGN.md 3.2, points 4 to 6; the rules in 3.4): from the side
/// it is asked for, in the subject's own directions, at the angle asked for, as close as the lens of the
/// size allows with everything the size frames inside the picture at every frame of the shot.
/// </summary>
public static class CameraSolver
{
    /// <summary>The lens of each size, in mm (35 mm full frame).</summary>
    public static double FocalLength(ShotSize size) => size switch { ShotSize.Wide => 35, ShotSize.Medium => 50, _ => 85 };

    /// <summary>Degrees the camera tilts up (low) or down (high) from level; eye is level.</summary>
    public static double Pitch(ShotAngle angle) => angle switch { ShotAngle.Low => 20, ShotAngle.High => -30, _ => 0 };

    /// <summary>How much of the half picture, from its middle to an edge, what is framed may take.</summary>
    private static double Fill(ShotSize size) => size == ShotSize.Wide ? 0.8 : 0.92;

    /// <summary>Lowest a camera stands, in metres above the floor; a low angle tilts less rather than go under it.</summary>
    public const double LowestCamera = 0.2;

    /// <summary>Nearest a framed point may be to the camera, in metres.</summary>
    private const double Nearest = 0.3;

    /// <summary>
    /// A subject that faces every way in turn during the shot has no front: the camera takes the
    /// audience's side (+z). Below this share of one direction, it is so.
    /// </summary>
    private const double Steady = 0.35;

    private static string N(double value) => Math.Round(value, 1).ToString(CultureInfo.InvariantCulture);

    public static List<SolvedCamera> Solve(BoundFile bound, List<Diagnostic> diagnostics)
    {
        var cameras = new List<SolvedCamera>();
        if (bound.Dance is not { } dance)
            return cameras;
        foreach (var shot in bound.Shots)
        {
            if (shot.Look.Count == 0)
                continue;
            cameras.Add(SolveShot(shot, dance, diagnostics));
        }
        return cameras;
    }

    public static SolvedCamera SolveShot(BoundShot shot, Dance dance, List<Diagnostic> diagnostics)
    {
        var joints = new JointNames(dance);
        var (first, last) = dance.FramesOf(shot.Start, shot.End);
        var figures = shot.Look.Select(t => t.Figure).Distinct().Select(name => dance.Figures.First(f => f.Name == name)).ToList();

        // Which way the subject faces over the shot: the mean of every frame's.
        var sum = Vec3.Zero;
        int counted = 0;
        foreach (var figure in figures)
        {
            for (int f = first; f <= last; f++)
            {
                if (Facing(figure, f, joints) is { } facing)
                {
                    sum += facing;
                    counted++;
                }
            }
        }
        var front = counted > 0 && sum.Length / counted >= Steady ? sum.Normalized() : new Vec3(0, 0, 1);
        if (counted > 0 && sum.Length / counted < Steady)
            diagnostics.Add(new Diagnostic("BM2002", DiagnosticSeverity.Info, shot.Syntax.Number.Span,
                $"In shot {shot.Number} {Subject(shot)} faces every way in turn: its front is taken as the audience's side."));
        var left = Vec3.Up.Cross(front);
        double side = SideDegrees(shot.Side) * Math.PI / 180;
        var towardCamera = front * Math.Cos(side) + left * Math.Sin(side);

        var points = new List<(Vec3 P, double R)>();
        foreach (var target in shot.Look)
        {
            var figure = dance.Figures.First(f => f.Name == target.Figure);
            for (int f = first; f <= last; f++)
                Framed(points, figure, f, target, shot.Size, joints);
        }

        double pitch = Pitch(shot.Angle), lens = FocalLength(shot.Size);
        var camera = Place(shot, -towardCamera, pitch, lens, points, front, first, last);
        if (camera.Position.Y < LowestCamera && pitch > 0)
        {
            // Tilted up, the camera went under the floor to frame the feet: tilt it less, as much as it can.
            double low = 0, high = pitch;
            for (int i = 0; i < 30; i++)
            {
                double mid = (low + high) / 2;
                if (Place(shot, -towardCamera, mid, lens, points, front, first, last).Position.Y >= LowestCamera)
                    low = mid;
                else
                    high = mid;
            }
            camera = Place(shot, -towardCamera, low, lens, points, front, first, last);
            diagnostics.Add(new Diagnostic("BM2001", DiagnosticSeverity.Info, shot.Syntax.Angle?.Span ?? shot.Syntax.Number.Span,
                $"Shot {shot.Number} looks up {N(low)}°, not {N(pitch)}°: any lower and the camera would be under the floor."));
        }
        return camera;
    }

    private static string Subject(BoundShot shot) => string.Join(" and ", shot.Look.Select(t => t.Figure).Distinct());

    /// <summary>Degrees round from the subject's front toward its left.</summary>
    public static double SideDegrees(ShotSide side) => side switch
    {
        ShotSide.FrontLeft => 45,
        ShotSide.Left => 90,
        ShotSide.BackLeft => 135,
        ShotSide.Back => 180,
        ShotSide.BackRight => -135,
        ShotSide.Right => -90,
        ShotSide.FrontRight => -45,
        _ => 0,
    };

    /// <summary>
    /// The camera looking along <paramref name="horizontal"/> tilted by <paramref name="pitch"/>, as close
    /// as it can be with every point inside the picture. Along each of the picture's axes the camera's
    /// distance has a least value where the points just fit; it takes the larger, and centres the points
    /// along the other axis — except that a medium or a close shot of a whole figure stands its lowest
    /// point (the waist, the chest) on the bottom edge, as the size says.
    /// </summary>
    private static SolvedCamera Place(BoundShot shot, Vec3 horizontal, double pitch, double lens, List<(Vec3 P, double R)> points,
        Vec3 facing, int first, int last)
    {
        double p = pitch * Math.PI / 180;
        var forward = (horizontal.Flat.Normalized() * Math.Cos(p) + Vec3.Up * Math.Sin(p)).Normalized();
        var right = forward.Cross(Vec3.Up).Normalized();
        var up = right.Cross(forward);

        double fill = Fill(shot.Size);
        double tx = SolvedCamera.SensorWidth / 2 / lens * fill, ty = SolvedCamera.SensorHeight / 2 / lens * fill;

        double xHigh = double.MinValue, xLow = double.MaxValue, yHigh = double.MinValue, yLow = double.MaxValue, near = double.MinValue;
        foreach (var (point, r) in points)
        {
            double px = point.Dot(right), py = point.Dot(up), pz = point.Dot(forward);
            xHigh = Math.Max(xHigh, px + r - pz * tx);
            xLow = Math.Min(xLow, px - r + pz * tx);
            yHigh = Math.Max(yHigh, py + r - pz * ty);
            yLow = Math.Min(yLow, py - r + pz * ty);
            near = Math.Max(near, Nearest + r - pz);
        }
        // The camera at a·right + b·up − d·forward: a point is inside when |x| ≤ z·tx and |y| ≤ z·ty, z = pz + d.
        double d = Math.Max(Math.Max((xHigh - xLow) / (2 * tx), (yHigh - yLow) / (2 * ty)), near);
        double a = ((xHigh - d * tx) + (xLow + d * tx)) / 2;
        double bLow = yHigh - d * ty, bHigh = yLow + d * ty;
        bool standOnBottom = shot.Size != ShotSize.Wide && shot.Look.Any(t => t.Part is null);
        double b = standOnBottom ? bHigh : (bLow + bHigh) / 2;

        var position = right * a + up * b - forward * d;
        // The point it aims at: straight ahead, as far as the middle of what is framed.
        var centre = points.Aggregate(Vec3.Zero, (s, x) => s + x.P) * (1.0 / points.Count);
        var aim = position + forward * Math.Max(Nearest, (centre - position).Dot(forward));
        return new SolvedCamera(shot, position, forward, up, right, lens, pitch, facing, aim, first, last);
    }

    /// <summary>Which way a figure faces at a frame, on the floor: square to its hips and shoulders (to its nose when it has none).</summary>
    private static Vec3? Facing(DanceFigure figure, int frame, JointNames j)
    {
        var across = Vec3.Zero;
        if (j.LeftHip >= 0 && j.RightHip >= 0)
            across += figure.Joint(frame, j.LeftHip) - figure.Joint(frame, j.RightHip);
        if (j.LeftShoulder >= 0 && j.RightShoulder >= 0)
            across += figure.Joint(frame, j.LeftShoulder) - figure.Joint(frame, j.RightShoulder);
        var facing = across.Flat.Cross(Vec3.Up);
        if (facing.Length < 1e-6 && j.Head >= 0 && j.Nose >= 0)
            facing = (figure.Joint(frame, j.Nose) - figure.Joint(frame, j.Head)).Flat;
        return facing.Length < 1e-6 ? null : facing.Normalized();
    }

    /// <summary>Radius of the head about its joint, and of a limb about its line, in metres.</summary>
    private const double HeadRadius = 0.12, Limb = 0.06;

    /// <summary>
    /// What a size frames of a target at a frame (DESIGN.md 3.2, point 4), as points with the room each
    /// needs about it. A whole figure: wide, all of it; medium, from the waist up; close, from the chest
    /// up. A part (alice.face): close, the part filling the picture; medium and wide, more room about it.
    /// </summary>
    private static void Framed(List<(Vec3, double)> points, DanceFigure figure, int frame, BoundTarget target, ShotSize size, JointNames j)
    {
        double Room(int joint) => joint == j.Head ? HeadRadius : Limb;
        if (target.Part is { } part)
        {
            double scale = size switch { ShotSize.Wide => 6, ShotSize.Medium => 2.5, _ => 1 };
            double room = (part switch { "face" or "head" => 0.15, "chest" or "hips" => 0.25, _ => 0.12 }) * scale;
            foreach (int joint in target.Joints)
                points.Add((figure.Joint(frame, joint), room));
            return;
        }
        if (size == ShotSize.Wide || j.Waist < 0 || j.Chest < 0)
        {
            for (int joint = 0; joint < j.Count; joint++)
                points.Add((figure.Joint(frame, joint), Room(joint)));
            return;
        }
        // Medium: the waist is the bottom edge, so nothing below it counts (a hand hanging lower is cut).
        // Close: the bottom edge is the chest, and the hands are out of it.
        int bottom = size == ShotSize.Medium ? j.Waist : j.Chest;
        double floor = figure.Joint(frame, bottom).Y;
        var upper = size == ShotSize.Medium ? j.UpperBody : j.HeadAndShoulders;
        foreach (int joint in upper)
        {
            var at = figure.Joint(frame, joint);
            points.Add((at with { Y = Math.Max(at.Y, floor) }, Room(joint)));
        }
    }

    /// <summary>The joints the solver needs, found by name in the clip (-1 when it has none).</summary>
    private sealed class JointNames
    {
        public JointNames(Dance dance)
        {
            int At(string name) => dance.JointIndex(name);
            Count = dance.Joints.Count;
            Waist = At("waist");
            Chest = At("chest");
            Head = At("head");
            Nose = At("nose");
            LeftHip = At("leftHip");
            RightHip = At("rightHip");
            LeftShoulder = At("leftShoulder");
            RightShoulder = At("rightShoulder");
            UpperBody = new[] { "waist", "chest", "head", "nose", "leftShoulder", "rightShoulder", "leftElbow", "rightElbow", "leftHand", "rightHand" }
                .Select(At).Where(i => i >= 0).ToArray();
            HeadAndShoulders = new[] { "chest", "head", "nose", "leftShoulder", "rightShoulder" }.Select(At).Where(i => i >= 0).ToArray();
        }

        public int Count { get; }
        public int Waist { get; }
        public int Chest { get; }
        public int Head { get; }
        public int Nose { get; }
        public int LeftHip { get; }
        public int RightHip { get; }
        public int LeftShoulder { get; }
        public int RightShoulder { get; }
        public int[] UpperBody { get; }
        public int[] HeadAndShoulders { get; }
    }
}
