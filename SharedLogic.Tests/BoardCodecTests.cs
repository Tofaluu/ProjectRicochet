using SharedLogic;

namespace SharedLogic.Tests;

public class BoardCodecTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(99)]
    [InlineData(2026)]
    public void RoundTripsAGeneratedRound(int seed)
    {
        RoundSetup original = BoardGenerator.GenerateRound(new Random(seed));

        byte[] data = BoardCodec.Encode(original);
        RoundSetup decoded = BoardCodec.Decode(data);

        Assert.Equal(128, data.Length);
        Assert.Equal(original.Grid.Width, decoded.Grid.Width);
        Assert.Equal(original.Grid.Height, decoded.Grid.Height);
        for (int x = 0; x < original.Grid.Width; x++)
            for (int y = 0; y < original.Grid.Height; y++)
                Assert.Equal(original.Grid.GetWalls(new GridPos(x, y)), decoded.Grid.GetWalls(new GridPos(x, y)));

        Assert.Equal(original.Grid.Targets, decoded.Grid.Targets);
        Assert.Equal(original.Robots, decoded.Robots);
        Assert.Equal(original.TargetIndex, decoded.TargetIndex);
    }

    [Fact]
    public void RejectsTruncatedData()
    {
        byte[] data = BoardCodec.Encode(BoardGenerator.GenerateRound(new Random(1)));
        Assert.Throws<FormatException>(() => BoardCodec.Decode(data.Take(data.Length - 1).ToArray()));
        Assert.Throws<FormatException>(() => BoardCodec.Decode(Array.Empty<byte>()));
    }

    [Fact]
    public void RejectsUnknownVersionAndTrailingBytes()
    {
        byte[] data = BoardCodec.Encode(BoardGenerator.GenerateRound(new Random(1)));

        byte[] wrongVersion = (byte[])data.Clone();
        wrongVersion[0] = 99;
        Assert.Throws<FormatException>(() => BoardCodec.Decode(wrongVersion));

        Assert.Throws<FormatException>(() => BoardCodec.Decode(data.Append((byte)0).ToArray()));
    }

    [Fact]
    public void RejectsRobotOutsideTheBoard()
    {
        byte[] data = BoardCodec.Encode(BoardGenerator.GenerateRound(new Random(1)));
        data[data.Length - 1 - 8] = 200; // Red's x
        Assert.Throws<FormatException>(() => BoardCodec.Decode(data));
    }
}
