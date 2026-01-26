using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Citolab.QTI.Converter;

public sealed partial class QtiTransform
{
    private static void QbCleanup(XDocument doc)
    {
        var itemBody = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "qti-item-body");
        if (itemBody is null) return;

        RemoveClass(itemBody, "defaultBody");
        if (string.Equals((string?)itemBody.Attribute("class"), string.Empty, StringComparison.Ordinal))
        {
            itemBody.SetAttributeValue("class", null);
        }

        foreach (var text in itemBody.DescendantNodesAndSelf().OfType<XText>())
        {
            if (text.Value.IndexOf('\u00A0') >= 0)
            {
                text.Value = text.Value.Replace('\u00A0', ' ');
            }
        }

        foreach (var content in doc.Descendants().Where(e => HasClass(e, "content")).ToList())
        {
            content.SetAttributeValue("class", "container");
        }

        UnwrapDivById(doc, "leftbody");
        UnwrapDivById(doc, "body");
        UnwrapDivById(doc, "mc");
        UnwrapDivById(doc, "question");

        foreach (var pInTd in doc.Descendants().Where(e => e.Name.LocalName == "p" && e.Parent?.Name.LocalName == "td").ToList())
        {
            UnwrapElement(pInTd);
        }

        foreach (var citoDiv in doc.Descendants().Where(e => e.Name.LocalName == "div" && ((string?)e.Attribute("class"))?.StartsWith("cito_genclass", StringComparison.Ordinal) == true).ToList())
        {
            UnwrapElement(citoDiv);
        }

        CleanupSpans(doc);
        CleanupUserSRVetBoldNesting(doc);

        foreach (var p in doc.Descendants().Where(e => e.Name.LocalName == "p").ToList())
        {
            var inner = string.Concat(p.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting))).Trim();
            if (inner == string.Empty)
            {
                p.Remove();
            }
        }

        foreach (var div in doc.Descendants().Where(e =>
                     e.Name.LocalName == "div" &&
                     e.Parent?.Name.LocalName == "qti-content-body" &&
                     e.Parent?.Parent?.Name.LocalName == "qti-rubric-block").ToList())
        {
            var innerHtml = string.Concat(div.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting))).Trim();
            var textContent = div.Value.Trim();

            if (string.IsNullOrWhiteSpace(textContent) || innerHtml == "<br/>")
            {
                div.Remove();
            }
            else
            {
                var first = div.Nodes().FirstOrDefault();
                if (first is XElement { Name.LocalName: "br" })
                {
                    first.Remove();
                }

                var last = div.Nodes().LastOrDefault();
                if (last is XElement { Name.LocalName: "br" })
                {
                    last.Remove();
                }
            }
        }

        var columns = doc.Descendants().Where(e => HasClass(e, "qti-layout-col6")).ToList();
        foreach (var column in columns)
        {
            var text = column.Value.Replace("\u00A0", string.Empty).Trim();
            if (text != string.Empty) continue;

            var hasNonTextContent = column
                .Descendants()
                .Any(el =>
                {
                    var tagName = el.Name.LocalName.ToLowerInvariant();
                    if (tagName.StartsWith("qti-", StringComparison.Ordinal)) return true;
                    if (tagName.StartsWith("dep:", StringComparison.Ordinal)) return true;
                    if (tagName is "video" or "audio" or "img" or "object" or "iframe" or "embed" or "svg" or "math" or "table") return true;
                    return el.HasAttributes;
                });

            if (!hasNonTextContent)
            {
                column.RemoveNodes();
                column.Add(new XText("\u00A0"));
            }
        }

        foreach (var choiceInteraction in doc.Descendants().Where(e => e.Name.LocalName == "qti-choice-interaction").ToList())
        {
            var currentClasses = (string?)choiceInteraction.Attribute("class") ?? string.Empty;
            var newClasses = currentClasses;

            if (currentClasses.Contains("two-columns")) newClasses = newClasses.Replace("two-columns", "qti-choices-stacking-2");
            if (currentClasses.Contains("three-columns")) newClasses = newClasses.Replace("three-columns", "qti-choices-stacking-3");
            if (currentClasses.Contains("four-columns")) newClasses = newClasses.Replace("four-columns", "qti-choices-stacking-4");
            if (currentClasses.Contains("five-columns")) newClasses = newClasses.Replace("five-columns", "qti-choices-stacking-5");
            if (currentClasses.Contains("one-column")) newClasses = newClasses.Replace("one-column", "qti-choices-stacking-1");

            if (currentClasses.Contains("horizontal")) newClasses = Regex.Replace(newClasses, "\\bhorizontal\\b", "qti-orientation-horizontal");
            if (currentClasses.Contains("vertical")) newClasses = Regex.Replace(newClasses, "\\bvertical\\b", "qti-orientation-vertical");

            newClasses = Regex.Replace(newClasses, "\\bcenteralign\\b", string.Empty).Trim();
            newClasses = Regex.Replace(newClasses, "\\s+", " ").Trim();

            if (!string.Equals(newClasses, currentClasses, StringComparison.Ordinal))
            {
                choiceInteraction.SetAttributeValue("class", string.IsNullOrWhiteSpace(newClasses) ? null : newClasses);
            }
        }

        var variableIds = doc.Descendants().Where(e => e.Name.LocalName == "qti-set-outcome-value")
            .Select(e => (string?)e.Attribute("identifier"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var responseProcessingList = doc.Descendants().Where(e => e.Name.LocalName == "qti-response-processing").ToList();
        foreach (var responseProcessing in responseProcessingList)
        {
            foreach (var variableId in variableIds)
            {
                var reset = new XElement(responseProcessing.Name.Namespace + "qti-set-outcome-value",
                    new XAttribute("identifier", variableId!),
                    new XElement(responseProcessing.Name.Namespace + "qti-base-value",
                        new XAttribute("base-type", "integer"),
                        "0"));
                responseProcessing.AddFirst(reset);
            }
        }
    }

    private static void CleanupSpans(XDocument doc)
    {
        var changed = true;
        while (changed)
        {
            changed = false;

            var spans = doc.Descendants().Where(e => e.Name.LocalName == "span").ToList();
            foreach (var span in spans)
            {
                if (span.Parent is null) continue;

                var textContent = span.Value.Trim();
                var htmlContent = string.Concat(span.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting))).Trim();

                var hasQtiElements = span
                    .Descendants()
                    .Any(e =>
                    {
                        var local = e.Name.LocalName;
                        if (local is "qti-gap" or "qti-gap-text" or "qti-gap-match-interaction" or "qti-extended-text-interaction" or "qti-choice-interaction" or
                            "qti-text-entry-interaction" or "qti-inline-choice-interaction" or "qti-hottext-interaction" or "qti-order-interaction" or
                            "qti-associate-interaction" or "qti-match-interaction" or "qti-hotspot-interaction" or "qti-select-point-interaction" or
                            "qti-graphic-order-interaction" or "qti-graphic-associate-interaction" or "qti-graphic-gap-match-interaction" or
                            "qti-position-object-interaction" or "qti-slider-interaction" or "qti-draw-interaction" or "qti-upload-interaction")
                        {
                            return true;
                        }

                        var cls = (string?)e.Attribute("class") ?? string.Empty;
                        var id = (string?)e.Attribute("id") ?? string.Empty;
                        return cls.Contains("qti-") || id.Contains("qti-");
                    });

                var hasMediaElements = span
                    .Descendants()
                    .Any(e => e.Name.LocalName is "img" or "video" or "audio" or "object" or "iframe" or "embed" or "svg" or "math" or "table" or "qti-media-interaction");

                if (string.IsNullOrWhiteSpace(textContent) && hasMediaElements)
                {
                    if (!span.HasAttributes)
                    {
                        UnwrapElement(span);
                        changed = true;
                    }
                    continue;
                }

                if ((string.IsNullOrWhiteSpace(textContent) || htmlContent == string.Empty || htmlContent == "&nbsp;") && !hasQtiElements)
                {
                    span.Remove();
                    changed = true;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(textContent) && Regex.IsMatch(htmlContent, @"^[\s&nbsp;]*$") && !hasQtiElements)
                {
                    span.Remove();
                    changed = true;
                    continue;
                }

                if (span.HasAttributes)
                {
                    continue;
                }

                var childSpans = span.Elements().Where(e => e.Name.LocalName == "span").ToList();
                if (childSpans.Count == 1)
                {
                    var childSpan = childSpans[0];
                    var childText = childSpan.Value.Trim();
                    if (!string.IsNullOrWhiteSpace(childText))
                    {
                        var outerContents = span.Nodes().ToList();
                        var hasOnlyWhitespaceAndOneSpan = true;

                        foreach (var node in outerContents)
                        {
                            if (node is XElement element && element.Name.LocalName == "span")
                            {
                                continue;
                            }
                            if (node is XText t)
                            {
                                if (!string.IsNullOrWhiteSpace(t.Value))
                                {
                                    hasOnlyWhitespaceAndOneSpan = false;
                                }
                                continue;
                            }
                            hasOnlyWhitespaceAndOneSpan = false;
                        }

                        if (hasOnlyWhitespaceAndOneSpan)
                        {
                            span.ReplaceWith(childSpan.Nodes());
                            changed = true;
                            continue;
                        }
                    }
                }

                var parent = span.Parent;
                if (childSpans.Count == 1)
                {
                    var childSpan = childSpans[0];
                    var childText = childSpan.Value.Trim();
                    var childHtml = string.Concat(childSpan.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting))).Trim();
                    if (string.IsNullOrWhiteSpace(childText) || childHtml == string.Empty || childHtml == "&nbsp;")
                    {
                        childSpan.Remove();
                        if (string.IsNullOrWhiteSpace(span.Value.Trim()))
                        {
                            span.Remove();
                            changed = true;
                            continue;
                        }
                    }
                }

                if (childSpans.Count == 0 && !string.IsNullOrWhiteSpace(textContent))
                {
                    var parentTagName = parent?.Name.LocalName.ToLowerInvariant();
                    var semanticParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "strong", "em", "b", "i", "u", "mark", "small", "del", "ins", "sub", "sup"
                    };

                    var isInSemanticParent = parentTagName is not null && semanticParents.Contains(parentTagName);
                    var hasPrevElementSibling = span.ElementsBeforeSelf().Any();
                    var hasNextElementSibling = span.ElementsAfterSelf().Any();

                    if (!isInSemanticParent && parentTagName == "p" && !hasPrevElementSibling && !hasNextElementSibling)
                    {
                        UnwrapElement(span);
                        changed = true;
                        continue;
                    }
                }
            }
        }
    }

    private static void CleanupUserSRVetBoldNesting(XDocument doc)
    {
        foreach (var el in doc.Descendants().Where(e => HasClass(e, "UserSRVet") && e.Ancestors().Any(a => a.Name.LocalName == "strong")).ToList())
        {
            RemoveClass(el, "UserSRVet");
            if (string.IsNullOrWhiteSpace((string?)el.Attribute("class")))
            {
                el.SetAttributeValue("class", null);
            }
        }

        foreach (var userEl in doc.Descendants().Where(e => HasClass(e, "UserSRVet")).ToList())
        {
            var strongs = userEl.Descendants().Where(e => e.Name.LocalName == "strong").ToList();
            foreach (var strong in strongs)
            {
                if (strong.NextNode is XElement && !strong.Value.EndsWith(" ", StringComparison.Ordinal))
                {
                    strong.AddAfterSelf(new XText(" "));
                }
                UnwrapElement(strong);
            }
        }
    }

    private static void UnwrapDivById(XDocument doc, string id)
    {
        foreach (var div in doc.Descendants().Where(e => e.Name.LocalName == "div" && string.Equals((string?)e.Attribute("id"), id, StringComparison.Ordinal)).ToList())
        {
            UnwrapElement(div);
        }
    }
}

