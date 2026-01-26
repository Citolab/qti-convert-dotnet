using System.Xml.Linq;
using Citolab.QTI.Converter;
using Xunit;

namespace Citolab.QTI.Uploader.Tests;

public sealed class QtiTransformTests
{
    [Fact]
    public void ObjectToImg_TransformsObjectImageToImg()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <object type="image/png" width="206" height="280" data="images/ukair.png">UK Map</object>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <img width="206" height="280" src="images/ukair.png" alt="UK Map" />
                                """;

        var result = QtiTransform.Create(input).ObjectToImg().Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void ObjectToVideo_TransformsObjectVideoToVideo()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <qti-media-interaction response-identifier="VIDEORESPONSE" autostart="false" max-plays="0" id="I6f9b9b94-5a79-477a-a462-19db78f7ebf1">
                               <object type="video/webm" data="../video/BB_14a_T_WebM_Facet384x288.webm" height="288" width="384" data-dep-description="" data-dep-controls="true" data-dep-controlslist="start pause stop scroll" />
                             </qti-media-interaction>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <qti-media-interaction response-identifier="VIDEORESPONSE" autostart="false" max-plays="0" id="I6f9b9b94-5a79-477a-a462-19db78f7ebf1">
                                  <video width="384" height="288" controls="true">
                                    <source src="../video/BB_14a_T_WebM_Facet384x288.webm" type="video/webm" />
                                  </video>
                                </qti-media-interaction>
                                """;

        var result = QtiTransform.Create(input).ObjectToVideo().Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void ObjectToAudio_TransformsObjectAudioToAudio()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <qti-media-interaction response-identifier="AUDIORESPONSE" autostart="false" max-plays="0" id="I6f9b9b94-5a79-477a-a462-19db78f7ebf1">
                               <object type="audio/mpeg" data="../audio/audio.mp3" height="10" width="11" data-dep-description="" data-dep-controls="true" />
                             </qti-media-interaction>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <qti-media-interaction response-identifier="AUDIORESPONSE" autostart="false" max-plays="0" id="I6f9b9b94-5a79-477a-a462-19db78f7ebf1">
                                  <audio width="11" height="10" controls="true">
                                    <source src="../audio/audio.mp3" type="audio/mpeg" />
                                  </audio>
                                </qti-media-interaction>
                                """;

        var result = QtiTransform.Create(input).ObjectToAudio().Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void ToMathMLWebcomponents_TransformsMathMlTags()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <math>
                               <mrow>
                                 <msup>
                                   <mi>x</mi><mn>2</mn>
                                 </msup>
                                 <msup>
                                   <mi>y</mi><mn>2</mn>
                                 </msup>
                               </mrow>
                             </math>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <math-ml>
                                  <math-row>
                                    <math-sup>
                                      <math-i>x</math-i><math-n>2</math-n>
                                    </math-sup>
                                    <math-sup>
                                      <math-i>y</math-i><math-n>2</math-n>
                                    </math-sup>
                                  </math-row>
                                </math-ml>
                                """;

        var result = QtiTransform.Create(input).ToMathMLWebcomponents().Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void CustomTypes_TypePrefix_RenamesElement()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <qti-simple-choice class="type:stats" identifier="CHOICE_1"></qti-simple-choice>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <qti-simple-choice-stats class="type:stats" identifier="CHOICE_1"></qti-simple-choice-stats>
                                """;

        var result = QtiTransform.Create(input).CustomTypes().Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void CustomTypes_CustomPrefix_RenamesElement()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <qti-simple-choice class="extend:stats" identifier="CHOICE_1"></qti-simple-choice>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <qti-simple-choice-stats class="extend:stats" identifier="CHOICE_1"></qti-simple-choice-stats>
                                """;

        var result = QtiTransform.Create(input).CustomTypes("extend").Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void CustomInteraction_MovesObjectAttributesToInteraction()
    {
        const string input = """
                             <qti-custom-interaction
                                   response-identifier="RESPONSE"
                                   id="Ie855768e-179b-4226-a30e-6ead190c14b7"
                                   data-dep-min-values="0">
                               <object
                                     type="application/javascript"
                                     height="370"
                                     width="467"
                                     data="../ref/6047-BiKB-bmi_467x370_29/json/manifest.json">
                                 <param name="responseLength" value="1" valuetype="DATA" />
                               </object>
                             </qti-custom-interaction>
                             """;

        const string expected = """
                                <qti-custom-interaction response-identifier="RESPONSE" id="Ie855768e-179b-4226-a30e-6ead190c14b7" data-dep-min-values="0" data-base-ref="/" data-base-item="/items/" data="../ref/6047-BiKB-bmi_467x370_29/json/manifest.json" width="467" height="370">
                                </qti-custom-interaction>
                                """;

        var result = QtiTransform.Create(input).CustomInteraction("/", "items/").Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void ChangeAssetLocation_UpdatesSrc()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <img type="image/png" width="206" height="280" src="../../images/ukair.png" alt="UK Map" />
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <img type="image/png" width="206" height="280" src="https://example.com/images/ukair.png" alt="UK Map" />
                                """;

        var baseUrl = new Uri("https://example.com/");
        var result = QtiTransform.Create(input)
            .ChangeAssetLocation(url => new Uri(baseUrl, url).ToString())
            .Xml();

        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public async Task ChangeAssetLocationAsync_UpdatesSrc()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <img type="image/png" width="206" height="280" src="../../images/ukair.png" alt="UK Map" />
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <img type="image/png" width="206" height="280" src="https://example.com/images/ukair.png" alt="UK Map" />
                                """;

        var baseUrl = new Uri("https://example.com/");
        var transform = QtiTransform.Create(input);
        await transform.ChangeAssetLocationAsync(async url =>
        {
            await Task.Delay(10);
            return new Uri(baseUrl, url).ToString();
        });
        var result = transform.Xml();

        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void StripMaterialInfo_RemovesCompanionMaterials()
    {
        const string input = """
                             <qti-companion-materials-info xmlns:dep="http://www.duo.nl/schema/dep_extension">
                               <dep:dep-calculator>
                                 <dep:dep-description/>
                               </dep:dep-calculator>
                             </qti-companion-materials-info>
                             <qti-item-body></qti-item-body>
                             """;

        const string expected = """
                                <qti-item-body></qti-item-body>
                                """;

        var result = QtiTransform.Create(input).StripMaterialInfo().Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void MinChoicesToOne_SetsToOneWhenMissingOrZero()
    {
        const string input = """
                             <qti-item-body>
                               <qti-choice-interaction min-choices="0" />
                               <qti-choice-interaction />
                             </qti-item-body>
                             """;

        var result = QtiTransform.Create(input).MinChoicesToOne().Xml();
        var doc = XDocument.Parse(result, LoadOptions.PreserveWhitespace);

        var interactions = doc.Descendants().Where(e => e.Name.LocalName == "qti-choice-interaction").ToList();
        Assert.Equal(2, interactions.Count);
        Assert.All(interactions, i => Assert.Equal("1", (string?)i.Attribute("min-choices")));
    }

    [Fact]
    public void ExternalScored_AddsExternalScoredWhenNoResponseProcessing()
    {
        const string input = """
                             <qti-assessment-item>
                               <qti-outcome-declaration identifier="SCORE" />
                             </qti-assessment-item>
                             """;

        var result = QtiTransform.Create(input).ExternalScored().Xml();
        var doc = XDocument.Parse(result, LoadOptions.PreserveWhitespace);

        var score = doc.Descendants().Single(e => e.Name.LocalName == "qti-outcome-declaration");
        Assert.Equal("human", (string?)score.Attribute("external-scored"));
    }

    [Fact]
    public void DepConvert_WrapsDialogTriggerInButtonAndSetsPopover()
    {
        const string id = "WIN_d579fd6a-c46d-409a-a9f6-df7bbabcb8e3";
        const string input = """
                             <qti-assessment-item xmlns="http://www.imsglobal.org/xsd/imsqtiasi_v3p0"
                               xmlns:dep="http://www.duo.nl/schema/dep_extension">
                               <qti-item-body>
                                 <div class="dep-dialogTrigger" data-stimulus-idref="WIN_d579fd6a-c46d-409a-a9f6-df7bbabcb8e3">
                                   <img src="../img/AKbb201cbt-12ak.gif" />
                                 </div>
                                 <div id="WIN_d579fd6a-c46d-409a-a9f6-df7bbabcb8e3" class="dep-dialog hide-dialog"></div>
                               </qti-item-body>
                             </qti-assessment-item>
                             """;

        var result = QtiTransform.Create(input).DepConvert().Xml();
        var doc = XDocument.Parse(result, LoadOptions.PreserveWhitespace);

        XNamespace qti = "http://www.imsglobal.org/xsd/imsqtiasi_v3p0";

        var button = doc.Descendants(qti + "button").Single();
        Assert.Equal(id, (string?)button.Attribute("popovertarget"));
        Assert.NotNull(button.Descendants().Single(e => e.Name.LocalName == "div" && (string?)e.Attribute("class") == "dep-dialogTrigger"));

        var dialog = doc.Descendants().Single(e => (string?)e.Attribute("id") == id);
        Assert.NotNull(dialog.Attribute("popover"));
    }

    [Fact]
    public void StripStylesheets_NoOptions_RemovesAll()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <qti-assessment-item>
                               <qti-stylesheet href="css/assessment.css" type="text/css" />
                               <qti-item-body></qti-item-body>
                             </qti-assessment-item>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <qti-assessment-item>
                                  <qti-item-body></qti-item-body>
                                </qti-assessment-item>
                                """;

        var result = QtiTransform.Create(input).StripStylesheets().Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void StripStylesheets_RemovePattern_ExactMatch()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <qti-assessment-item>
                               <qti-stylesheet href="css/main.css" type="text/css" />
                               <qti-stylesheet href="css/theme.css" type="text/css" />
                               <qti-item-body></qti-item-body>
                             </qti-assessment-item>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <qti-assessment-item>
                                  <qti-stylesheet href="css/theme.css" type="text/css" />
                                  <qti-item-body></qti-item-body>
                                </qti-assessment-item>
                                """;

        var result = QtiTransform.Create(input).StripStylesheets(removePattern: "css/main.css").Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void StripStylesheets_RemovePattern_StartsWithWildcard()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <qti-assessment-item>
                               <qti-stylesheet href="css/theme-light.css" type="text/css" />
                               <qti-stylesheet href="css/theme-dark.css" type="text/css" />
                               <qti-stylesheet href="css/main.css" type="text/css" />
                               <qti-item-body></qti-item-body>
                             </qti-assessment-item>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <qti-assessment-item>
                                  <qti-stylesheet href="css/main.css" type="text/css" />
                                  <qti-item-body></qti-item-body>
                                </qti-assessment-item>
                                """;

        var result = QtiTransform.Create(input).StripStylesheets(removePattern: "css/theme*").Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void StripStylesheets_RemovePattern_EndsWithWildcard()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <qti-assessment-item>
                               <qti-stylesheet href="styles/old-theme.css" type="text/css" />
                               <qti-stylesheet href="styles/old-layout.css" type="text/css" />
                               <qti-stylesheet href="styles/new-theme.css" type="text/css" />
                               <qti-item-body></qti-item-body>
                             </qti-assessment-item>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <qti-assessment-item>
                                  <qti-stylesheet href="styles/new-theme.css" type="text/css" />
                                  <qti-item-body></qti-item-body>
                                </qti-assessment-item>
                                """;

        var result = QtiTransform.Create(input).StripStylesheets(removePattern: "*old*").Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void StripStylesheets_RemovePattern_ContainsWildcard()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <qti-assessment-item>
                               <qti-stylesheet href="css/deprecated-styles.css" type="text/css" />
                               <qti-stylesheet href="css/main.css" type="text/css" />
                               <qti-stylesheet href="js/deprecated-script.js" type="text/css" />
                               <qti-item-body></qti-item-body>
                             </qti-assessment-item>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <qti-assessment-item>
                                  <qti-stylesheet href="css/main.css" type="text/css" />
                                  <qti-item-body></qti-item-body>
                                </qti-assessment-item>
                                """;

        var result = QtiTransform.Create(input).StripStylesheets(removePattern: "*deprecated*").Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void StripStylesheets_KeepPattern_ExactMatch()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <qti-assessment-item>
                               <qti-stylesheet href="css/main.css" type="text/css" />
                               <qti-stylesheet href="css/theme.css" type="text/css" />
                               <qti-stylesheet href="css/layout.css" type="text/css" />
                               <qti-item-body></qti-item-body>
                             </qti-assessment-item>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <qti-assessment-item>
                                  <qti-stylesheet href="css/main.css" type="text/css" />
                                  <qti-item-body></qti-item-body>
                                </qti-assessment-item>
                                """;

        var result = QtiTransform.Create(input).StripStylesheets(keepPattern: "css/main.css").Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void StripStylesheets_KeepPattern_StartsWithWildcard()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <qti-assessment-item>
                               <qti-stylesheet href="css/core-main.css" type="text/css" />
                               <qti-stylesheet href="css/core-theme.css" type="text/css" />
                               <qti-stylesheet href="css/plugin.css" type="text/css" />
                               <qti-item-body></qti-item-body>
                             </qti-assessment-item>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <qti-assessment-item>
                                  <qti-stylesheet href="css/core-main.css" type="text/css" />
                                  <qti-stylesheet href="css/core-theme.css" type="text/css" />
                                  <qti-item-body></qti-item-body>
                                </qti-assessment-item>
                                """;

        var result = QtiTransform.Create(input).StripStylesheets(keepPattern: "css/core*").Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void StripStylesheets_KeepPattern_EndsWithWildcard()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <qti-assessment-item>
                               <qti-stylesheet href="styles/main.css" type="text/css" />
                               <qti-stylesheet href="styles/theme.css" type="text/css" />
                               <qti-stylesheet href="styles/layout.js" type="text/css" />
                               <qti-item-body></qti-item-body>
                             </qti-assessment-item>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <qti-assessment-item>
                                  <qti-stylesheet href="styles/main.css" type="text/css" />
                                  <qti-stylesheet href="styles/theme.css" type="text/css" />
                                  <qti-item-body></qti-item-body>
                                </qti-assessment-item>
                                """;

        var result = QtiTransform.Create(input).StripStylesheets(keepPattern: "*.css").Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void StripStylesheets_KeepPattern_ContainsWildcard()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <qti-assessment-item>
                               <qti-stylesheet href="css/essential-theme.css" type="text/css" />
                               <qti-stylesheet href="css/optional-plugin.css" type="text/css" />
                               <qti-stylesheet href="css/essential-layout.css" type="text/css" />
                               <qti-item-body></qti-item-body>
                             </qti-assessment-item>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <qti-assessment-item>
                                  <qti-stylesheet href="css/essential-theme.css" type="text/css" />
                                  <qti-stylesheet href="css/essential-layout.css" type="text/css" />
                                  <qti-item-body></qti-item-body>
                                </qti-assessment-item>
                                """;

        var result = QtiTransform.Create(input).StripStylesheets(keepPattern: "*essential*").Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void StripStylesheets_HandlesMissingHrefAttribute()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <qti-assessment-item>
                               <qti-stylesheet type="text/css" />
                               <qti-stylesheet href="css/main.css" type="text/css" />
                               <qti-item-body></qti-item-body>
                             </qti-assessment-item>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <qti-assessment-item>
                                  <qti-stylesheet type="text/css" />
                                  <qti-item-body></qti-item-body>
                                </qti-assessment-item>
                                """;

        var result = QtiTransform.Create(input).StripStylesheets(removePattern: "css/main.css").Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void StripStylesheets_NoStylesheetsRemovedWhenPatternDoesNotMatch()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <qti-assessment-item>
                               <qti-stylesheet href="css/main.css" type="text/css" />
                               <qti-stylesheet href="css/theme.css" type="text/css" />
                               <qti-item-body></qti-item-body>
                             </qti-assessment-item>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <qti-assessment-item>
                                  <qti-stylesheet href="css/main.css" type="text/css" />
                                  <qti-stylesheet href="css/theme.css" type="text/css" />
                                  <qti-item-body></qti-item-body>
                                </qti-assessment-item>
                                """;

        var result = QtiTransform.Create(input).StripStylesheets(removePattern: "nonexistent.css").Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void StripStylesheets_EmptyOptionsBehavesLikeNoOptions()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <qti-assessment-item>
                               <qti-stylesheet href="css/main.css" type="text/css" />
                               <qti-stylesheet href="css/theme.css" type="text/css" />
                               <qti-item-body></qti-item-body>
                             </qti-assessment-item>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <qti-assessment-item>
                                  <qti-item-body></qti-item-body>
                                </qti-assessment-item>
                                """;

        var result = QtiTransform.Create(input).StripStylesheets(removePattern: null, keepPattern: null).Xml();
        XmlAssert.Equal(result, expected);
    }
}

