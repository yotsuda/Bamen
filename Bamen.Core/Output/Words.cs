using Bamen.Core.Syntax;

namespace Bamen.Core.Output;

/// <summary>The size, angle and side of a shot as the language writes them.</summary>
public static class Words
{
    public static string Of(ShotSize size) => size.ToString().ToLowerInvariant();

    public static string Of(ShotAngle angle) => angle.ToString().ToLowerInvariant();

    public static string Of(ShotSide side) => side switch
    {
        ShotSide.FrontLeft => "front-left",
        ShotSide.FrontRight => "front-right",
        ShotSide.BackLeft => "back-left",
        ShotSide.BackRight => "back-right",
        _ => side.ToString().ToLowerInvariant(),
    };
}
