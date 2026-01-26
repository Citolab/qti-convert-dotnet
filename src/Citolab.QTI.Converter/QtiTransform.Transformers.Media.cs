using System.Xml.Linq;

namespace Citolab.QTI.Converter;

public sealed partial class QtiTransform
{
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
                video.SetAttributeValue("controls", "true");
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
                audio.SetAttributeValue("controls", "true");
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
}

