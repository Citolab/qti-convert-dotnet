using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Citolab.QTI.Converter;
using Xunit;

namespace Citolab.QTI.Uploader.Tests;

public sealed class SharedVocabularyStylesheetTests
{
    private static readonly XNamespace Qti21 = "http://www.imsglobal.org/xsd/imsqti_v2p1";
    private static readonly XNamespace ImsCp21 = "http://www.imsglobal.org/xsd/imscp_v1p1";

    private static string Item(string id, string body) =>
        $"""<qti-assessment-item xmlns="http://www.imsglobal.org/xsd/imsqtiasi_v3p0" identifier="{id}" title="{id}" adaptive="false" time-dependent="false"><qti-item-body>{body}</qti-item-body></qti-assessment-item>""";

    private const string LayoutBody = """<div class="qti-layout-row"><div class="qti-layout-col6"><p class="qti-underline">x</p></div></div>""";

    [Fact]
    public void Css_IsThe1EdTechStylesheetUnmodified()
    {
        using var sha = SHA256.Create();
        var hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(QtiSharedVocabularyStylesheet.Css))).Replace("-", string.Empty);
        Assert.StartsWith("8800D526A03D8EB8", hash);
        Assert.Contains(".qti-layout-row", QtiSharedVocabularyStylesheet.Css);
    }

    [Fact]
    public void Convert_AddsTheStylesheetBeforeTheItemBody_OnlyWhenQtiClassesAreUsed()
    {
        var result = Qti3ToQti21XmlConverter.Convert(Item("A", LayoutBody), new Qti3ToQti21ConvertOptions { SharedVocabularyStylesheetHref = "../qti3p0.css" });
        var stylesheet = XDocument.Parse(result.Xml).Root!.Element(Qti21 + "stylesheet")!;

        Assert.Equal("../qti3p0.css", (string?)stylesheet.Attribute("href"));
        Assert.Equal("text/css", (string?)stylesheet.Attribute("type"));
        Assert.Equal(Qti21 + "itemBody", ((XElement)stylesheet.NextNode!).Name);
        Assert.Contains(result.Warnings, w => w.Code == Qti21WarningCode.SharedVocabularyStylesheet);
        Assert.DoesNotContain(result.Warnings, w => w.Code == Qti21WarningCode.SharedVocabularyClasses);

        var plain = Qti3ToQti21XmlConverter.Convert(Item("B", "<p>y</p>"), new Qti3ToQti21ConvertOptions { SharedVocabularyStylesheetHref = "qti3p0.css" });
        Assert.DoesNotContain("stylesheet", plain.Xml);
    }

    private static byte[] Zip(string prefix)
    {
        var entries = new[]
        {
            ("imsmanifest.xml", """
                <manifest xmlns="http://www.imsglobal.org/xsd/qti/qtiv3p0/imscp_v1p1" identifier="M"><resources>
                  <resource identifier="A" type="imsqti_item_xmlv3p0" href="items/a.xml"><file href="items/a.xml"/></resource>
                  <resource identifier="B" type="imsqti_item_xmlv3p0" href="items/b.xml"><file href="items/b.xml"/></resource>
                </resources></manifest>
                """),
            ("items/a.xml", Item("A", LayoutBody)),
            ("items/b.xml", Item("B", "<p>y</p>"))
        };
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(prefix + path).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        return ms.ToArray();
    }

    private static async Task<Dictionary<string, string>> ConvertAsync(byte[] zip, Qti3ToQti21PackageConverterOptions? options = null)
    {
        using var output = new MemoryStream();
        await new Qti3ToQti21PackageConverter(options).ConvertAsync(new MemoryStream(zip), output, CancellationToken.None);
        using var archive = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(e => e.FullName, e => new StreamReader(e.Open()).ReadToEnd());
    }

    [Theory]
    [InlineData("")]
    [InlineData("pkg/")] // the stylesheet goes next to the manifest, the package root
    public async Task ConvertAsync_AddsQti3p0CssNextToTheManifest(string prefix)
    {
        var files = await ConvertAsync(Zip(prefix));

        Assert.Equal(QtiSharedVocabularyStylesheet.Css, files[$"{prefix}qti3p0.css"]);
        Assert.Equal("../qti3p0.css", (string?)XDocument.Parse(files[$"{prefix}items/a.xml"]).Root!.Element(Qti21 + "stylesheet")!.Attribute("href"));
        Assert.DoesNotContain("stylesheet", files[$"{prefix}items/b.xml"]);

        var manifest = XDocument.Parse(files[$"{prefix}imsmanifest.xml"]).Root!;
        var resources = manifest.Descendants(ImsCp21 + "resource").ToDictionary(r => (string)r.Attribute("identifier")!);
        var css = resources["QTI3_SHARED_VOCABULARY_CSS"];
        Assert.Equal("webcontent", (string?)css.Attribute("type"));
        Assert.Equal("qti3p0.css", (string?)css.Element(ImsCp21 + "file")!.Attribute("href"));
        Assert.Equal("QTI3_SHARED_VOCABULARY_CSS", (string?)resources["A"].Element(ImsCp21 + "dependency")!.Attribute("identifierref"));
        Assert.Null(resources["B"].Element(ImsCp21 + "dependency"));
    }

    [Fact]
    public async Task ConvertAsync_CanBeSwitchedOff()
    {
        var files = await ConvertAsync(Zip(string.Empty), new Qti3ToQti21PackageConverterOptions { InjectSharedVocabularyStylesheet = false });

        Assert.False(files.ContainsKey("qti3p0.css"));
        Assert.DoesNotContain("stylesheet", files["items/a.xml"]);
    }

    [Fact]
    public void ConvertedItemWithStylesheet_IsValidQti21()
    {
        var schema = QtiSchemaValidator.Get("qti21", QtiSchemaValidator.Qti21XsdUrl);
        if (schema is null) return;

        var xml = Qti3ToQti21XmlConverter.Convert(Item("A", LayoutBody), new Qti3ToQti21ConvertOptions { SharedVocabularyStylesheetHref = "qti3p0.css" }).Xml;

        Assert.Empty(QtiSchemaValidator.Validate(xml, schema));
    }
}
