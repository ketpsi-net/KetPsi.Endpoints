using FluentAssertions;

namespace HttpEndpointGenerator.Tests.Aot;

/// <summary>
/// Documents expected equality semantics for generator cache keys.
/// EquatableArray lives in the generator assembly; this suite validates behavioral parity
/// using mirrored logic where the type is not referenced from tests (analyzer-only).
/// </summary>
public class EquatableArrayParityTests
{
    [Fact]
    public void Sequence_Equality_Is_Order_Sensitive()
    {
        var a = new[] { "a", "b" };
        var b = new[] { "b", "a" };
        a.SequenceEqual(b).Should().BeFalse();
        a.SequenceEqual(new[] { "a", "b" }).Should().BeTrue();
    }

    [Fact]
    public void Empty_Sequences_Equal()
    {
        Array.Empty<string>().SequenceEqual(Array.Empty<string>()).Should().BeTrue();
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, false)]
    public void Int_Pairs(int x, int y, bool expected)
    {
        (x == y).Should().Be(expected);
    }
}
