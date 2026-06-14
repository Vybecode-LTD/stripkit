using FluentAssertions;
using StripKit.Services;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// The SVG carve-out + safety pass that sits between a chatty model reply and the renderer. We assert
/// it pulls the SVG out of prose/markdown fences and removes anything active or external (scripts,
/// event handlers, embedded raster, off-document references) while keeping the real vector art and
/// local <c>#id</c> references intact.
/// </summary>
public class SvgSanitizerTests
{
    [Fact]
    public void Extracts_the_svg_from_a_fenced_chatty_reply()
    {
        const string reply = "Sure! Here is your knob:\n```svg\n<svg xmlns=\"http://www.w3.org/2000/svg\"><circle cx=\"5\" cy=\"5\" r=\"4\"/></svg>\n```\nEnjoy!";

        SvgSanitizer.TryClean(reply, out var svg, out var error).Should().BeTrue();
        error.Should().BeNull();
        svg.Should().StartWith("<svg").And.EndWith("</svg>");
        svg.Should().Contain("circle");
    }

    [Fact]
    public void Fails_when_there_is_no_svg()
    {
        SvgSanitizer.TryClean("I'm sorry, I can't do that.", out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Strips_script_image_and_foreignobject_elements()
    {
        const string dirty =
            """
            <svg xmlns="http://www.w3.org/2000/svg">
              <script>steal()</script>
              <image href="http://evil/x.png" x="0" y="0" width="10" height="10"/>
              <foreignObject><div>hi</div></foreignObject>
              <g id="body"><circle cx="5" cy="5" r="4"/></g>
            </svg>
            """;

        SvgSanitizer.TryClean(dirty, out var svg, out _).Should().BeTrue();
        svg.Should().NotContain("script");
        svg.Should().NotContain("foreignObject");
        svg.Should().NotContain("<image");
        svg.Should().Contain("circle", "the real vector art survives");
    }

    [Fact]
    public void Strips_event_handlers_and_off_document_href_but_keeps_local_gradient_refs()
    {
        const string dirty =
            """
            <svg xmlns="http://www.w3.org/2000/svg">
              <g id="body" onclick="steal()">
                <use href="https://evil/x"/>
                <circle cx="5" cy="5" r="4" fill="url(#g)"/>
              </g>
            </svg>
            """;

        SvgSanitizer.TryClean(dirty, out var svg, out _).Should().BeTrue();
        svg.Should().NotContain("onclick");
        svg.Should().NotContain("https://evil");
        svg.Should().Contain("url(#g)", "a local gradient reference in a fill attribute is safe and kept");
    }

    [Fact]
    public void Keeps_a_local_fragment_href()
    {
        const string input = """<svg xmlns="http://www.w3.org/2000/svg"><use href="#body"/><g id="body"><rect width="4" height="4"/></g></svg>""";

        SvgSanitizer.TryClean(input, out var svg, out _).Should().BeTrue();
        svg.Should().Contain("#body", "a local #id reference is preserved");
    }

    [Fact]
    public void Rejects_malformed_xml()
    {
        SvgSanitizer.TryClean("<svg><g></svg>", out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Rejects_a_doctype_entity_payload_instead_of_expanding_it()
    {
        // "Billion laughs": a tiny DTD whose nested entities would expand to a huge string under a
        // naive XDocument.Parse. The hardened parse (DtdProcessing.Prohibit) must reject it outright,
        // returning a clean failure rather than expanding the entities or hanging.
        const string bomb =
            "<?xml version=\"1.0\"?>" +
            "<!DOCTYPE svg [" +
            "<!ENTITY a \"AAAAAAAAAA\">" +
            "<!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\">" +
            "<!ENTITY c \"&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;\">" +
            "]>" +
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><text>&c;</text></svg>";

        SvgSanitizer.TryClean(bomb, out var svg, out var error).Should().BeFalse("a DTD-bearing document is rejected, not expanded");
        error.Should().NotBeNullOrEmpty();
        svg.Should().BeEmpty();
    }

    [Fact]
    public void Rejects_an_external_entity_probe()
    {
        // External-entity disclosure / SSRF: must not resolve, and is rejected at the DTD gate.
        const string xxe =
            "<?xml version=\"1.0\"?>" +
            "<!DOCTYPE svg [<!ENTITY x SYSTEM \"file:///etc/passwd\">]>" +
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><text>&x;</text></svg>";

        SvgSanitizer.TryClean(xxe, out var svg, out var error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
        svg.Should().BeEmpty();
    }
}
