using Citolab.QTI.Uploader;

namespace Citolab.QTI.Uploader.Tests;

internal sealed class FakeStore : IQtiPackageStore
{
    private readonly List<Stored> _stored = new();

    public IReadOnlyList<Stored> StoredFiles => _stored;

    public async Task StoreAsync(QtiStoredFile file, CancellationToken cancellationToken)
    {
        if (file is null) throw new ArgumentNullException(nameof(file));

        using var ms = new MemoryStream();
        await file.Content.CopyToAsync(ms, 1024 * 64, cancellationToken).ConfigureAwait(false);

        _stored.Add(new Stored(
            file.Kind,
            file.RelativePath,
            file.XmlKind,
            ms.ToArray()));
    }

    internal sealed record Stored(
        QtiStoredFileKind Kind,
        string RelativePath,
        QtiXmlKind XmlKind,
        byte[] Content);
}

