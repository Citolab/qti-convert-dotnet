namespace Citolab.QTI.Converter;

internal sealed class QtiPackageFile
{
    private QtiPackageFile(string? textContent, byte[]? binaryContent, QtiPackageXmlType xmlType)
    {
        TextContent = textContent;
        BinaryContent = binaryContent;
        XmlType = xmlType;
    }

    public string? TextContent { get; }
    public byte[]? BinaryContent { get; }
    public QtiPackageXmlType XmlType { get; }

    public static QtiPackageFile FromText(string text, QtiPackageXmlType xmlType) => new(text, null, xmlType);
    public static QtiPackageFile FromBytes(byte[] bytes, QtiPackageXmlType xmlType) => new(null, bytes, xmlType);
}

