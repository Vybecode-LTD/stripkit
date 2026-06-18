using FluentAssertions;
using ImageMagick;
using ImageMagick.Drawing;
using SkiaSharp;
using StripKit.Models;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// Layered-source import (★ #3 step 3): parsing a real layered file (SVG groups via Svg.Skia,
/// PSD layers via Magick.NET) into the renderer's layer stack. Fixtures are synthesized in
/// memory — an SVG string and a PSD written by Magick.NET — so the parsers are round-trip
/// tested without binary assets in the repo. We assert layer count, names, the name→behaviour
/// guess, canvas size, and that the groups/layers were isolated and registered (each layer holds
/// only its own art, in the right place on the canvas).
/// </summary>
public class LayeredImportServiceTests
{
    readonly LayeredImportService _import = new();

    static bool Dark(SKColor c) => c.Red < 110 && c.Alpha > 128;
    static bool Whiteish(SKColor c) => c.Red > 180 && c.Green > 180 && c.Blue > 180 && c.Alpha > 100;
    static bool Clear(SKColor c) => c.Alpha < 16;

    static SKColor At(SKBitmap b, double fx, double fy) =>
        b.GetPixel(Math.Clamp((int)(fx * b.Width), 0, b.Width - 1),
                   Math.Clamp((int)(fy * b.Height), 0, b.Height - 1));

    // ---- SVG ----

    const string TwoLayerSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100" viewBox="0 0 100 100">
          <g id="body"><circle cx="50" cy="50" r="40" fill="#333333"/></g>
          <g id="pointer"><line x1="50" y1="50" x2="50" y2="12" stroke="#ffffff" stroke-width="6"/></g>
        </svg>
        """;

    static string WriteTemp(string contents, string ext)
    {
        var path = Path.Combine(Path.GetTempPath(), $"stripkit_test_{Guid.NewGuid():N}{ext}");
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void Svg_groups_become_layers_with_names_and_guessed_behaviour()
    {
        var path = WriteTemp(TwoLayerSvg, ".svg");
        try
        {
            var result = _import.Import(path);

            result.Should().NotBeNull();
            result!.SourceFormat.Should().Be("SVG");
            result.Layers.Should().HaveCount(2);
            result.CanvasWidth.Should().BeGreaterThan(0);
            result.CanvasHeight.Should().BeGreaterThan(0);

            // Document order = bottom-first: body under pointer.
            result.Layers[0].Name.Should().Be("body");
            result.Layers[0].SuggestedBehavior.Should().Be(LayerBehavior.Static);
            result.Layers[1].Name.Should().Be("pointer");
            result.Layers[1].SuggestedBehavior.Should().Be(LayerBehavior.Rotate, "an indicator-like name is guessed to rotate");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Svg_layers_are_isolated_and_registered_on_the_canvas()
    {
        var path = WriteTemp(TwoLayerSvg, ".svg");
        try
        {
            var result = _import.Import(path)!;
            var body = result.Layers[0].Art;
            var pointer = result.Layers[1].Art;

            // The body layer carries the disc and nothing else.
            Dark(At(body, 0.5, 0.5)).Should().BeTrue("the body disc fills the centre");
            Clear(At(body, 0.05, 0.05)).Should().BeTrue("the corner is outside the disc");

            // The pointer layer carries only the upper needle — NOT the body fill below it.
            Whiteish(At(pointer, 0.5, 0.2)).Should().BeTrue("the needle runs up from the centre");
            Clear(At(pointer, 0.5, 0.78)).Should().BeTrue("the pointer group does not contain the body disc");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Svg_with_no_groups_is_a_single_static_layer()
    {
        const string flat = """<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64"><circle cx="32" cy="32" r="28" fill="#333333"/></svg>""";
        var path = WriteTemp(flat, ".svg");
        try
        {
            var result = _import.Import(path);
            result.Should().NotBeNull();
            result!.Layers.Should().HaveCount(1);
            result.Layers[0].SuggestedBehavior.Should().Be(LayerBehavior.Static);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_non_indicator_group_name_stays_static()
    {
        const string svg = """<svg xmlns="http://www.w3.org/2000/svg" width="50" height="50" viewBox="0 0 50 50"><g id="outline"><circle cx="25" cy="25" r="20" fill="#333333"/></g></svg>""";
        var path = WriteTemp(svg, ".svg");
        try
        {
            var result = _import.Import(path)!;
            result.Layers[0].Name.Should().Be("outline");
            result.Layers[0].SuggestedBehavior.Should().Be(LayerBehavior.Static, "'outline' is not an indicator word");
        }
        finally { File.Delete(path); }
    }

    // ---- PSD ----

    // ImageMagick's PSD writer treats image[0] as the flattened canvas (its label is dropped) and
    // images[1..] as the discrete named layers — exactly the [composite, layer, layer…] structure a
    // real Photoshop PSD has on read. So we write a background composite first, then the two layers.
    static string WriteTwoLayerPsd()
    {
        var path = Path.Combine(Path.GetTempPath(), $"stripkit_test_{Guid.NewGuid():N}.psd");
        using var coll = new MagickImageCollection();

        var background = new MagickImage(MagickColors.Transparent, 100, 100);
        new Drawables()
            .FillColor(new MagickColor("#333333")).Rectangle(20, 20, 80, 80)
            .FillColor(MagickColors.White).Rectangle(47, 10, 53, 50)
            .Draw(background);
        coll.Add(background);   // the merged composite — unlabeled, dropped on import

        var body = new MagickImage(MagickColors.Transparent, 100, 100);
        new Drawables().FillColor(new MagickColor("#333333")).Rectangle(20, 20, 80, 80).Draw(body);
        body.SetAttribute("label", "body");
        coll.Add(body);

        var pointer = new MagickImage(MagickColors.Transparent, 100, 100);
        new Drawables().FillColor(MagickColors.White).Rectangle(47, 10, 53, 50).Draw(pointer);
        pointer.SetAttribute("label", "pointer");
        coll.Add(pointer);

        coll.Write(path, MagickFormat.Psd);
        return path;
    }

    [Fact]
    public void Psd_layers_become_named_behaviour_tagged_layers()
    {
        var path = WriteTwoLayerPsd();
        try
        {
            var result = _import.Import(path);

            result.Should().NotBeNull();
            result!.SourceFormat.Should().Be("PSD");
            result.CanvasWidth.Should().Be(100);
            result.CanvasHeight.Should().Be(100);
            result.Layers.Should().HaveCount(2, "the merged composite is dropped, the two named layers kept");

            var names = result.Layers.Select(l => l.Name).ToList();
            names.Should().Contain("body").And.Contain("pointer");
            result.Layers.Single(l => l.Name == "body").SuggestedBehavior.Should().Be(LayerBehavior.Static);
            result.Layers.Single(l => l.Name == "pointer").SuggestedBehavior.Should().Be(LayerBehavior.Rotate);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Psd_layers_are_isolated_and_registered_on_the_canvas()
    {
        var path = WriteTwoLayerPsd();
        try
        {
            var result = _import.Import(path)!;
            var body = result.Layers.Single(l => l.Name == "body").Art;
            var pointer = result.Layers.Single(l => l.Name == "pointer").Art;

            body.Width.Should().Be(100);
            body.Height.Should().Be(100);

            Dark(At(body, 0.5, 0.5)).Should().BeTrue("the body rectangle fills the centre");
            Clear(At(body, 0.05, 0.05)).Should().BeTrue("the corner is outside the body rectangle");

            Whiteish(At(pointer, 0.5, 0.3)).Should().BeTrue("the pointer bar sits in the upper half");
            Clear(At(pointer, 0.5, 0.7)).Should().BeTrue("the pointer layer does not contain the body");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Svg_import_rejects_a_doctype_entity_bomb_without_expanding_it()
    {
        // "Billion laughs": a tiny DTD whose nested entities would expand to ~1e9 characters if a
        // parser processed it. Svg.Skia's underlying svg-net parser uses DtdProcessing.Parse with no
        // entity-character cap, and the file picker feeds it arbitrary user SVG — so the import MUST
        // reject the document at the hardened SafeXml gate BEFORE the text reaches Svg.Skia, returning
        // null quickly rather than expanding the entities (which would hang / exhaust memory). The
        // string itself is tiny; only its expansion is huge, so this is safe to keep in the suite.
        const string bomb =
            "<?xml version=\"1.0\"?>" +
            "<!DOCTYPE svg [" +
            "<!ENTITY a \"AAAAAAAAAA\">" +
            "<!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\">" +
            "<!ENTITY c \"&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;\">" +
            "<!ENTITY d \"&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;\">" +
            "<!ENTITY e \"&d;&d;&d;&d;&d;&d;&d;&d;&d;&d;\">" +
            "<!ENTITY f \"&e;&e;&e;&e;&e;&e;&e;&e;&e;&e;\">" +
            "<!ENTITY g \"&f;&f;&f;&f;&f;&f;&f;&f;&f;&f;\">" +
            "<!ENTITY h \"&g;&g;&g;&g;&g;&g;&g;&g;&g;&g;\">" +
            "<!ENTITY i \"&h;&h;&h;&h;&h;&h;&h;&h;&h;&h;\">" +
            "]>" +
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\" viewBox=\"0 0 64 64\"><text>&i;</text></svg>";

        var path = WriteTemp(bomb, ".svg");
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = _import.Import(path);
            sw.Stop();

            result.Should().BeNull("a DTD-bearing SVG is rejected at the hardened parse gate, not handed to the renderer");
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "the entity bomb must be rejected up front, never expanded");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Svg_import_does_not_resolve_an_external_entity()
    {
        // External-entity / SSRF: a SYSTEM entity pointing at a local file. Like the bomb above, the
        // DTD must be rejected at the gate so the path is never opened and no file content leaks into
        // the rasterized art.
        const string xxe =
            "<?xml version=\"1.0\"?>" +
            "<!DOCTYPE svg [<!ENTITY x SYSTEM \"file:///etc/passwd\">]>" +
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"64\" viewBox=\"0 0 64 64\"><text>&x;</text></svg>";

        var path = WriteTemp(xxe, ".svg");
        try { _import.Import(path).Should().BeNull("an external-entity DTD is rejected, never resolved"); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Import_returns_null_for_an_unreadable_or_unsupported_file()
    {
        _import.Import("C:\\does\\not\\exist.svg").Should().BeNull();

        var bogus = WriteTemp("not really an svg or psd", ".svg");
        try { _import.Import(bogus).Should().BeNull("garbage content has no usable layers"); }
        finally { File.Delete(bogus); }
    }

    [Fact]
    public void CanImport_recognizes_layered_extensions_only()
    {
        _import.CanImport("a.svg").Should().BeTrue();
        _import.CanImport("a.psd").Should().BeTrue();
        _import.CanImport("a.PSB").Should().BeTrue();
        _import.CanImport("a.png").Should().BeFalse();
    }
}
