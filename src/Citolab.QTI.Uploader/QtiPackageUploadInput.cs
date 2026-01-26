namespace Citolab.QTI.Uploader;

public sealed record QtiPackageUploadInput(
    string FileName,
    long Length,
    Func<Stream> OpenReadStream,
    string? ContentType = null);

