namespace Citolab.QTI.Converter;

public sealed class Qti2ToQti3PackageConverterOptions
{
    public bool ConvertManifest { get; set; } = true;
    public bool ApplyItemTransforms { get; set; } = true;
    public bool SyncAssessmentItemIdentifiers { get; set; } = true;

    public QtiItemTransformOptions ItemTransformOptions { get; set; } = new QtiItemTransformOptions();

    /// <summary>
    /// Optional hook that runs per assessment item after built-in transforms.
    /// Receives the transform, item path, file resolver for ZIP contents, and cancellation token.
    /// </summary>
    public Func<QtiTransform, string, Func<string, Task<string?>>?, CancellationToken, Task>? OnItemTransformedAsync { get; set; }
}
