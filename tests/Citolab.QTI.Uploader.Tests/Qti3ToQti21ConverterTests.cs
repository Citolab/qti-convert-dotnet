using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Citolab.QTI.Converter;
using Xunit;

namespace Citolab.QTI.Uploader.Tests;

public sealed class Qti3ToQti21ConverterTests
{
    private static readonly XNamespace Qti21 = "http://www.imsglobal.org/xsd/imsqti_v2p1";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    private const string Qti3Choice = """
        <?xml version="1.0" encoding="UTF-8"?>
        <?xml-model href="https://purl.imsglobal.org/spec/qti/v3p0/schema/xsd/imsqti_asiv3p0_v1p0.xsd" type="application/xml" schematypens="http://purl.oclc.org/dsdl/schematron"?>
        <qti-assessment-item xmlns="http://www.imsglobal.org/xsd/imsqtiasi_v3p0" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xsi:schemaLocation="http://www.imsglobal.org/xsd/imsqtiasi_v3p0 https://purl.imsglobal.org/spec/qti/v3p0/schema/xsd/imsqti_asiv3p0_v1p0.xsd"
          identifier="choice" title="Choice" adaptive="false" time-dependent="false" xml:lang="en">
          <qti-response-declaration identifier="RESPONSE" cardinality="single" base-type="identifier">
            <qti-correct-response><qti-value>A</qti-value></qti-correct-response>
          </qti-response-declaration>
          <qti-outcome-declaration identifier="SCORE" cardinality="single" base-type="float" external-scored="human"/>
          <qti-item-body>
            <div class="qti-layout-row" data-foo="bar" aria-label="row">
              <qti-choice-interaction response-identifier="RESPONSE" max-choices="1" data-max-selections-message="no">
                <qti-prompt>Kies één &amp; alleen één</qti-prompt>
                <qti-simple-choice identifier="A">A</qti-simple-choice>
                <qti-simple-choice identifier="B">B</qti-simple-choice>
              </qti-choice-interaction>
            </div>
            <math xmlns="http://www.w3.org/1998/Math/MathML"><mi mathvariant="bold">x</mi></math>
          </qti-item-body>
          <qti-response-processing template="https://purl.imsglobal.org/spec/qti/v3p0/rptemplates/match_correct.xml"/>
          <qti-modal-feedback outcome-identifier="FEEDBACK" identifier="correct" show-hide="show">
            <qti-content-body><p>Well done</p></qti-content-body>
          </qti-modal-feedback>
        </qti-assessment-item>
        """;

    private static string Item(string body, string head = "") => $"""
        <qti-assessment-item xmlns="http://www.imsglobal.org/xsd/imsqtiasi_v3p0" identifier="i1" adaptive="false" time-dependent="false">
          {head}
          <qti-item-body>{body}</qti-item-body>
        </qti-assessment-item>
        """;

    private static XElement Root(string xml) => XDocument.Parse(xml).Root!;
    private static XElement Single(XElement root, string name) => root.Descendants(Qti21 + name).Single();

    [Fact]
    public void Convert_RenamesElementsAndAttributesAndSetsTheQti21Namespace()
    {
        var result = Qti3ToQti21XmlConverter.Convert(Qti3Choice);
        var root = Root(result.Xml);

        Assert.DoesNotContain("<qti-", result.Xml);
        Assert.DoesNotContain("xml-model", result.Xml);
        Assert.Contains("<prompt>Kies één &amp; alleen één</prompt>", result.Xml);
        Assert.Equal(Qti21 + "assessmentItem", root.Name);
        Assert.Contains("imsqti_v2p1p2.xsd", (string?)root.Attribute(Xsi + "schemaLocation"));
        Assert.Equal("false", (string?)root.Attribute("timeDependent"));
        Assert.Equal("en", (string?)root.Attribute(XNamespace.Xml + "lang"));
        Assert.Equal("identifier", (string?)Single(root, "responseDeclaration").Attribute("baseType"));
        Assert.Equal("RESPONSE", (string?)Single(root, "choiceInteraction").Attribute("responseIdentifier"));
        Assert.Equal(2, root.Descendants(Qti21 + "simpleChoice").Count());
        Assert.Equal("http://www.imsglobal.org/question/qti_v2p1/rptemplates/match_correct",
            (string?)Single(root, "responseProcessing").Attribute("template"));
        Assert.Null(Single(root, "outcomeDeclaration").Attribute("externalScored"));
        Assert.Contains(result.Warnings, w => w.Code == Qti21WarningCode.RemovedAttribute);
        Assert.Contains(result.Warnings, w => w.Code == Qti21WarningCode.DataAttributesRemoved);
        Assert.Contains(result.Warnings, w => w.Code == Qti21WarningCode.SharedVocabularyClasses);
    }

    [Fact]
    public void Convert_UnwrapsContentBody_StripsDataAndAriaAttributes_KeepsMathMl()
    {
        var result = Qti3ToQti21XmlConverter.Convert(Qti3Choice);
        var root = Root(result.Xml);

        Assert.Equal("Well done", Single(root, "modalFeedback").Element(Qti21 + "p")!.Value);
        Assert.Equal("show", (string?)Single(root, "modalFeedback").Attribute("showHide"));
        Assert.DoesNotContain(root.DescendantsAndSelf().Attributes(), a => a.Name.LocalName.StartsWith("data"));
        // QTI 2.1 has no aria-*, role or dir
        Assert.Null(Single(root, "div").Attribute("aria-label"));
        Assert.Contains(result.Warnings, w => w.Code == Qti21WarningCode.AccessibilityAttributesRemoved);
        var mi = root.Descendants(XNamespace.Get("http://www.w3.org/1998/Math/MathML") + "mi").Single();
        Assert.Equal("bold", (string?)mi.Attribute("mathvariant"));
    }

    [Fact]
    public void Convert_UnwrapsNestedContentBodies()
    {
        var root = Root(Qti3ToQti21XmlConverter.Convert(Item("""
            <qti-template-block template-identifier="T" identifier="A" show-hide="show"><qti-content-body>
              <qti-feedback-block outcome-identifier="F" identifier="B" show-hide="show"><qti-content-body><p>Inner</p></qti-content-body></qti-feedback-block>
            </qti-content-body></qti-template-block>
            """)).Xml);

        Assert.Empty(root.Descendants().Where(e => e.Name.LocalName is "contentBody" or "qti-content-body"));
        Assert.Equal("Inner", Single(root, "templateBlock").Element(Qti21 + "feedbackBlock")!.Element(Qti21 + "p")!.Value);
    }

    [Fact]
    public void Convert_ConvertsAnImageOnlyGapTextToGapImg()
    {
        var result = Qti3ToQti21XmlConverter.Convert(Item("""
            <qti-gap-match-interaction response-identifier="RESPONSE">
              <qti-gap-text identifier="W1" match-max="1"><img src="a.png" alt="A"/></qti-gap-text>
              <qti-gap-text identifier="W2" match-max="1">text</qti-gap-text>
              <p>A <qti-gap identifier="G1"/></p>
            </qti-gap-match-interaction>
            """));
        var root = Root(result.Xml);

        var gapImg = Single(root, "gapImg");
        Assert.Equal("W1", (string?)gapImg.Attribute("identifier"));
        Assert.Equal("1", (string?)gapImg.Attribute("matchMax"));
        Assert.Equal("a.png", (string?)gapImg.Element(Qti21 + "object")!.Attribute("data"));
        Assert.Equal("text", Single(root, "gapText").Value);
        Assert.Contains(result.Warnings, w => w.Code == Qti21WarningCode.GapTextToGapImg);
    }

    [Fact]
    public void Convert_MapsIrregularOperatorNames()
    {
        var xml = Qti3ToQti21XmlConverter.Convert("""
            <qti-assessment-item xmlns="http://www.imsglobal.org/xsd/imsqtiasi_v3p0" identifier="x" adaptive="false" time-dependent="false">
              <qti-response-processing><qti-response-condition><qti-response-if>
                <qti-duration-lt><qti-variable identifier="duration"/><qti-base-value base-type="duration">10</qti-base-value></qti-duration-lt>
                <qti-set-outcome-value identifier="SCORE"><qti-base-value base-type="float">1</qti-base-value></qti-set-outcome-value>
              </qti-response-if></qti-response-condition></qti-response-processing>
            </qti-assessment-item>
            """).Xml;

        Assert.Contains("<durationLT>", xml);
        Assert.Contains("<setOutcomeValue identifier=\"SCORE\">", xml);
    }

    [Fact]
    public void Convert_InlinesASharedStimulusAndRebasesItsAssets()
    {
        const string stimulus = """
            <qti-assessment-stimulus xmlns="http://www.imsglobal.org/xsd/imsqtiasi_v3p0" identifier="S1" title="Passage" xml:lang="nl">
              <qti-stylesheet href="style.css" type="text/css"/>
              <qti-stimulus-body><p>Passage text</p><p><img src="img/a.png" alt="a"/></p></qti-stimulus-body>
            </qti-assessment-stimulus>
            """;
        var hrefs = new List<string>();
        var result = Qti3ToQti21XmlConverter.Convert(
            Item("<p>Question</p>", """<qti-assessment-stimulus-ref identifier="S1" href="../stimuli/s1.xml" title="Passage"/>"""),
            new Qti3ToQti21ConvertOptions { ResolveStimulus = href => { hrefs.Add(href); return stimulus; } });
        var root = Root(result.Xml);

        Assert.Equal(new[] { "../stimuli/s1.xml" }, hrefs);
        Assert.Empty(root.Descendants(Qti21 + "assessmentStimulusRef"));
        var shared = Single(root, "itemBody").Elements(Qti21 + "div").First();
        Assert.Equal("qti-shared-stimulus", (string?)shared.Attribute("class"));
        Assert.Equal("nl", (string?)shared.Attribute(XNamespace.Xml + "lang"));
        Assert.Equal("Passage text", shared.Element(Qti21 + "p")!.Value);
        Assert.Equal("../stimuli/img/a.png", (string?)Single(root, "img").Attribute("src"));
        var stylesheet = Single(root, "stylesheet");
        Assert.Equal("../stimuli/style.css", (string?)stylesheet.Attribute("href"));
        Assert.Equal(Qti21 + "itemBody", stylesheet.ElementsAfterSelf().First().Name);
        Assert.Contains(result.Warnings, w => w.Code == Qti21WarningCode.StimulusInlined);
    }

    [Fact]
    public void Convert_RemovesAnUnresolvableStimulusRefWithAWarning()
    {
        var result = Qti3ToQti21XmlConverter.Convert(Item(string.Empty, """<qti-assessment-stimulus-ref identifier="S1" href="s1.xml"/>"""));

        Assert.DoesNotContain("stimulus", result.Xml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Warnings, w => w.Code == Qti21WarningCode.StimulusUnresolved);
    }

    [Fact]
    public void Convert_ConvertsHtml5MediaAndGraphicInteractionImagesToObject()
    {
        var result = Qti3ToQti21XmlConverter.Convert(Item("""
            <audio controls="controls"><source src="media/a.mp3" type="audio/mpeg"/>Audio</audio>
            <p><video src="media/v.mp4" width="320"/></p>
            <figure><img src="x.png" alt="x"/><figcaption>X</figcaption></figure>
            <qti-select-point-interaction response-identifier="RESPONSE" max-choices="1">
              <img src="map.png" width="60" height="40" alt="Map"/>
            </qti-select-point-interaction>
            """));
        var root = Root(result.Xml);
        var objects = root.Descendants(Qti21 + "object").ToDictionary(o => (string)o.Attribute("data")!);

        Assert.Empty(root.Descendants().Where(e => e.Name.LocalName is "audio" or "video" or "figure" or "figcaption"));
        Assert.Equal("audio/mpeg", (string?)objects["media/a.mp3"].Attribute("type"));
        Assert.Equal("Audio", objects["media/a.mp3"].Value);
        // object is inline in QTI 2.1, so directly in the item body it gets a block wrapper
        Assert.Equal(Qti21 + "div", objects["media/a.mp3"].Parent!.Name);
        Assert.Equal(Qti21 + "p", objects["media/v.mp4"].Parent!.Name);
        Assert.Equal("video/mp4", (string?)objects["media/v.mp4"].Attribute("type"));
        Assert.Equal("320", (string?)objects["media/v.mp4"].Attribute("width"));
        Assert.Equal(Qti21 + "selectPointInteraction", objects["map.png"].Parent!.Name);
        Assert.Equal("image/png", (string?)objects["map.png"].Attribute("type"));
        Assert.Equal("Map", objects["map.png"].Value);
        Assert.Contains(result.Warnings, w => w.Code == Qti21WarningCode.MediaToObject);
        Assert.Contains(result.Warnings, w => w.Code == Qti21WarningCode.ImgToObject);
        Assert.Contains(result.Warnings, w => w.Code == Qti21WarningCode.Html5Element);
    }

    [Fact]
    public void Convert_WrapsAPciInACustomInteraction()
    {
        var result = Qti3ToQti21XmlConverter.Convert(Item("""
            <qti-portable-custom-interaction response-identifier="RESPONSE" custom-interaction-type-identifier="likert" module="likert">
              <qti-interaction-markup><div>markup</div></qti-interaction-markup>
            </qti-portable-custom-interaction>
            """));
        XNamespace pci = "http://www.imsglobal.org/xsd/portableCustomInteraction";
        var root = Root(result.Xml);

        Assert.DoesNotContain("<qti-", result.Xml);
        var custom = Single(root, "customInteraction");
        Assert.Equal("RESPONSE", (string?)custom.Attribute("responseIdentifier"));
        var wrapper = custom.Element(pci + "portableCustomInteraction")!;
        Assert.Equal("likert", (string?)wrapper.Attribute("customInteractionTypeIdentifier"));
        Assert.NotNull(wrapper.Element(pci + "interactionMarkup"));
        Assert.Contains(result.Warnings, w => w.Code == Qti21WarningCode.Pci);
    }

    [Fact]
    public void Convert_LeavesQti2InputUnchanged()
    {
        const string input = """<assessmentItem xmlns="http://www.imsglobal.org/xsd/imsqti_v2p1" identifier="x"/>""";
        var result = Qti3ToQti21XmlConverter.Convert(input);

        Assert.Equal(input, result.Xml);
        Assert.Equal(Qti21WarningCode.AlreadyQti2, result.Warnings.Single().Code);
    }

    [Fact]
    public void Convert_RoundTripsQti21ThroughQti3()
    {
        const string qti21 = """
            <?xml version="1.0" encoding="UTF-8"?>
            <assessmentItem xmlns="http://www.imsglobal.org/xsd/imsqti_v2p1" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
              xsi:schemaLocation="http://www.imsglobal.org/xsd/imsqti_v2p1 http://www.imsglobal.org/xsd/qti/qtiv2p1/imsqti_v2p1p2.xsd"
              identifier="roundtrip" title="Round trip" adaptive="false" timeDependent="false">
              <responseDeclaration identifier="RESPONSE" cardinality="multiple" baseType="directedPair">
                <correctResponse><value>W1 G1</value></correctResponse>
                <mapping defaultValue="0"><mapEntry mapKey="W1 G1" mappedValue="1"/></mapping>
              </responseDeclaration>
              <outcomeDeclaration identifier="SCORE" cardinality="single" baseType="float"/>
              <outcomeDeclaration identifier="FEEDBACK" cardinality="single" baseType="identifier"/>
              <itemBody>
                <p>Intro <textEntryInteraction responseIdentifier="RESPONSE2" expectedLength="10"/></p>
                <gapMatchInteraction responseIdentifier="RESPONSE" shuffle="false">
                  <gapText identifier="W1" matchMax="1">word</gapText>
                  <p>A <gap identifier="G1"/> sentence.</p>
                </gapMatchInteraction>
                <feedbackBlock outcomeIdentifier="FEEDBACK" identifier="fb" showHide="show"><p>Feedback</p></feedbackBlock>
              </itemBody>
              <responseProcessing template="http://www.imsglobal.org/question/qti_v2p1/rptemplates/map_response"/>
            </assessmentItem>
            """;

        var qti3 = Qti2ToQti3XmlConverter.Convert(qti21);
        Assert.Contains("<qti-gap-match-interaction", qti3);
        var back = Qti3ToQti21XmlConverter.Convert(qti3).Xml;

        Assert.Equal(XmlCanonical.Of(qti21), XmlCanonical.Of(back));
    }

    private const string Manifest = """
        <?xml version="1.0" encoding="UTF-8"?>
        <manifest xmlns="http://www.imsglobal.org/xsd/qti/qtiv3p0/imscp_v1p1" xmlns:imsqti="http://www.imsglobal.org/xsd/imsqti_metadata_v3p0" identifier="M">
          <metadata><schema>QTI Package</schema><schemaversion>3.0.0</schemaversion></metadata>
          <organizations/>
          <resources>
            <resource identifier="test" type="imsqti_test_xmlv3p0" href="test.xml"><file href="test.xml"/><dependency identifierref="item"/></resource>
            <resource identifier="item" type="imsqti_item_xmlv3p0" href="items/item.xml"><file href="items/item.xml"/><dependency identifierref="stim"/></resource>
            <resource identifier="stim" type="imsqti_stimulus_xmlv3p0" href="stimuli/s1.xml">
              <file href="stimuli/s1.xml"/><file href="stimuli/style.css"/><dependency identifierref="img"/>
            </resource>
            <resource identifier="img" type="webcontent" href="stimuli/img/a.png"><file href="stimuli/img/a.png"/></resource>
          </resources>
        </manifest>
        """;

    private static readonly XNamespace ImsCp21 = "http://www.imsglobal.org/xsd/imscp_v1p1";

    private static XElement Resource(XElement root, string id) =>
        root.Descendants(ImsCp21 + "resource").Single(r => (string?)r.Attribute("identifier") == id);

    [Fact]
    public void ConvertManifest_ConvertsNamespacesSchemaVersionAndResourceTypes()
    {
        var root = Root(Qti3ToQti21ManifestConverter.Convert(Manifest));

        Assert.Equal(ImsCp21 + "manifest", root.Name);
        Assert.Equal("http://www.imsglobal.org/xsd/imsqti_metadata_v2p1", (string?)root.Attribute(XNamespace.Xmlns + "imsqti"));
        Assert.Equal("QTIv2.1 Package", root.Descendants(ImsCp21 + "schema").Single().Value);
        Assert.Equal("1.0.0", root.Descendants(ImsCp21 + "schemaversion").Single().Value);
        Assert.Equal("imsqti_test_xmlv2p1", (string?)Resource(root, "test").Attribute("type"));
        Assert.Equal("imsqti_item_xmlv2p1", (string?)Resource(root, "item").Attribute("type"));
        Assert.Equal("webcontent", (string?)Resource(root, "stim").Attribute("type"));
    }

    [Fact]
    public void ConvertManifest_ReplacesInlinedStimulusResourcesByTheirFilesAndDependencies()
    {
        var root = Root(Qti3ToQti21ManifestConverter.Convert(Manifest, new HashSet<string> { "stimuli/s1.xml" }));
        var item = Resource(root, "item");

        Assert.DoesNotContain(root.Descendants(ImsCp21 + "resource"), r => (string?)r.Attribute("identifier") == "stim");
        Assert.Equal(new[] { "img" }, item.Elements(ImsCp21 + "dependency").Select(d => (string?)d.Attribute("identifierref")));
        Assert.Equal(new[] { "items/item.xml", "stimuli/style.css" }, item.Elements(ImsCp21 + "file").Select(f => (string?)f.Attribute("href")));
    }

    private static byte[] Zip(params (string Path, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        return ms.ToArray();
    }

    private static Dictionary<string, string> Unzip(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(e => e.FullName, e => new StreamReader(e.Open()).ReadToEnd());
    }

    [Fact]
    public async Task ConvertAsync_InlinesStimuliAndDropsTheStimulusFile()
    {
        var input = Zip(
            ("imsmanifest.xml", """
                <manifest xmlns="http://www.imsglobal.org/xsd/qti/qtiv3p0/imscp_v1p1" identifier="M"><resources>
                  <resource identifier="item" type="imsqti_item_xmlv3p0" href="items/item.xml"><file href="items/item.xml"/><dependency identifierref="stim"/></resource>
                  <resource identifier="stim" type="imsqti_stimulus_xmlv3p0" href="stimuli/s1.xml"><file href="stimuli/s1.xml"/></resource>
                </resources></manifest>
                """),
            ("items/item.xml", Item("<p>Q</p>", """<qti-assessment-stimulus-ref identifier="stim" href="../stimuli/s1.xml"/>""")),
            ("stimuli/s1.xml", """<qti-assessment-stimulus xmlns="http://www.imsglobal.org/xsd/imsqtiasi_v3p0" identifier="stim" title="S"><qti-stimulus-body><p>Passage</p></qti-stimulus-body></qti-assessment-stimulus>"""));

        using var output = new MemoryStream();
        var warnings = await new Qti3ToQti21PackageConverter().ConvertAsync(new MemoryStream(input), output, CancellationToken.None);
        var files = Unzip(output.ToArray());

        Assert.Equal(new[] { "imsmanifest.xml", "items/item.xml" }, files.Keys.OrderBy(k => k));
        Assert.Contains("<div class=\"qti-shared-stimulus\"><p>Passage</p></div>", files["items/item.xml"]);
        Assert.DoesNotContain("stim", files["imsmanifest.xml"]);
        Assert.Contains(warnings, w => w.Code == Qti21WarningCode.StimulusInlined && w.File == "items/item.xml");
    }

    [Fact]
    public async Task ConvertAsync_UsesTheConversionHooks()
    {
        var input = Zip(
            ("imsmanifest.xml", """<manifest xmlns="http://www.imsglobal.org/xsd/qti/qtiv3p0/imscp_v1p1" identifier="M"/>"""),
            ("item.xml", Item(string.Empty)),
            ("test.xml", """<qti-assessment-test xmlns="http://www.imsglobal.org/xsd/imsqtiasi_v3p0" identifier="t" title="t"/>"""));
        var seen = new List<string>();
        var converter = new Qti3ToQti21PackageConverter(new Qti3ToQti21PackageConverterOptions
        {
            ConvertItem = (xml, context) =>
            {
                seen.Add($"item:{context.Path}");
                var result = Qti3ToQti21XmlConverter.Convert(xml);
                return new Qti21ConversionResult(result.Xml.Replace("identifier=\"i1\"", "identifier=\"custom\""), result.Warnings);
            },
            ConvertAssessment = (xml, context) => { seen.Add($"test:{context.Path}"); return Qti3ToQti21XmlConverter.Convert(xml); },
            ConvertManifest = (xml, _) => { seen.Add("manifest"); return xml.Replace("identifier=\"M\"", "identifier=\"M2\""); }
        });

        using var output = new MemoryStream();
        await converter.ConvertAsync(new MemoryStream(input), output, CancellationToken.None);
        var files = Unzip(output.ToArray());

        Assert.Equal(new[] { "item:item.xml", "test:test.xml", "manifest" }, seen);
        Assert.Contains("identifier=\"custom\"", files["item.xml"]);
        Assert.Contains("identifier=\"M2\"", files["imsmanifest.xml"]);
    }

    [Fact]
    public async Task ConvertQti3PackageToQti21Async_ConvertsTheQti3SamplePackage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Citolab.QTI.Converter.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var inputZip = Path.Combine(tempDir, "qti3-sample.zip");
        File.Copy(TestDataPaths.Qti3ZipPath, inputZip);

        var result = await new Qti3ToQti21PackageConverter().ConvertQti3PackageToQti21Async(inputZip, CancellationToken.None);

        Assert.EndsWith("qti3-sample-qti21.zip", result.OutputZipPath);
        using var input = ZipFile.OpenRead(inputZip);
        using var output = ZipFile.OpenRead(result.OutputZipPath);
        Assert.Equal(input.Entries.Count(e => !e.FullName.EndsWith("/")), output.Entries.Count);
        foreach (var entry in output.Entries.Where(e => e.FullName.EndsWith(".xml")))
        {
            var xml = new StreamReader(entry.Open()).ReadToEnd();
            var root = XDocument.Parse(xml).Root!;
            if (root.Name.LocalName == "manifest")
            {
                Assert.DoesNotContain("v3p0", xml);
                continue;
            }
            Assert.False(xml.Contains("<qti-"), entry.FullName);
            Assert.Equal(Qti21, root.Name.Namespace);
        }
    }
}
