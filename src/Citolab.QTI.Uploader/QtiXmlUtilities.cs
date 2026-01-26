using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Citolab.QTI.Uploader;

internal static class QtiXmlUtilities
{
    public static bool TryGetRootLocalName(Stream xmlStream, out string rootLocalName)
    {
        rootLocalName = string.Empty;
        try
        {
            if (xmlStream.CanSeek) xmlStream.Position = 0;
            using var reader = XmlReader.Create(xmlStream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreWhitespace = true,
                CloseInput = false
            });

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    rootLocalName = reader.LocalName;
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (xmlStream.CanSeek) xmlStream.Position = 0;
        }
    }

    public static bool IsQtiAssessmentItemOrTest(Stream xmlStream, out QtiXmlKind xmlKind, out string rootLocalName)
    {
        xmlKind = QtiXmlKind.None;
        rootLocalName = string.Empty;

        if (!TryGetRootLocalName(xmlStream, out rootLocalName)) return false;

        if (rootLocalName is "qti-assessment-item" or "assessmentItem")
        {
            xmlKind = QtiXmlKind.AssessmentItem;
            return true;
        }

        if (rootLocalName is "qti-assessment-test" or "assessmentTest")
        {
            xmlKind = QtiXmlKind.AssessmentTest;
            return true;
        }

        return false;
    }

    public static QtiPackageVersion UpdateVersionFromRoot(QtiPackageVersion current, string rootLocalName)
    {
        var is2 = rootLocalName is "assessmentItem" or "assessmentTest";
        var is3 = rootLocalName is "qti-assessment-item" or "qti-assessment-test";

        if (!is2 && !is3) return current;
        if (current == QtiPackageVersion.Unknown) return is2 ? QtiPackageVersion.Qti2 : QtiPackageVersion.Qti3;
        if ((current == QtiPackageVersion.Qti2 && is3) || (current == QtiPackageVersion.Qti3 && is2)) return QtiPackageVersion.Mixed;
        return current;
    }

    public static string PrettyPrintXml(XDocument doc)
    {
        var sb = new StringBuilder();
        using (var writer = new StringWriter(sb))
        {
            doc.Save(writer, SaveOptions.None);
        }
        return sb.ToString();
    }

    public static void CleanXml(XElement? element)
    {
        if (element == null) return;
        var tags = new[] { "div", "span", "td" };

        foreach (var child in element.Elements().ToList())
        {
            CleanXml(child);
        }

        if (element.Parent == null) return;

        if (tags.Contains(element.Name.LocalName) && element.HasElements && !element.HasAttributes)
        {
            var childElements = element.Elements().ToList();
            if (childElements.Count == 1)
            {
                var onlyChild = childElements[0];
                if (onlyChild.Name == element.Name && !onlyChild.HasAttributes)
                {
                    var hasSignificantText = element.Nodes()
                        .OfType<XText>()
                        .Any(t => !string.IsNullOrWhiteSpace(t.Value));

                    if (!hasSignificantText)
                    {
                        element.ReplaceWith(new XElement(element.Name, onlyChild.Nodes()));
                        return;
                    }
                }
            }
        }

        if (element.Parent == null) return;

        if (element.Name.LocalName == "span" && !element.HasAttributes)
        {
            if (string.IsNullOrWhiteSpace(element.Value) && !element.HasElements)
            {
                element.Remove();
                return;
            }

            var spanChildren = element.Elements("span").ToList();
            if (spanChildren.Count == 1 && !spanChildren[0].HasAttributes)
            {
                var childSpan = spanChildren[0];
                var childText = childSpan.Value?.Trim();
                if (!string.IsNullOrEmpty(childText))
                {
                    var outerText = element.Nodes()
                        .OfType<XText>()
                        .Select(t => t.Value)
                        .Aggregate("", (acc, text) => acc + text)
                        .Trim();

                    if (string.IsNullOrEmpty(outerText))
                    {
                        element.ReplaceWith(childSpan.Nodes());
                    }
                }
            }
        }
    }
}

