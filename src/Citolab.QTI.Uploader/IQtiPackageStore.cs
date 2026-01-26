namespace Citolab.QTI.Uploader;

public interface IQtiPackageStore
{
    Task StoreAsync(QtiStoredFile file, CancellationToken cancellationToken);
}

