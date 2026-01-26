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
            OnItemTransformedAsync = async (transform, path, ct) =>
            {
                Assert.Equal(itemPath, path);
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

