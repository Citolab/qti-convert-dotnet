using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
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
        Assert.Contains(store.StoredFiles, f => f.Kind == QtiStoredFileKind.ExtractedEntry && f.RelativePath == "items/item1.xml" && f.XmlKind == QtiXmlKind.AssessmentItem);
        Assert.Contains(store.StoredFiles, f => f.Kind == QtiStoredFileKind.ExtractedEntry && f.RelativePath == "tests/test1.xml" && f.XmlKind == QtiXmlKind.AssessmentTest);
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

        var itemXml = ReadEntryText(archive, "items/item1.xml");
        Assert.Equal("qti-assessment-item", GetRootLocalName(itemXml));

        var testXml = ReadEntryText(archive, "tests/test1.xml");
        Assert.Equal("qti-assessment-test", GetRootLocalName(testXml));
        Assert.Equal("ITEM-1", GetFirstElementAttributeByLocalName(testXml, "qti-assessment-item-ref", "identifier"));

        Assert.Contains(store.StoredFiles, f => f.Kind == QtiStoredFileKind.ExtractedEntry && f.RelativePath == "items/item1.xml" && f.XmlKind == QtiXmlKind.AssessmentItem);
        Assert.Contains(store.StoredFiles, f => f.Kind == QtiStoredFileKind.ExtractedEntry && f.RelativePath == "tests/test1.xml" && f.XmlKind == QtiXmlKind.AssessmentTest);
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

    private static string? GetFirstElementAttributeByLocalName(string xml, string elementLocalName, string attributeName)
    {
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var element = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == elementLocalName);
        return (string?)element?.Attribute(attributeName);
    }
}
