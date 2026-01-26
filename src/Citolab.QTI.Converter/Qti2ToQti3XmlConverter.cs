using System.Xml.Linq;

namespace Citolab.QTI.Converter;

internal static class Qti2ToQti3XmlConverter
{
    private static readonly XNamespace Qti3 = "http://www.imsglobal.org/xsd/imsqtiasi_v3p0";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    private static readonly XNamespace MathMl1998 = "http://www.w3.org/1998/Math/MathML";
    private static readonly XNamespace MathMl2010 = "http://www.w3.org/2010/Math/MathML";

    private static readonly XNamespace Ssml2001 = "http://www.w3.org/2001/10/synthesis";
    private static readonly XNamespace Ssml2010 = "http://www.w3.org/2010/10/synthesis";

    private const string Qti3SchemaLocation = "http://www.imsglobal.org/xsd/imsqtiasi_v3p0 https://purl.imsglobal.org/spec/qti/v3p0/schema/xsd/imsqti_asiv3p0_v1p0.xsd";

    public static string Convert(string qti2Xml)
    {
        if (string.IsNullOrWhiteSpace(qti2Xml)) return qti2Xml;

        var doc = XDocument.Parse(qti2Xml, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        if (doc.Root is null) return qti2Xml;

        var rootLocalName = doc.Root.Name.LocalName;
        if (rootLocalName is not ("assessmentItem" or "assessmentTest" or "assessmentStimulus"))
        {
            return qti2Xml;
        }

#if NET9_0_OR_GREATER
        try
        {
            if (SaxonXslt30Transformer.IsAvailable)
            {
                return SaxonXslt30Transformer.Transform(qti2Xml);
            }
        }
        catch
        {
        }
#endif

        var convertedRoot = ConvertElement(doc.Root);

        convertedRoot.SetAttributeValue(XNamespace.Xmlns + "xsi", Xsi.NamespaceName);
        convertedRoot.SetAttributeValue(Xsi + "schemaLocation", Qti3SchemaLocation);

        var output = new XDocument(doc.Declaration, convertedRoot);
        return output.ToString(SaveOptions.DisableFormatting);
    }

    private static XElement ConvertElement(XElement element)
    {
        var (targetNamespace, targetLocalName) = ConvertElementName(element.Name);
        var converted = new XElement(targetNamespace + targetLocalName);

        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration) continue;
            var convertedAttributeName = ConvertAttributeName(attribute.Name);
            converted.SetAttributeValue(convertedAttributeName, attribute.Value);
        }

        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XElement child:
                    converted.Add(ConvertElement(child));
                    break;
                case XCData cdata:
                    converted.Add(new XCData(cdata.Value));
                    break;
                case XText text:
                    converted.Add(new XText(text.Value));
                    break;
                case XComment comment:
                    converted.Add(new XComment(comment.Value));
                    break;
                case XProcessingInstruction pi:
                    converted.Add(new XProcessingInstruction(pi.Target, pi.Data));
                    break;
                default:
                    break;
            }
        }

        return converted;
    }

    private static (XNamespace targetNamespace, string targetLocalName) ConvertElementName(XName sourceName)
    {
        if (sourceName.Namespace == MathMl2010) return (MathMl1998, sourceName.LocalName);
        if (sourceName.Namespace == Ssml2010) return (Ssml2001, sourceName.LocalName);

        var localName = sourceName.LocalName;
        var shouldKabobify = localName.Any(char.IsUpper);

        if (!shouldKabobify)
        {
            return (Qti3, localName);
        }

        var kabobified = XmlNameUtilities.Kabobify(localName);
        var qtiName = $"qti-{kabobified}";
        return (Qti3, qtiName);
    }

    private static XName ConvertAttributeName(XName sourceName)
    {
        if (sourceName.Namespace != XNamespace.None)
        {
            return sourceName;
        }

        if (sourceName.LocalName.StartsWith("aria-", StringComparison.OrdinalIgnoreCase) ||
            sourceName.LocalName.StartsWith("data-", StringComparison.OrdinalIgnoreCase))
        {
            return sourceName;
        }

        return XName.Get(XmlNameUtilities.Kabobify(sourceName.LocalName));
    }
}
