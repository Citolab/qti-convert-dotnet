using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Citolab.QTI.Uploader.Tests;

/// <summary>
/// Namespace-aware canonical form for comparing XML: expanded names, attributes without namespace declarations
/// (sorted), collapsed text (CDATA counts as text), comments and processing instructions kept.
/// </summary>
internal static class XmlCanonical
{
    public static string Of(string xml)
    {
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        return string.Join("\n", doc.Nodes().Select(Node).Where(s => s.Length > 0));
    }

    private static string Node(XNode node) => node switch
    {
        XElement e => Element(e),
        XText t => Collapse(t.Value), // includes XCData
        XComment c => $"<!--{c.Value}-->",
        XProcessingInstruction pi => $"<?{pi.Target} {Collapse(pi.Data)}?>",
        _ => string.Empty
    };

    private static string Element(XElement e)
    {
        var attributes = e.Attributes()
            .Where(a => !a.IsNamespaceDeclaration)
            .Select(a => $" {a.Name}=\"{a.Value}\"")
            .OrderBy(a => a, StringComparer.Ordinal);
        // merge adjacent text nodes (text and CDATA) before collapsing
        var parts = new List<string>();
        var text = string.Empty;
        foreach (var child in e.Nodes())
        {
            if (child is XText t)
            {
                text += t.Value;
                continue;
            }
            if (Collapse(text).Length > 0) parts.Add(Collapse(text));
            text = string.Empty;
            var s = Node(child);
            if (s.Length > 0) parts.Add(s);
        }
        if (Collapse(text).Length > 0) parts.Add(Collapse(text));
        return $"<{e.Name}{string.Concat(attributes)}>{string.Join("|", parts)}</{e.Name}>";
    }

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ").Trim();
}
