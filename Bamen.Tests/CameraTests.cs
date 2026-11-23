using System.Text.Json;
using Bamen.Core;
using Bamen.Core.Cameras;
using Bamen.Core.Dances;
using Bamen.Core.Output;

namespace Bamen.Tests;

public class CameraTests
{
    private static readonly string[] Joints =
    [
        "pelvis", "waist", "chest", "head", "nose",
        "leftShoulder", "leftElbow", "leftHand", "rightShoulder", "rightElbow", "rightHand",
        "leftHip", "leftKnee", "leftAnkle", "leftToe", "rightHip", "rightKnee", "rightAnkle", "rightToe",
    ];

    // A 1.7 m figure standing, facing +z, as Shosa's rig has it (x is its left).
    private static readonly double[][] Standing =
    [
        [0, 0.96, 0], [0, 1.21, 0], [0, 1.46, 0], [0, 1.70, 0], [0, 1.70, 0.12],
        [0.19, 1.43, 0], [0.21, 1.15, 0], [0.22, 0.86, 0], [-0.19, 1.43, 0], [-0.21, 1.15, 0], [-0.22, 0.86, 0],
        [0.10, 0.96, 0], [0.10, 0.53, 0], [0.10, 0.08, 0], [0.10, 0, 0.17], [-0.10, 0.96, 0], [-0.10, 0.53, 0], [-0.10, 0.08, 0], [-0.10, 0, 0.17],
    ];

    private static readonly DanceBone[] Bones =
    [
        new(0, 1, 'c'), new(1, 2, 'c'), new(2, 3, 'c'), new(3, 4, 'c'), new(2, 5, 'c'), new(5, 6, 'l'), new(6, 7, 'l'),
        new(2, 8, 'c'), new(8, 9, 'r'), new(9, 10, 'r'), new(0, 11, 'l'), new(11, 12, 'l'), new(12, 13, 'l'), new(13, 14, 'l'),
        new(0, 15, 'r'), new(15, 16, 'r'), new(16, 17, 'r'), new(17, 18, 'r'),
    ];

    /// <summary>The standing figure at (x, z), turned <paramref name="degrees"/> from +z toward +x.</summary>
    private static float[] Pose(double x, double z, double degrees)
    {
        double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        var frame = new float[Joints.Length * 3];
        for (int j = 0; j < Joints.Length; j++)
        {
            var (px, py, pz) = (Standing[j][0], Standing[j][1], Standing[j][2]);
            frame[j * 3] = (float)(x + px * c + pz * s);
            frame[j * 3 + 1] = (float)py;
            frame[j * 3 + 2] = (float)(z - px * s + pz * c);
        }
        return frame;
    }

    /// <summary>16 beats at 120 bpm (8 s) at 10 frames a second; each dancer's frames from a function of the frame.</summary>
    private static Dance Dance(params (string Name, Func<int, float[]> At)[] figures) =>
        new("", 10, 16, [new TempoChange(0, 120)], [new MeterChange(0, 4)], Joints,
            figures.Select(f => new DanceFigure(f.Name, Enumerable.Range(0, 81).Select(f.At).ToList())).ToList(), null, Bones);

    private static BamenCompilation Compile(string shots, Dance dance) =>
        BamenCompilation.Compile("dance \"d.shosa\"\n" + shots, (_, _) => new DanceLoad(dance, null));

    /// <summary>The furthest any joint of the looked-at dancers gets from the middle of the picture over the shot, along x and along y (1 is the edge).</summary>
    private static (double X, double Y) Extent(SolvedCamera camera, Dance dance)
    {
        double ex = 0, ey = 0;
        foreach (var figure in dance.Figures.Where(f => camera.Shot.Look.Any(t => t.Figure == f.Name)))
        {
            for (int f = camera.FirstFrame; f <= camera.LastFrame; f++)
            {
                for (int j = 0; j < Joints.Length; j++)
                {
                    var (x, y, depth) = camera.Project(figure.Joint(f, j));
                    Assert.True(depth > 0);
                    ex = Math.Max(ex, Math.Abs(x));
                    ey = Math.Max(ey, Math.Abs(y));
                }
            }
        }
        return (ex, ey);
    }

    [Fact]
    public void AWideShotFramesTheWholeFigureFromItsFront()
    {
        var dance = Dance(("alice", _ => Pose(0, 0, 0)));
        var c = Compile("shot 1 wide 8s look alice\n", dance);
        var camera = Assert.Single(c.Cameras);
        Assert.True(camera.Position.Z > 2, $"in front: {camera.Position}");
        Assert.Equal(0, camera.Position.X, 3);
        Assert.Equal(0, camera.Pitch);
        Assert.Equal(35, camera.FocalLength);
        var (x, y) = Extent(camera, dance);
        Assert.InRange(y, 0.7, 0.8);   // the height binds: the figure takes 80 % of the picture, head and feet included
        Assert.True(x < y);
    }

    [Fact]
    public void TheSideIsTheSubjectsOwn()
    {
        // alice faces +x: her front is +x, her left -z, her back -x.
        var dance = Dance(("alice", _ => Pose(0, 0, 90)));
        var c = Compile("""
            shot 1 wide 2s look alice
            shot 2 wide 2s look alice from left
            shot 3 wide 2s look alice from back
            shot 4 wide 2s look alice from front-right
            """, dance);
        var p = c.Cameras.Select(k => k.Position).ToArray();
        Assert.True(p[0].X > 2 && Math.Abs(p[0].Z) < 0.01, $"front: {p[0]}");
        Assert.True(p[1].Z < -2 && Math.Abs(p[1].X) < 0.2, $"left: {p[1]}");   // the nose and toes stick out in front: the middle is a little forward
        Assert.True(p[2].X < -2, $"back: {p[2]}");
        Assert.True(p[3].X > 1 && p[3].Z > 1, $"front-right: {p[3]}");
    }

    [Fact]
    public void AMovingSubjectStaysInThePictureAllThrough()
    {
        // alice walks from x = -2 to x = 2 over the 8 s, facing the audience.
        var dance = Dance(("alice", f => Pose(-2 + 4 * f / 80.0, 0, 0)));
        var c = Compile("shot 1 medium 8s look alice\n", dance);
        var camera = Assert.Single(c.Cameras);
        var (x, _) = Extent(camera, dance);
        Assert.InRange(x, 0.8, 0.95);   // the walk binds, across
        Assert.Equal(0, camera.Position.X, 1);   // the middle of the walk (to the last frame before 8 s)
    }

    [Fact]
    public void AMediumShotStandsTheWaistOnTheBottomEdge()
    {
        var dance = Dance(("alice", _ => Pose(0, 0, 0)));
        var camera = Assert.Single(Compile("shot 1 medium 8s look alice\n", dance).Cameras);
        Assert.Equal(50, camera.FocalLength);
        var waist = camera.Project(dance.Figures[0].Joint(0, 1)).Y;
        var head = camera.Project(dance.Figures[0].Joint(0, 3)).Y;
        var knee = camera.Project(dance.Figures[0].Joint(0, 12)).Y;
        Assert.InRange(waist, -1, -0.7);
        Assert.InRange(head, 0.3, 1);
        Assert.True(knee < -1, "the knee is below the picture");
    }

    [Fact]
    public void ACloseShotOfTheFaceFillsThePicture()
    {
        var dance = Dance(("alice", _ => Pose(0, 0, 0)));
        var camera = Assert.Single(Compile("shot 1 close 8s look alice.face\n", dance).Cameras);
        Assert.Equal(85, camera.FocalLength);
        var head = camera.Project(dance.Figures[0].Joint(0, 3));
        var chest = camera.Project(dance.Figures[0].Joint(0, 2));
        Assert.InRange(head.Y, -0.3, 0.3);
        Assert.True(chest.Y < -1, "the chest is below the picture");
        Assert.InRange(head.Depth, 0.5, 2);
    }

    [Fact]
    public void LowLooksUpAndHighLooksDown()
    {
        var dance = Dance(("alice", _ => Pose(0, 0, 0)));
        var c = Compile("""
            shot 1 medium low 4s look alice
            shot 2 medium high 4s look alice
            """, dance);
        Assert.Equal(20, c.Cameras[0].Pitch);
        Assert.Equal(-30, c.Cameras[1].Pitch);
        Assert.True(c.Cameras[0].Position.Y < 1.2 && c.Cameras[1].Position.Y > 1.8, $"{c.Cameras[0].Position} {c.Cameras[1].Position}");
        Assert.Empty(c.Diagnostics);
    }

    [Fact]
    public void ALowWideShotTiltsLessRatherThanGoUnderTheFloor()
    {
        var dance = Dance(("alice", _ => Pose(0, 0, 0)));
        var c = Compile("shot 1 wide low 8s look alice\n", dance);
        var camera = Assert.Single(c.Cameras);
        Assert.True(camera.Position.Y >= CameraSolver.LowestCamera - 1e-6);
        Assert.InRange(camera.Pitch, 0.1, 19.9);
        Assert.Equal("BM2001", Assert.Single(c.Diagnostics).Code);
        Extent(camera, dance);
    }

    [Fact]
    public void ASubjectThatSpinsHasTheAudienceAsItsFront()
    {
        var dance = Dance(("alice", f => Pose(0, 0, f * 360.0 / 40)));   // round twice
        var c = Compile("shot 1 wide 8s look alice from left\n", dance);
        Assert.Equal("BM2002", Assert.Single(c.Diagnostics).Code);
        var camera = Assert.Single(c.Cameras);
        Assert.True(camera.Position.X > 2, $"the audience's front is +z, so its left is +x: {camera.Position}");
    }

    [Fact]
    public void AGroupIsFramedTogether()
    {
        var dance = Dance(("alice", _ => Pose(0, 1, 0)), ("bob", _ => Pose(-1.5, 0, 0)), ("carol", _ => Pose(1.5, 0, 0)));
        var c = Compile("shot 1 wide 8s look alice bob carol\n", dance);
        var camera = Assert.Single(c.Cameras);
        var (x, y) = Extent(camera, dance);
        Assert.True(Math.Max(x, y) is > 0.7 and < 0.81, $"{x} {y}");
    }

    [Fact]
    public void AShotEndsTheFrameBeforeTheNextBegins()
    {
        var dance = Dance(("alice", _ => Pose(0, 0, 0)));
        var c = Compile("""
            shot 1 wide 2.25s look alice
            shot 2 wide 2s look alice
            """, dance);
        Assert.Equal((0, 22), (c.Cameras[0].FirstFrame, c.Cameras[0].LastFrame));
        Assert.Equal((23, 42), (c.Cameras[1].FirstFrame, c.Cameras[1].LastFrame));
    }

    [Fact]
    public void CamerasAreWrittenAsJson()
    {
        var dance = Dance(("alice", _ => Pose(0, 0, 0)));
        var c = BamenCompilation.Compile("title \"T\"\ndance \"d.shosa\"\nshot 1 wide 4s look alice\nshot 2 close 4s look alice.face from left\n",
            (_, _) => new DanceLoad(dance, null));
        using var json = JsonDocument.Parse(CamerasJson.Write(c));
        var shots = json.RootElement.GetProperty("shots");
        Assert.Equal(2, shots.GetArrayLength());
        Assert.Equal("left", shots[1].GetProperty("from").GetString());
        Assert.Equal("alice.face", shots[1].GetProperty("look")[0].GetString());
        Assert.Equal(85, shots[1].GetProperty("camera").GetProperty("focalLength").GetDouble());
        Assert.Equal(3, shots[0].GetProperty("camera").GetProperty("position").GetArrayLength());
    }

    [Fact]
    public void TheSheetHasARowForEveryShot()
    {
        var dance = Dance(("alice", _ => Pose(0, 0, 0)), ("bob", _ => Pose(1, -1, 0)));
        var one = StoryboardSheet.Render(Compile("shot 1 wide 4s look alice\n", dance));
        var two = StoryboardSheet.Render(Compile("shot 1 wide 4s look alice\nshot 2 close 4s look alice.face\n", dance));
        Assert.Equal(one.Width, two.Width);
        Assert.True(two.Height > one.Height);
        var png = two.ToPng();
        Assert.Equal(0x89, png[0]);
        Assert.Contains(Enumerable.Range(0, one.Width).SelectMany(x => Enumerable.Range(0, one.Height).Select(y => one.ColorAt(x, y))), color => color == 0x2F6FDF);
    }
}
