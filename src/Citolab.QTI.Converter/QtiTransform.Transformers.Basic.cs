using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Citolab.QTI.Converter;

public sealed partial class QtiTransform
{
    private static void StripMaterialInfo(XDocument doc)
    {
        doc.Descendants().Where(e => e.Name.LocalName == "qti-companion-materials-info").Remove();
    }

    private static void MinChoicesToOne(XDocument doc)
    {
        foreach (var interaction in doc.Descendants().Where(e => e.Name.LocalName == "qti-choice-interaction"))
        {
            var minChoices = (string?)interaction.Attribute("min-choices");
            if (string.IsNullOrWhiteSpace(minChoices) || minChoices == "0")
            {
                interaction.SetAttributeValue("min-choices", "1");
            }
        }
    }

    private static void ExternalScored(XDocument doc)
    {
        var assessmentItem = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "qti-assessment-item");
        if (assessmentItem is null) return;

        if (!assessmentItem.Descendants().Any(e => e.Name.LocalName == "qti-response-processing"))
        {
            var scoreOutcome = assessmentItem
                .Descendants()
                .FirstOrDefault(e =>
                    e.Name.LocalName == "qti-outcome-declaration" &&
                    string.Equals((string?)e.Attribute("identifier"), "SCORE", StringComparison.OrdinalIgnoreCase));

            scoreOutcome?.SetAttributeValue("external-scored", "human");
        }
    }

    private static void DepConvert(XDocument doc)
    {
        var triggers = doc.Descendants()
            .Where(e => HasClass(e, "dep-dialogTrigger") && !e.Ancestors().Any(a => a.Name.LocalName == "button"))
            .ToList();

        foreach (var trigger in triggers)
        {
            var refId = (string?)trigger.Attribute("data-stimulus-idref");
            if (string.IsNullOrWhiteSpace(refId)) continue;

            if (trigger.Parent is not XElement parent || parent.Name.LocalName == "button") continue;

            var dialog = doc.Descendants().FirstOrDefault(e => string.Equals((string?)e.Attribute("id"), refId, StringComparison.Ordinal));
            dialog?.SetAttributeValue("popover", "");

            var button = new XElement(trigger.Name.Namespace + "button",
                new XAttribute("popovertarget", refId),
                new XElement(trigger));

            trigger.ReplaceWith(button);
        }
    }

    private static void DepConvertExtended(XDocument doc)
    {
        var triggers = doc.Descendants().Where(e => HasClass(e, "dep-dialogTrigger")).ToList();
        foreach (var trigger in triggers)
        {
            var refId = (string?)trigger.Attribute("data-stimulus-idref");
            if (string.IsNullOrWhiteSpace(refId)) continue;

            var dialog = doc.Descendants().FirstOrDefault(e => string.Equals((string?)e.Attribute("id"), refId, StringComparison.Ordinal));
            if (dialog is null) continue;

            var caption = (string?)dialog.Attribute("data-dep-dialog-caption") ?? string.Empty;
            var width = (string?)dialog.Attribute("data-dep-dialog-width") ?? string.Empty;
            var height = (string?)dialog.Attribute("data-dep-dialog-height") ?? string.Empty;
            var resizemode = (string?)dialog.Attribute("data-dep-dialog-resizemode") ?? string.Empty;
            var modal = (string?)dialog.Attribute("data-dep-dialog-modal") ?? string.Empty;

            var popup = new XElement(trigger.Name.Namespace + "dep-popup",
                new XAttribute("caption", caption),
                new XAttribute("width", width),
                new XAttribute("height", height),
                new XAttribute("resizemode", resizemode),
                new XAttribute("modal", modal),
                new XElement(trigger),
                new XElement(trigger.Name.Namespace + "div", new XAttribute("slot", "popup"), dialog.Nodes()));

            dialog.Remove();
            trigger.ReplaceWith(popup);
        }
    }

    private static void HideInputsForChoiceInteractionWithImages(XDocument doc)
    {
        var choiceInteractions = doc.Descendants().Where(e => e.Name.LocalName == "qti-choice-interaction").ToList();

        foreach (var choiceInteraction in choiceInteractions)
        {
            var simpleChoices = choiceInteraction.Descendants().Where(e => e.Name.LocalName == "qti-simple-choice").ToList();
            if (simpleChoices.Count == 0) continue;

            var allHaveImages = simpleChoices.All(choice => choice.Descendants().Any(e => e.Name.LocalName == "img"));
            if (!allHaveImages) continue;

            AddClass(choiceInteraction, "qti-input-control-hidden");
        }
    }

    private static void StripStylesheets(XDocument doc, string? removePattern, string? keepPattern)
    {
        var stylesheets = doc.Descendants().Where(e => e.Name.LocalName == "qti-stylesheet").ToList();
        if (stylesheets.Count == 0) return;

        if (string.IsNullOrWhiteSpace(removePattern) && string.IsNullOrWhiteSpace(keepPattern))
        {
            foreach (var stylesheet in stylesheets)
            {
                stylesheet.Remove();
            }
            return;
        }

        foreach (var stylesheet in stylesheets)
        {
            var href = (string?)stylesheet.Attribute("href");
            if (href is null || href.Trim().Length == 0) continue;

            var shouldRemove = false;
            if (!string.IsNullOrWhiteSpace(keepPattern))
            {
                shouldRemove = !MatchesPattern(href, keepPattern!);
            }
            else if (!string.IsNullOrWhiteSpace(removePattern))
            {
                shouldRemove = MatchesPattern(href, removePattern!);
            }

            if (shouldRemove)
            {
                stylesheet.Remove();
            }
        }
    }

    private static bool MatchesPattern(string filename, string pattern)
    {
        if (pattern.StartsWith("*", StringComparison.Ordinal) && pattern.EndsWith("*", StringComparison.Ordinal) && pattern.Length >= 2)
        {
            var searchTerm = pattern.Substring(1, pattern.Length - 2);
            return filename.Contains(searchTerm);
        }

        if (pattern.StartsWith("*", StringComparison.Ordinal))
        {
            var searchTerm = pattern.Substring(1);
            return filename.EndsWith(searchTerm, StringComparison.Ordinal);
        }

        if (pattern.EndsWith("*", StringComparison.Ordinal))
        {
            var searchTerm = pattern.Substring(0, pattern.Length - 1);
            return filename.StartsWith(searchTerm, StringComparison.Ordinal);
        }

        return string.Equals(filename, pattern, StringComparison.Ordinal);
    }

    private static void Suffixa(XDocument doc, string[] elements, string suffix)
    {
        var elementSet = new HashSet<string>(elements, StringComparer.Ordinal);
        foreach (var el in doc.Descendants().ToList())
        {
            if (elementSet.Contains(el.Name.LocalName))
            {
                el.Name = XName.Get($"{el.Name.LocalName}-{suffix}", el.Name.NamespaceName);
            }
        }
    }

    private static void CustomTypes(XDocument doc, string param)
    {
        foreach (var el in doc.Descendants().ToList())
        {
            var classValue = (string?)el.Attribute("class");
            if (classValue is null || classValue.Trim().Length == 0) continue;

            var tokens = classValue.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                if (token.StartsWith(param + ":", StringComparison.Ordinal))
                {
                    el.Name = XName.Get($"{el.Name.LocalName}-{token.Substring((param + ":").Length)}", el.Name.NamespaceName);
                }
            }
        }
    }

    private static void ToMathMLWebcomponents(XDocument doc)
    {
        foreach (var math in doc.Descendants().Where(e => e.Name.LocalName == "math").ToList())
        {
            math.Name = XName.Get("math-ml", math.Name.NamespaceName);

            foreach (var child in math.Descendants().ToList())
            {
                var name = child.Name.LocalName;
                if (name.Length == 0) continue;
                var withoutM = name.Length > 1 ? name.Substring(1) : string.Empty;
                child.Name = XName.Get($"math-{withoutM}", child.Name.NamespaceName);
            }
        }
    }

    private static void CustomInteraction(XDocument doc, string baseRef, string baseItem)
    {
        var customInteraction = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "qti-custom-interaction");
        if (customInteraction is null) return;

        var obj = customInteraction.Descendants().FirstOrDefault(e => e.Name.LocalName == "object");
        if (obj is null) return;

        customInteraction.SetAttributeValue("data-base-ref", baseRef);
        customInteraction.SetAttributeValue("data-base-item", baseRef + baseItem);
        customInteraction.SetAttributeValue("data", (string?)obj.Attribute("data"));
        customInteraction.SetAttributeValue("width", (string?)obj.Attribute("width"));
        customInteraction.SetAttributeValue("height", (string?)obj.Attribute("height"));

        obj.Remove();
    }

    private static void ChangeAssetLocation(XDocument doc, Func<string, string> getNewUrl, string[] srcAttributes, bool skipBase64)
    {
        foreach (var attributeName in srcAttributes)
        {
            var nodes = doc.Descendants().Where(e => e.Attribute(attributeName) is not null).ToList();
            foreach (var node in nodes)
            {
                if (node.Name.LocalName == "qti-assessment-item-ref") continue;

                var srcValue = (string?)node.Attribute(attributeName) ?? string.Empty;
                if (skipBase64 && srcValue.StartsWith("data:", StringComparison.Ordinal)) continue;

                node.SetAttributeValue(attributeName, RemoveDoubleSlashes(getNewUrl(srcValue)));
            }
        }
    }

    private static async Task ChangeAssetLocationAsync(XDocument doc, Func<string, Task<string>> getNewUrlAsync, string[] srcAttributes, bool skipBase64)
    {
        foreach (var attributeName in srcAttributes)
        {
            var nodes = doc.Descendants().Where(e => e.Attribute(attributeName) is not null).ToList();
            foreach (var node in nodes)
            {
                if (node.Name.LocalName == "qti-assessment-item-ref") continue;

                var srcValue = (string?)node.Attribute(attributeName) ?? string.Empty;
                if (skipBase64 && srcValue.StartsWith("data:", StringComparison.Ordinal)) continue;

                var newValue = await getNewUrlAsync(srcValue).ConfigureAwait(false);
                node.SetAttributeValue(attributeName, RemoveDoubleSlashes(newValue));
            }
        }
    }

    private static string RemoveDoubleSlashes(string str)
    {
        var singleForwardSlashes = Regex.Replace(str, @"([^:]\/)\/+", "$1", RegexOptions.Singleline);
        singleForwardSlashes = singleForwardSlashes.Replace("//", "/");
        singleForwardSlashes = singleForwardSlashes.Replace("http:/", "http://").Replace("https:/", "https://");
        return singleForwardSlashes;
    }

    private static readonly HttpClient DefaultHttpClient = new HttpClient();

    private static async Task StylesheetsInlineAsync(XDocument doc, Func<string, Task<string?>>? getStylesheetContentAsync = null, string? basePath = null)
    {
        getStylesheetContentAsync ??= FetchStylesheetContentAsync;

        var stylesheetElements = doc.Descendants()
            .Where(e => e.Name.LocalName == "qti-stylesheet")
            .ToList();

        foreach (var stylesheet in stylesheetElements)
        {
            var href = (string?)stylesheet.Attribute("href");
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            try
            {
                var cssContent = await GetStylesheetContent(href!, getStylesheetContentAsync, basePath).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(cssContent))
                {
                    stylesheet.RemoveNodes();
                    stylesheet.Add(new XText(cssContent));
                }
            }
            catch (Exception ex)
            {
                // Log warning but don't fail the entire transformation
                Console.WriteLine($"Warning: Failed to inline stylesheet \"{href}\": {ex.Message}");
            }
        }
    }

    private static async Task StylesheetsInlineAsync(XDocument doc, Func<string, string, Task<string?>>? getFileContentAsync = null, string? itemPath = null)
    {
        var stylesheetElements = doc.Descendants()
            .Where(e => e.Name.LocalName == "qti-stylesheet")
            .ToList();

        foreach (var stylesheet in stylesheetElements)
        {
            var href = (string?)stylesheet.Attribute("href");
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            try
            {
                string? cssContent = null;

                if (IsUrl(href!))
                {
                    // Handle HTTP/HTTPS URLs
                    cssContent = await FetchStylesheetContentAsync(href!).ConfigureAwait(false);
                }
                else if (getFileContentAsync != null && !string.IsNullOrEmpty(itemPath))
                {
                    // Handle relative paths using the file content resolver
                    var resolvedPath = ResolveRelativePath(href!, itemPath!);
                    cssContent = await getFileContentAsync(resolvedPath, itemPath!).ConfigureAwait(false);
                }
                else if (!string.IsNullOrEmpty(itemPath))
                {
                    // Fallback: try to read as file relative to item path
                    var resolvedPath = ResolveRelativePath(href!, itemPath!);
                    cssContent = await ReadFileContentAsync(resolvedPath).ConfigureAwait(false);
                }

                if (!string.IsNullOrEmpty(cssContent))
                {
                    stylesheet.RemoveNodes();
                    stylesheet.Add(new XText(cssContent));
                }
            }
            catch (Exception ex)
            {
                // Log warning but don't fail the entire transformation
                Console.WriteLine($"Warning: Failed to inline stylesheet \"{href}\": {ex.Message}");
            }
        }
    }

    private static async Task<string?> GetStylesheetContent(string href, Func<string, Task<string?>> getStylesheetContentAsync, string? basePath)
    {
        if (IsUrl(href))
        {
            return await getStylesheetContentAsync(href).ConfigureAwait(false);
        }

        // Handle relative path
        if (!string.IsNullOrEmpty(basePath))
        {
            var resolvedPath = Path.Combine(basePath, href.Replace('/', Path.DirectorySeparatorChar));
            return await ReadFileContentAsync(resolvedPath).ConfigureAwait(false);
        }

        // Fallback to treating as URL
        return await getStylesheetContentAsync(href).ConfigureAwait(false);
    }

    private static bool IsUrl(string href)
    {
        return href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               href.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveRelativePath(string href, string itemPath)
    {
        // Get the directory of the item file
        var itemDirectory = Path.GetDirectoryName(itemPath) ?? string.Empty;
        
        // Combine with the stylesheet href, converting forward slashes to system separators
        var normalizedHref = href.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(itemDirectory, normalizedHref).Replace('\\', '/');
    }

    private static async Task<string?> ReadFileContentAsync(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
#if NETSTANDARD2_0
                return await Task.Run(() => File.ReadAllText(filePath)).ConfigureAwait(false);
#else
                return await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
#endif
            }
        }
        catch
        {
            // Ignore file access errors
        }
        return null;
    }

    private static async Task<string?> FetchStylesheetContentAsync(string href)
    {
        using var response = await DefaultHttpClient.GetAsync(href).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Failed to fetch stylesheet: {href} (Status: {response.StatusCode})");
        }

        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }
}

