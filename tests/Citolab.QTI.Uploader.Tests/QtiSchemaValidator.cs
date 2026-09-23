using System.Net.Http;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;

namespace Citolab.QTI.Uploader.Tests;

/// <summary>
/// Validates XML against the official QTI XSDs. The schemas are downloaded once and cached; imported MathML, SSML,
/// XInclude and APIP schemas are replaced by lax stubs, so the QTI part is checked strictly.
/// </summary>
internal static class QtiSchemaValidator
{
    public const string Qti21XsdUrl = "https://www.imsglobal.org/xsd/qti/qtiv2p1/imsqti_v2p1p2.xsd";
    public const string Qti3XsdUrl = "https://purl.imsglobal.org/spec/qti/v3p0/schema/xsd/imsqti_asiv3p0p1_v1p0.xsd";

    private const string XmlXsdUrl = "https://www.w3.org/2001/xml.xsd";
    private static readonly string CacheDir = Path.Combine(Path.GetTempPath(), "qti-convert-dotnet-xsd-cache");
    private static readonly string[] LaxNamespaces =
    {
        "http://www.w3.org/1998/Math/MathML",
        "http://www.w3.org/2001/XInclude",
        "http://www.w3.org/2001/10/synthesis",
        "http://www.imsglobal.org/xsd/apip/apipv1p0/imsapip_qtiv1p0"
    };
    private static readonly Dictionary<string, XmlSchemaSet?> Schemas = new();

    /// <summary>The compiled schema, or null when it could not be downloaded (the caller then skips validation).</summary>
    public static XmlSchemaSet? Get(string name, string url)
    {
        lock (Schemas)
        {
            if (!Schemas.TryGetValue(name, out var set))
            {
                set = Load(name, url);
                Schemas[name] = set;
            }
            return set;
        }
    }

    private static XmlSchemaSet? Load(string name, string url)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var schemaPath = Path.Combine(CacheDir, $"{name}-local.xsd");
            if (!File.Exists(schemaPath))
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var schema = http.GetStringAsync(url).GetAwaiter().GetResult();
                var xmlXsd = Path.Combine(CacheDir, "xml.xsd");
                if (!File.Exists(xmlXsd)) File.WriteAllText(xmlXsd, http.GetStringAsync(XmlXsdUrl).GetAwaiter().GetResult());

                foreach (Match import in Regex.Matches(schema, "<xs:import\\s+namespace=\"([^\"]+)\"\\s+schemaLocation=\"([^\"]+)\"\\s*/>"))
                {
                    var ns = import.Groups[1].Value;
                    var location = import.Groups[2].Value;
                    if (ns == "http://www.w3.org/XML/1998/namespace")
                    {
                        schema = schema.Replace(location, "xml.xsd");
                    }
                    else if (LaxNamespaces.Contains(ns))
                    {
                        var prefixes = Regex.Matches(schema, "xmlns:([\\w-]+)=\"([^\"]+)\"").Cast<Match>()
                            .Where(m => m.Groups[2].Value == ns).Select(m => m.Groups[1].Value).ToList();
                        var elements = prefixes
                            .SelectMany(p => Regex.Matches(schema, $"ref=\"{p}:([\\w-]+)\"").Cast<Match>().Select(m => m.Groups[1].Value))
                            .Distinct().DefaultIfEmpty("unused");
                        var stub = $"{name}-stub-{Array.IndexOf(LaxNamespaces, ns)}.xsd";
                        File.WriteAllText(Path.Combine(CacheDir, stub), LaxSchema(ns, elements));
                        schema = schema.Replace(location, stub);
                    }
                }
                File.WriteAllText(schemaPath, schema);
            }

            var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
            using (var reader = XmlReader.Create(schemaPath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, XmlResolver = new XmlUrlResolver() }))
            {
                set.Add(null, reader);
            }
            set.Compile();
            return set;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            Console.WriteLine($"{name} XSD could not be downloaded, skipping XSD validation: {exception.Message}");
            return null;
        }
    }

    private static string LaxSchema(string ns, IEnumerable<string> elements) =>
        $"""
         <?xml version="1.0"?>
         <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="{ns}" elementFormDefault="qualified">
         {string.Join("\n", elements.Select(e => $"""  <xs:element name="{e}"><xs:complexType mixed="true"><xs:sequence><xs:any processContents="skip" minOccurs="0" maxOccurs="unbounded"/></xs:sequence><xs:anyAttribute processContents="skip"/></xs:complexType></xs:element>"""))}
         </xs:schema>
         """;

    /// <summary>Returns the validation errors (empty when valid).</summary>
    public static List<string> Validate(string xml, XmlSchemaSet schemas)
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas, DtdProcessing = DtdProcessing.Ignore };
        settings.ValidationFlags |= XmlSchemaValidationFlags.ReportValidationWarnings;
        settings.ValidationEventHandler += (_, e) =>
        {
            if (e.Severity == XmlSeverityType.Error) errors.Add($"line {e.Exception?.LineNumber}: {e.Message}");
        };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        while (reader.Read())
        {
        }
        return errors;
    }
}
