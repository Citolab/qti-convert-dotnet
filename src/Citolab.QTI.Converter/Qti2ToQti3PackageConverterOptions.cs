namespace Citolab.QTI.Converter;

public sealed class Qti2ToQti3PackageConverterOptions
{
    public bool ConvertManifest { get; set; } = true;
    public bool ApplyItemTransforms { get; set; } = true;
    public bool SyncAssessmentItemIdentifiers { get; set; } = true;
}

