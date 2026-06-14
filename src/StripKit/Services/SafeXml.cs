using System.Xml;
using System.Xml.Linq;

namespace StripKit.Services;

/// <summary>
/// Hardened XML parsing for <b>untrusted</b> SVG (AI replies and user-imported SVG files). DTDs are
/// prohibited and no external resolver is used, which closes two attacks a bare
/// <see cref="XDocument.Parse(string)"/> leaves open on attacker-influenced input:
/// external-entity disclosure / SSRF (<c>&lt;!ENTITY x SYSTEM "file://…"&gt;</c>) and, more importantly,
/// internal entity-expansion ("billion laughs") denial-of-service. A document carrying a
/// <c>&lt;!DOCTYPE&gt;</c> throws <see cref="XmlException"/>, which both callers already treat as
/// "malformed SVG" — legitimate generated control art never has a DTD, so the happy path is unaffected.
/// </summary>
internal static class SafeXml
{
    public static XDocument Parse(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,   // reject DTDs → no entity expansion at all
            XmlResolver = null,                        // never resolve external refs
            MaxCharactersFromEntities = 0,             // belt-and-braces vs entity expansion
        };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        return XDocument.Load(reader);
    }
}
