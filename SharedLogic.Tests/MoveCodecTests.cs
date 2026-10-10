using SharedLogic;

namespace SharedLogic.Tests;

public class MoveCodecTests
{
    [Fact]
    public void RoundTripsEveryRobotAndDirection()
    {
        var moves = Enum.GetValues<RobotColor>()
            .SelectMany(r => Enum.GetValues<Direction>().Select(d => new Move(r, d)))
            .ToArray();

        byte[] data = MoveCodec.Encode(moves);

        Assert.Equal(16, data.Length);
        Assert.Equal(moves, MoveCodec.Decode(data));
    }

    [Fact]
    public void RejectsBytesThatAreNotMoves()
    {
        Assert.Throws<FormatException>(() => MoveCodec.Decode(new byte[] { 3, 16 }));
    }
}
