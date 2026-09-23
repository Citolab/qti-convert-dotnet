using System.IO.Compression;
using Citolab.QTI.Converter;
using Xunit;

namespace Citolab.QTI.Uploader.Tests;

/// <summary>
/// Validates converter output against the official QTI 2.1 and 3.0 XSDs (skipped when they can't be downloaded).
/// Inputs that are themselves invalid are left out: the TAO fixtures (identifiers starting with a digit, inline
/// script/style) and qti2-sample items that use QTI 2.2-only or non-standard markup.
/// </summary>
public sealed class QtiSchemaValidationTests
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "TestData", "upgrader-fixtures");

    public static IEnumerable<object[]> Qti2Fixtures() =>
        new[] { "ts-kitchen-sink-item", "ts-kitchen-sink-test" }
            .Concat(Directory.GetFiles(FixtureDir, "ts-qti21-*.qti2.xml").Select(p => Path.GetFileName(p).Replace(".qti2.xml", string.Empty)))
            .Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(Qti2Fixtures))]
    public void UpgradedQti3_IsValid(string name)
    {
        var schema = QtiSchemaValidator.Get("qti3", QtiSchemaValidator.Qti3XsdUrl);
        if (schema is null) return;

        var qti3 = Qti2ToQti3XmlConverter.Convert(XmlStringUtilities.CleanXmlString(File.ReadAllText(Path.Combine(FixtureDir, $"{name}.qti2.xml"))));

        Assert.Empty(QtiSchemaValidator.Validate(qti3, schema));
    }

    /// <summary>Sample items using constructs QTI 2.1 cannot express, reported in the conversion warnings or not at all.</summary>
    private static readonly Dictionary<string, string> NotExpressibleInQti21 = new()
    {
        ["items/data-attributes.xml"] = "gap inside a table cell",
        ["items/graphic_gap_match_text.xml"] = "gapText in graphicGapMatchInteraction (QTI 2.2)",
        ["items/inline_choice_math.xml"] = "MathML inside inlineChoice",
        ["items/media_coords.xml"] = "coords on object and width/height=\"undefined\" in the sample data"
    };

    public static IEnumerable<object[]> Qti3SampleItems()
    {
        using var zip = ZipFile.OpenRead(TestDataPaths.Qti3ZipPath);
        return zip.Entries
            .Where(e => e.FullName.StartsWith("items/", StringComparison.Ordinal) && e.FullName.EndsWith(".xml", StringComparison.Ordinal))
            .Where(e => e.FullName != "items/imsmanifest.xml" && !NotExpressibleInQti21.ContainsKey(e.FullName))
            .Select(e => new object[] { e.FullName })
            .ToList();
    }

    [Theory]
    [MemberData(nameof(Qti3SampleItems))]
    public void DowngradedQti21_IsValid(string path)
    {
        var schema = QtiSchemaValidator.Get("qti21", QtiSchemaValidator.Qti21XsdUrl);
        if (schema is null) return;

        string qti3;
        using (var zip = ZipFile.OpenRead(TestDataPaths.Qti3ZipPath))
        {
            qti3 = new StreamReader(zip.GetEntry(path)!.Open()).ReadToEnd();
        }
        var qti21 = Qti3ToQti21XmlConverter.Convert(XmlStringUtilities.CleanXmlString(qti3)).Xml;

        Assert.Empty(QtiSchemaValidator.Validate(qti21, schema));
    }
}
