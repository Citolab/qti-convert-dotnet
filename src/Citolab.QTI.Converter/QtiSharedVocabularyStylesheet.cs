using System.Reflection;
using System.Text;

namespace Citolab.QTI.Converter;

/// <summary>
/// The 1EdTech QTI 3 shared vocabulary stylesheet (qti3p0.css, unmodified, from
/// https://www.imsglobal.org/sites/default/files/spec/qti/v3/guide/img/qti3p0.css). The QTI 3 to QTI 2.1 conversion adds
/// it to the output so QTI 2.1 players can style the qti-* shared vocabulary classes.
/// </summary>
public static class QtiSharedVocabularyStylesheet
{
    /// <summary>File name of the stylesheet the package conversion adds next to the manifest.</summary>
    public const string FileName = "qti3p0.css";

    private static readonly Lazy<string> Content = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Citolab.QTI.Converter.qti3p0.css")
                           ?? throw new InvalidOperationException("Embedded resource qti3p0.css not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    });

    /// <summary>The stylesheet content.</summary>
    public static string Css => Content.Value;
}
