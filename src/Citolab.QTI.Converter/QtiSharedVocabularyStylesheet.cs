using System.Reflection;
using System.Text;

namespace Citolab.QTI.Converter;

/// <summary>
/// Stylesheet for the QTI 3 shared vocabulary classes (qti-*), added to QTI 2.1 output so QTI 2.1 players can style
/// them: the 1EdTech QTI 3 shared css (https://www.imsglobal.org/sites/default/files/spec/qti/v3/guide/img/qti3p0.css)
/// without its page-level layout section, plus a row-relative grid and fixes for QTI 2.1 players. Shared with
/// qti-convert; see the header of Assets/qti3-shared-vocabulary.css.
/// </summary>
public static class QtiSharedVocabularyStylesheet
{
    /// <summary>File name of the stylesheet the package conversion adds next to the manifest.</summary>
    public const string FileName = "qti3-shared-vocabulary.css";

    private static readonly Lazy<string> Content = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Citolab.QTI.Converter.qti3-shared-vocabulary.css")
                           ?? throw new InvalidOperationException("Embedded resource qti3-shared-vocabulary.css not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    });

    /// <summary>The stylesheet content.</summary>
    public static string Css => Content.Value;
}
