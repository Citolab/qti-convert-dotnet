using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Citolab.QTI.Converter;

public sealed partial class QtiTransform
{
    private static async Task ConfigurePciAsync(XDocument doc, string baseUrl, Func<string, Task<ModuleResolutionConfig?>> getModuleResolutionConfig)
    {
        var customInteractionTypeIdentifiers = new List<string>();
        var interactions = doc.Descendants().Where(e => e.Name.LocalName == "qti-portable-custom-interaction").ToList();

        var moduleResolutionConfig = await getModuleResolutionConfig("/modules/module_resolution.js").ConfigureAwait(false);
        var moduleResolutionFallbackConfig = await getModuleResolutionConfig("/modules/fallback_module_resolution.js").ConfigureAwait(false);

        foreach (var interaction in interactions)
        {
            interaction.SetAttributeValue("data-base-url", baseUrl);

            var customInteractionTypeIdentifier = (string?)interaction.Attribute("custom-interaction-type-identifier") ?? string.Empty;
            if (customInteractionTypeIdentifiers.Contains(customInteractionTypeIdentifier))
            {
                customInteractionTypeIdentifier = customInteractionTypeIdentifier + customInteractionTypeIdentifiers.Count;
                interaction.SetAttributeValue("custom-interaction-type-identifier", customInteractionTypeIdentifier);
                customInteractionTypeIdentifiers.Add(customInteractionTypeIdentifier);
            }
            customInteractionTypeIdentifiers.Add(customInteractionTypeIdentifier);

            var existingModulesElement = interaction.Descendants().FirstOrDefault(e => e.Name.LocalName == "qti-interaction-modules");

            var primaryConfiguration = (string?)existingModulesElement?.Attribute("primary-configuration");
            if (existingModulesElement is not null && !string.IsNullOrWhiteSpace(primaryConfiguration))
            {
                var primaryConfig = await getModuleResolutionConfig("/" + primaryConfiguration).ConfigureAwait(false);
                if (primaryConfig?.Paths is null) continue;

                var existingModules = existingModulesElement.Descendants().Where(e => e.Name.LocalName == "qti-interaction-module").ToList();
                foreach (var moduleEl in existingModules)
                {
                    var moduleId = (string?)moduleEl.Attribute("id");
                    if (moduleId is null || moduleId.Trim().Length == 0) continue;

                    if (primaryConfig.Paths.TryGetValue(moduleId, out var paths) && paths is not null && paths.Length > 0)
                    {
                        moduleEl.SetAttributeValue("primary-path", paths[0]);

                        if (moduleResolutionFallbackConfig?.Paths is not null &&
                            moduleResolutionFallbackConfig.Paths.TryGetValue(moduleId, out var fallbackPaths) &&
                            fallbackPaths is not null && fallbackPaths.Length > 0)
                        {
                            moduleEl.SetAttributeValue("fallback-path", fallbackPaths[0]);
                        }
                    }
                }

                foreach (var kvp in primaryConfig.Paths)
                {
                    var moduleId = kvp.Key;
                    var primaryPaths = kvp.Value ?? Array.Empty<string>();
                    if (existingModulesElement.Descendants().Any(e => e.Name.LocalName == "qti-interaction-module" && (string?)e.Attribute("id") == moduleId))
                    {
                        continue;
                    }

                    var primaryPathString = primaryPaths.Length > 0 ? primaryPaths[0] : string.Empty;
                    var newModule = new XElement(interaction.Name.Namespace + "qti-interaction-module",
                        new XAttribute("id", moduleId),
                        new XAttribute("primary-path", primaryPathString));

                    if (moduleResolutionFallbackConfig?.Paths is not null &&
                        moduleResolutionFallbackConfig.Paths.TryGetValue(moduleId, out var fallbackPaths) &&
                        fallbackPaths is not null && fallbackPaths.Length > 0)
                    {
                        newModule.SetAttributeValue("fallback-path", fallbackPaths[0]);
                    }

                    existingModulesElement.Add(newModule);
                }

                if (!string.IsNullOrWhiteSpace(primaryConfig.UrlArgs))
                {
                    existingModulesElement.SetAttributeValue("url-args", primaryConfig.UrlArgs);
                }

                continue;
            }

            if (moduleResolutionConfig is null || moduleResolutionConfig.Paths.Count == 0) continue;

            if (existingModulesElement is null)
            {
                existingModulesElement = new XElement(interaction.Name.Namespace + "qti-interaction-modules");
                interaction.Add(existingModulesElement);
            }

            foreach (var module in moduleResolutionConfig.Paths.Keys)
            {
                var primary = moduleResolutionConfig.Paths[module] ?? Array.Empty<string>();

                string[] fallback = Array.Empty<string>();
                if (moduleResolutionFallbackConfig?.Paths is not null && moduleResolutionFallbackConfig.Paths.TryGetValue(module, out var fb))
                {
                    fallback = fb ?? Array.Empty<string>();
                }

                var primaryArray = primary.Length > 0 ? primary : new[] { string.Empty };
                var fallbackArray = fallback.Length > 0 ? fallback : new[] { string.Empty };

                var combined = new List<(string Primary, string Fallback)>();
                for (var i = 0; i < primaryArray.Length; i++)
                {
                    var fbPath = fallbackArray.Length > i ? fallbackArray[i] : string.Empty;
                    combined.Add((primaryArray[i], fbPath));
                }

                foreach (var fbPath in fallbackArray)
                {
                    if (!combined.Any(p => string.Equals(p.Fallback, fbPath, StringComparison.Ordinal)))
                    {
                        combined.Add((primaryArray.Length > 0 ? primaryArray[0] : fbPath, fbPath));
                    }
                }

                foreach (var (primaryPath, fallbackPath) in combined)
                {
                    var moduleEl = new XElement(interaction.Name.Namespace + "qti-interaction-module",
                        new XAttribute("id", module),
                        new XAttribute("primary-path", primaryPath));

                    if (!string.IsNullOrWhiteSpace(fallbackPath))
                    {
                        moduleEl.SetAttributeValue("fallback-path", fallbackPath);
                    }

                    existingModulesElement.Add(moduleEl);
                }
            }
        }
    }

    private static void UpgradePci(XDocument doc)
    {
        var customInteraction = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "qti-custom-interaction");
        if (customInteraction is null) return;

        var portable = customInteraction.Descendants().FirstOrDefault(e => e.Name.LocalName == "qti-portable-custom-interaction");
        if (portable is null) return;

        static string KebabToDashedNotation(string input)
            => Regex.Replace(input, "[A-Z]", m => "-" + m.Value.ToLowerInvariant());

        void ParseProperties(XElement element, string parentKey)
        {
            foreach (var child in element.Elements().ToList())
            {
                var key = (string?)child.Attribute("key");
                var value = child.Value.Trim();
                if (string.IsNullOrWhiteSpace(key)) continue;

                var dashedAttribute = string.IsNullOrWhiteSpace(parentKey)
                    ? KebabToDashedNotation(key!)
                    : $"{parentKey}__{KebabToDashedNotation(key!)}";

                if (child.Elements().Any())
                {
                    ParseProperties(child, dashedAttribute);
                }
                else
                {
                    portable.SetAttributeValue("data-" + dashedAttribute, value);
                }
            }
        }

        portable.Descendants().Where(e => e.Name.LocalName == "style").Remove();

        foreach (var properties in portable.Descendants().Where(e => e.Name.LocalName == "properties").ToList())
        {
            ParseProperties(properties, string.Empty);
        }

        portable.Descendants().Where(e => e.Name.LocalName == "properties").Remove();

        XElement GetOrAddModules()
        {
            var modules = portable.Elements().FirstOrDefault(e => e.Name.LocalName == "qti-interaction-modules");
            if (modules is null)
            {
                modules = new XElement(portable.Name.Namespace + "qti-interaction-modules");
                portable.AddFirst(modules);
            }
            return modules;
        }

        var libraries = portable.Descendants().FirstOrDefault(e => e.Name.LocalName == "libraries");
        if (libraries is not null)
        {
            var modules = GetOrAddModules();
            foreach (var lib in libraries.Descendants().Where(e => e.Name.LocalName == "lib").ToList())
            {
                var id = (string?)lib.Attribute("id");
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (modules.Descendants().Any(e => e.Name.LocalName == "qti-interaction-module" && (string?)e.Attribute("id") == id))
                {
                    continue;
                }

                var newModule = new XElement(portable.Name.Namespace + "qti-interaction-module");
                newModule.SetAttributeValue("id", id);
                newModule.SetAttributeValue("primary-path", id);
                modules.Add(newModule);
            }
        }

        var customInteractionTypeIdentifier = (string?)portable.Attribute("custom-interaction-type-identifier");
        var moduleValue = (string?)portable.Attribute("module");
        if (string.IsNullOrWhiteSpace(moduleValue))
        {
            portable.SetAttributeValue("module", customInteractionTypeIdentifier);
            moduleValue = customInteractionTypeIdentifier;
        }

        var hook = (string?)portable.Attribute("hook");
        if (!string.IsNullOrWhiteSpace(hook))
        {
            var qtiModules = GetOrAddModules();
            var newModule = new XElement(portable.Name.Namespace + "qti-interaction-module");
            newModule.SetAttributeValue("id", moduleValue);
            newModule.SetAttributeValue("primary-path", hook);
            qtiModules.Add(newModule);
        }

        portable.SetAttributeValue("hook", null);

        var modulesLegacy = portable.Descendants().FirstOrDefault(e => e.Name.LocalName == "modules");
        if (modulesLegacy is not null)
        {
            foreach (var module in modulesLegacy.Descendants().Where(e => e.Name.LocalName == "module").ToList())
            {
                var qtiModules = GetOrAddModules();
                var newModule = new XElement(portable.Name.Namespace + "qti-interaction-module");

                var id = ((string?)module.Attribute("id") ?? string.Empty).Split('/')[0];
                newModule.SetAttributeValue("id", id);
                newModule.SetAttributeValue("primary-path", (string?)module.Attribute("primary-path"));
                qtiModules.Add(newModule);
            }

            modulesLegacy.Remove();
        }

        var assessmentItem = portable.Ancestors().FirstOrDefault(e => e.Name.LocalName == "qti-assessment-item");
        if (assessmentItem is not null)
        {
            var resources = portable.Descendants().FirstOrDefault(e => e.Name.LocalName == "resources");
            var stylesheets = resources?.Descendants().FirstOrDefault(e => e.Name.LocalName == "stylesheets");
            if (stylesheets is not null)
            {
                foreach (var link in stylesheets.Descendants().Where(e => e.Name.LocalName == "link").ToList())
                {
                    var href = (string?)link.Attribute("href");
                    if (string.IsNullOrWhiteSpace(href)) continue;
                    var stylesheet = new XElement(assessmentItem.Name.Namespace + "qti-stylesheet");
                    stylesheet.SetAttributeValue("href", href);
                    stylesheet.SetAttributeValue("type", "text/css");
                    assessmentItem.AddFirst(stylesheet);
                }
                stylesheets.Remove();
            }
        }

        portable.SetAttributeValue("data-base-ref", null);
        portable.SetAttributeValue("data-base-item", null);
        portable.SetAttributeValue("data-base-url", null);

        var responseIdentifier = (string?)customInteraction.Attribute("response-identifier");
        if (!string.IsNullOrWhiteSpace(responseIdentifier))
        {
            portable.SetAttributeValue("response-identifier", responseIdentifier);
        }

        var markup = portable.Descendants().FirstOrDefault(e => e.Name.LocalName == "markup");
        if (markup is not null)
        {
            markup.ReplaceWith(new XElement(portable.Name.Namespace + "qti-interaction-markup", markup.Nodes()));
        }

        var newPortable = new XElement(portable);
        customInteraction.ReplaceWith(newPortable);

        newPortable.Descendants()
            .Where(e => e.Name.LocalName is "qti-resources" or "resources")
            .Remove();
    }
}

