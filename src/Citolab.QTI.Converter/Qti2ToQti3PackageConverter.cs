using System.IO.Compression;
using System.Text;
using Citolab.QTI.Uploader;

namespace Citolab.QTI.Converter;

public sealed class Qti2ToQti3PackageConverter : IQtiPackageConverter
{
    private readonly Qti2ToQti3PackageConverterOptions _options;

    public Qti2ToQti3PackageConverter(Qti2ToQti3PackageConverterOptions? options = null)
    {
        _options = options ?? new Qti2ToQti3PackageConverterOptions();
    }

    public async Task<string> ConvertQti2PackageToQti3Async(string inputZipPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputZipPath)) throw new ArgumentNullException(nameof(inputZipPath));
        if (!File.Exists(inputZipPath)) throw new FileNotFoundException("Input zip not found.", inputZipPath);

        var workingDir = Path.GetDirectoryName(inputZipPath) ?? Directory.GetCurrentDirectory();
        var outputZipPath = Path.Combine(
            workingDir,
            $"{Path.GetFileNameWithoutExtension(inputZipPath)}-qti3.zip");

        if (File.Exists(outputZipPath))
        {
            try { File.Delete(outputZipPath); } catch { }
        }

        var files = new Dictionary<string, QtiPackageFile>(StringComparer.OrdinalIgnoreCase);
        string? manifestPath = null;

        using (var input = File.OpenRead(inputZipPath))
        using (var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false))
        {
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(entry.FullName)) continue;
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;

                using var entryStream = entry.Open();
                using var ms = new MemoryStream();
                await entryStream.CopyToAsync(ms, 1024 * 64, cancellationToken).ConfigureAwait(false);
                ms.Position = 0;

                var relativePath = entry.FullName.Replace('\\', '/');

                if (relativePath.IndexOf("__MACOSX/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    relativePath.EndsWith(".DS_Store", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (relativePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    var xml = await ReadXmlAsync(ms, cancellationToken).ConfigureAwait(false);
                    var detected = QtiPackageXmlDetector.Detect(xml);

                    if (detected == QtiPackageXmlType.Manifest && _options.ConvertManifest)
                    {
                        xml = QtiPackageManifestConverter.ConvertToQti3(xml);
                        manifestPath = relativePath;
                    }
                    else if (detected == QtiPackageXmlType.AssessmentItem || detected == QtiPackageXmlType.AssessmentTest)
                    {
                        xml = Qti2ToQti3XmlConverter.Convert(xml);

                        if (_options.ApplyItemTransforms && detected == QtiPackageXmlType.AssessmentItem)
                        {
                            var transform = QtiTransform.Create(xml);
                            ApplyConfiguredItemTransforms(transform, _options.ItemTransformOptions);

                            if (_options.OnItemTransformedAsync is not null)
                            {
                                // Create file resolver for accessing other files in the ZIP package
                                Func<string, Task<string?>> fileResolver = (string filePath) =>
                                {
                                    if (files.TryGetValue(filePath, out var file))
                                    {
                                        if (file.TextContent is not null)
                                        {
                                            return Task.FromResult<string?>(file.TextContent);
                                        }
                                        if (file.BinaryContent is not null)
                                        {
                                            return Task.FromResult<string?>(System.Text.Encoding.UTF8.GetString(file.BinaryContent));
                                        }
                                    }
                                    return Task.FromResult<string?>(null);
                                };

                                await _options.OnItemTransformedAsync(transform, relativePath, fileResolver, cancellationToken).ConfigureAwait(false);
                                cancellationToken.ThrowIfCancellationRequested();
                            }

                            xml = transform.Xml();
                        }
                    }

                    files[relativePath] = QtiPackageFile.FromText(xml, detected);
                }
                else
                {
                    files[relativePath] = QtiPackageFile.FromBytes(ms.ToArray(), QtiPackageXmlType.Other);
                }
            }
        }

        if (_options.SyncAssessmentItemIdentifiers && manifestPath is not null)
        {
            QtiPackagePostProcessor.SyncAssessmentItemIdentifiers(files, manifestPath);
        }

        await WriteZipAsync(outputZipPath, files, cancellationToken).ConfigureAwait(false);
        return outputZipPath;
    }

    private static void ApplyConfiguredItemTransforms(QtiTransform transform, QtiItemTransformOptions options)
    {
        if (options.ObjectToImg) transform.ObjectToImg();
        if (options.ObjectToVideo) transform.ObjectToVideo();
        if (options.ObjectToAudio) transform.ObjectToAudio();
        if (options.SsmlSubToSpan) transform.SsmlSubToSpan();
        if (options.StripMaterialInfo) transform.StripMaterialInfo();
        if (options.MinChoicesToOne) transform.MinChoicesToOne();
        if (options.ExternalScored) transform.ExternalScored();

        if (options.QbCleanup) transform.QbCleanup();
        if (options.DepConvert) transform.DepConvert();
        if (options.DepConvertExtended) transform.DepConvertExtended();
        if (options.HideInputsForChoiceInteractionWithImages) transform.HideInputsForChoiceInteractionWithImages();
        if (options.UpgradePci) transform.UpgradePci();

        if (options.StripStylesheets)
        {
            transform.StripStylesheets(options.StripStylesheetsRemovePattern, options.StripStylesheetsKeepPattern);
        }
        else if (!string.IsNullOrWhiteSpace(options.StripStylesheetsRemovePattern) || !string.IsNullOrWhiteSpace(options.StripStylesheetsKeepPattern))
        {
            transform.StripStylesheets(options.StripStylesheetsRemovePattern, options.StripStylesheetsKeepPattern);
        }
    }

    private static async Task<string> ReadXmlAsync(Stream stream, CancellationToken cancellationToken)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        var xml = await reader.ReadToEndAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return XmlStringUtilities.CleanXmlString(xml);
    }

    private static async Task WriteZipAsync(string outputZipPath, Dictionary<string, QtiPackageFile> files, CancellationToken cancellationToken)
    {
        using var output = File.Create(outputZipPath);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var kvp in files.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = kvp.Key;
            var file = kvp.Value;

            var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            using var entryStream = entry.Open();

            if (file.TextContent is not null)
            {
                using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1024, leaveOpen: true);
                await writer.WriteAsync(file.TextContent).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
            }
            else
            {
                var bytes = file.BinaryContent ?? Array.Empty<byte>();
                await entryStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
