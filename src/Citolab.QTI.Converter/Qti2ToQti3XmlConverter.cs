using System.Xml.Linq;

namespace Citolab.QTI.Converter;

/// <summary>
/// Converts QTI 2.x XML (item, test or stimulus) to QTI 3.0. A port of qti30upgrader/qti2xTo30.xsl (ETS, Apache-2.0)
/// plus the Citolab additions, without an XSLT processor, so it behaves the same on every target framework.
/// Deliberate fixes compared to the XSLT:
/// - stimulusBody, durationLT/GTE and a few QTI 2.x elements missing from the XSLT lists get their qti- name
/// - testFeedback content is wrapped in qti-content-body like the other feedback elements
/// - qti-rubric-block gets the use attribute QTI 3 requires (scoring for scorer-only rubrics, else instructions)
/// - elements are matched by local name, so prefixed QTI 2 elements convert correctly
/// - an object video keeps its converted children once (the XSLT copied them twice)
/// - inline SVG stays in the SVG namespace
/// </summary>
internal static class Qti2ToQti3XmlConverter
{
    private const string Qti3SchemaLocation =
        "http://www.imsglobal.org/xsd/imsqtiasi_v3p0 https://purl.imsglobal.org/spec/qti/v3p0/schema/xsd/imsqti_asiv3p0_v1p0.xsd";
    private const string Qti3RpTemplatesUri = "https://purl.imsglobal.org/spec/qti/v3p0/rptemplates/";
    private const string XmlModelData =
        "href=\"https://purl.imsglobal.org/spec/qti/v3p0/schema/xsd/imsqti_asiv3p0_v1p0.xsd\" type=\"application/xml\" schematypens=\"http://purl.oclc.org/dsdl/schematron\"";

    private static readonly HashSet<string> RootElements = new(StringComparer.Ordinal)
        { "assessmentItem", "assessmentStimulus", "assessmentTest" };

    private static readonly HashSet<string> ContentBodyElements = new(StringComparer.Ordinal)
        { "feedbackBlock", "modalFeedback", "rubricBlock", "templateBlock", "testFeedback" };

    private static readonly XNamespace Qti3 = QtiNames.Qti3;

    public static string Convert(string qti2Xml)
    {
        if (string.IsNullOrWhiteSpace(qti2Xml)) return qti2Xml;

        var doc = XDocument.Parse(qti2Xml, LoadOptions.PreserveWhitespace);
        if (doc.Root is null) return qti2Xml;
        if (!RootElements.Contains(doc.Root.Name.LocalName)) return qti2Xml;

        var convertedRoot = ConvertElement(doc.Root)!;
        DeclareAttributeNamespaces(doc.Root, convertedRoot);

        var nodes = new List<XNode> { new XProcessingInstruction("xml-model", XmlModelData) };
        foreach (var node in doc.Nodes())
        {
            switch (node)
            {
                case XElement:
                    nodes.Add(convertedRoot);
                    break;
                case XComment comment:
                    nodes.Add(new XComment(comment.Value));
                    break;
                case XProcessingInstruction pi when !IsSchematronAssociation(pi):
                    nodes.Add(new XProcessingInstruction(pi.Target, pi.Data));
                    break;
            }
        }

        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
               string.Join("\n", nodes.Select(n => n.ToString(SaveOptions.DisableFormatting)));
    }

    private static bool IsSchematronAssociation(XProcessingInstruction pi) =>
        pi.Target == "xml-model" && pi.Data.Contains("dsdl/schematron");

    private static XElement? ConvertElement(XElement source)
    {
        var ns = source.Name.Namespace;
        var local = source.Name.LocalName;

        if (local == "apipAccessibility") return null;

        if (ns == QtiNames.MathMl || ns == QtiNames.MathMl2010) return CopyInNamespace(source, QtiNames.MathMl);
        if (ns == QtiNames.Ssml || ns == QtiNames.Ssml2010) return CopyInNamespace(source, QtiNames.Ssml);
        if (ns == QtiNames.Svg) return CopyInNamespace(source, QtiNames.Svg);

        var type = (string?)source.Attribute("type") ?? string.Empty;
        if (local == "object" && type.StartsWith("image", StringComparison.Ordinal))
        {
            var img = new XElement(Qti3 + "img",
                new XAttribute("src", (string?)source.Attribute("data") ?? string.Empty),
                new XAttribute("alt", source.Value));
            AddAttributes(img, CopiedAttributes(source).Where(a => a.Name.LocalName is not ("data" or "type") || a.Name.Namespace != XNamespace.None));
            return img;
        }

        if (local == "object" && type.StartsWith("video", StringComparison.Ordinal))
        {
            var data = (string?)source.Attribute("data") ?? string.Empty;
            var video = new XElement(Qti3 + "video", new XAttribute("src", data));
            AddAttributes(video, CopiedAttributes(source).Where(a => a.Name.LocalName is not ("data" or "type") || a.Name.Namespace != XNamespace.None));
            AddConvertedNodes(video, source.Nodes());
            video.Add(new XElement(Qti3 + "source", new XAttribute("type", type), new XAttribute("src", data)));
            return video;
        }

        if (RootElements.Contains(local))
        {
            var root = new XElement(Qti3 + QtiNames.QtiKabobify(local));
            root.Add(new XAttribute(XNamespace.Xmlns + "xsi", QtiNames.Xsi.NamespaceName));
            root.Add(new XAttribute(QtiNames.Xsi + "schemaLocation", Qti3SchemaLocation));
            AddAttributes(root, KabobAttributes(source, a => a.Name == QtiNames.Xsi + "schemaLocation"));
            AddConvertedNodes(root, source.Nodes());
            return root;
        }

        if (ContentBodyElements.Contains(local))
        {
            var container = new XElement(Qti3 + QtiNames.QtiKabobify(local));
            AddAttributes(container, KabobAttributes(source));
            if (local == "rubricBlock" && source.Attribute("use") is null)
            {
                // use is required in QTI 3; derive it from the audience
                var views = ((string?)source.Attribute("view") ?? string.Empty).Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                container.SetAttributeValue("use", views.Contains("scorer") && !views.Contains("candidate") ? "scoring" : "instructions");
            }

            bool IsStylesheet(XNode node) => node is XElement e && e.Name.LocalName == "stylesheet";
            AddConvertedNodes(container, source.Nodes().Where(IsStylesheet));
            var body = new XElement(Qti3 + "qti-content-body");
            AddConvertedNodes(body, source.Nodes().Where(n => !IsStylesheet(n)));
            container.Add(body);
            return container;
        }

        var converted = new XElement(Qti3 + (QtiNames.Qti2ElementNameToQti3(local) ?? local));
        var attributes = KabobAttributes(source);
        if (local == "responseProcessing")
        {
            attributes = attributes.Select(a => a.Name == "template"
                ? new XAttribute("template", (a.Value.Contains("/") ? Qti3RpTemplatesUri + a.Value.Substring(a.Value.LastIndexOf('/') + 1) : a.Value) + ".xml")
                : a);
        }
        AddAttributes(converted, attributes);
        AddConvertedNodes(converted, source.Nodes());
        return converted;
    }

    /// <summary>MathML, SSML and SVG: same local names in the target namespace, attributes copied as they are.</summary>
    private static XElement CopyInNamespace(XElement source, XNamespace ns)
    {
        var copy = new XElement(ns + source.Name.LocalName);
        AddAttributes(copy, CopiedAttributes(source));
        AddConvertedNodes(copy, source.Nodes());
        return copy;
    }

    private static IEnumerable<XAttribute> CopiedAttributes(XElement source) =>
        source.Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a => new XAttribute(a.Name, a.Value));

    /// <summary>Attribute names (except aria-* and data-*) get kabobized; the namespace is kept.</summary>
    private static IEnumerable<XAttribute> KabobAttributes(XElement source, Func<XAttribute, bool>? exclude = null) =>
        source.Attributes()
            .Where(a => !a.IsNamespaceDeclaration && !(exclude?.Invoke(a) ?? false))
            .Select(a =>
            {
                var local = a.Name.LocalName;
                var name = a.Name.Namespace == XNamespace.None &&
                           (local.StartsWith("aria-", StringComparison.Ordinal) || local.StartsWith("data-", StringComparison.Ordinal))
                    ? a.Name
                    : a.Name.Namespace + QtiNames.Kabobize(local);
                return new XAttribute(name, a.Value);
            });

    private static void AddAttributes(XElement target, IEnumerable<XAttribute> attributes)
    {
        foreach (var attribute in attributes)
        {
            target.SetAttributeValue(attribute.Name, attribute.Value);
        }
    }

    private static void AddConvertedNodes(XElement target, IEnumerable<XNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case XElement element:
                    var converted = ConvertElement(element);
                    if (converted is not null) target.Add(converted);
                    break;
                case XCData cdata:
                    target.Add(new XCData(cdata.Value));
                    break;
                case XText text:
                    target.Add(new XText(text.Value));
                    break;
                case XComment comment:
                    target.Add(new XComment(comment.Value));
                    break;
                case XProcessingInstruction pi when !IsSchematronAssociation(pi):
                    target.Add(new XProcessingInstruction(pi.Target, pi.Data));
                    break;
            }
        }
    }

    /// <summary>Declares the namespaces of prefixed attributes (e.g. xlink:href) with their original prefix.</summary>
    private static void DeclareAttributeNamespaces(XElement sourceRoot, XElement convertedRoot)
    {
        var prefixes = sourceRoot.DescendantsAndSelf()
            .SelectMany(e => e.Attributes())
            .Where(a => a.IsNamespaceDeclaration && a.Name.Namespace == XNamespace.Xmlns)
            .GroupBy(a => a.Value)
            .ToDictionary(g => g.Key, g => g.First().Name.LocalName);

        var used = convertedRoot.DescendantsAndSelf()
            .SelectMany(e => e.Attributes())
            .Where(a => !a.IsNamespaceDeclaration)
            .Select(a => a.Name.Namespace)
            .Where(ns => ns != XNamespace.None && ns != XNamespace.Xml && ns != QtiNames.Xsi)
            .Distinct();

        var index = 0;
        foreach (var ns in used)
        {
            var prefix = prefixes.TryGetValue(ns.NamespaceName, out var p) ? p : $"ns{++index}";
            convertedRoot.SetAttributeValue(XNamespace.Xmlns + prefix, ns.NamespaceName);
        }
    }
}
