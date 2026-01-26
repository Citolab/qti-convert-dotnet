namespace Citolab.QTI.Uploader;

public interface IQtiPackageConverter
{
    Task<string> ConvertQti2PackageToQti3Async(string inputZipPath, CancellationToken cancellationToken);
}

