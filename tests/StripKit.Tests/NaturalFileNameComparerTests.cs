using System.Linq;
using FluentAssertions;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// The natural-order comparer that sequences a render folder. The point of the engine is that
/// <c>frame_2</c> sorts before <c>frame_10</c> — an ordinal sort gets this wrong.
/// </summary>
public class NaturalFileNameComparerTests
{
    [Theory]
    [InlineData("frame_2.png", "frame_10.png", -1)]
    [InlineData("frame_10.png", "frame_9.png", 1)]
    [InlineData("a.png", "a.png", 0)]
    [InlineData("knob_0001.png", "knob_0002.png", -1)]
    [InlineData("knob_02.png", "knob_2.png", 0)]   // leading zeros compare equal numerically
    public void Compares_numbered_names_numerically(string a, string b, int expectedSign)
    {
        System.Math.Sign(NaturalFileNameComparer.Instance.Compare(a, b)).Should().Be(expectedSign);
    }

    [Fact]
    public void Sorts_an_unpadded_sequence_into_render_order()
    {
        var input = new[] { "f_10.png", "f_2.png", "f_1.png", "f_20.png", "f_3.png" };
        var sorted = input.OrderBy(x => x, NaturalFileNameComparer.Instance).ToArray();
        sorted.Should().Equal("f_1.png", "f_2.png", "f_3.png", "f_10.png", "f_20.png");
    }

    [Fact]
    public void Falls_back_to_case_insensitive_text_for_non_numeric_names()
    {
        var input = new[] { "beta.png", "Alpha.png", "gamma.png" };
        var sorted = input.OrderBy(x => x, NaturalFileNameComparer.Instance).ToArray();
        sorted.Should().Equal("Alpha.png", "beta.png", "gamma.png");
    }
}
