using Citolab.QTI.Converter;
using Xunit;

namespace Citolab.QTI.Uploader.Tests;

public sealed class QtiTransformMoreTests
{
    [Fact]
    public void Suffix_AppendsSuffixToMatchingElements()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <qti-item-body>
                               <qti-choice-interaction response-identifier="RESPONSE" shuffle="false" max-choices="1" min-choices="0">
                                 <qti-simple-choice identifier="CHOICE_1"></qti-simple-choice>
                                 <qti-simple-choice identifier="CHOICE_2"></qti-simple-choice>
                               </qti-choice-interaction>
                             </qti-item-body>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <qti-item-body>
                                  <qti-choice-interaction response-identifier="RESPONSE" shuffle="false" max-choices="1" min-choices="0">
                                    <qti-simple-choice-post identifier="CHOICE_1"></qti-simple-choice-post>
                                    <qti-simple-choice-post identifier="CHOICE_2"></qti-simple-choice-post>
                                  </qti-choice-interaction>
                                </qti-item-body>
                                """;

        var result = QtiTransform.Create(input).Suffix(new[] { "qti-simple-choice" }, "post").Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void SsmlSubToSpan_ConvertsSsmlElementsToSpans()
    {
        const string input = """
                             <p xmlns:ssml="http://www.w3.org/2001/10/synthesis">
                               Temperature is <ssml:sub alias="degrees Celsius">&#xb0;C</ssml:sub>.
                             </p>
                             """;

        const string expected = """
                                <p xmlns:ssml="http://www.w3.org/2001/10/synthesis">
                                  Temperature is <span data-ssml-sub-alias="degrees Celsius">&#xb0;C</span>.
                                </p>
                                """;

        var result = QtiTransform.Create(input).SsmlSubToSpan().Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public async Task ConfigurePciAsync_MergesModuleResolution()
    {
        const string input = """
                             <qti-portable-custom-interaction
                                 custom-interaction-type-identifier="GraphAmpIO"
                                 data-height="360"
                                 data-prompt="Use the drawing tool(s) to form the correct answer on the provided graph."
                                 data-show-axes="true"
                                 data-width="360"
                                 data-x="-10,10"
                                 data-x-step="1"
                                 data-y="-10,10"
                                 data-y-step="1"
                                 module="graphInteraction"
                                 response-identifier="RESPONSE"
                                 data-base-url="/assets/qti-portable-custom-interaction/"
                               >
                               <qti-interaction-markup>
                                 <div class="qti-padding-2">
                                   <div class="graphInteraction">
                                     <div class="graph-interaction">
                                       <div class="graph-interaction__prompt"></div>
                                       <div class="graph-interaction__canvas"></div>
                                     </div>
                                   </div>
                                 </div>
                               </qti-interaction-markup>
                             </qti-portable-custom-interaction>
                             """;

        const string expected = """
                                <qti-portable-custom-interaction
                                    custom-interaction-type-identifier="GraphAmpIO"
                                    data-height="360"
                                    data-prompt="Use the drawing tool(s) to form the correct answer on the provided graph."
                                    data-show-axes="true"
                                    data-width="360"
                                    data-x="-10,10"
                                    data-x-step="1"
                                    data-y="-10,10"
                                    data-y-step="1"
                                    module="graphInteraction"
                                    response-identifier="RESPONSE"
                                    data-base-url="/assets/qti-portable-custom-interaction/"
                                  >
                                  <qti-interaction-markup>
                                    <div class="qti-padding-2">
                                      <div class="graphInteraction">
                                        <div class="graph-interaction">
                                          <div class="graph-interaction__prompt"></div>
                                          <div class="graph-interaction__canvas"></div>
                                        </div>
                                      </div>
                                    </div>
                                  </qti-interaction-markup>
                                  <qti-interaction-modules>
                                    <qti-interaction-module id="graphInteraction" primary-path="modules/graphInteraction"></qti-interaction-module>
                                    <qti-interaction-module id="tap" primary-path="tap"></qti-interaction-module>
                                    <qti-interaction-module id="d3" primary-path="modules/d3.v5.min"></qti-interaction-module>
                                  </qti-interaction-modules>
                                </qti-portable-custom-interaction>
                                """;

        Task<QtiTransform.ModuleResolutionConfig?> GetConfig(string url)
        {
            if (url.Contains("fallback", StringComparison.Ordinal))
            {
                return Task.FromResult<QtiTransform.ModuleResolutionConfig?>(null);
            }

            return Task.FromResult<QtiTransform.ModuleResolutionConfig?>(new QtiTransform.ModuleResolutionConfig
            {
                WaitSeconds = 60,
                Paths =
                {
                    ["graphInteraction"] = new[] { "modules/graphInteraction" },
                    ["tap"] = new[] { "tap" },
                    ["d3"] = new[] { "modules/d3.v5.min" }
                }
            });
        }

        var transform = QtiTransform.Create(input);
        await transform.ConfigurePciAsync("/assets/qti-portable-custom-interaction/", GetConfig);
        var result = transform.Xml();

        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public async Task ConfigurePciAsync_MakesCustomInteractionTypeIdentifierUnique()
    {
        const string input = """
                             <div>
                               <qti-portable-custom-interaction custom-interaction-type-identifier="urn:fdc:hmhco.com:pci:shading" module="shading"></qti-portable-custom-interaction>
                               <qti-portable-custom-interaction custom-interaction-type-identifier="urn:fdc:hmhco.com:pci:shading" module="shading"></qti-portable-custom-interaction>
                             </div>
                             """;

        var transform = QtiTransform.Create(input);
        await transform.ConfigurePciAsync("/assets/qti-portable-custom-interaction/", _ => Task.FromResult<QtiTransform.ModuleResolutionConfig?>(null));
        var result = transform.Xml();

        var doc = System.Xml.Linq.XDocument.Parse(result, System.Xml.Linq.LoadOptions.PreserveWhitespace);
        var ids = doc.Descendants().Where(e => e.Name.LocalName == "qti-portable-custom-interaction").Select(e => (string?)e.Attribute("custom-interaction-type-identifier")).ToList();
        Assert.Equal(2, ids.Count);
        Assert.Equal("urn:fdc:hmhco.com:pci:shading", ids[0]);
        Assert.Equal("urn:fdc:hmhco.com:pci:shading1", ids[1]);
    }

    [Fact]
    public void UpgradePci_UpgradesTaoExportedPci()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <qti-custom-interaction response-identifier="RESPONSE"
                               data-base-ref="https://example.com/base"
                               data-base-item="https://example.com/base">
                               <qti-portable-custom-interaction custom-interaction-type-identifier="colorProportions" data-version="1.0.1" data-base-url="https://example.com/base">
                                 <properties>
                                   <property key="colors">red, blue, yellow</property>
                                   <property key="width">400</property>
                                   <property key="height">400</property>
                                   <property key="kebabCase">true</property>
                                 </properties>
                                 <modules>
                                   <module id="colorProportions/interaction/runtime/js/index" primary-path="http://localhost:3333/some/path/index.js" />
                                 </modules>
                                 <markup>
                                   <div class="pciInteraction">
                                     <style>.pciInteraction {}</style>
                                     <div class="prompt" />
                                     <ul class="pci" />
                                   </div>
                                 </markup>
                               </qti-portable-custom-interaction>
                             </qti-custom-interaction>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <qti-portable-custom-interaction custom-interaction-type-identifier="colorProportions" data-version="1.0.1" data-colors="red, blue, yellow" data-width="400" data-height="400" data-kebab-case="true" module="colorProportions" response-identifier="RESPONSE">
                                  <qti-interaction-modules>
                                    <qti-interaction-module id="colorProportions" primary-path="http://localhost:3333/some/path/index.js" />
                                  </qti-interaction-modules>
                                  <qti-interaction-markup>
                                    <div class="pciInteraction">
                                      <div class="prompt" />
                                      <ul class="pci" />
                                    </div>
                                  </qti-interaction-markup>
                                </qti-portable-custom-interaction>
                                """;

        var result = QtiTransform.Create(input).UpgradePci().Xml();
        XmlAssert.Equal(result, expected);
    }

    [Fact]
    public void UpgradePci_UpgradesTaoExportedPci_WithNestedProperties()
    {
        const string input = """
                             <?xml version="1.0" encoding="UTF-8"?>
                             <qti-custom-interaction response-identifier="RESPONSE">
                               <qti-portable-custom-interaction custom-interaction-type-identifier="colorProportions" data-version="1.0.1" data-base-url="https://example.com/base">
                                 <properties>
                                   <properties key="data">
                                     <properties key="0">
                                       <property key="stimulusindex">1</property>
                                       <property key="stimulus">5 + 7 = 12</property>
                                       <property key="response">1
                                       </property>
                                     </properties>
                                     <properties key="1">
                                       <property key="stimulusindex">2</property>
                                       <property key="stimulus">4 + 4 = 9</property>
                                       <property key="response">2
                                       </property>
                                     </properties>
                                     <properties key="2">
                                       <property key="stimulusindex">3</property>
                                       <property key="stimulus">7 + 6 = 13</property>
                                       <property key="response">1</property>
                                     </properties>
                                   </properties>
                                   <property key="uploadedFname">stimuli_IIL_item.csv</property>
                                   <property key="feedback">true</property>
                                   <property key="shufflestimuli"></property>
                                   <property key="respkey"></property>
                                   <property key="tlimit">0</property>
                                   <property key="level">2</property>
                                   <property key="buttonlabel0">True</property>
                                   <property key="buttonlabel1">False</property>
                                   <property key="buttonlabel2"></property>
                                   <property key="buttonlabel3"></property>
                                   <property key="buttonlabel4"></property>
                                   <property key="buttonlabel5"></property>
                                   <property key="buttonlabel6"></property>
                                   <property key="buttonlabel7"></property>
                                 </properties>
                                 <modules>
                                   <module id="colorProportions/interaction/runtime/js/index" primary-path="http://localhost:3333/some/path/index.js" />
                                 </modules>
                                 <markup>
                                   <div class="pciInteraction">
                                     <div class="prompt" />
                                     <ul class="pci" />
                                   </div>
                                 </markup>
                               </qti-portable-custom-interaction>
                             </qti-custom-interaction>
                             """;

        const string expected = """
                                <?xml version="1.0" encoding="UTF-8"?>
                                <qti-portable-custom-interaction custom-interaction-type-identifier="colorProportions" data-version="1.0.1" data-data__0__stimulusindex="1" data-data__0__stimulus="5 + 7 = 12" data-data__0__response="1" data-data__1__stimulusindex="2" data-data__1__stimulus="4 + 4 = 9" data-data__1__response="2" data-data__2__stimulusindex="3" data-data__2__stimulus="7 + 6 = 13" data-data__2__response="1" data-uploaded-fname="stimuli_IIL_item.csv" data-feedback="true" data-shufflestimuli="" data-respkey="" data-tlimit="0" data-level="2" data-buttonlabel0="True" data-buttonlabel1="False" data-buttonlabel2="" data-buttonlabel3="" data-buttonlabel4="" data-buttonlabel5="" data-buttonlabel6="" data-buttonlabel7="" data-0__stimulusindex="1" data-0__stimulus="5 + 7 = 12" data-0__response="1" data-1__stimulusindex="2" data-1__stimulus="4 + 4 = 9" data-1__response="2" data-2__stimulusindex="3" data-2__stimulus="7 + 6 = 13" data-2__response="1" data-stimulusindex="3" data-stimulus="7 + 6 = 13" data-response="1" module="colorProportions" response-identifier="RESPONSE">
                                  <qti-interaction-modules>
                                    <qti-interaction-module id="colorProportions" primary-path="http://localhost:3333/some/path/index.js"/>
                                  </qti-interaction-modules>
                                  <qti-interaction-markup>
                                    <div class="pciInteraction">
                                      <div class="prompt"/>
                                      <ul class="pci"/>
                                    </div>
                                  </qti-interaction-markup>
                                </qti-portable-custom-interaction>
                                """;

        var result = QtiTransform.Create(input).UpgradePci().Xml();
        XmlAssert.Equal(result, expected);
    }
}
