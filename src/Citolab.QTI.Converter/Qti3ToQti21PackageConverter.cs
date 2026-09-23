using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Citolab.QTI.Converter;

/// <summary>Context for converting one file of a package.</summary>
public sealed class Qti21FileContext
{
    public Qti21FileContext(string path, Func<string, string?> resolveStimulus, string? sharedVocabularyStylesheetHref = null)
    {
        Path = path;
        ResolveStimulus = resolveStimulus;
        SharedVocabularyStylesheetHref = sharedVocabularyStylesheetHref;
    }

    /// <summary>Path of the file inside the package.</summary>
    public string Path { get; }

    /// <summary>Resolves a stimulus href relative to this file to its QTI 3 XML (and marks it as inlined).</summary>
    public Func<string, string?> ResolveStimulus { get; }

    /// <summary>Href (relative to this file) of the shared vocabulary stylesheet, unless it is switched off.</summary>
    public string? SharedVocabularyStylesheetHref { get; }
}

/// <summary>Overrides for the default conversions, like the hooks of <see cref="Qti2ToQti3PackageConverter"/>.</summary>
public sealed class Qti3ToQti21PackageConverterOptions
{
    public Func<string, Qti21FileContext, Qti21ConversionResult>? ConvertItem { get; set; }
    public Func<string, Qti21FileContext, Qti21ConversionResult>? ConvertAssessment { get; set; }

    /// <summary>Receives the manifest XML and the package paths of the stimuli that were inlined into items.</summary>
    public Func<string, ISet<string>, string>? ConvertManifest { get; set; }

    /// <summary>
    /// Adds the 1EdTech QTI 3 shared vocabulary stylesheet (qti3p0.css) next to the manifest and links it from every
    /// item that uses qti-* classes, so QTI 2.1 players can style them. Default true.
    /// </summary>
    public bool InjectSharedVocabularyStylesheet { get; set; } = true;
}

public sealed class Qti3ToQti21PackageConversionResult
{
    public Qti3ToQti21PackageConversionResult(string outputZipPath, IReadOnlyList<Qti21Warning> warnings)
    {
        OutputZipPath = outputZipPath;
        Warnings = warnings;
    }

    public string OutputZipPath { get; }
    public IReadOnlyList<Qti21Warning> Warnings { get; }
}

/// <summary>
/// Converts a QTI 3 package to QTI 2.1. Shared stimuli are inlined into the items that reference them and removed
/// from the package; constructs without a QTI 2.1 equivalent are reported as warnings. Non-QTI files are copied.
/// </summary>
public sealed class Qti3ToQti21PackageConverter
{
    private readonly Qti3ToQti21PackageConverterOptions _options;

    public Qti3ToQti21PackageConverter(Qti3ToQti21PackageConverterOptions? options = null)
    {
        _options = options ?? new Qti3ToQti21PackageConverterOptions();
    }

    /// <summary>Writes &lt;input&gt;-qti21.zip next to the input zip.</summary>
    public async Task<Qti3ToQti21PackageConversionResult> ConvertQti3PackageToQti21Async(string inputZipPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputZipPath)) throw new ArgumentNullException(nameof(inputZipPath));
        if (!File.Exists(inputZipPath)) throw new FileNotFoundException("Input zip not found.", inputZipPath);

        var outputZipPath = Path.Combine(
            Path.GetDirectoryName(inputZipPath) ?? Directory.GetCurrentDirectory(),
            $"{Path.GetFileNameWithoutExtension(inputZipPath)}-qti21.zip");

        using var input = File.OpenRead(inputZipPath);
        using var output = File.Create(outputZipPath);
        var warnings = await ConvertAsync(input, output, cancellationToken).ConfigureAwait(false);
        return new Qti3ToQti21PackageConversionResult(outputZipPath, warnings);
    }

    /// <summary>Reads a QTI 3 package zip from <paramref name="input"/> and writes the QTI 2.1 zip to <paramref name="output"/>.</summary>
    public async Task<IReadOnlyList<Qti21Warning>> ConvertAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        var files = await ReadZipAsync(input, cancellationToken).ConfigureAwait(false);
        var (converted, warnings) = ConvertFiles(files);
        await WriteZipAsync(output, converted, cancellationToken).ConfigureAwait(false);
        return warnings;
    }

    internal (Dictionary<string, byte[]> Files, List<Qti21Warning> Warnings) ConvertFiles(Dictionary<string, byte[]> files)
    {
        var warnings = new List<Qti21Warning>();
        var xmlFiles = files
            .Where(f => f.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => f.Key, f => XmlStringUtilities.CleanXmlString(Decode(f.Value)), StringComparer.OrdinalIgnoreCase);
        var byNormalizedPath = xmlFiles.Keys.ToDictionary(QtiPackagePath.Normalize, p => p, StringComparer.OrdinalIgnoreCase);

        var inlinedStimulusPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var stimulusPaths = new List<string>();
        var manifestPaths = new List<string>();
        var itemsWithSharedVocabulary = new List<string>();
        // The package root is the folder of the manifest; the stylesheet goes there
        var manifestPath = xmlFiles.Keys.FirstOrDefault(p => RootLocalName(xmlFiles[p]) == "manifest");
        var stylesheetPath = QtiPackagePath.Join(QtiPackagePath.DirectoryName(manifestPath ?? string.Empty), QtiSharedVocabularyStylesheet.FileName);

        foreach (var file in files)
        {
            var path = file.Key;
            if (!xmlFiles.TryGetValue(path, out var xml))
            {
                output[path] = file.Value;
                continue;
            }
            var root = RootLocalName(xml);
            if (root == "manifest")
            {
                manifestPaths.Add(path);
                continue;
            }
            if (root == "qti-assessment-stimulus")
            {
                stimulusPaths.Add(path);
                continue;
            }
            // Non-QTI files (and QTI 2.x files) are passed through
            if (!root.StartsWith("qti-", StringComparison.Ordinal))
            {
                output[path] = file.Value;
                continue;
            }

            var context = new Qti21FileContext(
                path,
                href =>
                {
                    var stimulusPath = QtiPackagePath.Join(QtiPackagePath.DirectoryName(path), href);
                    if (!byNormalizedPath.TryGetValue(stimulusPath, out var actualPath)) return null;
                    inlinedStimulusPaths.Add(QtiPackagePath.Normalize(actualPath));
                    return xmlFiles[actualPath];
                },
                _options.InjectSharedVocabularyStylesheet ? QtiPackagePath.Relative(QtiPackagePath.DirectoryName(path), stylesheetPath) : null);
            var convert = root == "qti-assessment-test" ? _options.ConvertAssessment : _options.ConvertItem;
            var result = convert is not null
                ? convert(xml, context)
                : Qti3ToQti21XmlConverter.Convert(xml, new Qti3ToQti21ConvertOptions
                {
                    FilePath = path,
                    ResolveStimulus = context.ResolveStimulus,
                    SharedVocabularyStylesheetHref = context.SharedVocabularyStylesheetHref
                });
            output[path] = Encode(result.Xml);
            warnings.AddRange(result.Warnings);
            if (result.Warnings.Any(w => w.Code == Qti21WarningCode.SharedVocabularyStylesheet)) itemsWithSharedVocabulary.Add(path);
        }

        // Stimuli that no item referenced are kept (as a QTI 2.2 assessmentStimulus)
        foreach (var path in stimulusPaths.Where(p => !inlinedStimulusPaths.Contains(QtiPackagePath.Normalize(p))))
        {
            var result = Qti3ToQti21XmlConverter.Convert(xmlFiles[path], new Qti3ToQti21ConvertOptions { FilePath = path });
            output[path] = Encode(result.Xml);
            warnings.AddRange(result.Warnings);
        }

        // An existing qti3p0.css in the package is kept (and used)
        if (itemsWithSharedVocabulary.Count > 0 && !output.ContainsKey(stylesheetPath))
        {
            output[stylesheetPath] = Encode(QtiSharedVocabularyStylesheet.Css);
        }

        foreach (var path in manifestPaths)
        {
            var convertManifest = _options.ConvertManifest ?? ((xml, inlined) => Qti3ToQti21ManifestConverter.Convert(xml, inlined));
            var manifest = convertManifest(xmlFiles[path], inlinedStimulusPaths);
            if (itemsWithSharedVocabulary.Count > 0)
            {
                manifest = Qti3ToQti21ManifestConverter.AddSharedVocabularyStylesheet(manifest, path, stylesheetPath, itemsWithSharedVocabulary);
            }
            output[path] = Encode(manifest);
        }

        return (output, warnings);
    }

    private static string RootLocalName(string xml)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element) return reader.LocalName;
            }
        }
        catch (XmlException)
        {
        }
        return string.Empty;
    }

    private static string Decode(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static byte[] Encode(string text) => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);

    private static async Task<Dictionary<string, byte[]>> ReadZipAsync(Stream input, CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = entry.FullName.Replace('\\', '/');
            if (path.Length == 0 || path.EndsWith("/", StringComparison.Ordinal)) continue;
            if (path.IndexOf("__MACOSX/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.EndsWith(".DS_Store", StringComparison.OrdinalIgnoreCase)) continue;

            using var entryStream = entry.Open();
            using var ms = new MemoryStream();
            await entryStream.CopyToAsync(ms, 1024 * 64, cancellationToken).ConfigureAwait(false);
            files[path] = ms.ToArray();
        }
        return files;
    }

    private static async Task WriteZipAsync(Stream output, Dictionary<string, byte[]> files, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var file in files.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.CreateEntry(file.Key, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            await entryStream.WriteAsync(file.Value, 0, file.Value.Length, cancellationToken).ConfigureAwait(false);
        }
    }
}
