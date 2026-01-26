namespace Citolab.QTI.Converter;

public sealed class QtiItemTransformOptions
{
    public bool ObjectToImg { get; set; } = true;
    public bool ObjectToVideo { get; set; } = true;
    public bool ObjectToAudio { get; set; } = true;

    public bool SsmlSubToSpan { get; set; } = true;
    public bool StripMaterialInfo { get; set; } = true;
    public bool MinChoicesToOne { get; set; } = true;
    public bool ExternalScored { get; set; } = true;

    public bool QbCleanup { get; set; } = false;
    public bool DepConvert { get; set; } = false;
    public bool DepConvertExtended { get; set; } = false;
    public bool HideInputsForChoiceInteractionWithImages { get; set; } = false;
    public bool UpgradePci { get; set; } = false;

    public bool StripStylesheets { get; set; } = false;
    public string? StripStylesheetsRemovePattern { get; set; }
    public string? StripStylesheetsKeepPattern { get; set; }
}

