using System.Text.RegularExpressions;

namespace Citolab.QTI.Converter;

/// <summary>POSIX path helpers for package-relative paths (zip entry names).</summary>
internal static class QtiPackagePath
{
    private static readonly Regex AbsoluteUrl = new(@"^([a-z][a-z0-9+.-]*:|/|#)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = "image/png",
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["gif"] = "image/gif",
        ["svg"] = "image/svg+xml",
        ["webp"] = "image/webp",
        ["mp3"] = "audio/mpeg",
        ["wav"] = "audio/wav",
        ["ogg"] = "audio/ogg",
        ["m4a"] = "audio/mp4",
        ["mp4"] = "video/mp4",
        ["webm"] = "video/webm",
        ["ogv"] = "video/ogg"
    };

    public static bool IsRelativeUrl(string url) => !AbsoluteUrl.IsMatch(url.Trim());

    public static string Normalize(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Replace('\\', '/').Split('/'))
        {
            if (part.Length == 0 || part == ".") continue;
            if (part == ".." && parts.Count > 0 && parts[parts.Count - 1] != "..")
            {
                parts.RemoveAt(parts.Count - 1);
            }
            else
            {
                parts.Add(part);
            }
        }
        return string.Join("/", parts);
    }

    public static string DirectoryName(string path)
    {
        var index = path.Replace('\\', '/').LastIndexOf('/');
        return index == -1 ? string.Empty : path.Substring(0, index);
    }

    public static string Join(string baseDirectory, string relative) =>
        baseDirectory.Length == 0 ? Normalize(relative) : Normalize($"{baseDirectory}/{relative}");

    /// <summary>Path of <paramref name="target"/> relative to the directory <paramref name="fromDirectory"/>.</summary>
    public static string Relative(string fromDirectory, string target)
    {
        var from = Normalize(fromDirectory).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var to = Normalize(target).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var common = 0;
        while (common < from.Length && common < to.Length - 1 && from[common] == to[common]) common++;
        return string.Join("/", Enumerable.Repeat("..", from.Length - common).Concat(to.Skip(common)));
    }

    public static string MimeTypeFromPath(string path)
    {
        var clean = path.Split('?', '#')[0];
        var extension = clean.IndexOf('.') >= 0 ? clean.Substring(clean.LastIndexOf('.') + 1) : string.Empty;
        return MimeTypes.TryGetValue(extension, out var type) ? type : "application/octet-stream";
    }
}
