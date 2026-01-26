using System.Security.Cryptography;

namespace Citolab.QTI.Uploader;

public static class QtiPackageHasher
{
    public static async Task<string> ComputeSha256Async(QtiPackageUploadInput input, CancellationToken cancellationToken = default)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        if (input.OpenReadStream is null) throw new ArgumentException("OpenReadStream must be provided.", nameof(input));

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = input.OpenReadStream();

        var buffer = new byte[1024 * 64];
        int read;
        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
        {
            hasher.AppendData(buffer, 0, read);
        }

        var hash = hasher.GetHashAndReset();
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
