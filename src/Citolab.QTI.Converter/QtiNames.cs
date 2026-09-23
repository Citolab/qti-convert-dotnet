using System.Text;
using System.Xml.Linq;

namespace Citolab.QTI.Converter;

/// <summary>
/// The single element name table shared by the QTI 2 to 3 upgrader and the QTI 3 to 2.1 downgrader.
/// QTI 3 names are the QTI 2 names "kabobized" with a qti- prefix (choiceInteraction -> qti-choice-interaction),
/// except for a few irregular ones.
/// </summary>
internal static class QtiNames
{
    public static readonly XNamespace Qti3 = "http://www.imsglobal.org/xsd/imsqtiasi_v3p0";
    public static readonly XNamespace Qti21 = "http://www.imsglobal.org/xsd/imsqti_v2p1";
    public static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    public static readonly XNamespace MathMl = "http://www.w3.org/1998/Math/MathML";
    public static readonly XNamespace MathMl2010 = "http://www.w3.org/2010/Math/MathML";
    public static readonly XNamespace Ssml = "http://www.w3.org/2001/10/synthesis";
    public static readonly XNamespace Ssml2010 = "http://www.w3.org/2010/10/synthesis";
    public static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    /// <summary>QTI 2.x elements (item, test and stimulus) that have a qti-* counterpart in QTI 3.</summary>
    public static readonly IReadOnlyCollection<string> Qti2ElementNames = new HashSet<string>((
        "and anyN areaMapEntry areaMapping assessmentStimulusRef associableHotspot associateInteraction baseValue " +
        "calculator calculatorInfo calculatorType card cardEntry catalog catalogInfo choiceInteraction " +
        "companionMaterialsInfo containerSize contains contentBody contextDeclaration contextVariable correct " +
        "correctResponse customInteraction customOperator default defaultValue delete description digitalMaterial " +
        "divide drawingInteraction endAttemptInteraction equal equalRounded exitResponse " +
        "exitTemplate extendedTextInteraction feedbackInline fieldValue fileHref gap gapImg gapMatchInteraction " +
        "gapText gcd graphicAssociateInteraction graphicGapMatchInteraction graphicOrderInteraction gt gte " +
        "hotspotChoice hotspotInteraction hottext hottextInteraction htmlContent index " +
        "inlineChoice inlineChoiceInteraction inside integerDivide integerModulus integerToFloat interactionMarkup " +
        "interactionModule interactionModules interpolationTable interpolationTableEntry isNull itemBody label lcm " +
        "lookupOutcomeValue lt lte majorIncrement mapEntry mapResponse mapResponsePoint mapping match " +
        "matchInteraction matchTable matchTableEntry mathConstant mathOperator max mediaInteraction member min " +
        "minimumLength minorIncrement multiple not null numberCorrect numberIncorrect numberPresented " +
        "numberResponded numberSelected or orderInteraction ordered outcomeDeclaration outcomeMaximum " +
        "outcomeMinimum patternMatch physicalMaterial portableCustomInteraction positionObjectInteraction " +
        "positionObjectStage power printedVariable product prompt protractor random randomFloat randomInteger " +
        "repeat resourceIcon responseCondition responseDeclaration responseElse responseElseIf responseIf " +
        "responseProcessing responseProcessingFragment round roundTo rule " +
        "selectPointInteraction setCorrectResponse setDefaultValue setOutcomeValue setTemplateValue " +
        "simpleAssociableChoice simpleChoice simpleMatchSet sliderInteraction statsOperator stringMatch stylesheet " +
        "substring subtract sum templateBlock templateCondition templateConstraint templateDeclaration templateElse " +
        "templateElseIf templateIf templateInline templateProcessing templateVariable textEntryInteraction truncate " +
        "uploadInteraction value variable " +
        // assessment test elements
        "assessmentTest testPart assessmentSection assessmentSectionRef assessmentItemRef weight outcomeProcessing " +
        "outcomeCondition outcomeIf outcomeElse testVariables timeLimits itemSessionControl selection ordering " +
        "adaptiveSelection adaptiveEngineRef adaptiveSettingsRef metadataRef branchRule preCondition " +
        // roots and feedback containers
        "assessmentItem assessmentStimulus feedbackBlock modalFeedback rubricBlock " +
        // not in the qti2xTo30.xsl lists
        "stimulusBody outcomeElseIf exitTest testFeedback templateDefault variableMapping infoControl " +
        // irregular, see below
        "durationLT durationGTE incrementSI incrementUS ruleSystemSI ruleSystemUS"
    ).Split(' '), StringComparer.Ordinal);

    /// <summary>QTI 2 names whose QTI 3 name is not the plain kabobized form.</summary>
    private static readonly Dictionary<string, string> IrregularQti2ToQti3 = new(StringComparer.Ordinal)
    {
        ["durationLT"] = "qti-duration-lt",
        ["durationGTE"] = "qti-duration-gte",
        ["incrementSI"] = "qti-increment-si",
        ["incrementUS"] = "qti-increment-us",
        ["ruleSystemSI"] = "qti-rule-system-si",
        ["ruleSystemUS"] = "qti-rule-system-us"
    };

    private static readonly Dictionary<string, string> Qti3ToQti2 =
        Qti2ElementNames.ToDictionary(QtiKabobify, name => name, StringComparer.Ordinal);

    /// <summary>baseType -> base-type (as in qti2xTo30.xsl: every capital becomes -lowercase).</summary>
    public static string Kabobize(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        foreach (var ch in name)
        {
            if (ch is >= 'A' and <= 'Z')
            {
                sb.Append('-').Append(char.ToLowerInvariant(ch));
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    /// <summary>base-type -> baseType</summary>
    public static string Camelize(string name)
    {
        var sb = new StringBuilder(name.Length);
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] == '-' && i + 1 < name.Length && (char.IsLower(name[i + 1]) || char.IsDigit(name[i + 1])))
            {
                sb.Append(char.ToUpperInvariant(name[++i]));
            }
            else
            {
                sb.Append(name[i]);
            }
        }
        return sb.ToString();
    }

    /// <summary>Converts a name the QTI 3 way; use for names known to be QTI elements.</summary>
    public static string QtiKabobify(string qti2Name) =>
        IrregularQti2ToQti3.TryGetValue(qti2Name, out var irregular) ? irregular : $"qti-{Kabobize(qti2Name)}";

    /// <summary>choiceInteraction -> qti-choice-interaction; null for names that are not QTI elements (e.g. XHTML).</summary>
    public static string? Qti2ElementNameToQti3(string qti2Name) =>
        Qti2ElementNames.Contains(qti2Name) ? QtiKabobify(qti2Name) : null;

    /// <summary>
    /// qti-choice-interaction -> choiceInteraction; null for names without the qti- prefix.
    /// QTI 3 elements that are not in the table (QTI 3-only) fall back to a plain camelCase of the name.
    /// </summary>
    public static string? Qti3ElementNameToQti2(string qti3Name) =>
        !qti3Name.StartsWith("qti-", StringComparison.Ordinal)
            ? null
            : Qti3ToQti2.TryGetValue(qti3Name, out var name) ? name : Camelize(qti3Name.Substring("qti-".Length));
}
