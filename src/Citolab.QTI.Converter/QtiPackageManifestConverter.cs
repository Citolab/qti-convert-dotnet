using System.Xml.Linq;

namespace Citolab.QTI.Converter;

internal static class QtiPackageManifestConverter
{
    private static readonly XNamespace QtiPackage3 = "http://www.imsglobal.org/xsd/qti/qtiv3p0/imscp_v1p1";
    private static readonly XNamespace ImSqtiMetadata3 = "http://www.imsglobal.org/xsd/imsqti_metadata_v3p0";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private const string ImsCpNamespaceMarker = "http://www.imsglobal.org/xsd/imscp";

    private const string SchemaLocation =
        "http://ltsc.ieee.org/xsd/LOM https://purl.imsglobal.org/spec/md/v1p3/schema/xsd/imsmd_loose_v1p3p2.xsd " +
        "http://www.imsglobal.org/xsd/qti/qtiv3p0/imscp_v1p1 https://purl.imsglobal.org/spec/qti/v3p0/schema/xsd/imsqtiv3p0_imscpv1p2_v1p0.xsd " +
        "http://www.imsglobal.org/xsd/imsqti_metadata_v3p0 https://purl.imsglobal.org/spec/qti/v3p0/schema/xsd/imsqti_metadatav3p0_v1p0.xsd";

    public static string ConvertToQti3(string manifestXml)
    {
        if (string.IsNullOrWhiteSpace(manifestXml)) return manifestXml;
        var doc = XDocument.Parse(manifestXml, LoadOptions.PreserveWhitespace);
        if (doc.Root is null) return manifestXml;

        var convertedRoot = ConvertElementNamespaces(doc.Root);

        convertedRoot.SetAttributeValue(XNamespace.Xmlns + "imsqti", ImSqtiMetadata3.NamespaceName);
        convertedRoot.SetAttributeValue(XNamespace.Xmlns + "xsi", Xsi.NamespaceName);
        convertedRoot.SetAttributeValue(Xsi + "schemaLocation", SchemaLocation);

        var metadata = convertedRoot.Element(QtiPackage3 + "metadata");
        if (metadata is not null)
        {
            var schema = metadata.Element(QtiPackage3 + "schema");
            if (schema is null)
            {
                metadata.Add(new XElement(QtiPackage3 + "schema", "QTI Package"));
            }
            else
            {
                schema.Value = "QTI Package";
            }

            var schemaversion = metadata.Element(QtiPackage3 + "schemaversion");
            if (schemaversion is null)
            {
                metadata.Add(new XElement(QtiPackage3 + "schemaversion", "3.0.0"));
            }
            else
            {
                schemaversion.Value = "3.0.0";
            }
        }

        foreach (var resource in convertedRoot.Descendants(QtiPackage3 + "resource"))
        {
            var type = (string?)resource.Attribute("type") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(type)) continue;

            if (type.IndexOf("item", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                resource.SetAttributeValue("type", "imsqti_item_xmlv3p0");
            }
            else if (type.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                resource.SetAttributeValue("type", "imsqti_test_xmlv3p0");
            }
            else if (type.IndexOf("associatedcontent", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                resource.SetAttributeValue("type", "webcontent");
            }
        }

        var output = new XDocument(doc.Declaration, convertedRoot);
        return output.ToString(SaveOptions.DisableFormatting);
    }

    private static XElement ConvertElementNamespaces(XElement source)
    {
        var targetNamespace = ShouldConvertToQtiPackageNamespace(source.Name.NamespaceName) ? QtiPackage3 : source.Name.Namespace;
        var converted = new XElement(targetNamespace + source.Name.LocalName);

        foreach (var attribute in source.Attributes())
        {
            if (attribute.IsNamespaceDeclaration) continue;
            converted.SetAttributeValue(attribute.Name, attribute.Value);
        }

        foreach (var node in source.Nodes())
        {
            switch (node)
            {
                case XElement el:
                    converted.Add(ConvertElementNamespaces(el));
                    break;
                default:
                    converted.Add(node);
                    break;
            }
        }

        return converted;
    }

    private static bool ShouldConvertToQtiPackageNamespace(string namespaceName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName)) return true;
        return namespaceName.StartsWith(ImsCpNamespaceMarker, StringComparison.OrdinalIgnoreCase);
    }
}
