using System.IO.Compression;
using System.Text;
using System.Xml;
using Citolab.QTI.Converter;
using Citolab.QTI.Uploader;
using Xunit;

namespace Citolab.QTI.Uploader.Tests;

public sealed class QtiPackageUploaderTests
{
    [Fact]
    public async Task Upload_Qti2Zip_NoConversion_DetectsQti2_AndStoresEntries()
    {
        var zipBytes = await File.ReadAllBytesAsync(TestDataPaths.Qti2ZipPath);
        var input = new QtiPackageUploadInput(
            FileName: "qti2-sample.zip",
            Length: zipBytes.Length,
            OpenReadStream: () => new MemoryStream(zipBytes),
            ContentType: "application/zip");

        var store = new FakeStore();
        var uploader = new QtiPackageUploader();

        var result = await uploader.UploadAsync(input, store, new QtiUploaderOptions
        {
            StoreOriginalPackage = true,
            StoreConvertedPackage = false,
            ConvertQti2ToQti3 = false,
            CleanAndPrettyPrintQtiXml = true
        });

        Assert.Equal(QtiPackageVersion.Qti2, result.DetectedVersion);
        Assert.False(result.ConvertedToQti3);

        Assert.Contains(store.StoredFiles, f => f.Kind == QtiStoredFileKind.OriginalPackage);

        var extractedHotspot = store.StoredFiles.Single(f =>
            f.Kind == QtiStoredFileKind.ExtractedEntry &&
            f.RelativePath == "items/hotspot.xml");
        Assert.Equal(QtiXmlKind.AssessmentItem, extractedHotspot.XmlKind);
        Assert.Equal("assessmentItem", GetRootLocalName(ReadUtf8Text(extractedHotspot.Content)));
    }

    [Fact]
    public async Task Upload_Qti2Zip_WithConversion_ProducesQti3Zip_AndExtractsConvertedEntries()
    {
        var zipBytes = await File.ReadAllBytesAsync(TestDataPaths.Qti2ZipPath);
        var input = new QtiPackageUploadInput(
            FileName: "qti2-sample.zip",
            Length: zipBytes.Length,
            OpenReadStream: () => new MemoryStream(zipBytes),
            ContentType: "application/zip");

        var store = new FakeStore();
        var uploader = new QtiPackageUploader();

        var result = await uploader.UploadAsync(input, store, new QtiUploaderOptions
        {
            StoreOriginalPackage = false,
            StoreConvertedPackage = true,
            ConvertQti2ToQti3 = true,
            Converter = new Qti2ToQti3PackageConverter(),
            CleanAndPrettyPrintQtiXml = true
        });

        Assert.Equal(QtiPackageVersion.Qti2, result.DetectedVersion);
        Assert.True(result.ConvertedToQti3);
        Assert.NotNull(result.ConvertedPackageFileName);

        var converted = store.StoredFiles.Single(f => f.Kind == QtiStoredFileKind.ConvertedPackage);
        using var ms = new MemoryStream(converted.Content);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);

        var itemXml = ReadEntryText(archive, "items/hotspot.xml");
        Assert.Equal("qti-assessment-item", GetRootLocalName(itemXml));

        var extractedHotspot = store.StoredFiles.Single(f =>
            f.Kind == QtiStoredFileKind.ExtractedEntry &&
            f.RelativePath == "items/hotspot.xml");
        Assert.Equal(QtiXmlKind.AssessmentItem, extractedHotspot.XmlKind);
        Assert.Equal("qti-assessment-item", GetRootLocalName(ReadUtf8Text(extractedHotspot.Content)));
    }

    [Fact]
    public async Task Upload_Qti3Zip_WithConversionEnabled_DoesNotConvert()
    {
        var zipBytes = await File.ReadAllBytesAsync(TestDataPaths.Qti3ZipPath);
        var input = new QtiPackageUploadInput(
            FileName: "qti3-sample.zip",
            Length: zipBytes.Length,
            OpenReadStream: () => new MemoryStream(zipBytes),
            ContentType: "application/zip");

        var store = new FakeStore();
        var uploader = new QtiPackageUploader();

        var result = await uploader.UploadAsync(input, store, new QtiUploaderOptions
        {
            StoreOriginalPackage = false,
            StoreConvertedPackage = true,
            ConvertQti2ToQti3 = true,
            Converter = new Qti2ToQti3PackageConverter(),
            CleanAndPrettyPrintQtiXml = true
        });

        Assert.Equal(QtiPackageVersion.Qti3, result.DetectedVersion);
        Assert.False(result.ConvertedToQti3);
        Assert.Null(result.ConvertedPackageFileName);
        Assert.DoesNotContain(store.StoredFiles, f => f.Kind == QtiStoredFileKind.ConvertedPackage);
    }

    [Fact]
    public async Task Upload_CleanAndPrettyPrint_KeepsWhitespaceBetweenInlineElements()
    {
        const string itemXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <qti-assessment-item xmlns="http://www.imsglobal.org/xsd/imsqtiasi_v3p0" identifier="ITM-1" title="ws">
              <qti-item-body>
                <p>Geef aan of ze <strong>waar</strong> of <strong>niet</strong> <strong>waar</strong> zijn.</p>
                <p>Kies <strong>niet</strong><span> </span><strong>waar</strong> of een<span> <span>woord</span> </span>hier.</p>
              </qti-item-body>
            </qti-assessment-item>
            """;
        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("items/item.xml").Open(), Encoding.UTF8);
            writer.Write(itemXml);
        }
        var zipBytes = zipStream.ToArray();
        var input = new QtiPackageUploadInput(
            FileName: "ws.zip",
            Length: zipBytes.Length,
            OpenReadStream: () => new MemoryStream(zipBytes),
            ContentType: "application/zip");

        var store = new FakeStore();
        await new QtiPackageUploader().UploadAsync(input, store, new QtiUploaderOptions
        {
            StoreOriginalPackage = false,
            StoreConvertedPackage = false,
            ConvertQti2ToQti3 = false,
            CleanAndPrettyPrintQtiXml = true
        });

        var extracted = store.StoredFiles.Single(f =>
            f.Kind == QtiStoredFileKind.ExtractedEntry &&
            f.RelativePath == "items/item.xml");
        var xml = ReadUtf8Text(extracted.Content);
        Assert.Contains("<strong>niet</strong> <strong>waar</strong> zijn.", xml);
        Assert.Contains("<p>Kies <strong>niet</strong> <strong>waar</strong> of een<span> woord </span>hier.</p>", xml);
    }

    private static string ReadEntryText(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        Assert.NotNull(entry);
        using var stream = entry!.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string GetRootLocalName(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            IgnoreComments = true,
            IgnoreWhitespace = true
        });

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                return reader.LocalName;
            }
        }

        return string.Empty;
    }

    private static string ReadUtf8Text(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var reader = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        return reader.ReadToEnd();
    }
}
