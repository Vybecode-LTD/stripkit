using System.Collections.Generic;
using FluentAssertions;
using SkiaSharp;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Golden-image lock for the assembler's packing of real (non-solid) art, so the cell placement and
/// transparent clear stay byte-stable. Regenerate intentionally with <c>UPDATE_BASELINES=1</c>.
/// </summary>
public class FrameSequenceAssemblerGoldenTests
{
    readonly FrameSequenceAssembler _assembler = new(new FilmstripImporter());

    [Fact]
    public void Assembles_real_art_into_a_locked_vertical_strip()
    {
        var frames = new List<SKBitmap>
        {
            TestImages.Knob(40), TestImages.KnobBody(40), TestImages.Pointer(40), TestImages.Knob(40),
        };
        try
        {
            var result = _assembler.Assemble(frames, new FrameSequenceOptions { Direction = StackDirection.Vertical });
            using (result.Strip)
            {
                result.Strip.Width.Should().Be(40);
                result.Strip.Height.Should().Be(160);
                ImageAssert.MatchesBaseline(result.Strip, "assemble_knob_mix_4");
            }
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }
}
