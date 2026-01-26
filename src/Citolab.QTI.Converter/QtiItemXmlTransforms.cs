using System.Xml.Linq;

namespace Citolab.QTI.Converter;

internal static class QtiItemXmlTransforms
{
    private static readonly XNamespace Qti3 = "http://www.imsglobal.org/xsd/imsqtiasi_v3p0";
    private static readonly XNamespace Ssml2001 = "http://www.w3.org/2001/10/synthesis";
    private static readonly XNamespace Ssml2010 = "http://www.w3.org/2010/10/synthesis";

    public static string Apply(string qti3ItemXml)
    {
        if (string.IsNullOrWhiteSpace(qti3ItemXml)) return qti3ItemXml;
        var doc = XDocument.Parse(qti3ItemXml, LoadOptions.PreserveWhitespace);
        if (doc.Root is null) return qti3ItemXml;

        ObjectToImg(doc);
        ObjectToVideo(doc);
        ObjectToAudio(doc);
        SsmlToSpans(doc);
        StripMaterialInfo(doc);
        MinChoicesToOne(doc);
        ExternalScored(doc);

        return doc.ToString(SaveOptions.DisableFormatting);
    }

    private static void ObjectToImg(XDocument doc)
    {
        var objects = doc.Descendants()
            .Where(e => e.Name.LocalName == "object" &&
                        (string?)e.Attribute("type") is string t &&
                        t.StartsWith("image", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var obj in objects)
        {
            var img = new XElement(obj.Name.Namespace + "img");
            CopyIfPresent(obj, img, "width");
            CopyIfPresent(obj, img, "height");
            CopyIfPresentAs(obj, img, "data", "src");

            var alt = obj.Value;
            if (!string.IsNullOrWhiteSpace(alt))
            {
                img.SetAttributeValue("alt", alt);
            }

            obj.ReplaceWith(img);
        }
    }

    private static void ObjectToVideo(XDocument doc)
    {
        NormalizeMediaControls(doc, "video");

        var objects = doc.Descendants()
            .Where(e => e.Name.LocalName == "object" &&
                        (string?)e.Attribute("type") is string t &&
                        t.StartsWith("video", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var obj in objects)
        {
            var video = new XElement(obj.Name.Namespace + "video");
            CopyIfPresent(obj, video, "width");
            CopyIfPresent(obj, video, "height");

            if (string.Equals((string?)obj.Attribute("data-dep-controls"), "true", StringComparison.OrdinalIgnoreCase))
            {
                video.SetAttributeValue("controls", "");
            }

            var source = new XElement(obj.Name.Namespace + "source");
            CopyIfPresentAs(obj, source, "data", "src");
            CopyIfPresent(obj, source, "type");
            video.Add(source);

            obj.ReplaceWith(video);
        }
    }

    private static void ObjectToAudio(XDocument doc)
    {
        NormalizeMediaControls(doc, "audio");

        var objects = doc.Descendants()
            .Where(e => e.Name.LocalName == "object" &&
                        (string?)e.Attribute("type") is string t &&
                        t.StartsWith("audio", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var obj in objects)
        {
            var audio = new XElement(obj.Name.Namespace + "audio");
            CopyIfPresent(obj, audio, "width");
            CopyIfPresent(obj, audio, "height");

            if (string.Equals((string?)obj.Attribute("data-dep-controls"), "true", StringComparison.OrdinalIgnoreCase))
            {
                audio.SetAttributeValue("controls", "");
            }

            var source = new XElement(obj.Name.Namespace + "source");
            CopyIfPresentAs(obj, source, "data", "src");
            CopyIfPresent(obj, source, "type");
            audio.Add(source);

            obj.ReplaceWith(audio);
        }
    }

    private static void NormalizeMediaControls(XDocument doc, string tag)
    {
        var elements = doc.Descendants().Where(e => e.Name.LocalName == tag).ToList();
        foreach (var el in elements)
        {
            var hasControls = el.Attribute("controls") is not null;
            var depControls = string.Equals((string?)el.Attribute("data-dep-controls"), "true", StringComparison.OrdinalIgnoreCase);
            var controlsTrue = string.Equals((string?)el.Attribute("controls"), "true", StringComparison.OrdinalIgnoreCase);

            var shouldHaveControls = depControls || controlsTrue || hasControls;
            if (shouldHaveControls)
            {
                el.SetAttributeValue("controls", "");
            }
            else
            {
                el.SetAttributeValue("controls", null);
            }
        }
    }

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

    private static void StripMaterialInfo(XDocument doc)
    {
        doc.Descendants(Qti3 + "qti-companion-materials-info").Remove();
    }

    private static void MinChoicesToOne(XDocument doc)
    {
        foreach (var interaction in doc.Descendants(Qti3 + "qti-choice-interaction"))
        {
            var minChoices = (string?)interaction.Attribute("min-choices");
            if (string.IsNullOrWhiteSpace(minChoices) || minChoices == "0")
            {
                interaction.SetAttributeValue("min-choices", "1");
            }
        }
    }

    private static void ExternalScored(XDocument doc)
    {
        var assessmentItem = doc.Root;
        if (assessmentItem is null) return;
        if (assessmentItem.Name != Qti3 + "qti-assessment-item") return;

        if (!assessmentItem.Descendants(Qti3 + "qti-response-processing").Any())
        {
            var scoreOutcome = assessmentItem
                .Descendants(Qti3 + "qti-outcome-declaration")
                .FirstOrDefault(e => string.Equals((string?)e.Attribute("identifier"), "SCORE", StringComparison.OrdinalIgnoreCase));

            scoreOutcome?.SetAttributeValue("external-scored", "human");
        }
    }

    private static void CopyIfPresent(XElement source, XElement dest, string attributeName)
    {
        var value = (string?)source.Attribute(attributeName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            dest.SetAttributeValue(attributeName, value);
        }
    }

    private static void CopyIfPresentAs(XElement source, XElement dest, string sourceAttributeName, string destAttributeName)
    {
        var value = (string?)source.Attribute(sourceAttributeName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            dest.SetAttributeValue(destAttributeName, value);
        }
    }

    private static XAttribute? OptionalAttr(string name, string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new XAttribute(name, value);
}

