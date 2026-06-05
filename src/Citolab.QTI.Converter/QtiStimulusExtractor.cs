using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Citolab.QTI.Converter;

/// <summary>A manifest file (path + XML) passed through the stimulus extractor.</summary>
public sealed class ManifestFile
{
    public ManifestFile(string path, string xml)
    {
        Path = path;
        Xml = xml;
    }

    public string Path { get; set; }
    public string Xml { get; set; }
}

public sealed class ConvertToStimulusOptions
{
    /// <summary>Package-root-relative directory for new stimulus files. Default <c>ref/</c>.</summary>
    public string? RefDir { get; set; }

    /// <summary>Minimum number of items that must share identical left-column content. Default 2.</summary>
    public int MinItems { get; set; } = 2;
}

public sealed class ConvertToStimulusResult
{
    public ConvertToStimulusResult(
        Dictionary<string, string> items,
        Dictionary<string, string> stimuli,
        ManifestFile? manifest)
    {
        Items = items;
        Stimuli = stimuli;
        Manifest = manifest;
    }

    /// <summary>All items; rewritten where their left column was extracted, unchanged otherwise.</summary>
    public Dictionary<string, string> Items { get; }

    /// <summary>New stimulus files (package-root-relative path -> XML).</summary>
    public Dictionary<string, string> Stimuli { get; }

    /// <summary>The manifest, updated when one was supplied.</summary>
    public ManifestFile? Manifest { get; }
}

/// <summary>
/// Detects items that share identical left-column content (<c>div.qti-layout-row &gt;
/// div.qti-layout-col6</c>), extracts it into a shared <c>qti-assessment-stimulus</c> file, and
/// rewrites each sharing item to reference the stimulus (an in-body <c>div.qti-shared-stimulus</c>
/// plus a top-level <c>qti-assessment-stimulus-ref</c>). Optionally updates the package manifest.
/// </summary>
public static class QtiStimulusExtractor
{
    private static readonly XNamespace Qti3 = "http://www.imsglobal.org/xsd/imsqtiasi_v3p0";
    private const string StimulusResourceType = "imsqti_stimulus_xmlv3p0";

    private sealed class ItemCandidate
    {
        public ItemCandidate(string path, XDocument doc, XElement leftColumn, string innerXml, string key)
        {
            Path = path;
            Doc = doc;
            LeftColumn = leftColumn;
            InnerXml = innerXml;
            Key = key;
        }

        public string Path { get; }
        public XDocument Doc { get; }
        public XElement LeftColumn { get; }
        public string InnerXml { get; }
        public string Key { get; }
    }

    public static ConvertToStimulusResult ConvertToStimulus(
        IReadOnlyDictionary<string, string> items,
        ManifestFile? manifest = null,
        ConvertToStimulusOptions? options = null)
    {
        var refDir = (options?.RefDir ?? "ref/").TrimEnd('/') + "/";
        var minItems = options?.MinItems ?? 2;

        var resultItems = new Dictionary<string, string>(items.Count);
        foreach (var kv in items) resultItems[kv.Key] = kv.Value;

        var stimuli = new Dictionary<string, string>();
        var stimulusIdByPath = new Dictionary<string, string>();
        var itemStimulusIds = new Dictionary<string, string>();

        // 1. Collect candidate left columns grouped by normalized content.
        var byKey = new Dictionary<string, List<ItemCandidate>>(StringComparer.Ordinal);
        foreach (var kv in items)
        {
            var doc = XDocument.Parse(kv.Value, LoadOptions.PreserveWhitespace);
            if (doc.Root == null) continue;

            if (doc.Descendants().Any(e => e.Name.LocalName == "qti-assessment-stimulus-ref")) continue;
            if (doc.Descendants().Any(e => e.Name.LocalName == "div" && HasClass(e, "qti-shared-stimulus"))) continue;

            var leftColumn = FindLeftColumn(doc);
            if (leftColumn == null) continue;

            var innerXml = InnerXml(leftColumn);
            if (innerXml.Trim().Length == 0) continue;

            var key = NormalizeContent(innerXml);
            if (!byKey.TryGetValue(key, out var bucket))
            {
                bucket = new List<ItemCandidate>();
                byKey[key] = bucket;
            }
            bucket.Add(new ItemCandidate(kv.Key, doc, leftColumn, innerXml, key));
        }

        // 2. For each shared group build a stimulus and rewrite the items.
        foreach (var group in byKey.Values)
        {
            if (group.Count < minItems) continue;

            var hash = HashContent(group[0].Key);
            var identifier = $"RES-stimulus-{hash}";
            var stimulusPath = $"{refDir}stimulus-{hash}.xml";

            stimuli[stimulusPath] = BuildStimulus(identifier, hash, group[0].LeftColumn);
            stimulusIdByPath[stimulusPath] = identifier;

            foreach (var candidate in group)
            {
                var href = RelativeHref(candidate.Path, stimulusPath);

                candidate.LeftColumn.RemoveNodes();
                candidate.LeftColumn.Add(new XElement(Qti3 + "div",
                    new XAttribute("class", "qti-shared-stimulus"),
                    new XAttribute("data-stimulus-idref", identifier)));

                var refEl = new XElement(Qti3 + "qti-assessment-stimulus-ref",
                    new XAttribute("identifier", identifier),
                    new XAttribute("href", href));

                var body = candidate.Doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "qti-item-body");
                if (body != null) body.AddBeforeSelf(refEl);
                else candidate.Doc.Root!.Add(refEl);

                resultItems[candidate.Path] = Serialize(candidate.Doc);
                itemStimulusIds[candidate.Path] = identifier;
            }
        }

        // 3. Update the manifest with stimulus resources + item dependencies.
        var resultManifest = manifest;
        if (manifest != null && stimuli.Count > 0)
        {
            resultManifest = new ManifestFile(manifest.Path, UpdateManifest(manifest.Xml, stimulusIdByPath, itemStimulusIds));
        }

        return new ConvertToStimulusResult(resultItems, stimuli, resultManifest);
    }

    private static XElement? FindLeftColumn(XDocument doc)
    {
        var rows = doc.Descendants().Where(e => e.Name.LocalName == "div" && HasClass(e, "qti-layout-row"));
        foreach (var row in rows)
        {
            var cols = row.Elements()
                .Where(e => e.Name.LocalName == "div" && HasClass(e, "qti-layout-col6"))
                .ToList();
            if (cols.Count >= 2) return cols[0];
        }
        return null;
    }

    private static bool HasClass(XElement el, string cls)
    {
        var value = el.Attribute("class")?.Value ?? string.Empty;
        return value.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Contains(cls);
    }

    private static string InnerXml(XElement element) =>
        string.Concat(element.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting)));

    /// <summary>Collapse insignificant whitespace so equal content compares equal.</summary>
    private static string NormalizeContent(string html) =>
        Regex.Replace(Regex.Replace(html, @">\s+<", "><"), @"\s+", " ").Trim();

    /// <summary>FNV-1a hash as base36 — matches the TypeScript implementation.</summary>
    private static string HashContent(string content)
    {
        uint h = 0x811c9dc5;
        foreach (var c in content)
        {
            h ^= c;
            h *= 0x01000193;
        }
        return ToBase36(h);
    }

    private static string ToBase36(uint value)
    {
        if (value == 0) return "0";
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        var sb = new StringBuilder();
        while (value > 0)
        {
            sb.Insert(0, digits[(int)(value % 36)]);
            value /= 36;
        }
        return sb.ToString();
    }

    /// <summary>Package-root-relative path from the directory of <paramref name="fromPath"/> to <paramref name="toPath"/>.</summary>
    private static string RelativeHref(string fromPath, string toPath)
    {
        var fromParts = fromPath.Split('/');
        var fromDir = fromParts.Take(fromParts.Length - 1).ToList();
        var to = toPath.Split('/');

        var i = 0;
        while (i < fromDir.Count && i < to.Length - 1 && fromDir[i] == to[i]) i++;

        var ups = Enumerable.Repeat("..", fromDir.Count - i);
        var downs = to.Skip(i);
        var parts = ups.Concat(downs).ToList();
        return parts.Count > 0 ? string.Join("/", parts) : to[to.Length - 1];
    }

    private static string BuildStimulus(string identifier, string hash, XElement leftColumn)
    {
        var body = new XElement(Qti3 + "qti-stimulus-body");
        foreach (var node in leftColumn.Nodes()) body.Add(CloneNode(node));

        var root = new XElement(Qti3 + "qti-assessment-stimulus",
            new XAttribute("identifier", identifier),
            new XAttribute("title", $"stimulus-{hash}"),
            body);

        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + root.ToString(SaveOptions.None);
    }

    private static XNode CloneNode(XNode node) => node switch
    {
        XElement e => new XElement(e),
        XText t => new XText(t),
        _ => new XText(node.ToString())
    };

    private static string UpdateManifest(
        string manifestXml,
        Dictionary<string, string> stimulusIdByPath,
        Dictionary<string, string> itemStimulusIds)
    {
        var doc = XDocument.Parse(manifestXml, LoadOptions.PreserveWhitespace);
        var resources = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "resources");
        if (resources == null) return manifestXml;

        var ns = resources.Name.Namespace;

        foreach (var kv in stimulusIdByPath)
        {
            resources.Add(new XElement(ns + "resource",
                new XAttribute("identifier", kv.Value),
                new XAttribute("type", StimulusResourceType),
                new XAttribute("href", kv.Key),
                new XElement(ns + "file", new XAttribute("href", kv.Key))));
        }

        foreach (var kv in itemStimulusIds)
        {
            var resource = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "resource" && (string?)e.Attribute("href") == kv.Key);
            resource?.Add(new XElement(ns + "dependency", new XAttribute("identifierref", kv.Value)));
        }

        return Serialize(doc);
    }

    private static string Serialize(XDocument doc)
    {
        var declaration = doc.Declaration?.ToString() ?? "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";
        return declaration + "\n" + doc.Root!.ToString(SaveOptions.None);
    }
}
