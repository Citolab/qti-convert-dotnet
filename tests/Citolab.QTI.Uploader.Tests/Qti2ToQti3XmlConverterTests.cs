using System.Xml.Linq;
using Citolab.QTI.Converter;
using Xunit;

namespace Citolab.QTI.Uploader.Tests;

public sealed class Qti2ToQti3XmlConverterTests
{
    [Fact]
    public void Convert_AssessmentItem_ConvertsElementAndAttributeNamesToKebabCase()
    {
        const string qti2 = """
                            <?xml version="1.0" encoding="utf-8"?>
                            <assessmentItem xmlns="http://www.imsglobal.org/xsd/imsqti_v2p2"
                                            identifier="hotspot"
                                            title="UK Airports"
                                            adaptive="false"
                                            timeDependent="false">
                              <responseDeclaration identifier="RESPONSE" cardinality="single" baseType="identifier" />
                              <itemBody>
                                <hotspotInteraction responseIdentifier="RESPONSE" maxChoices="1" />
                              </itemBody>
                            </assessmentItem>
                            """;

        var qti3 = Qti2ToQti3XmlConverter.Convert(qti2);
        var doc = XDocument.Parse(qti3, LoadOptions.PreserveWhitespace);

        Assert.NotNull(doc.Root);
        Assert.Equal("qti-assessment-item", doc.Root!.Name.LocalName);

        Assert.NotNull(doc.Root.Attribute("identifier"));
        Assert.NotNull(doc.Root.Attribute("time-dependent"));
        Assert.Null(doc.Root.Attribute("timeDependent"));

        var responseDeclaration = doc.Descendants().Single(e => e.Name.LocalName == "qti-response-declaration");
        Assert.NotNull(responseDeclaration.Attribute("base-type"));
        Assert.Null(responseDeclaration.Attribute("baseType"));

        var hotspotInteraction = doc.Descendants().Single(e => e.Name.LocalName == "qti-hotspot-interaction");
        Assert.NotNull(hotspotInteraction.Attribute("response-identifier"));
        Assert.NotNull(hotspotInteraction.Attribute("max-choices"));
        Assert.Null(hotspotInteraction.Attribute("responseIdentifier"));
        Assert.Null(hotspotInteraction.Attribute("maxChoices"));
    }
}

