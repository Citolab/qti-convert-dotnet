using System.Xml.Linq;

namespace Citolab.QTI.Converter;

internal static class QtiPackagePostProcessor
{
    private static readonly XNamespace Qti3 = "http://www.imsglobal.org/xsd/imsqtiasi_v3p0";
    private static readonly XNamespace QtiPackage3 = "http://www.imsglobal.org/xsd/qti/qtiv3p0/imscp_v1p1";

    public static void SyncAssessmentItemIdentifiers(Dictionary<string, QtiPackageFile> files, string manifestPath)
    {
        if (!files.TryGetValue(manifestPath, out var manifestFile) || manifestFile.TextContent is null) return;

        var manifest = XDocument.Parse(manifestFile.TextContent, LoadOptions.PreserveWhitespace);
        var manifestRoot = manifest.Root;
        if (manifestRoot is null) return;

        var tests = files
            .Where(kvp => kvp.Value.XmlType == QtiPackageXmlType.AssessmentTest && kvp.Value.TextContent is not null)
            .Select(kvp => (Path: kvp.Key, Xml: kvp.Value.TextContent!))
            .ToList();

        if (tests.Count == 0) return;

        var manifestChanged = false;

        foreach (var (testPath, testXml) in tests)
        {
            var testDoc = XDocument.Parse(testXml, LoadOptions.PreserveWhitespace);
            var testRoot = testDoc.Root;
            if (testRoot is null) continue;

            var testDir = GetDirectory(testPath);
            var testChanged = false;

            var itemRefs = testRoot.Descendants(Qti3 + "qti-assessment-item-ref").ToList();
            foreach (var itemRef in itemRefs)
            {
                var href = (string?)itemRef.Attribute("href");
                if (string.IsNullOrWhiteSpace(href)) continue;

                var itemPath = NormalizePath(CombinePaths(testDir, href!));
                if (!files.TryGetValue(itemPath, out var itemFile) || itemFile.TextContent is null) continue;

                var itemDoc = XDocument.Parse(itemFile.TextContent, LoadOptions.PreserveWhitespace);
                var itemRoot = itemDoc.Root;
                if (itemRoot is null) continue;

                var itemIdentifier = (string?)itemRoot.Attribute("identifier");
                if (string.IsNullOrWhiteSpace(itemIdentifier)) continue;

                var refIdentifier = (string?)itemRef.Attribute("identifier");
                if (!string.Equals(refIdentifier, itemIdentifier, StringComparison.Ordinal))
                {
                    itemRef.SetAttributeValue("identifier", itemIdentifier);
                    testChanged = true;
                }

                var resource = FindResourceByHref(manifestRoot, href!);
                if (resource is not null)
                {
                    var resourceId = (string?)resource.Attribute("identifier");
                    if (!string.Equals(resourceId, itemIdentifier, StringComparison.Ordinal))
                    {
                        resource.SetAttributeValue("identifier", itemIdentifier);
                        manifestChanged = true;
                    }
                }
            }

            if (testChanged)
            {
                files[testPath] = QtiPackageFile.FromText(testDoc.ToString(SaveOptions.DisableFormatting), QtiPackageXmlType.AssessmentTest);
            }
        }

        if (manifestChanged)
        {
            files[manifestPath] = QtiPackageFile.FromText(manifest.ToString(SaveOptions.DisableFormatting), QtiPackageXmlType.Manifest);
        }
    }

    private static XElement? FindResourceByHref(XElement manifestRoot, string href)
    {
        var normalized = NormalizePath(href);
        return manifestRoot
            .Descendants(QtiPackage3 + "resource")
            .FirstOrDefault(r => string.Equals(NormalizePath((string?)r.Attribute("href") ?? string.Empty), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetDirectory(string path)
    {
        var normalized = NormalizePath(path);
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash <= 0 ? string.Empty : normalized.Substring(0, lastSlash);
    }

    private static string CombinePaths(string left, string right)
    {
        if (string.IsNullOrEmpty(left)) return right;
        if (string.IsNullOrEmpty(right)) return left;
        return $"{left.TrimEnd('/')}/{right.TrimStart('/')}";
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
