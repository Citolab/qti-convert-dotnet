using System.Reflection;
using System.Text;

namespace Citolab.QTI.Converter;

internal static class SaxonXslt30Transformer
{
#if NET9_0_OR_GREATER
    private static readonly Lazy<Saxon.Api.XsltExecutable> Executable = new(CompileStylesheet);

    public static bool IsAvailable => true;

    public static string Transform(string sourceXml)
    {
        var processor = new Saxon.Api.Processor(false);
        var builder = processor.NewDocumentBuilder();

        using var reader = new StringReader(sourceXml);
        var xdmNode = builder.Build(reader);

        var transformer = Executable.Value.Load();
        transformer.InitialContextNode = xdmNode;

        var output = new StringWriter();
        var destination = processor.NewSerializer(output);
        transformer.Run(destination);

        return output.ToString();
    }

    private static Saxon.Api.XsltExecutable CompileStylesheet()
    {
        var processor = new Saxon.Api.Processor(false);
        var compiler = processor.NewXsltCompiler();

        var stylesheetText = ReadEmbeddedResource("Citolab.QTI.Converter.Assets.qti2xTo30.xsl");
        using var stylesheetReader = new StringReader(stylesheetText);
        return compiler.Compile(stylesheetReader);
    }
#else
    public static bool IsAvailable => false;
#endif

    private static string ReadEmbeddedResource(string resourceName)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
