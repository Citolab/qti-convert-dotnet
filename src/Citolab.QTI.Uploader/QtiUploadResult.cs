namespace Citolab.QTI.Uploader;

public sealed record QtiUploadResult(
    QtiPackageVersion DetectedVersion,
    bool ConvertedToQti3,
    int StoredFileCount,
    string? ConvertedPackageFileName);

