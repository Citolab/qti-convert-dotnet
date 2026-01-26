using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Citolab.QTI.Uploader.Tests;

internal static class XmlAssert
{
    public static void Equal(string actualXml, string expectedXml)
    {
        var actual = Normalize(ParseWrapped(actualXml));
        var expected = Normalize(ParseWrapped(expectedXml));

        if (!XNode.DeepEquals(actual, expected))
        {
            Assert.Fail($"XML not equal.\n\nActual:\n{actual}\n\nExpected:\n{expected}\n");
        }
    }

    private static XElement ParseWrapped(string xml)
    {
        var cleaned = StripXmlDeclaration(xml ?? string.Empty);
        var wrapped = $"<root>{cleaned}</root>";
        return XDocument.Parse(wrapped, LoadOptions.PreserveWhitespace).Root!;
    }

    private static string StripXmlDeclaration(string xml)
        => Regex.Replace(xml, @"^\s*<\?xml[^>]*\?>\s*", string.Empty, RegexOptions.Singleline);

    private static XElement Normalize(XElement element)
    {
        var normalized = new XElement(element.Name);

        foreach (var attribute in element.Attributes().OrderBy(a => a.Name.NamespaceName).ThenBy(a => a.Name.LocalName))
        {
            normalized.Add(new XAttribute(attribute.Name, attribute.Value));
        }

        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XCData cdata:
                    {
                        var value = NormalizeText(cdata.Value);
                        if (value.Length == 0) continue;
                        normalized.Add(new XCData(value));
                        break;
                    }
                case XComment:
                    continue;
                case XElement child:
                    normalized.Add(Normalize(child));
                    break;
                case XText text:
                    {
                        var value = NormalizeText(text.Value);
                        if (value.Length == 0) continue;
                        normalized.Add(new XText(value));
                        break;
                    }
                default:
                    // Preserve other node types as-is.
                    normalized.Add(node);
                    break;
            }
        }

        return normalized;
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var collapsed = Regex.Replace(text, @"\s+", " ");
        return collapsed.Trim();
    }
}
