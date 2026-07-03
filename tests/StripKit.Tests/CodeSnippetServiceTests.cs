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
        int frames = 64,
        StripLayout layout = StripLayout.Strip,
        int gridColumns = 1) =>
        new(type, frames, 80, 80, stack, $"{id}_{frames}frames.png", asset2x, id, param, layout, gridColumns);

    static CodeSnippetRequest GridReq(ComponentType type = ComponentType.RotaryKnob, int frames = 8, int cols = 4) =>
        Req(type, frames: frames, layout: StripLayout.Grid, gridColumns: cols);

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

    [Fact]
    public void IPlug2_toggle_emits_an_IBSwitchControl()
    {
        _svc.Generate(CodeTarget.IPlug2, Req(ComponentType.Toggle))
            .Should().Contain("new IBSwitchControl(bounds, filterCutoffBitmap, kFilterCutoff)");
    }

    // ---- Button / Toggle ----

    [Fact]
    public void Juce_toggle_emits_a_latching_toggle_button()
    {
        var code = _svc.Generate(CodeTarget.Juce, Req(ComponentType.Toggle));
        code.Should().Contain("class FilterCutoffToggle : public juce::Button");
        code.Should().Contain("setClickingTogglesState (true)");
        code.Should().Contain("getToggleState() ? 1 : 0");
        code.Should().NotContain("drawRotarySlider");
    }

    [Fact]
    public void Juce_button_and_toggle_differ_only_in_the_class_name()
    {
        _svc.Generate(CodeTarget.Juce, Req(ComponentType.Button)).Should().Contain("class FilterCutoffButton : public juce::Button");
        _svc.Generate(CodeTarget.Juce, Req(ComponentType.Toggle)).Should().Contain("class FilterCutoffToggle : public juce::Button");
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

    // ---- React ----

    [Fact]
    public void React_emits_a_value_driven_sprite_component()
    {
        var code = _svc.Generate(CodeTarget.React, Req());
        code.Should().Contain("import React from 'react';");
        code.Should().Contain("export default function FilterCutoff(");
        code.Should().Contain("value = 0");
        code.Should().Contain("const FRAMES = 64;");
        code.Should().Contain("Math.round(value * (FRAMES - 1))");
        code.Should().Contain("backgroundImage: `url(\"filterCutoff_64frames.png\")`");
    }

    [Fact]
    public void React_background_axis_follows_the_stack_direction()
    {
        _svc.Generate(CodeTarget.React, Req(stack: StackDirection.Vertical))
            .Should().Contain("const HORIZONTAL = false;");
        _svc.Generate(CodeTarget.React, Req(stack: StackDirection.Horizontal))
            .Should().Contain("const HORIZONTAL = true;");
    }

    // ---- Grid layout ----

    [Fact]
    public void Juce_knob_grid_computes_column_and_row_from_the_frame()
    {
        var code = _svc.Generate(CodeTarget.Juce, GridReq());
        code.Should().Contain("const int cols   = 4;");
        code.Should().Contain("(frame % cols) * frameW, (frame / cols) * frameH");
    }

    [Fact]
    public void Juce_meter_and_fader_grid_also_declare_cols()
    {
        _svc.Generate(CodeTarget.Juce, GridReq(ComponentType.Meter)).Should().Contain("const int cols   = 4;");
        _svc.Generate(CodeTarget.Juce, GridReq(ComponentType.VerticalFader)).Should().Contain("const int cols   = 4;");
    }

    [Fact]
    public void Juce_non_grid_output_is_unaffected_by_the_new_grid_fields()
    {
        // Layout defaults to Strip — output must stay byte-identical to the pre-grid snippet.
        _svc.Generate(CodeTarget.Juce, Req()).Should().NotContain("cols");
    }

    [Fact]
    public void Css_grid_uses_col_and_row_custom_properties()
    {
        var code = _svc.Generate(CodeTarget.Css, GridReq());
        code.Should().Contain("calc(var(--col, 0) * -80px) calc(var(--row, 0) * -80px)");
        code.Should().Contain("const cols = 4;");
        code.Should().Contain("el.style.setProperty('--col', frame % cols);");
        code.Should().Contain("el.style.setProperty('--row', Math.floor(frame / cols));");
        code.Should().NotContain("--frame");
    }

    [Fact]
    public void Css_non_grid_still_uses_the_single_frame_variable()
    {
        var code = _svc.Generate(CodeTarget.Css, Req());
        code.Should().Contain("--frame");
        code.Should().NotContain("--col").And.NotContain("--row");
    }

    [Fact]
    public void Hise_grid_computes_column_and_row_offsets()
    {
        var code = _svc.Generate(CodeTarget.Hise, GridReq());
        code.Should().Contain("(frame % 4) * 80");
        code.Should().Contain("Math.floor(frame / 4) * 80");
    }

    [Fact]
    public void React_grid_computes_column_and_row_in_the_background_position()
    {
        var code = _svc.Generate(CodeTarget.React, GridReq());
        code.Should().Contain("const GRID_COLS = 4;");
        code.Should().Contain("frame % GRID_COLS");
        code.Should().Contain("Math.floor(frame / GRID_COLS)");
    }

    [Fact]
    public void IPlug2_grid_warns_that_the_builtin_bitmap_control_cannot_read_a_2d_atlas()
    {
        var code = _svc.Generate(CodeTarget.IPlug2, GridReq());
        code.Should().Contain("NOTE").And.Contain("Grid layout").And.Contain("iPlug2");
    }

    [Fact]
    public void IPlug2_non_grid_has_no_grid_warning()
    {
        _svc.Generate(CodeTarget.IPlug2, Req()).Should().NotContain("NOTE:");
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
    [InlineData(CodeTarget.React, "filterCutoff.jsx")]
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
