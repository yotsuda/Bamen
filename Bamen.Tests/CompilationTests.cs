using Bamen.Core;
using Bamen.Core.Dances;
using Bamen.Core.Syntax;

namespace Bamen.Tests;

public class CompilationTests
{
    private static readonly string[] Joints = ["pelvis", "waist", "chest", "head", "nose", "leftHand", "rightHand", "leftAnkle", "leftToe", "rightAnkle", "rightToe"];

    /// <summary>Three dancers, 32 beats in 4/4 at 120 bpm: a bar is 2 s, the dance 16 s.</summary>
    private static Dance Sample(IReadOnlyList<TempoChange>? tempos = null, IReadOnlyList<MeterChange>? meters = null) =>
        new("Dance", 30, 32, tempos ?? [new TempoChange(0, 120)], meters ?? [new MeterChange(0, 4)], Joints,
            new[] { "alice", "bob", "carol" }.Select(n => new DanceFigure(n, [new float[Joints.Length * 3]])).ToList(), "song.lys");

    private static BamenCompilation Compile(string text, Dance? dance = null) =>
        BamenCompilation.Compile(text, (_, _) => new DanceLoad(dance ?? Sample(), null));

    private static string[] Codes(BamenCompilation c) => c.Diagnostics.Select(d => d.Code).ToArray();

    [Fact]
    public void TheM0SampleHasNoProblems()
    {
        var c = Compile("""
            title "Dance in three shots"
            dance "dance.shosa"   // the dancers, their movement and the song

            shot 1 wide   eye   bars 1-4   look alice bob carol
            shot 2 medium low   bars 5-6   look alice
            shot 3 close        bars 7-8   look alice.face
            """);
        // The camera's (BM2xxx) aside: this dance has every joint on the floor at one point, which no camera frames well.
        Assert.Empty(c.Diagnostics.Where(d => !d.Code.StartsWith("BM2")));
        var shots = c.Bound.Shots;
        Assert.Equal(3, shots.Count);
        Assert.Equal((0, 8), (shots[0].Start, shots[0].End));
        Assert.Equal((8, 12), (shots[1].Start, shots[1].End));
        Assert.Equal((12, 16), (shots[2].Start, shots[2].End));
        Assert.Equal(ShotSize.Medium, shots[1].Size);
        Assert.Equal(ShotAngle.Low, shots[1].Angle);
        Assert.Equal(ShotAngle.Eye, shots[2].Angle);          // not written: eye level
        Assert.Equal(ShotSide.Front, shots[2].Side);          // not written: from the front
        Assert.Equal("alice.face", shots[2].Look.Single().ToString());
        Assert.Equal(2, shots[2].Look.Single().Joints.Count); // head and nose
        Assert.Equal(3, shots[0].Look.Count);
    }

    [Fact]
    public void ClausesComeInAnyOrder()
    {
        var c = Compile("""
            dance "d.shosa"
            shot 1 look bob from back-left bars 1-8 high wide
            """);
        Assert.Empty(c.Diagnostics);
        var shot = c.Bound.Shots.Single();
        Assert.Equal((ShotSize.Wide, ShotAngle.High, ShotSide.BackLeft), (shot.Size, shot.Angle, shot.Side));
        Assert.Equal("bob", shot.Look.Single().ToString());
    }

    [Fact]
    public void BeatsAndSecondsArePlacesInTime()
    {
        var c = Compile("""
            dance "d.shosa"
            shot 1 wide beats 1-4 look alice
            shot 2 wide 2s look alice
            shot 3 wide 4s-16s look alice
            """);
        Assert.Empty(c.Diagnostics);
        var s = c.Bound.Shots;
        Assert.Equal((0, 2), (s[0].Start, s[0].End));
        Assert.Equal((2, 4), (s[1].Start, s[1].End));        // a length follows the shot before
        Assert.Equal((4, 16), (s[2].Start, s[2].End));
    }

    [Fact]
    public void BarsFollowTheTempoAndTheTime()
    {
        // 4/4 at 120 for two bars (beats 0-8), then 3/4 at 60: bar 3 begins at beat 8 = 4 s, a bar of 3/4 at 60 is 3 s.
        var dance = Sample([new TempoChange(0, 120), new TempoChange(8, 60)], [new MeterChange(0, 4), new MeterChange(8, 3)]);
        var c = Compile("""
            dance "d.shosa"
            shot 1 wide bars 1-2 look alice
            shot 2 wide bars 3-4 look alice
            """, dance);
        var s = c.Bound.Shots;
        Assert.Equal((0, 4), (s[0].Start, s[0].End));
        Assert.Equal((4, 10), (s[1].Start, s[1].End));
    }

    [Fact]
    public void GapsOverlapsAndTheEndAreSaid()
    {
        var c = Compile("""
            dance "d.shosa"
            shot 1 wide bars 2-3 look alice
            shot 2 wide bars 5-6 look alice
            shot 3 wide bars 6-9 look alice
            """);
        Assert.Equal(["BM1013", "BM1013", "BM1014", "BM1015"], Codes(c));
    }

    [Fact]
    public void AnUnshotEndIsInformation()
    {
        var c = Compile("""
            dance "d.shosa"
            shot 1 wide bars 1-4 look alice
            """);
        var d = Assert.Single(c.Diagnostics);
        Assert.Equal(("BM1016", DiagnosticSeverity.Info), (d.Code, d.Severity));
        Assert.False(c.HasErrors);
    }

    [Fact]
    public void WhatAShotLacksIsSaid()
    {
        var c = Compile("""
            dance "d.shosa"
            shot 1 bars 1-8
            """);
        Assert.Equal(["BM1004", "BM1006"], Codes(c));
    }

    [Fact]
    public void UnknownDancersAndPartsAreSaid()
    {
        var c = Compile("""
            dance "d.shosa"
            shot 1 wide bars 1-8 look dave alice.elbow alice alice
            """);
        Assert.Equal(["BM1007", "BM1008", "BM1009"], Codes(c));
        Assert.Contains("alice, bob, carol", c.Diagnostics[0].Message);
    }

    [Fact]
    public void NumbersAreUniqueAndInOrder()
    {
        var c = Compile("""
            dance "d.shosa"
            shot 2 wide bars 1-4 look alice
            shot 1 wide bars 5-6 look alice
            shot 1 wide bars 7-8 look alice
            """);
        Assert.Equal(["BM1011", "BM1010"], Codes(c));
    }

    [Fact]
    public void SyntaxMistakesAreSaidAndTheRestIsRead()
    {
        var c = Compile("""
            dance "d.shosa"
            camera 1
            shot 1 wide wide zoom bars 1-8 look alice
            shot x
            title "open
            """);
        Assert.Equal(["BM0001", "BM0003", "BM0006", "BM0002", "BM0004"], Codes(c));
        Assert.Single(c.Bound.Shots);
    }

    [Fact]
    public void LookStopsAtTheNextClause()
    {
        var c = Compile("""
            dance "d.shosa"
            shot 1 look alice bob close bars 1-8 from right
            """);
        Assert.Empty(c.Diagnostics);
        Assert.Equal(2, c.Bound.Shots.Single().Look.Count);
        Assert.Equal(ShotSide.Right, c.Bound.Shots.Single().Side);
    }

    [Fact]
    public void WordsMayBeInAnyScript()
    {
        var dance = new Dance("", 30, 16, [new TempoChange(0, 120)], [new MeterChange(0, 4)], Joints,
            [new DanceFigure("花子", [new float[Joints.Length * 3]])], null);
        var c = Compile("""
            dance "d.shosa"
            shot 1 close bars 1-4 look 花子.face
            """, dance);
        Assert.Empty(c.Diagnostics);
    }

    [Fact]
    public void WithoutADanceLineThereIsNothingToShoot()
    {
        var c = Compile("shot 1 wide 2s look alice\n");
        Assert.Contains("BM1001", Codes(c));
    }

    [Fact]
    public void ADanceThatCannotBeReadIsAnError()
    {
        var c = BamenCompilation.Compile("dance \"missing.shosa\"\n", (_, _) => DanceLoad.Failed("There is no file missing.shosa."));
        var d = Assert.Single(c.Diagnostics);
        Assert.Equal(("BM1002", DiagnosticSeverity.Error), (d.Code, d.Severity));
        Assert.Equal(new TextSpan(6, 15), d.Span);
    }
}
