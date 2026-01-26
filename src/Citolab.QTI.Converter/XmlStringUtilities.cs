namespace Citolab.QTI.Converter;

internal static class XmlStringUtilities
{
    public static string CleanXmlString(string xmlString)
    {
        if (string.IsNullOrWhiteSpace(xmlString)) return xmlString;

        var idx = xmlString.IndexOf("<?xml", StringComparison.OrdinalIgnoreCase);
        if (idx > 0)
        {
            return xmlString.Substring(idx).Replace("&#xfeff;", "");
        }

        if (idx == 0)
        {
            return xmlString.Replace("&#xfeff;", "");
        }

        return $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n{xmlString.Replace("&#xfeff;", string.Empty)}";
    }
}
