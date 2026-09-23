using Citolab.QTI.Converter;
using Xunit;

namespace Citolab.QTI.Uploader.Tests;

/// <summary>
/// TestData/upgrader-fixtures/&lt;name&gt;.qti2.xml is the input and &lt;name&gt;.qti3.xml the output of the original
/// qti2xTo30.xsl (recorded with Saxon when the XSLT was replaced, plus the documented fixes such as the rubric
/// use attribute). The ts-* fixtures are shared with the TypeScript qti-convert package.
/// </summary>
public sealed class Qti2ToQti3UpgraderFixtureTests
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "TestData", "upgrader-fixtures");

    public static IEnumerable<object[]> Fixtures() =>
        Directory.GetFiles(FixtureDir, "*.qti2.xml")
            .Select(path => new object[] { Path.GetFileName(path).Replace(".qti2.xml", string.Empty) })
            .OrderBy(f => (string)f[0]);

    [Fact]
    public void FixturesArePresent() => Assert.True(Fixtures().Count() > 50);

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Convert_MatchesTheXsltOutput(string name)
    {
        var input = File.ReadAllText(Path.Combine(FixtureDir, $"{name}.qti2.xml"));
        var expected = File.ReadAllText(Path.Combine(FixtureDir, $"{name}.qti3.xml"));

        var actual = Qti2ToQti3XmlConverter.Convert(XmlStringUtilities.CleanXmlString(input));

        Assert.Equal(XmlCanonical.Of(expected), XmlCanonical.Of(actual));
    }
}
