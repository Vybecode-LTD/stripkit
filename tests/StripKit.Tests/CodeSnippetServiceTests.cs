using FluentAssertions;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Code-snippet generation: each target emits the right control class / draw method for the
/// component type, the universal frame-selection math, the correct source axis for the stack
/// direction, and sanitised identifiers. Pure string assertions — fast and deterministic.
/// </summary>
public class CodeSnippetServiceTests
{
    readonly CodeSnippetService _svc = new();

    static CodeSnippetRequest Req(
        ComponentType type = ComponentType.RotaryKnob,
        StackDirection stack = StackDirection.Vertical,
        string id = "filterCutoff",
        string param = "filterCutoff",
        string? asset2x = null,
        int frames = 64) =>
        new(type, frames, 80, 80, stack, $"{id}_{frames}frames.png", asset2x, id, param);

    // ---- JUCE ----

    [Fact]
    public void Juce_knob_emits_a_rotary_lookandfeel()
    {
        var code = _svc.Generate(CodeTarget.Juce, Req());
        code.Should().Contain("class FilterCutoffLookAndFeel : public juce::LookAndFeel_V4");
        code.Should().Contain("void drawRotarySlider");
        code.Should().Contain("BinaryData::filterCutoff_64frames_png");
        code.Should().Contain("(int) std::lround (sliderPosProportional * (frames - 1))");
        code.Should().Contain("const int frames = 64;");
    }

    [Fact]
    public void Juce_fader_emits_a_linear_lookandfeel()
    {
        var code = _svc.Generate(CodeTarget.Juce, Req(ComponentType.VerticalFader));
        code.Should().Contain("void drawLinearSlider");
        code.Should().Contain("valueToProportionOfLength");
        code.Should().NotContain("drawRotarySlider");
    }

    [Fact]
    public void Juce_meter_emits_a_component_with_setLevel()
    {
        var code = _svc.Generate(CodeTarget.Juce, Req(ComponentType.Meter));
        code.Should().Contain("class FilterCutoffMeter : public juce::Component");
        code.Should().Contain("void setLevel (float newLevel");
    }

    [Fact]
    public void Juce_source_axis_follows_the_stack_direction()
    {
        _svc.Generate(CodeTarget.Juce, Req(stack: StackDirection.Vertical))
            .Should().Contain("0, frame * frameH, frameW, frameH");
        _svc.Generate(CodeTarget.Juce, Req(stack: StackDirection.Horizontal))
            .Should().Contain("frame * frameW, 0, frameW, frameH");
    }

    // ---- CSS / HTML ----

    [Fact]
    public void Css_emits_html_with_a_value_setter()
    {
        var code = _svc.Generate(CodeTarget.Css, Req());
        code.Should().Contain("<style>");
        code.Should().Contain(".filtercutoff {");   // camelCase id lowercases (no separator → no hyphen)
        code.Should().Contain("background-image: url(\"filterCutoff_64frames.png\")");
        code.Should().Contain("function setFilterCutoff(el, value)");
        code.Should().Contain("Math.round(value * (frames - 1))");
    }

    [Fact]
    public void Css_axis_and_hidpi_follow_inputs()
    {
        _svc.Generate(CodeTarget.Css, Req(stack: StackDirection.Vertical))
            .Should().Contain("background-position: 0 calc(var(--frame, 0) * -80px)");
        _svc.Generate(CodeTarget.Css, Req(stack: StackDirection.Horizontal))
            .Should().Contain("background-position: calc(var(--frame, 0) * -80px) 0");

        var noHidpi = _svc.Generate(CodeTarget.Css, Req(asset2x: null));
        noHidpi.Should().NotContain("min-resolution");
        var withHidpi = _svc.Generate(CodeTarget.Css, Req(asset2x: "filterCutoff_64frames@2x.png"));
        withHidpi.Should().Contain("filterCutoff_64frames@2x.png");
        withHidpi.Should().Contain("background-size: 80px 5120px"); // 80 × (64·80)
    }

    // ---- iPlug2 ----

    [Fact]
    public void IPlug2_knob_emits_an_IBKnobControl()
    {
        var code = _svc.Generate(CodeTarget.IPlug2, Req());
        code.Should().Contain("LoadBitmap(FILTERCUTOFF_FN, 64 /* nStates */, false /* framesAreHorizontal */)");
        code.Should().Contain("new IBKnobControl(bounds, filterCutoffBitmap, kFilterCutoff)");
    }

    [Fact]
    public void IPlug2_fader_emits_an_IBSliderControl_with_direction()
    {
        _svc.Generate(CodeTarget.IPlug2, Req(ComponentType.VerticalFader))
            .Should().Contain("new IBSliderControl(bounds, filterCutoffBitmap, kFilterCutoff, EDirection::Vertical)");
        _svc.Generate(CodeTarget.IPlug2, Req(ComponentType.HorizontalSlider, StackDirection.Horizontal))
            .Should().Contain("new IBSliderControl(bounds, filterCutoffBitmap, kFilterCutoff, EDirection::Horizontal)");
    }

    // ---- HISE ----

    [Fact]
    public void Hise_emits_a_scriptpanel_paint_routine()
    {
        var code = _svc.Generate(CodeTarget.Hise, Req());
        code.Should().Contain("Content.addPanel(\"filterCutoff\"");
        code.Should().Contain(".loadImage(\"{PROJECT_FOLDER}filterCutoff_64frames.png\", \"filmstrip\")");
        code.Should().Contain("setPaintRoutine");
        code.Should().Contain("Math.round(v * (FILTERCUTOFF_FRAMES - 1))");
        code.Should().Contain("g.drawImage(\"filmstrip\", [0, 0, 80, 80], 0, frame * 80)");
    }

    // ---- identifiers / file names / I/O ----

    [Fact]
    public void Identifiers_are_sanitised()
    {
        var req = Req(id: "Filter Cutoff!", param: "filter cutoff");
        var juce = _svc.Generate(CodeTarget.Juce, req);
        juce.Should().Contain("class FilterCutoffLookAndFeel");
        _svc.Generate(CodeTarget.Css, req).Should().Contain(".filter-cutoff {");
        _svc.Generate(CodeTarget.IPlug2, req).Should().Contain("kFilterCutoff");
    }

    [Theory]
    [InlineData(CodeTarget.Juce, "filterCutoff.juce.h")]
    [InlineData(CodeTarget.Css, "filterCutoff.html")]
    [InlineData(CodeTarget.IPlug2, "filterCutoff.iplug2.cpp")]
    [InlineData(CodeTarget.Hise, "filterCutoff.hise.js")]
    public void FileName_maps_each_target(CodeTarget target, string expected) =>
        _svc.FileName(target, "filterCutoff").Should().Be(expected);

    [Fact]
    public async Task SaveAsync_writes_the_snippet_to_disk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "stripkit_codegen_" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = await _svc.SaveAsync(CodeTarget.Juce, Req(), dir);
            File.Exists(path).Should().BeTrue();
            (await File.ReadAllTextAsync(path)).Should().Be(_svc.Generate(CodeTarget.Juce, Req()));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

}
