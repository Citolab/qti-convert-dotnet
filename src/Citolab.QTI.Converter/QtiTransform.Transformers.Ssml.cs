using System.Xml.Linq;

namespace Citolab.QTI.Converter;

public sealed partial class QtiTransform
{
    private static void SsmlToSpans(XDocument doc)
    {
        var ssmlElements = doc
            .Descendants()
            .Where(e => e.Name.Namespace == Ssml2001 || e.Name.Namespace == Ssml2010)
            .ToList();

        foreach (var el in ssmlElements)
        {
            XElement? replacement = el.Name.LocalName switch
            {
                "sub" => new XElement("span",
                    new XAttribute("data-ssml-sub-alias", (string?)el.Attribute("alias") ?? string.Empty),
                    el.Nodes()),
                "break" => new XElement("span",
                    OptionalAttr("data-ssml-break-time", (string?)el.Attribute("time")),
                    OptionalAttr("data-ssml-break-strength", (string?)el.Attribute("strength"))),
                "say-as" => new XElement("span",
                    OptionalAttr("data-ssml-say-as", (string?)el.Attribute("interpret-as")),
                    OptionalAttr("data-ssml-say-as-format", (string?)el.Attribute("format")),
                    OptionalAttr("data-ssml-say-as-detail", (string?)el.Attribute("detail")),
                    el.Nodes()),
                "phoneme" => new XElement("span",
                    OptionalAttr("data-ssml-phoneme-ph", (string?)el.Attribute("ph")),
                    OptionalAttr("data-ssml-phoneme-alphabet", (string?)el.Attribute("alphabet")),
                    el.Nodes()),
                "prosody" => new XElement("span",
                    OptionalAttr("data-ssml-prosody-pitch", (string?)el.Attribute("pitch")),
                    OptionalAttr("data-ssml-prosody-rate", (string?)el.Attribute("rate")),
                    OptionalAttr("data-ssml-prosody-volume", (string?)el.Attribute("volume")),
                    OptionalAttr("data-ssml-prosody-contour", (string?)el.Attribute("contour")),
                    OptionalAttr("data-ssml-prosody-range", (string?)el.Attribute("range")),
                    OptionalAttr("data-ssml-prosody-duration", (string?)el.Attribute("duration")),
                    el.Nodes()),
                "emphasis" => new XElement("span",
                    OptionalAttr("data-ssml-emphasis-level", (string?)el.Attribute("level")),
                    el.Nodes()),
                "voice" => new XElement("span",
                    OptionalAttr("data-ssml-voice-gender", (string?)el.Attribute("gender")),
                    OptionalAttr("data-ssml-voice-age", (string?)el.Attribute("age")),
                    OptionalAttr("data-ssml-voice-variant", (string?)el.Attribute("variant")),
                    OptionalAttr("data-ssml-voice-name", (string?)el.Attribute("name")),
                    OptionalAttr("data-ssml-voice-languages", (string?)el.Attribute("languages")),
                    el.Nodes()),
                _ => null
            };

            if (replacement is not null)
            {
                el.ReplaceWith(replacement);
            }
        }
    }

    private static XAttribute? OptionalAttr(string name, string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new XAttribute(name, value);
}

