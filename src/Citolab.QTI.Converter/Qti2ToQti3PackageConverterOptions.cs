namespace Citolab.QTI.Converter;

public sealed class Qti2ToQti3PackageConverterOptions
{
    public bool ConvertManifest { get; set; } = true;
    public bool ApplyItemTransforms { get; set; } = true;
    public bool SyncAssessmentItemIdentifiers { get; set; } = true;

    public QtiItemTransformOptions ItemTransformOptions { get; set; } = new QtiItemTransformOptions();

    public Func<QtiTransform, string, CancellationToken, Task>? OnItemTransformedAsync { get; set; }
}
