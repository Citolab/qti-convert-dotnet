using System.Xml.Linq;

namespace Citolab.QTI.Converter;

public sealed partial class QtiTransform
{
    private static void UnwrapElement(XElement element)
    {
        element.ReplaceWith(element.Nodes());
    }

    private static bool HasClass(XElement element, string className)
    {
        var cls = (string?)element.Attribute("class");
        if (cls is null || cls.Trim().Length == 0) return false;
        var parts = cls.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(p => string.Equals(p, className, StringComparison.Ordinal));
    }

    private static void AddClass(XElement element, string className)
    {
        var cls = (string?)element.Attribute("class") ?? string.Empty;
        var parts = cls.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Any(p => string.Equals(p, className, StringComparison.Ordinal))) return;
        parts.Add(className);
        element.SetAttributeValue("class", string.Join(" ", parts));
    }

    private static void RemoveClass(XElement element, string className)
    {
        var cls = (string?)element.Attribute("class");
        if (cls is null || cls.Trim().Length == 0) return;
        var parts = cls.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !string.Equals(p, className, StringComparison.Ordinal))
            .ToList();
        element.SetAttributeValue("class", parts.Count == 0 ? null : string.Join(" ", parts));
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
}

