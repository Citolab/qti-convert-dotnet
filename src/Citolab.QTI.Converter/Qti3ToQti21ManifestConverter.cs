using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Citolab.QTI.Converter;

/// <summary>
/// Converts a QTI 3 imsmanifest.xml to QTI 2.1. Stimulus resources whose file was inlined into the items are
/// removed; their dependencies and files move to the items that depended on them.
/// </summary>
public static class Qti3ToQti21ManifestConverter
{
    private static readonly XNamespace ImsCp3 = "http://www.imsglobal.org/xsd/qti/qtiv3p0/imscp_v1p1";
    private static readonly XNamespace ImsCp21 = "http://www.imsglobal.org/xsd/imscp_v1p1";
    private static readonly XNamespace QtiMetadata3 = "http://www.imsglobal.org/xsd/imsqti_metadata_v3p0";
    private static readonly XNamespace QtiMetadata21 = "http://www.imsglobal.org/xsd/imsqti_metadata_v2p1";
    private static readonly Regex StimulusResourceType = new("stimulus", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string SchemaLocation = string.Join(" ",
        $"{ImsCp21.NamespaceName} http://www.imsglobal.org/xsd/qti/qtiv2p1/qtiv2p1_imscpv1p2_v1p0.xsd",
        $"{QtiMetadata21.NamespaceName} http://www.imsglobal.org/xsd/qti/qtiv2p1/imsqti_metadata_v2p1p1.xsd",
        "http://ltsc.ieee.org/xsd/LOM http://www.imsglobal.org/xsd/imsmd_loose_v1p3p2.xsd");

    public static string Convert(string manifestXml, ISet<string>? inlinedStimulusPaths = null)
    {
        inlinedStimulusPaths ??= new HashSet<string>();
        var doc = XDocument.Parse(manifestXml, LoadOptions.PreserveWhitespace);
        var root = doc.Root;
        if (root is null) return manifestXml;

        foreach (var el in root.DescendantsAndSelf())
        {
            foreach (var declaration in el.Attributes().Where(a => a.IsNamespaceDeclaration))
            {
                if (declaration.Value == ImsCp3.NamespaceName) declaration.Value = ImsCp21.NamespaceName;
                else if (declaration.Value == QtiMetadata3.NamespaceName) declaration.Value = QtiMetadata21.NamespaceName;
            }
            if (el.Name.Namespace == ImsCp3) el.Name = ImsCp21 + el.Name.LocalName;
            else if (el.Name.Namespace == QtiMetadata3) el.Name = QtiMetadata21 + el.Name.LocalName;
        }

        root.SetAttributeValue(XNamespace.Xmlns + "xsi", QtiNames.Xsi.NamespaceName);
        root.SetAttributeValue(QtiNames.Xsi + "schemaLocation", SchemaLocation);
        var metadata = root.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata");
        if (metadata is not null)
        {
            foreach (var (name, value) in new[] { ("schema", "QTIv2.1 Package"), ("schemaversion", "1.0.0") })
            {
                var existing = metadata.Descendants().FirstOrDefault(e => e.Name.LocalName == name);
                if (existing is not null) existing.Value = value;
                else metadata.Add(new XElement(metadata.Name.Namespace + name, value));
            }
        }

        var resources = root.Descendants().Where(e => e.Name.LocalName == "resource").ToList();
        var inlinedStimuli = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            var type = (string?)resource.Attribute("type") ?? string.Empty;
            if (StimulusResourceType.IsMatch(type))
            {
                if (inlinedStimulusPaths.Contains(QtiPackagePath.Normalize((string?)resource.Attribute("href") ?? string.Empty)))
                {
                    inlinedStimuli[(string?)resource.Attribute("identifier") ?? string.Empty] = resource;
                }
                else
                {
                    resource.SetAttributeValue("type", "webcontent");
                }
            }
            else if (type.EndsWith("xmlv3p0", StringComparison.Ordinal))
            {
                resource.SetAttributeValue("type", type.Substring(0, type.Length - "xmlv3p0".Length) + "xmlv2p1");
            }
        }

        foreach (var resource in resources.Where(r => !inlinedStimuli.ContainsValue(r)))
        {
            foreach (var dependency in resource.Elements().Where(e => e.Name.LocalName == "dependency").ToList())
            {
                if (!inlinedStimuli.TryGetValue((string?)dependency.Attribute("identifierref") ?? string.Empty, out var stimulus)) continue;

                var ns = dependency.Name.Namespace;
                var existingDependencies = new HashSet<string?>(resource.Elements(ns + "dependency").Select(d => (string?)d.Attribute("identifierref")));
                var existingFiles = new HashSet<string>(resource.Elements(ns + "file").Select(f => QtiPackagePath.Normalize((string?)f.Attribute("href") ?? string.Empty)));
                var stimulusHref = QtiPackagePath.Normalize((string?)stimulus.Attribute("href") ?? string.Empty);
                foreach (var file in stimulus.Elements().Where(e => e.Name.LocalName == "file"))
                {
                    var href = QtiPackagePath.Normalize((string?)file.Attribute("href") ?? string.Empty);
                    if (href != stimulusHref && existingFiles.Add(href))
                    {
                        // a resource holds metadata?, file*, dependency* in that order
                        var previous = resource.Elements(ns + "file").LastOrDefault() ?? resource.Element(ns + "metadata");
                        var newFile = new XElement(ns + "file", new XAttribute("href", href));
                        if (previous is not null) previous.AddAfterSelf(newFile);
                        else resource.AddFirst(newFile);
                    }
                }
                foreach (var stimulusDependency in stimulus.Elements().Where(e => e.Name.LocalName == "dependency"))
                {
                    var identifierRef = (string?)stimulusDependency.Attribute("identifierref");
                    if (existingDependencies.Add(identifierRef))
                    {
                        dependency.AddBeforeSelf(new XElement(ns + "dependency", new XAttribute("identifierref", identifierRef ?? string.Empty)));
                    }
                }
                dependency.Remove();
            }
        }
        foreach (var stimulus in inlinedStimuli.Values) stimulus.Remove();

        return Serialize(doc);
    }

    /// <summary>Registers the shared vocabulary stylesheet as a webcontent resource and a dependency of the given items.</summary>
    public static string AddSharedVocabularyStylesheet(string manifestXml, string manifestPath, string stylesheetPath, IEnumerable<string> itemPaths)
    {
        var doc = XDocument.Parse(manifestXml, LoadOptions.PreserveWhitespace);
        var resourcesElement = doc.Root?.Descendants().FirstOrDefault(e => e.Name.LocalName == "resources");
        if (resourcesElement is null) return manifestXml;

        var ns = resourcesElement.Name.Namespace;
        var manifestDirectory = QtiPackagePath.DirectoryName(manifestPath);
        string PackagePathOf(XElement resource) => QtiPackagePath.Join(manifestDirectory, (string?)resource.Attribute("href") ?? string.Empty);

        var resources = resourcesElement.Elements().Where(e => e.Name.LocalName == "resource").ToList();
        var existing = resources.FirstOrDefault(r => PackagePathOf(r) == QtiPackagePath.Normalize(stylesheetPath));
        var identifier = (string?)existing?.Attribute("identifier") ?? "QTI3_SHARED_VOCABULARY_CSS";
        if (existing is null)
        {
            var href = QtiPackagePath.Relative(manifestDirectory, stylesheetPath);
            resourcesElement.Add(new XElement(ns + "resource",
                new XAttribute("identifier", identifier), new XAttribute("type", "webcontent"), new XAttribute("href", href),
                new XElement(ns + "file", new XAttribute("href", href))));
        }

        var items = new HashSet<string>(itemPaths.Select(QtiPackagePath.Normalize), StringComparer.OrdinalIgnoreCase);
        foreach (var resource in resources.Where(r => items.Contains(PackagePathOf(r))))
        {
            if (!resource.Elements().Any(e => e.Name.LocalName == "dependency" && (string?)e.Attribute("identifierref") == identifier))
            {
                resource.Add(new XElement(ns + "dependency", new XAttribute("identifierref", identifier)));
            }
        }
        return Serialize(doc);
    }

    private static string Serialize(XDocument doc) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        string.Join("\n", doc.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting)));
}
