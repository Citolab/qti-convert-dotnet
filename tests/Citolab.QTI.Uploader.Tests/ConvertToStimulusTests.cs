using System.Xml.Linq;
using Citolab.QTI.Converter;
using Xunit;

namespace Citolab.QTI.Uploader.Tests;

public sealed class ConvertToStimulusTests
{
    private static string Item(string identifier, string leftXml, string rightXml) => $"""
<?xml version="1.0" encoding="UTF-8"?>
<qti-assessment-item xmlns="http://www.imsglobal.org/xsd/imsqtiasi_v3p0" identifier="{identifier}">
  <qti-item-body>
    <div class="content">
      <div class="qti-layout-row">
        <div class="qti-layout-col6">{leftXml}</div>
        <div class="qti-layout-col6">{rightXml}</div>
      </div>
    </div>
  </qti-item-body>
</qti-assessment-item>
""";

    private static XDocument Parse(string xml) => XDocument.Parse(xml);

    private static IEnumerable<XElement> ByLocalName(XDocument doc, string localName) =>
        doc.Descendants().Where(e => e.Name.LocalName == localName);

    [Fact]
    public void ExtractsIdenticalLeftColumns_IntoSharedStimulus_AndRewritesItems()
    {
        var items = new Dictionary<string, string>
        {
            ["depitems/a.xml"] = Item("ITM-A", "<p>Shared source text</p>", "<div id=\"question\"><p>Q A</p></div>"),
            ["depitems/b.xml"] = Item("ITM-B", "<p>Shared source text</p>", "<div id=\"question\"><p>Q B</p></div>"),
        };

        var result = QtiStimulusExtractor.ConvertToStimulus(items);

        Assert.Single(result.Stimuli);
        var (stimulusPath, stimulusXml) = result.Stimuli.First();
        Assert.Matches(@"^ref/stimulus-.*\.xml$", stimulusPath);

        var stimulusDoc = Parse(stimulusXml);
        var stimulusRoot = ByLocalName(stimulusDoc, "qti-assessment-stimulus").Single();
        var stimulusId = stimulusRoot.Attribute("identifier")!.Value;
        Assert.Contains("Shared source text", ByLocalName(stimulusDoc, "qti-stimulus-body").Single().Value);

        foreach (var path in new[] { "depitems/a.xml", "depitems/b.xml" })
        {
            var doc = Parse(result.Items[path]);
            var refEl = ByLocalName(doc, "qti-assessment-stimulus-ref").Single();
            Assert.Equal(stimulusId, refEl.Attribute("identifier")!.Value);
            Assert.Equal("../" + stimulusPath, refEl.Attribute("href")!.Value);

            var sharedDiv = doc.Descendants().Single(e =>
                e.Name.LocalName == "div" && (e.Attribute("class")?.Value ?? "").Contains("qti-shared-stimulus"));
            Assert.Equal(stimulusId, sharedDiv.Attribute("data-stimulus-idref")!.Value);

            // right column question preserved; left source content gone
            Assert.Contains(doc.Descendants(), e => e.Attribute("id")?.Value == "question");
            Assert.DoesNotContain("Shared source text", ByLocalName(doc, "qti-item-body").Single().Value);
        }
    }

    [Fact]
    public void DoesNotExtract_WhenLeftColumnsDiffer()
    {
        var items = new Dictionary<string, string>
        {
            ["a.xml"] = Item("ITM-A", "<p>Source A</p>", "<div id=\"question\"><p>Q A</p></div>"),
            ["b.xml"] = Item("ITM-B", "<p>Source B</p>", "<div id=\"question\"><p>Q B</p></div>"),
        };

        var result = QtiStimulusExtractor.ConvertToStimulus(items);

        Assert.Empty(result.Stimuli);
        Assert.Equal(items["a.xml"], result.Items["a.xml"]);
        Assert.Equal(items["b.xml"], result.Items["b.xml"]);
    }

    [Fact]
    public void IgnoresWhitespaceDifferences_WhenComparing()
    {
        var items = new Dictionary<string, string>
        {
            ["a.xml"] = Item("ITM-A", "<p>Shared</p>", "<div id=\"question\"><p>Q A</p></div>"),
            ["b.xml"] = Item("ITM-B", "<p>Shared</p>   \n  ", "<div id=\"question\"><p>Q B</p></div>"),
        };

        var result = QtiStimulusExtractor.ConvertToStimulus(items);

        Assert.Single(result.Stimuli);
    }

    [Fact]
    public void SkipsItems_ThatAlreadyReferenceAStimulus()
    {
        const string withRef = """
<?xml version="1.0" encoding="UTF-8"?>
<qti-assessment-item xmlns="http://www.imsglobal.org/xsd/imsqtiasi_v3p0" identifier="ITM-A">
  <qti-assessment-stimulus-ref identifier="RES-existing" href="../ref/existing.xml" />
  <qti-item-body>
    <div class="qti-layout-row">
      <div class="qti-layout-col6"><p>Shared source text</p></div>
      <div class="qti-layout-col6"><div id="question"><p>Q A</p></div></div>
    </div>
  </qti-item-body>
</qti-assessment-item>
""";
        var items = new Dictionary<string, string>
        {
            ["a.xml"] = withRef,
            ["b.xml"] = Item("ITM-B", "<p>Shared source text</p>", "<div id=\"question\"><p>Q B</p></div>"),
        };

        var result = QtiStimulusExtractor.ConvertToStimulus(items);

        // Only one item still exposes the shared content -> below MinItems -> no extraction.
        Assert.Empty(result.Stimuli);
        Assert.Equal(withRef, result.Items["a.xml"]);
    }

    [Fact]
    public void UpdatesManifest_WithStimulusResourceAndItemDependencies()
    {
        var items = new Dictionary<string, string>
        {
            ["depitems/a.xml"] = Item("ITM-A", "<p>Shared source text</p>", "<div id=\"question\"><p>Q A</p></div>"),
            ["depitems/b.xml"] = Item("ITM-B", "<p>Shared source text</p>", "<div id=\"question\"><p>Q B</p></div>"),
        };
        var manifest = new ManifestFile("imsmanifest.xml", """
<?xml version="1.0" encoding="UTF-8"?>
<manifest xmlns="http://www.imsglobal.org/xsd/qti/qtiv3p0/imscp_v1p1" identifier="MANIFEST">
  <resources>
    <resource identifier="ITM-A" type="imsqti_item_xmlv3p0" href="depitems/a.xml"><file href="depitems/a.xml" /></resource>
    <resource identifier="ITM-B" type="imsqti_item_xmlv3p0" href="depitems/b.xml"><file href="depitems/b.xml" /></resource>
  </resources>
</manifest>
""");

        var result = QtiStimulusExtractor.ConvertToStimulus(items, manifest);

        var doc = Parse(result.Manifest!.Xml);
        var stimulusResource = doc.Descendants()
            .Single(e => e.Name.LocalName == "resource" && e.Attribute("type")?.Value == StimulusResourceType());
        var stimulusId = stimulusResource.Attribute("identifier")!.Value;
        Assert.Matches(@"^ref/stimulus-.*\.xml$", stimulusResource.Attribute("href")!.Value);

        foreach (var id in new[] { "ITM-A", "ITM-B" })
        {
            var resource = doc.Descendants()
                .Single(e => e.Name.LocalName == "resource" && e.Attribute("identifier")?.Value == id);
            var dependency = resource.Elements().Single(e => e.Name.LocalName == "dependency");
            Assert.Equal(stimulusId, dependency.Attribute("identifierref")!.Value);
        }
    }

    private static string StimulusResourceType() => "imsqti_stimulus_xmlv3p0";
}
