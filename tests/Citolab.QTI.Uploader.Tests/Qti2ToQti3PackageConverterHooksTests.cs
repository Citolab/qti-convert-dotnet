using System.IO.Compression;
using System.Text;
using Citolab.QTI.Converter;
using Xunit;

namespace Citolab.QTI.Uploader.Tests;

public sealed class Qti2ToQti3PackageConverterHooksTests
{
    [Fact]
    public async Task ConvertQti2PackageToQti3Async_CallsOnItemTransformedAsync_AndHonorsOptions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Citolab.QTI.Converter.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var inputZipPath = Path.Combine(tempDir, "input.zip");
        var itemPath = "items/item.xml";

        const string itemXml = """
                               <?xml version="1.0" encoding="UTF-8"?>
                               <qti-assessment-item xmlns="http://www.imsglobal.org/xsd/imsqtiasi_v3p0" identifier="ITEM1">
                                 <qti-item-body>
                                   <object type="image/png" width="10" height="11" data="images/a.png">Alt</object>
                                 </qti-item-body>
                               </qti-assessment-item>
                               """;

        CreateZip(inputZipPath, (itemPath, itemXml));

        var callbackCalled = false;

        var converter = new Qti2ToQti3PackageConverter(new Qti2ToQti3PackageConverterOptions
        {
            ConvertManifest = false,
            SyncAssessmentItemIdentifiers = false,
            ItemTransformOptions = new QtiItemTransformOptions
            {
                ObjectToImg = false,
                ObjectToVideo = false,
                ObjectToAudio = false,
                SsmlSubToSpan = false,
                StripMaterialInfo = false,
                MinChoicesToOne = false,
                ExternalScored = false
            },
            OnItemTransformedAsync = async (transform, path, fileResolver, ct) =>
            {
                Assert.Equal(itemPath, path);
                Assert.NotNull(fileResolver); // Verify file resolver is provided
                callbackCalled = true;

                await transform.FnChAsync(doc =>
                {
                    doc.Root?.SetAttributeValue("data-callback", "1");
                    return Task.CompletedTask;
                });
            }
        });

        var outputZipPath = await converter.ConvertQti2PackageToQti3Async(inputZipPath, CancellationToken.None);
        Assert.True(callbackCalled);

        var outputItemXml = ReadZipEntryText(outputZipPath, itemPath);

        Assert.Contains("data-callback=\"1\"", outputItemXml);
        Assert.Contains("<object", outputItemXml); // ObjectToImg disabled
        Assert.DoesNotContain("<img", outputItemXml);
    }

    [Fact]
    public async Task ConvertQti2PackageToQti3Async_StripsStylesheetsFromStimulusFiles()
    {
        var (inputZipPath, stimulusPath) = CreateStimulusPackage();

        var converter = new Qti2ToQti3PackageConverter(new Qti2ToQti3PackageConverterOptions
        {
            ConvertManifest = false,
            SyncAssessmentItemIdentifiers = false,
            ItemTransformOptions = new QtiItemTransformOptions { StripStylesheets = true }
        });

        var outputZipPath = await converter.ConvertQti2PackageToQti3Async(inputZipPath, CancellationToken.None);
        var stimulusXml = ReadZipEntryText(outputZipPath, stimulusPath);

        Assert.DoesNotContain("qti-stylesheet", stimulusXml); // both stylesheets stripped
        Assert.Contains("qti-stimulus-body", stimulusXml); // body preserved
        Assert.Contains("Hello stimulus", stimulusXml);
    }

    [Fact]
    public async Task ConvertQti2PackageToQti3Async_HonorsKeepPatternForStimulusStylesheets()
    {
        var (inputZipPath, stimulusPath) = CreateStimulusPackage();

        var converter = new Qti2ToQti3PackageConverter(new Qti2ToQti3PackageConverterOptions
        {
            ConvertManifest = false,
            SyncAssessmentItemIdentifiers = false,
            ItemTransformOptions = new QtiItemTransformOptions { StripStylesheetsKeepPattern = "*keep.css" }
        });

        var outputZipPath = await converter.ConvertQti2PackageToQti3Async(inputZipPath, CancellationToken.None);
        var stimulusXml = ReadZipEntryText(outputZipPath, stimulusPath);

        Assert.Contains("css/keep.css", stimulusXml);
        Assert.DoesNotContain("css/drop.css", stimulusXml);
    }

    [Fact]
    public async Task ConvertQti2PackageToQti3Async_LeavesStimulusStylesheetsWhenStrippingDisabled()
    {
        var (inputZipPath, stimulusPath) = CreateStimulusPackage();

        var converter = new Qti2ToQti3PackageConverter(new Qti2ToQti3PackageConverterOptions
        {
            ConvertManifest = false,
            SyncAssessmentItemIdentifiers = false,
            ItemTransformOptions = new QtiItemTransformOptions() // StripStylesheets defaults to false
        });

        var outputZipPath = await converter.ConvertQti2PackageToQti3Async(inputZipPath, CancellationToken.None);
        var stimulusXml = ReadZipEntryText(outputZipPath, stimulusPath);

        Assert.Contains("css/keep.css", stimulusXml);
        Assert.Contains("css/drop.css", stimulusXml);
    }

    private static (string inputZipPath, string stimulusPath) CreateStimulusPackage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Citolab.QTI.Converter.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var inputZipPath = Path.Combine(tempDir, "input.zip");
        const string stimulusPath = "ref/stimulus.xml";

        const string stimulusXml = """
                                   <?xml version="1.0" encoding="UTF-8"?>
                                   <qti-assessment-stimulus xmlns="http://www.imsglobal.org/xsd/imsqtiasi_v3p0" identifier="STIM1" title="Stimulus">
                                     <qti-stylesheet href="css/keep.css" type="text/css" />
                                     <qti-stylesheet href="css/drop.css" type="text/css" />
                                     <qti-stimulus-body>
                                       <p>Hello stimulus</p>
                                     </qti-stimulus-body>
                                   </qti-assessment-stimulus>
                                   """;

        CreateZip(inputZipPath, (stimulusPath, stimulusXml));
        return (inputZipPath, stimulusPath);
    }

    private static void CreateZip(string zipPath, params (string path, string text)[] entries)
    {
        using var output = File.Create(zipPath);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var (path, text) in entries)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1024, leaveOpen: false);
            writer.Write(text);
        }
    }

    private static string ReadZipEntryText(string zipPath, string entryPath)
    {
        using var input = File.OpenRead(zipPath);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);

        var entry = archive.GetEntry(entryPath);
        Assert.NotNull(entry);

        using var entryStream = entry!.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}

