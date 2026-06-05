using System.Xml.Linq;
using Citolab.QTI.Converter;
using Xunit;

namespace Citolab.QTI.Uploader.Tests;

public sealed class WrapStimulusInSectionTests
{
    private static Func<string, string, Task<ItemRefNormalizationMeta?>> Resolver(
        IReadOnlyDictionary<string, ItemRefNormalizationMeta> meta) =>
        (_, identifier) => Task.FromResult(meta.TryGetValue(identifier, out var m) ? m : null);

    private static List<XElement> Sections(string xml) =>
        XDocument.Parse(xml).Descendants()
            .Where(e => e.Name.LocalName is "qti-assessment-section" or "assessmentSection")
            .ToList();

    private static string[] ItemIds(XElement section) =>
        section.Descendants()
            .Where(e => e.Name.LocalName is "qti-assessment-item-ref" or "assessmentItemRef")
            .Select(e => e.Attribute("identifier")?.Value!)
            .Where(v => !string.IsNullOrEmpty(v))
            .ToArray();

    private static XElement TestPart(string xml) =>
        XDocument.Parse(xml).Descendants().First(e => e.Name.LocalName is "qti-test-part" or "testPart");

    [Fact]
    public async Task GroupsConsecutiveSharedStimulus_AndSetsKeepTogether()
    {
        const string input = """
<qti-assessment-test xmlns="http://www.imsglobal.org/xsd/imsqtiasi_v3p0">
  <qti-test-part identifier="P1">
    <qti-assessment-item-ref identifier="ITM-1" href="1.xml" />
    <qti-assessment-item-ref identifier="ITM-2" href="2.xml" />
    <qti-assessment-item-ref identifier="ITM-3" href="3.xml" />
  </qti-test-part>
</qti-assessment-test>
""";
        var meta = new Dictionary<string, ItemRefNormalizationMeta>
        {
            ["ITM-1"] = new(false, "STIM-A", "Vraag 1"),
            ["ITM-2"] = new(false, "STIM-A", "Vraag 2"),
            ["ITM-3"] = new(false, null, "Vraag 3"),
        };

        var result = (await QtiTransform.Create(input).WrapStimulusInSectionAsync(Resolver(meta))).Xml();

        var sections = Sections(result);
        Assert.Equal(2, sections.Count);
        Assert.Equal("true", sections[0].Attribute("keep-together")?.Value);
        Assert.Null(sections[1].Attribute("keep-together"));
        Assert.Equal(new[] { "ITM-1", "ITM-2" }, ItemIds(sections[0]));
        Assert.Equal(new[] { "ITM-3" }, ItemIds(sections[1]));
        Assert.Equal("section", TestPart(result).Attribute("data-navigation-entity")?.Value);
        Assert.Null(TestPart(result).Attribute("data-cito-navigate"));
    }

    [Fact]
    public async Task DoesNotMergeInfoItems()
    {
        const string input = """
<qti-assessment-test xmlns="http://www.imsglobal.org/xsd/imsqtiasi_v3p0">
  <qti-test-part identifier="P1">
    <qti-assessment-item-ref identifier="INFO-1" href="i.xml" category="dep-informational" />
    <qti-assessment-item-ref identifier="ITM-2" href="2.xml" />
    <qti-assessment-item-ref identifier="ITM-3" href="3.xml" />
  </qti-test-part>
</qti-assessment-test>
""";
        var meta = new Dictionary<string, ItemRefNormalizationMeta>
        {
            ["INFO-1"] = new(false, "STIM-X", "Informatie"),
            ["ITM-2"] = new(false, "STIM-X", "Vraag 2"),
            ["ITM-3"] = new(false, "STIM-X", "Vraag 3"),
        };

        var result = (await QtiTransform.Create(input).WrapStimulusInSectionAsync(Resolver(meta))).Xml();

        var sections = Sections(result);
        Assert.Equal(2, sections.Count);
        Assert.Equal(new[] { "INFO-1" }, ItemIds(sections[0]));
        Assert.Null(sections[0].Attribute("keep-together"));
        Assert.Equal(new[] { "ITM-2", "ITM-3" }, ItemIds(sections[1]));
        Assert.Equal("true", sections[1].Attribute("keep-together")?.Value);
    }

    [Fact]
    public async Task DoesNotRewrite_WhenNoSharedStimulusGroupExists()
    {
        const string input = """
<qti-assessment-test xmlns="http://www.imsglobal.org/xsd/imsqtiasi_v3p0">
  <qti-test-part identifier="P1">
    <qti-assessment-item-ref identifier="ITM-1" href="1.xml" />
    <qti-assessment-item-ref identifier="ITM-2" href="2.xml" />
    <qti-assessment-item-ref identifier="ITM-3" href="3.xml" />
  </qti-test-part>
</qti-assessment-test>
""";
        var meta = new Dictionary<string, ItemRefNormalizationMeta>
        {
            ["ITM-1"] = new(false, "STIM-A", "Vraag 1"),
            ["ITM-2"] = new(false, "STIM-B", "Vraag 2"),
            ["ITM-3"] = new(false, null, "Vraag 3"),
        };

        var result = (await QtiTransform.Create(input).WrapStimulusInSectionAsync(Resolver(meta))).Xml();

        Assert.Empty(Sections(result));
        Assert.Null(TestPart(result).Attribute("data-navigation-entity"));
        Assert.Equal(3, XDocument.Parse(result).Descendants()
            .Count(e => e.Name.LocalName is "qti-assessment-item-ref" or "assessmentItemRef"));
    }

    [Fact]
    public async Task NormalizesExistingSharedStimulusSection()
    {
        const string input = """
<qti-assessment-test xmlns="http://www.imsglobal.org/xsd/imsqtiasi_v3p0">
  <qti-test-part identifier="P1">
    <qti-assessment-section identifier="SOURCE-SECTION" visible="true">
      <qti-assessment-item-ref identifier="ITM-1" href="1.xml" />
      <qti-assessment-item-ref identifier="ITM-2" href="2.xml" />
    </qti-assessment-section>
    <qti-assessment-section identifier="SINGLE" visible="true">
      <qti-assessment-item-ref identifier="ITM-3" href="3.xml" />
    </qti-assessment-section>
  </qti-test-part>
</qti-assessment-test>
""";
        var meta = new Dictionary<string, ItemRefNormalizationMeta>
        {
            ["ITM-1"] = new(false, "STIM-A", "Vraag 1"),
            ["ITM-2"] = new(false, "STIM-A", "Vraag 2"),
            ["ITM-3"] = new(false, null, "Vraag 3"),
        };
        var assignments = new List<(string, string)>();

        var result = (await QtiTransform.Create(input)
            .WrapStimulusInSectionAsync(Resolver(meta), new WrapStimulusInSectionOptions { AssignmentsOut = assignments }))
            .Xml();

        var sections = Sections(result);
        Assert.Equal(2, sections.Count);
        Assert.Equal("true", sections[0].Attribute("keep-together")?.Value);
        Assert.Equal(new[] { "ITM-3" }, ItemIds(sections[1]));
        Assert.Equal(new[] { "ITM-1", "ITM-2", "ITM-3" }, assignments.Select(a => a.Item1).ToArray());
    }

    [Fact]
    public async Task LeavesTestPartAlone_WhenSingleWrappingSection()
    {
        const string input = """
<qti-assessment-test xmlns="http://www.imsglobal.org/xsd/imsqtiasi_v3p0">
  <qti-test-part identifier="P1">
    <qti-assessment-section identifier="ROOT" visible="true">
      <qti-assessment-section identifier="SUB" visible="true">
        <qti-assessment-item-ref identifier="ITM-1" href="1.xml" />
        <qti-assessment-item-ref identifier="ITM-2" href="2.xml" />
      </qti-assessment-section>
    </qti-assessment-section>
  </qti-test-part>
</qti-assessment-test>
""";
        var meta = new Dictionary<string, ItemRefNormalizationMeta>
        {
            ["ITM-1"] = new(false, "STIM-A", "Vraag 1"),
            ["ITM-2"] = new(false, "STIM-A", "Vraag 2"),
        };

        var result = (await QtiTransform.Create(input).WrapStimulusInSectionAsync(Resolver(meta))).Xml();

        var doc = XDocument.Parse(result);
        Assert.Contains(doc.Descendants(), e => e.Attribute("identifier")?.Value == "ROOT");
        Assert.Contains(doc.Descendants(), e => e.Attribute("identifier")?.Value == "SUB");
        Assert.Null(TestPart(result).Attribute("data-navigation-entity"));
    }
}
