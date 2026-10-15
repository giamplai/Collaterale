using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Collaterale.Core.Languages;

public enum StructuredFormat { None, Json, Xml }

/// <summary>
/// Rewrites JSON and XML into one canonical layout (fixed indentation, one value per line, object keys and XML attributes in alphabetical
/// order) so that a comparison shows only changes of meaning, not re-formatting or re-ordering. Returns null when the text cannot be parsed.
/// </summary>
public static class StructuralNormalizer
{
    public static StructuredFormat FormatFor(string? path)
    {
        var ext = Path.GetExtension(path ?? "").ToLowerInvariant();
        return ext switch
        {
            ".json" or ".jsonc" or ".json5" or ".geojson" or ".webmanifest" or ".har" => StructuredFormat.Json,
            ".xml" or ".svg" or ".xaml" or ".axaml" or ".csproj" or ".vbproj" or ".fsproj" or ".props" or ".targets" or ".config" or ".resx" or ".plist" or ".xsl" or ".xslt" or ".xsd" or ".nuspec" or ".gpx" or ".kml" or ".rss" or ".atom" => StructuredFormat.Xml,
            _ => StructuredFormat.None,
        };
    }

    public static string[]? Normalize(string text, StructuredFormat format, bool sortKeys = true)
    {
        try
        {
            var s = format switch
            {
                StructuredFormat.Json => Json(text, sortKeys),
                StructuredFormat.Xml => Xml(text, sortKeys),
                _ => null,
            };
            return s?.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        }
        catch (Exception ex) when (ex is JsonException or XmlException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string Json(string text, bool sortKeys)
    {
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, IndentSize = 2, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, NewLine = "\n" }))
        {
            WriteJson(w, doc.RootElement, sortKeys);
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteJson(Utf8JsonWriter w, JsonElement e, bool sortKeys)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                w.WriteStartObject();
                IEnumerable<JsonProperty> props = e.EnumerateObject();
                if (sortKeys) { props = props.OrderBy(p => p.Name, StringComparer.Ordinal); }
                foreach (var p in props)
                {
                    w.WritePropertyName(p.Name);
                    WriteJson(w, p.Value, sortKeys);
                }

                w.WriteEndObject();
                break;
            case JsonValueKind.Array:
                w.WriteStartArray();
                foreach (var item in e.EnumerateArray()) { WriteJson(w, item, sortKeys); }
                w.WriteEndArray();
                break;
            default:
                e.WriteTo(w);                    // numbers keep their written form
                break;
        }
    }

    private static string Xml(string text, bool sortAttributes)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreWhitespace = true, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(text), settings);
        var doc = XDocument.Load(reader, LoadOptions.None);
        foreach (var empty in doc.Descendants().Where(d => !d.Nodes().Any()).ToList())
        {
            empty.RemoveNodes();               // <e></e> and <e/> mean the same
        }

        if (sortAttributes)
        {
            foreach (var el in doc.Descendants())
            {
                if (el.Attributes().Count() < 2) { continue; }
                var ordered = el.Attributes().OrderBy(a => a.IsNamespaceDeclaration ? 0 : 1).ThenBy(a => a.Name.ToString(), StringComparer.Ordinal).ToList();
                el.RemoveAttributes();
                el.Add(ordered);
            }
        }

        var sb = new StringBuilder();
        var ws = new XmlWriterSettings { Indent = true, IndentChars = "  ", NewLineChars = "\n", NewLineHandling = NewLineHandling.Replace, OmitXmlDeclaration = doc.Declaration is null, NewLineOnAttributes = false, Encoding = new UTF8Encoding(false) };
        using (var xw = XmlWriter.Create(sb, ws)) { doc.Save(xw); }
        return sb.ToString();
    }
}
