using Bamen.Core.Dances;

namespace Bamen.Tests;

public class ClipReaderTests
{
    private const string OneScore = """
        {"name":"","title":"Dance","fps":30,"tempo":108,"beatsPerBar":4,
         "tempos":[{"beat":0,"bpm":108}],"meters":[{"beat":0,"beatsPerBar":4,"beatUnit":4}],
         "totalBeats":32,"joints":["pelvis","head"],
         "figures":[{"id":"alice","name":"alice","frames":[[0,1,0,0,1.7,0],[0,1,0.1,0,1.7,0.1]]}],
         "music":{"file":"song.lys"}}
        """;

    [Fact]
    public void AClipIsRead()
    {
        var dance = ShosaClipReader.Parse(OneScore, out string? note);
        Assert.Null(note);
        Assert.NotNull(dance);
        Assert.Equal("alice", dance.Figures.Single().Name);
        Assert.Equal(2, dance.Figures[0].Frames.Count);
        Assert.Equal(1.7f, dance.Figures[0].Frames[0][4]);
        Assert.Equal(32 * 60 / 108.0, dance.TotalSeconds, 6);
        Assert.Equal("song.lys", dance.Music);
        Assert.Equal(1, dance.JointIndex("head"));
    }

    [Fact]
    public void OfSeveralScoresTheUnnamedOneIsShot()
    {
        string named = OneScore.Replace("\"name\":\"\"", "\"name\":\"Idle\"").Replace("\"totalBeats\":32", "\"totalBeats\":8");
        var dance = ShosaClipReader.Parse($"[{named},{OneScore}]", out string? note);
        Assert.Equal(32, dance!.TotalBeats);
        Assert.Contains("2 scores", note);
    }

    [Fact]
    public void AMissingFileIsSaid()
    {
        var load = ShosaClipReader.Read("no-such.shosa", Path.GetTempPath());
        Assert.Null(load.Dance);
        Assert.Contains("no-such.shosa", load.Problem);
    }
}
