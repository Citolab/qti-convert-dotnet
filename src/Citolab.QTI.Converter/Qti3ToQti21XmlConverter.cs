using System.Xml.Linq;

namespace Citolab.QTI.Converter;

public enum Qti21WarningCode
{
    AlreadyQti2,
    NotQti,
    StimulusInlined,
    StimulusUnresolved,
    StimulusStandalone,
    RemovedElement,
    RemovedAttribute,
    DataAttributesRemoved,
    SsmlRemoved,
    MediaToObject,
    ImgToObject,
    Html5Element,
    Pci,
    SharedVocabularyClasses,
    AccessibilityAttributesRemoved,
    GapTextToGapImg
}

/// <summary>Something that could not be expressed in QTI 2.1 and was converted or removed.</summary>
public sealed class Qti21Warning
{
    public Qti21Warning(Qti21WarningCode code, string message, string? file)
    {
        Code = code;
        Message = message;
        File = file;
    }

    public Qti21WarningCode Code { get; }
    public string Message { get; }
    public string? File { get; }

    public override string ToString() => $"[{Code}]{(File is null ? string.Empty : $" {File}:")} {Message}";
}

public sealed class Qti21ConversionResult
{
    public Qti21ConversionResult(string xml, IReadOnlyList<Qti21Warning> warnings)
    {
        Xml = xml;
        Warnings = warnings;
    }

    public string Xml { get; }
    public IReadOnlyList<Qti21Warning> Warnings { get; }
}

public sealed class Qti3ToQti21ConvertOptions
{
    /// <summary>
    /// Returns the QTI 3 stimulus XML for the href of a qti-assessment-stimulus-ref (relative to the item), or null
    /// when it can't be found. Without a resolver stimulus refs are removed with a warning.
    /// </summary>
    public Func<string, string?>? ResolveStimulus { get; set; }

    /// <summary>Used to tag warnings.</summary>
    public string? FilePath { get; set; }
}

/// <summary>
/// Converts QTI 3.0 XML (item, test or stimulus) to QTI 2.1. Best-effort: constructs without a QTI 2.1 equivalent
/// are converted or removed and reported as warnings.
/// </summary>
public static class Qti3ToQti21XmlConverter
{
    private const string Qti21SchemaLocation =
        "http://www.imsglobal.org/xsd/imsqti_v2p1 http://www.imsglobal.org/xsd/qti/qtiv2p1/imsqti_v2p1p2.xsd";
    private const string Qti21RpTemplatesUri = "http://www.imsglobal.org/question/qti_v2p1/rptemplates/";
    private static readonly XNamespace Pci = "http://www.imsglobal.org/xsd/portableCustomInteraction";
    private static readonly XNamespace Qti3 = QtiNames.Qti3;

    /// <summary>QTI 3 elements that only exist in QTI 3.0 (or 2.2) and are removed including their content.</summary>
    private static readonly string[] Qti3OnlyRemove =
    {
        "qti-catalog-info", "qti-companion-materials-info", "qti-context-declaration",
        "qti-assessment-stimulus-ref" // inlined when resolvable
    };

    /// <summary>Attributes added in QTI 3.0 (or 2.2) that have no QTI 2.1 equivalent, per QTI 3 element.</summary>
    private static readonly Dictionary<string, string[]> Qti3OnlyAttributes = new(StringComparer.Ordinal)
    {
        ["qti-rubric-block"] = new[] { "use" },
        ["qti-outcome-declaration"] = new[] { "external-scored", "variable-identifier-ref" },
        ["qti-choice-interaction"] = new[] { "orientation" }
    };

    /// <summary>Elements only valid in QTI 2.2+ that are renamed to generic XHTML.</summary>
    private static readonly Dictionary<string, string> Html5ToHtml4 = new(StringComparer.Ordinal)
    {
        ["article"] = "div", ["aside"] = "div", ["bdi"] = "span", ["figcaption"] = "div", ["figure"] = "div",
        ["footer"] = "div", ["header"] = "div", ["mark"] = "span", ["nav"] = "div", ["section"] = "div",
        ["bdo"] = "span", ["ruby"] = "span", ["rb"] = "span", ["rt"] = "span", ["rp"] = "span"
    };

    /// <summary>Interactions that require an object (not an img) as background in QTI 2.1.</summary>
    private static readonly HashSet<string> GraphicInteractions = new(StringComparer.Ordinal)
    {
        "qti-hotspot-interaction", "qti-select-point-interaction", "qti-graphic-order-interaction",
        "qti-graphic-associate-interaction", "qti-graphic-gap-match-interaction", "qti-position-object-stage",
        "qti-position-object-interaction", "qti-drawing-interaction", "qti-gap-img"
    };

    /// <summary>object is inline in QTI 2.1, so it needs a block wrapper in these parents.</summary>
    private static readonly HashSet<string> BlockOnlyParents = new(StringComparer.Ordinal)
        { "qti-item-body", "blockquote", "qti-rubric-block" };

    private static readonly string[] RebaseAttributes = { "src", "data", "href", "poster" };

    private sealed class WarningCollector
    {
        private readonly string? _file;
        public List<Qti21Warning> Warnings { get; } = new();
        public WarningCollector(string? file) => _file = file;

        public void Add(Qti21WarningCode code, string message)
        {
            if (!Warnings.Any(w => w.Code == code && w.Message == message))
            {
                Warnings.Add(new Qti21Warning(code, message, _file));
            }
        }
    }

    public static Qti21ConversionResult Convert(string qti3Xml, Qti3ToQti21ConvertOptions? options = null)
    {
        options ??= new Qti3ToQti21ConvertOptions();
        var warnings = new WarningCollector(options.FilePath);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(qti3Xml, LoadOptions.PreserveWhitespace);
        }
        catch (System.Xml.XmlException)
        {
            warnings.Add(Qti21WarningCode.NotQti, "The file is not well-formed XML; it was left unchanged.");
            return new Qti21ConversionResult(qti3Xml, warnings.Warnings);
        }

        var root = doc.Root;
        if (root is null || !root.Name.LocalName.StartsWith("qti-", StringComparison.Ordinal))
        {
            var local = root?.Name.LocalName ?? string.Empty;
            warnings.Add(
                local is "assessmentItem" or "assessmentTest" or "assessmentStimulus" ? Qti21WarningCode.AlreadyQti2 : Qti21WarningCode.NotQti,
                $"<{local}> is not a QTI 3 document; it was left unchanged.");
            return new Qti21ConversionResult(qti3Xml, warnings.Warnings);
        }
        if (root.Name.LocalName == "qti-assessment-stimulus")
        {
            warnings.Add(Qti21WarningCode.StimulusStandalone, "assessmentStimulus is a QTI 2.2 construct; QTI 2.1 players will not recognise it.");
        }

        doc.DescendantNodes().OfType<XProcessingInstruction>().Where(pi => pi.Target == "xml-model").ToList().ForEach(pi => pi.Remove());

        InlineSharedStimuli(root, options.ResolveStimulus, warnings);

        foreach (var name in Qti3OnlyRemove)
        {
            foreach (var el in root.Descendants(Qti3 + name).ToList())
            {
                warnings.Add(Qti21WarningCode.RemovedElement, $"<{name}> is not supported in QTI 2.1 and was removed.");
                el.Remove();
            }
        }
        foreach (var body in root.Descendants(Qti3 + "qti-content-body").ToList()) Unwrap(body);

        ConvertPci(root, warnings);
        ConvertMediaToObject(root, warnings);
        ConvertGraphicImagesToObject(root, warnings);
        ConvertImageGapTextsToGapImg(root, warnings);
        root.Descendants(Qti3 + "wbr").Concat(root.Descendants(Qti3 + "track"))
            .Concat(root.Descendants(Qti3 + "picture").Elements(Qti3 + "source")).ToList().ForEach(e => e.Remove());
        foreach (var picture in root.Descendants(Qti3 + "picture").ToList()) Unwrap(picture);
        StripSsml(root, warnings);

        if (root.DescendantsAndSelf().Any(e => ((string?)e.Attribute("class") ?? string.Empty).Contains("qti-")))
        {
            warnings.Add(Qti21WarningCode.SharedVocabularyClasses, "QTI 3 shared vocabulary classes (qti-*) were kept; QTI 2.1 players will ignore them.");
        }

        var dataAttributes = 0;
        RenameTree(root, warnings, ref dataAttributes);
        if (dataAttributes > 0)
        {
            warnings.Add(Qti21WarningCode.DataAttributesRemoved, $"{dataAttributes} data-* attribute(s) were removed.");
        }

        // Namespaces and schema location
        foreach (var el in root.DescendantsAndSelf())
        {
            el.Attributes().Where(a => a.IsNamespaceDeclaration && a.Value == Qti3.NamespaceName).ToList().ForEach(a => a.Remove());
            if (el.Name.Namespace == Qti3) el.Name = QtiNames.Qti21 + el.Name.LocalName;
        }
        var rootAttributes = root.Attributes()
            .Where(a => a.Name != QtiNames.Xsi + "schemaLocation" && a.Name != XNamespace.Xmlns + "xsi")
            .Select(a => new XAttribute(a.Name, a.Value))
            .ToList();
        root.ReplaceAttributes(
            new XAttribute(XNamespace.Xmlns + "xsi", QtiNames.Xsi.NamespaceName),
            new XAttribute(QtiNames.Xsi + "schemaLocation", Qti21SchemaLocation),
            rootAttributes);

        var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                  string.Join("\n", doc.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting)));
        return new Qti21ConversionResult(xml, warnings.Warnings);
    }

    private static void Unwrap(XElement element)
    {
        // detach first: adding nodes that still have a parent would add copies
        var nodes = element.Nodes().ToList();
        foreach (var node in nodes) node.Remove();
        element.AddBeforeSelf(nodes);
        element.Remove();
    }

    private static string RebaseUrl(string baseDirectory, string value) =>
        baseDirectory.Length > 0 && QtiPackagePath.IsRelativeUrl(value) ? QtiPackagePath.Join(baseDirectory, value) : value;

    /// <summary>Replaces qti-assessment-stimulus-ref with the stimulus body at the start of the item body.</summary>
    private static void InlineSharedStimuli(XElement root, Func<string, string?>? resolveStimulus, WarningCollector warnings)
    {
        foreach (var reference in root.Descendants(Qti3 + "qti-assessment-stimulus-ref").ToList())
        {
            var href = (string?)reference.Attribute("href") ?? string.Empty;
            var identifier = (string?)reference.Attribute("identifier") ?? href;
            var stimulusXml = href.Length > 0 ? resolveStimulus?.Invoke(href) : null;
            var itemBody = root.Element(Qti3 + "qti-item-body");
            XElement? stimulusBody = null;
            XElement? stimulusRoot = null;
            if (stimulusXml is not null)
            {
                try
                {
                    stimulusRoot = XDocument.Parse(stimulusXml, LoadOptions.PreserveWhitespace).Root;
                    stimulusBody = stimulusRoot?.Descendants(Qti3 + "qti-stimulus-body").FirstOrDefault();
                }
                catch (System.Xml.XmlException)
                {
                }
            }
            if (stimulusBody is null || stimulusRoot is null || itemBody is null)
            {
                warnings.Add(Qti21WarningCode.StimulusUnresolved, $"Shared stimulus \"{identifier}\" could not be resolved and was removed.");
                reference.Remove();
                continue;
            }

            var baseDirectory = QtiPackagePath.DirectoryName(href);
            foreach (var el in stimulusBody.Descendants())
            {
                foreach (var name in RebaseAttributes)
                {
                    var attribute = el.Attribute(name);
                    if (attribute is not null) attribute.Value = RebaseUrl(baseDirectory, attribute.Value);
                }
            }
            foreach (var stylesheet in stimulusRoot.Elements(Qti3 + "qti-stylesheet"))
            {
                var copy = new XElement(stylesheet);
                var hrefAttribute = copy.Attribute("href");
                if (hrefAttribute is not null) hrefAttribute.Value = RebaseUrl(baseDirectory, hrefAttribute.Value);
                itemBody.AddBeforeSelf(copy);
            }

            var div = new XElement(Qti3 + "div", new XAttribute("class", "qti-shared-stimulus"));
            var lang = stimulusRoot.Attribute(XNamespace.Xml + "lang");
            if (lang is not null) div.SetAttributeValue(XNamespace.Xml + "lang", lang.Value);
            div.Add(stimulusBody.Nodes());
            itemBody.AddFirst(div);
            reference.Remove();
            warnings.Add(Qti21WarningCode.StimulusInlined, $"Shared stimulus \"{identifier}\" was inlined into the item body.");
        }
    }

    private static XElement Object(string data, string? type, XElement source, IEnumerable<object> fallback)
    {
        var obj = new XElement(Qti3 + "object",
            new XAttribute("data", data),
            new XAttribute("type", string.IsNullOrEmpty(type) ? QtiPackagePath.MimeTypeFromPath(data) : type));
        foreach (var name in new[] { "width", "height", "id", "class" })
        {
            var value = (string?)source.Attribute(name);
            if (!string.IsNullOrEmpty(value)) obj.SetAttributeValue(name, value);
        }
        obj.Add(fallback);
        return obj;
    }

    private static void ConvertMediaToObject(XElement root, WarningCollector warnings)
    {
        foreach (var media in root.Descendants().Where(e => e.Name == Qti3 + "audio" || e.Name == Qti3 + "video").ToList())
        {
            var local = media.Name.LocalName;
            var source = media.Elements(Qti3 + "source").FirstOrDefault();
            var src = (string?)media.Attribute("src") ?? (string?)source?.Attribute("src");
            if (string.IsNullOrEmpty(src))
            {
                media.Remove();
                warnings.Add(Qti21WarningCode.MediaToObject, $"<{local}> without a source was removed.");
                continue;
            }
            var fallback = media.Nodes()
                .Where(n => n is not XElement e || e.Name.LocalName is not ("source" or "track"))
                .ToList();
            var obj = Object(src!, (string?)source?.Attribute("type"), media, fallback);
            var parent = media.Parent?.Name.LocalName ?? string.Empty;
            media.ReplaceWith(BlockOnlyParents.Contains(parent) ? new XElement(Qti3 + "div", obj) : obj);
            warnings.Add(Qti21WarningCode.MediaToObject, $"HTML5 <{local}> was converted to <object>.");
        }
    }

    private static void ConvertGraphicImagesToObject(XElement root, WarningCollector warnings)
    {
        foreach (var interaction in root.Descendants().Where(e => e.Name.Namespace == Qti3 && GraphicInteractions.Contains(e.Name.LocalName)).ToList())
        {
            foreach (var img in interaction.Elements(Qti3 + "img").ToList())
            {
                var alt = (string?)img.Attribute("alt");
                img.ReplaceWith(Object((string?)img.Attribute("src") ?? string.Empty, null, img, alt is null ? Array.Empty<object>() : new object[] { alt }));
                warnings.Add(Qti21WarningCode.ImgToObject, $"<img> in {interaction.Name.LocalName} was converted to <object>, as QTI 2.1 requires.");
            }
        }
    }

    /// <summary>A qti-gap-text with only an image becomes a gapImg: QTI 2.1 gapText can only contain text.</summary>
    private static void ConvertImageGapTextsToGapImg(XElement root, WarningCollector warnings)
    {
        foreach (var gapText in root.Descendants(Qti3 + "qti-gap-text").ToList())
        {
            var images = gapText.Elements(Qti3 + "img").ToList();
            var hasText = gapText.Nodes().OfType<XText>().Any(t => t.Value.Trim().Length > 0);
            if (images.Count != 1 || hasText || gapText.Elements().Count() != 1) continue;

            var img = images[0];
            var alt = (string?)img.Attribute("alt");
            var gapImg = new XElement(Qti3 + "qti-gap-img", gapText.Attributes().Select(a => new XAttribute(a.Name, a.Value)));
            gapImg.Add(Object((string?)img.Attribute("src") ?? string.Empty, null, img, alt is null ? Array.Empty<object>() : new object[] { alt }));
            gapText.ReplaceWith(gapImg);
            warnings.Add(Qti21WarningCode.GapTextToGapImg, "A gap text with only an image was converted to gapImg, as QTI 2.1 requires.");
        }
    }

    /// <summary>qti-portable-custom-interaction -> customInteraction wrapping the PCI markup in the PCI namespace.</summary>
    private static void ConvertPci(XElement root, WarningCollector warnings)
    {
        foreach (var pci in root.Descendants(Qti3 + "qti-portable-custom-interaction").ToList())
        {
            var wrapper = new XElement(Pci + "portableCustomInteraction", new XAttribute(XNamespace.Xmlns + "pci", Pci.NamespaceName));
            foreach (var attribute in pci.Attributes().Where(a => !a.IsNamespaceDeclaration))
            {
                var name = attribute.Name.LocalName;
                if (attribute.Name.Namespace != XNamespace.None || name == "response-identifier" || name.StartsWith("data-", StringComparison.Ordinal)) continue;
                wrapper.SetAttributeValue(QtiNames.Camelize(name), attribute.Value);
            }
            wrapper.Add(pci.Nodes());
            // Children of the PCI are renamed into the PCI namespace (qti-interaction-markup -> pci:interactionMarkup)
            foreach (var el in wrapper.Descendants().Where(e => e.Name.Namespace == Qti3))
            {
                var qti2Name = QtiNames.Qti3ElementNameToQti2(el.Name.LocalName);
                if (qti2Name is not null) el.Name = Pci + qti2Name;
            }
            pci.ReplaceWith(new XElement(QtiNames.Qti21 + "customInteraction",
                new XAttribute("responseIdentifier", (string?)pci.Attribute("response-identifier") ?? string.Empty),
                wrapper));
            warnings.Add(Qti21WarningCode.Pci, "Portable custom interaction was wrapped in a customInteraction; check it in the target player.");
        }
    }

    private static void StripSsml(XElement root, WarningCollector warnings)
    {
        foreach (var el in root.Descendants().Where(e => e.Name.Namespace == QtiNames.Ssml || e.Name.Namespace == QtiNames.Ssml2010).ToList())
        {
            if (el.Parent is null) continue;
            Unwrap(el);
            warnings.Add(Qti21WarningCode.SsmlRemoved, "SSML markup is not supported in QTI 2.1 and was removed (text is kept).");
        }
        foreach (var el in root.DescendantsAndSelf())
        {
            el.Attributes().Where(a => a.IsNamespaceDeclaration && a.Value.Contains("synthesis")).ToList().ForEach(a => a.Remove());
        }
    }

    /// <summary>Renames elements and attributes to QTI 2.1 and strips what 2.1 doesn't allow.</summary>
    private static void RenameTree(XElement el, WarningCollector warnings, ref int dataAttributes)
    {
        var ns = el.Name.Namespace;
        if (ns == QtiNames.MathMl || ns == QtiNames.MathMl2010 || ns == QtiNames.Svg || ns == Pci) return;

        var originalName = el.Name.LocalName;
        var isQti3 = ns == Qti3;
        var qti21Name = isQti3 ? QtiNames.Qti3ElementNameToQti2(originalName) : null;

        var attributes = new List<XAttribute>();
        foreach (var attribute in el.Attributes())
        {
            var name = attribute.Name.LocalName;
            var plain = !attribute.IsNamespaceDeclaration && attribute.Name.Namespace == XNamespace.None;
            if (plain && name.StartsWith("data-", StringComparison.Ordinal))
            {
                dataAttributes++;
                continue;
            }
            if (plain && (name.StartsWith("aria-", StringComparison.Ordinal) || name is "role" or "dir"))
            {
                warnings.Add(Qti21WarningCode.AccessibilityAttributesRemoved, "aria-*, role and dir attributes are not allowed in QTI 2.1 and were removed.");
                continue;
            }
            if (qti21Name is null || !plain)
            {
                attributes.Add(new XAttribute(attribute.Name, attribute.Value));
                continue;
            }
            if (Qti3OnlyAttributes.TryGetValue(originalName, out var removed) && removed.Contains(name))
            {
                warnings.Add(Qti21WarningCode.RemovedAttribute, $"Attribute \"{name}\" on {originalName} is not supported in QTI 2.1.");
                continue;
            }
            var value = originalName == "qti-response-processing" && name == "template" ? RpTemplateToQti21(attribute.Value) : attribute.Value;
            attributes.Add(new XAttribute(QtiNames.Camelize(name), value));
        }
        el.ReplaceAttributes(attributes);

        if (qti21Name is not null)
        {
            el.Name = Qti3 + qti21Name;
        }
        else if (isQti3 && Html5ToHtml4.TryGetValue(originalName, out var html4Name))
        {
            warnings.Add(Qti21WarningCode.Html5Element, $"HTML5 <{originalName}> was converted to <{html4Name}>.");
            el.Name = Qti3 + html4Name;
        }

        foreach (var child in el.Elements().ToList()) RenameTree(child, warnings, ref dataAttributes);
    }

    /// <summary>https://purl.imsglobal.org/spec/qti/v3p0/rptemplates/match_correct.xml -> the QTI 2.1 template URI.</summary>
    internal static string RpTemplateToQti21(string template)
    {
        var index = template.IndexOf("rptemplates/", StringComparison.Ordinal);
        if (index < 0) return template;
        var name = template.Substring(index + "rptemplates/".Length);
        if (name.Contains("/")) return template;
        if (name.EndsWith(".xml", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 4);
        return Qti21RpTemplatesUri + name;
    }
}
