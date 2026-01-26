using System.IO;
using System.Xml;

namespace Citolab.QTI.Converter;

internal enum QtiPackageXmlType
{
    Other,
    Manifest,
    AssessmentItem,
    AssessmentTest
}

internal static class QtiPackageXmlDetector
{
    public static QtiPackageXmlType Detect(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return QtiPackageXmlType.Other;

        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreWhitespace = true
            });

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                return reader.LocalName switch
                {
                    "manifest" or "imsmanifest" => QtiPackageXmlType.Manifest,
                    "assessmentItem" or "qti-assessment-item" => QtiPackageXmlType.AssessmentItem,
                    "assessmentTest" or "qti-assessment-test" => QtiPackageXmlType.AssessmentTest,
                    _ => QtiPackageXmlType.Other
                };
            }
        }
        catch
        {
        }

        return QtiPackageXmlType.Other;
    }
}
