using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Citolab.QTI.Uploader;

public sealed class QtiPackageUploader
{
    private static readonly string[] AllowedExtensions = [".zip", ".qti"];
    private static readonly string[] AllowedMimeTypes =
    [
        "application/zip",
        "application/x-zip-compressed",
        "application/octet-stream"
    ];

    public async Task<QtiUploadResult> UploadAsync(
        QtiPackageUploadInput input,
        IQtiPackageStore store,
        QtiUploaderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        if (store is null) throw new ArgumentNullException(nameof(store));
        if (input.OpenReadStream is null) throw new InvalidDataException("No input stream was provided.");

        options ??= new QtiUploaderOptions();

        ValidateInput(input, options);

        var tempDir = Path.Combine(Path.GetTempPath(), "Citolab.QTI.Uploader");
        Directory.CreateDirectory(tempDir);

        var tempZipPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.zip");
        string? convertedZipPath = null;

        try
        {
            await CopyToTempZipAsync(input, tempZipPath, cancellationToken).ConfigureAwait(false);

            var detectedVersion = await DetectPackageVersionAsync(tempZipPath, cancellationToken).ConfigureAwait(false);
            var convertedToQti3 = false;

            var effectiveZipPath = tempZipPath;
            if (options.ConvertQti2ToQti3 && detectedVersion == QtiPackageVersion.Qti2)
            {
                if (options.Converter is null) throw new InvalidDataException("QTI 2.x package detected, but no converter is configured.");

                convertedZipPath = await options.Converter.ConvertQti2PackageToQti3Async(tempZipPath, cancellationToken).ConfigureAwait(false);
                effectiveZipPath = convertedZipPath;
                convertedToQti3 = true;
            }

            var storedCount = 0;

            if (options.StoreOriginalPackage)
            {
                using var originalStream = File.OpenRead(tempZipPath);
                await store.StoreAsync(
                    new QtiStoredFile(QtiStoredFileKind.OriginalPackage, input.FileName, originalStream),
                    cancellationToken).ConfigureAwait(false);
                storedCount++;
            }

            if (convertedToQti3 && options.StoreConvertedPackage && convertedZipPath is not null)
            {
                using var convertedStream = File.OpenRead(convertedZipPath);
                await store.StoreAsync(
                    new QtiStoredFile(QtiStoredFileKind.ConvertedPackage, Path.GetFileName(convertedZipPath), convertedStream),
                    cancellationToken).ConfigureAwait(false);
                storedCount++;
            }

            storedCount += await ExtractAndStoreAsync(effectiveZipPath, store, options, cancellationToken).ConfigureAwait(false);

            return new QtiUploadResult(
                detectedVersion,
                convertedToQti3,
                storedCount,
                convertedZipPath is null ? null : Path.GetFileName(convertedZipPath));
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("Failed to process QTI package zip.", ex);
        }
        finally
        {
            SafeDelete(tempZipPath);
            if (convertedZipPath is not null) SafeDelete(convertedZipPath);
        }
    }

    private static void ValidateInput(QtiPackageUploadInput input, QtiUploaderOptions options)
    {
        if (string.IsNullOrWhiteSpace(input.FileName))
        {
            throw new InvalidDataException("FileName is required.");
        }

        if (input.Length <= 0)
        {
            throw new InvalidDataException("No file uploaded. Please provide a QTI package file.");
        }

        if (input.Length > options.MaxPackageBytes)
        {
            throw new InvalidDataException($"File too large. Maximum size is {options.MaxPackageBytes} bytes.");
        }

        var extension = Path.GetExtension(input.FileName).ToLowerInvariant();
        var contentTypeOk = input.ContentType is not null && AllowedMimeTypes.Contains(input.ContentType);
        var extensionOk = AllowedExtensions.Contains(extension);

        if (!contentTypeOk && !extensionOk)
        {
            throw new InvalidDataException("Invalid file type. Only ZIP and QTI package files are allowed.");
        }
    }

    private static async Task CopyToTempZipAsync(QtiPackageUploadInput input, string tempZipPath, CancellationToken cancellationToken)
    {
        using var output = File.Create(tempZipPath);
        using var source = input.OpenReadStream();
        await source.CopyToAsync(output, 1024 * 64, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<QtiPackageVersion> DetectPackageVersionAsync(string zipPath, CancellationToken cancellationToken)
    {
        using var zipStream = File.OpenRead(zipPath);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);

        var detected = QtiPackageVersion.Unknown;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.Name)) continue;
            if (!entry.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;

            using var entryStream = entry.Open();
            using var ms = new MemoryStream();
            await entryStream.CopyToAsync(ms, 1024 * 64, cancellationToken).ConfigureAwait(false);
            ms.Position = 0;

            if (!QtiXmlUtilities.TryGetRootLocalName(ms, out var rootLocalName)) continue;
            detected = QtiXmlUtilities.UpdateVersionFromRoot(detected, rootLocalName);

            if (detected == QtiPackageVersion.Mixed) return detected;
        }

        return detected;
    }

    private static async Task<int> ExtractAndStoreAsync(
        string zipPath,
        IQtiPackageStore store,
        QtiUploaderOptions options,
        CancellationToken cancellationToken)
    {
        using var zipStream = File.OpenRead(zipPath);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);

        var count = 0;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.Name)) continue;

            var relativePath = entry.FullName.Replace('\\', '/');

            using var entryStream = entry.Open();
            using var memoryStream = new MemoryStream();
            await entryStream.CopyToAsync(memoryStream, 1024 * 64, cancellationToken).ConfigureAwait(false);
            memoryStream.Position = 0;

            var xmlKind = QtiXmlKind.None;

            if (options.CleanAndPrettyPrintQtiXml &&
                entry.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                QtiXmlUtilities.IsQtiAssessmentItemOrTest(memoryStream, out xmlKind, out _))
            {
                memoryStream.Position = 0;
                var xdoc = XDocument.Load(memoryStream);
                QtiXmlUtilities.CleanXml(xdoc.Root);
                var prettyXml = QtiXmlUtilities.PrettyPrintXml(xdoc);

                memoryStream.SetLength(0);
                using (var writer = new StreamWriter(memoryStream, Encoding.UTF8, 1024, leaveOpen: true))
                {
                    writer.Write(prettyXml);
                    writer.Flush();
                }
                memoryStream.Position = 0;
            }
            else
            {
                memoryStream.Position = 0;
            }

            await store.StoreAsync(
                new QtiStoredFile(QtiStoredFileKind.ExtractedEntry, relativePath, memoryStream, xmlKind),
                cancellationToken).ConfigureAwait(false);
            count++;
        }

        return count;
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }
}
