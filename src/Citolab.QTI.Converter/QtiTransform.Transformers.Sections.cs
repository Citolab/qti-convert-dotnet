using System.Xml.Linq;

namespace Citolab.QTI.Converter;

/// <summary>
/// Metadata for an assessment-item-ref, supplied by the caller because the items are separate
/// files that the test document does not contain.
/// </summary>
public sealed class ItemRefNormalizationMeta
{
    public ItemRefNormalizationMeta(bool isInfo, string? stimulusIdentifier, string? title)
    {
        IsInfo = isInfo;
        StimulusIdentifier = stimulusIdentifier;
        Title = title;
    }

    public bool IsInfo { get; }
    public string? StimulusIdentifier { get; }
    public string? Title { get; }
}

/// <summary>Options for <see cref="QtiTransform.WrapStimulusInSectionAsync"/>.</summary>
public sealed class WrapStimulusInSectionOptions
{
    /// <summary>Item-ref <c>category</c> values (substring match, case-insensitive) marking info items.</summary>
    public string[]? InfoCategories { get; set; }

    /// <summary>Collects the produced item-identifier -> section-identifier assignments.</summary>
    public IList<(string ItemIdentifier, string SectionIdentifier)>? AssignmentsOut { get; set; }
}

public sealed partial class QtiTransform
{
    private const string NavigationEntityAttr = "data-navigation-entity";
    private const string LegacyNavigateAttr = "data-cito-navigate";

    private sealed class ResolvedSectionMeta
    {
        public ResolvedSectionMeta(string? stimulusIdentifier, string title, bool isInfo)
        {
            StimulusIdentifier = stimulusIdentifier;
            Title = title;
            IsInfo = isInfo;
        }

        public string? StimulusIdentifier { get; }
        public string Title { get; }
        public bool IsInfo { get; }
    }

    /// <summary>
    /// Rewrites the assessment test so each navigation step maps to one assessment-section:
    /// consecutive items sharing the same stimulus become one section with
    /// <c>keep-together="true"</c>, info items stay isolated, every other item becomes a
    /// single-item section, and the test part is marked <c>data-navigation-entity="section"</c>.
    /// Test parts already shaped as a single wrapping section are left untouched. The document is
    /// only mutated when at least one 2+ shared-stimulus cluster exists. Returns whether it changed.
    /// </summary>
    private static async Task<bool> WrapStimulusInSection(
        XDocument doc,
        Func<string, string, Task<ItemRefNormalizationMeta?>> resolver,
        WrapStimulusInSectionOptions? options)
    {
        var root = doc.Root;
        if (root == null) return false;

        var localRoot = root.Name.LocalName;
        if (localRoot is not ("qti-assessment-test" or "assessmentTest")) return false;

        var infoCategories = (options?.InfoCategories ?? new[] { "dep-informational", "dep-info" })
            .Select(c => c.ToLowerInvariant())
            .ToArray();

        var allParts = root.Elements()
            .Where(e => e.Name.LocalName is "qti-test-part" or "testPart")
            .ToList();

        // A part with a single wrapping section (and no direct item-refs) is already section-shaped.
        var parts = allParts
            .Where(part =>
            {
                var directSections = part.Elements()
                    .Count(e => e.Name.LocalName is "qti-assessment-section" or "assessmentSection");
                var directRefs = part.Elements()
                    .Count(e => e.Name.LocalName is "qti-assessment-item-ref" or "assessmentItemRef");
                return !(directSections == 1 && directRefs == 0);
            })
            .ToList();

        // Pre-resolve metadata for every item-ref (async), then rewrite synchronously.
        var metaByEl = new Dictionary<XElement, ResolvedSectionMeta>();
        var refsByPart = new Dictionary<XElement, List<XElement>>();
        foreach (var part in parts)
        {
            var refs = part.Descendants()
                .Where(e => e.Name.LocalName is "qti-assessment-item-ref" or "assessmentItemRef")
                .ToList();
            refsByPart[part] = refs;

            foreach (var r in refs)
            {
                var identifier = (r.Attribute("identifier")?.Value ?? string.Empty).Trim();
                var href = (r.Attribute("href")?.Value ?? string.Empty).Trim();
                var category = (r.Attribute("category")?.Value ?? string.Empty).ToLowerInvariant();
                var infoByCategory = category.Length > 0 && infoCategories.Any(c => category.Contains(c));

                var resolved = identifier.Length > 0
                    ? await resolver(href, identifier).ConfigureAwait(false)
                    : null;

                metaByEl[r] = new ResolvedSectionMeta(
                    string.IsNullOrWhiteSpace(resolved?.StimulusIdentifier) ? null : resolved!.StimulusIdentifier!.Trim(),
                    string.IsNullOrWhiteSpace(resolved?.Title) ? string.Empty : resolved!.Title!.Trim(),
                    infoByCategory || resolved?.IsInfo == true);
            }
        }

        var plans = parts
            .Select(part => (Part: part, Clusters: BuildSectionClusters(refsByPart[part], metaByEl)))
            .Where(p => refsByPart[p.Part].Count > 0)
            .ToList();

        if (!plans.Any(p => HasSharedStimulusSection(p.Clusters, metaByEl)))
        {
            return false;
        }

        foreach (var plan in plans)
        {
            NormalizeTestPart(plan.Part, plan.Clusters, metaByEl, options?.AssignmentsOut);
        }

        return true;
    }

    private static List<List<XElement>> BuildSectionClusters(
        List<XElement> orderedRefs,
        Dictionary<XElement, ResolvedSectionMeta> metaByEl)
    {
        var sections = new List<List<XElement>>();
        var i = 0;

        while (i < orderedRefs.Count)
        {
            var meta = metaByEl[orderedRefs[i]];

            if (meta.IsInfo || string.IsNullOrEmpty(meta.StimulusIdentifier))
            {
                sections.Add(new List<XElement> { orderedRefs[i] });
                i++;
                continue;
            }

            var stimulus = meta.StimulusIdentifier;
            var group = new List<XElement> { orderedRefs[i] };
            i++;

            while (i < orderedRefs.Count)
            {
                var next = metaByEl[orderedRefs[i]];
                if (next.IsInfo || string.IsNullOrEmpty(next.StimulusIdentifier)) break;
                if (string.Equals(next.StimulusIdentifier, stimulus, StringComparison.Ordinal))
                {
                    group.Add(orderedRefs[i]);
                    i++;
                    continue;
                }

                break;
            }

            sections.Add(group);
        }

        return sections;
    }

    private static bool HasSharedStimulusSection(
        List<List<XElement>> clusters,
        Dictionary<XElement, ResolvedSectionMeta> metaByEl)
    {
        foreach (var group in clusters.Where(g => g.Count >= 2))
        {
            var first = metaByEl[group[0]];
            if (first.IsInfo || string.IsNullOrEmpty(first.StimulusIdentifier)) continue;

            if (group.All(r =>
                metaByEl[r] is { IsInfo: false } m
                && string.Equals(m.StimulusIdentifier, first.StimulusIdentifier, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static void NormalizeTestPart(
        XElement testPart,
        List<List<XElement>> clusters,
        Dictionary<XElement, ResolvedSectionMeta> metaByEl,
        IList<(string ItemIdentifier, string SectionIdentifier)>? assignmentsOut)
    {
        var templateSection = testPart.Descendants()
            .FirstOrDefault(e => e.Name.LocalName is "qti-assessment-section" or "assessmentSection");
        XName sectionElementName = templateSection?.Name
            ?? XName.Get("qti-assessment-section", testPart.Name.Namespace.NamespaceName);

        foreach (var group in clusters)
        {
            foreach (var r in group) r.Remove();
        }

        foreach (var child in testPart.Elements().ToList())
        {
            child.Remove();
        }

        ApplyStudentDeliveryHints(testPart);

        var partKey = testPart.Attribute("identifier")?.Value?.Trim();
        var sectionPrefix = string.IsNullOrEmpty(partKey) ? "PART" : SanitizeSectionPrefix(partKey!);

        var sectionIndex = 0;
        foreach (var group in clusters)
        {
            sectionIndex++;
            var sectionId = $"{sectionPrefix}-SEC-{sectionIndex}";
            var keepTogether = group.Count >= 2;
            var sectionTitle = metaByEl[group[0]].Title;

            var section = new XElement(sectionElementName);
            section.SetAttributeValue("identifier", sectionId);
            section.SetAttributeValue("visible", "true");
            if (!string.IsNullOrEmpty(sectionTitle)) section.SetAttributeValue("title", sectionTitle);
            if (keepTogether) section.SetAttributeValue("keep-together", "true");

            foreach (var r in group)
            {
                section.Add(r);
                var itemId = r.Attribute("identifier")?.Value;
                if (!string.IsNullOrWhiteSpace(itemId))
                {
                    assignmentsOut?.Add((itemId!.Trim(), sectionId));
                }
            }

            testPart.Add(section);
        }
    }

    private static void ApplyStudentDeliveryHints(XElement testPart)
    {
        SetSectionAttributeIfMissing(testPart, "navigation-mode", "nonlinear");
        SetSectionAttributeIfMissing(testPart, "submission-mode", "simultaneous");

        testPart.Attributes()
            .FirstOrDefault(a => a.Name.Namespace == XNamespace.None && a.Name.LocalName == LegacyNavigateAttr)
            ?.Remove();
        testPart.SetAttributeValue(XNamespace.None + NavigationEntityAttr, "section");
    }

    private static void SetSectionAttributeIfMissing(XElement element, string localName, string value)
    {
        var attr = element.Attributes()
            .FirstOrDefault(a => a.Name.Namespace == XNamespace.None && a.Name.LocalName == localName);
        if (attr == null)
        {
            element.SetAttributeValue(localName, value);
        }
    }

    /// <summary>Prefix section identifiers so multi-part tests cannot collide on SEC-n ids.</summary>
    private static string SanitizeSectionPrefix(string identifier)
    {
        var trimmed = identifier.Trim();
        if (trimmed.Length == 0) return "PART";

        var filtered = trimmed.ToUpperInvariant()
            .Where(ch =>
                (ch >= 'A' && ch <= 'Z')
                || (ch >= '0' && ch <= '9')
                || ch == '_'
                || ch == '-')
            .Take(120)
            .ToArray();

        return filtered.Length == 0 ? "PART" : new string(filtered);
    }
}
