using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Citolab.QTI.Converter;

public sealed partial class QtiTransform
{
    public static readonly string[] QtiReferenceAttributes =
        new[]
        {
            "src",
            "href",
            "data",
            "primary-path",
            "fallback-path",
            "template-location"
        };

    private static readonly XNamespace Ssml2001 = "http://www.w3.org/2001/10/synthesis";
    private static readonly XNamespace Ssml2010 = "http://www.w3.org/2010/10/synthesis";

    private readonly XDocument _doc;
    private readonly bool _isFragment;

    private QtiTransform(XDocument doc, bool isFragment)
    {
        _doc = doc;
        _isFragment = isFragment;
    }

    public static QtiTransform Create(string xml)
    {
        if (xml is null) throw new ArgumentNullException(nameof(xml));
        var doc = ParseXmlOrFragment(xml, out var isFragment);
        return new QtiTransform(doc, isFragment);
    }

    public QtiTransform FnCh(Action<XDocument> fn)
    {
        if (fn is null) throw new ArgumentNullException(nameof(fn));
        fn(_doc);
        return this;
    }

    public async Task<QtiTransform> FnChAsync(Func<XDocument, Task> fn)
    {
        if (fn is null) throw new ArgumentNullException(nameof(fn));
        await fn(_doc).ConfigureAwait(false);
        return this;
    }

    public QtiTransform ObjectToImg()
    {
        ObjectToImg(_doc);
        return this;
    }

    public QtiTransform ObjectToVideo()
    {
        ObjectToVideo(_doc);
        return this;
    }

    public QtiTransform ObjectToAudio()
    {
        ObjectToAudio(_doc);
        return this;
    }

    public QtiTransform SsmlSubToSpan()
    {
        SsmlToSpans(_doc);
        return this;
    }

    public QtiTransform StripMaterialInfo()
    {
        StripMaterialInfo(_doc);
        return this;
    }

    public QtiTransform MinChoicesToOne()
    {
        MinChoicesToOne(_doc);
        return this;
    }

    public QtiTransform ExternalScored()
    {
        ExternalScored(_doc);
        return this;
    }

    public QtiTransform DepConvert()
    {
        DepConvert(_doc);
        return this;
    }

    public QtiTransform DepConvertExtended()
    {
        DepConvertExtended(_doc);
        return this;
    }

    public QtiTransform HideInputsForChoiceInteractionWithImages()
    {
        HideInputsForChoiceInteractionWithImages(_doc);
        return this;
    }

    public QtiTransform StripStylesheets(string? removePattern = null, string? keepPattern = null)
    {
        StripStylesheets(_doc, removePattern, keepPattern);
        return this;
    }

    public QtiTransform Suffix(string[] elements, string suffix)
    {
        if (elements is null) throw new ArgumentNullException(nameof(elements));
        if (suffix is null) throw new ArgumentNullException(nameof(suffix));

        Suffixa(_doc, elements, suffix);
        return this;
    }

    public QtiTransform ChangeAssetLocation(Func<string, string> getNewUrl, string[]? srcAttributes = null, bool skipBase64 = true)
    {
        if (getNewUrl is null) throw new ArgumentNullException(nameof(getNewUrl));
        ChangeAssetLocation(_doc, getNewUrl, srcAttributes ?? QtiReferenceAttributes, skipBase64);
        return this;
    }

    public async Task<QtiTransform> ChangeAssetLocationAsync(Func<string, Task<string>> getNewUrlAsync, string[]? srcAttributes = null, bool skipBase64 = true)
    {
        if (getNewUrlAsync is null) throw new ArgumentNullException(nameof(getNewUrlAsync));
        await ChangeAssetLocationAsync(_doc, getNewUrlAsync, srcAttributes ?? QtiReferenceAttributes, skipBase64).ConfigureAwait(false);
        return this;
    }

    public QtiTransform UpgradePci()
    {
        UpgradePci(_doc);
        return this;
    }

    public QtiTransform QbCleanup()
    {
        QbCleanup(_doc);
        return this;
    }

    public QtiTransform ToMathMLWebcomponents()
    {
        ToMathMLWebcomponents(_doc);
        return this;
    }

    public QtiTransform CustomTypes(string param = "type")
    {
        CustomTypes(_doc, param);
        return this;
    }

    public QtiTransform CustomInteraction(string baseRef, string baseItem)
    {
        if (baseRef is null) throw new ArgumentNullException(nameof(baseRef));
        if (baseItem is null) throw new ArgumentNullException(nameof(baseItem));
        CustomInteraction(_doc, baseRef, baseItem);
        return this;
    }

    public async Task<QtiTransform> ConfigurePciAsync(string baseUrl, Func<string, Task<ModuleResolutionConfig?>> getModuleResolutionConfig)
    {
        if (baseUrl is null) throw new ArgumentNullException(nameof(baseUrl));
        if (getModuleResolutionConfig is null) throw new ArgumentNullException(nameof(getModuleResolutionConfig));
        await ConfigurePciAsync(_doc, baseUrl, getModuleResolutionConfig).ConfigureAwait(false);
        return this;
    }

    public async Task<QtiTransform> StylesheetsInlineAsync(Func<string, Task<string?>>? getStylesheetContentAsync = null, string? basePath = null)
    {
        await StylesheetsInlineAsync(_doc, getStylesheetContentAsync, basePath).ConfigureAwait(false);
        return this;
    }

    public async Task<QtiTransform> StylesheetsInlineAsync(Func<string, string, Task<string?>>? getFileContentAsync = null, string? itemPath = null)
    {
        await StylesheetsInlineAsync(_doc, getFileContentAsync, itemPath).ConfigureAwait(false);
        return this;
    }

    public string Xml()
    {
        if (!_isFragment)
        {
            return _doc.ToString(SaveOptions.DisableFormatting);
        }

        var root = _doc.Root;
        if (root is null) return string.Empty;

        return string.Concat(root.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting)));
    }

    public sealed class ModuleResolutionConfig
    {
        public int? WaitSeconds { get; set; }
        public string? Context { get; set; }
        public bool? CatchError { get; set; }
        public string? UrlArgs { get; set; }

        public Dictionary<string, string[]?> Paths { get; set; } = new Dictionary<string, string[]?>(StringComparer.Ordinal);

        public Dictionary<string, ShimConfig>? Shim { get; set; }

        public sealed class ShimConfig
        {
            public string[]? Deps { get; set; }
            public string? Exports { get; set; }
        }
    }

    private static XDocument ParseXmlOrFragment(string xml, out bool isFragment)
    {
        var cleaned = StripXmlDeclaration(xml);

        try
        {
            var doc = XDocument.Parse(cleaned, LoadOptions.PreserveWhitespace);
            isFragment = false;
            return doc;
        }
        catch
        {
            isFragment = true;
            var wrapped = $"<qti-transform-root>{cleaned}</qti-transform-root>";
            return XDocument.Parse(wrapped, LoadOptions.PreserveWhitespace);
        }
    }

    private static string StripXmlDeclaration(string xml)
        => Regex.Replace(xml, @"^\s*<\?xml[^>]*\?>\s*", string.Empty, RegexOptions.Singleline);
}
