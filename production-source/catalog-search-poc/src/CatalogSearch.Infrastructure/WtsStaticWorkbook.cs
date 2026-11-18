using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
namespace CatalogSearch.Infrastructure;

// WTS-only, in-memory read projection. Original bytes remain the persistence/evidence source.
internal static class WtsStaticWorkbook
{
    public static byte[] Project(byte[] original)
    {
        using var zip = new ZipArchive(new MemoryStream(original, false), ZipArchiveMode.Read);
        if (zip.Entries.Count > 2000 || zip.Entries.Sum(e => e.Length) > 100_000_000 ||
            zip.Entries.Select(e=>e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=zip.Entries.Count)
            throw new InvalidDataException("Workbook limits or duplicate parts.");
        if (!zip.Entries.Any(e=>e.FullName.StartsWith("xl/externalLinks/",StringComparison.Ordinal))) return original;
        var xml = new Dictionary<string,XDocument>();
        foreach(var entry in zip.Entries)
        {
            var name=entry.FullName;
            if(name.StartsWith("xl/printerSettings/",StringComparison.Ordinal) && name.EndsWith(".bin",StringComparison.Ordinal)) continue;
            if (name.Contains("..") || name.Contains('\\') || name.StartsWith('/') ||
                !(name.EndsWith(".xml",StringComparison.Ordinal) || name.EndsWith(".rels",StringComparison.Ordinal) || (name.StartsWith("package/services/metadata/core-properties/",StringComparison.Ordinal) && name.EndsWith(".psmdcp",StringComparison.Ordinal))))
                throw new FolderImportHeldException("外部リンク付き資料に未対応の部品があります。静的読取を保留しました。");
            using var input=entry.Open();
            using var reader=XmlReader.Create(input,new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=100_000_000 });
            XDocument doc;
            try {doc=XDocument.Load(reader);} catch(XmlException){throw new InvalidDataException("Workbook XML invalid.");}
            xml[name]=doc;
            if(name.StartsWith("xl/externalLinks/",StringComparison.Ordinal)) continue;
            // Never infer formula/cache values or load data connections.
            if(doc.Descendants().Any(e=>e.Name.LocalName is "f"  or "connection" or "queryTable" or "oleObject" ))
                throw new FolderImportHeldException("外部リンク付き資料に数式・データ接続などの依存があります。静的読取を保留しました。");
            if(name.EndsWith(".rels",StringComparison.Ordinal))
                foreach(var rel in doc.Root!.Elements())
                    if((string?)rel.Attribute("TargetMode")=="External" && !((string?)rel.Attribute("Type")??"").EndsWith("/externalLink",StringComparison.Ordinal) && !((string?)rel.Attribute("Type")??"").EndsWith("/hyperlink",StringComparison.Ordinal))
                        throw new FolderImportHeldException("外部リンク以外の外部参照があります。静的読取を保留しました。");
        }
        using var output=new MemoryStream();
        using(var clean=new ZipArchive(output,ZipArchiveMode.Create,true))
        foreach(var entry in zip.Entries)
        {
            var name=entry.FullName;
            if(name.StartsWith("xl/printerSettings/",StringComparison.Ordinal) && name.EndsWith(".bin",StringComparison.Ordinal)) continue;
            if(name.StartsWith("xl/externalLinks/",StringComparison.Ordinal))continue;
            var doc=xml[name];var changed=false;
            // Optional defined-name/extension metadata is not an input to the static cell parser.
            // Remove it from the read projection rather than resolving or evaluating it.
            var metadata=doc.Descendants().Where(e=>e.Name.LocalName is "definedNames" or "extLst" or "hyperlinks" or "hlinkClick" or "hlinkHover" or "pageSetup").ToList();
            if(metadata.Count>0){metadata.Remove();changed=true;}
            if(name.EndsWith(".rels",StringComparison.Ordinal)) {
                var links=doc.Root!.Elements().Where(e=>((string?)e.Attribute("Type")??"").EndsWith("/hyperlink",StringComparison.Ordinal) || ((string?)e.Attribute("Type")??"").EndsWith("/printerSettings",StringComparison.Ordinal)).ToList();
                if(links.Count>0){links.Remove();changed=true;}
            }
            if(name=="xl/workbook.xml") {doc.Descendants().Where(e=>e.Name.LocalName=="externalReferences").Remove();changed=true;}
            if(name=="xl/_rels/workbook.xml.rels") {doc.Root!.Elements().Where(e=>((string?)e.Attribute("Type")??"").EndsWith("/externalLink",StringComparison.Ordinal)).Remove();changed=true;}
            if(name=="[Content_Types].xml") {doc.Root!.Elements().Where(e=>((string?)e.Attribute("PartName")??"").StartsWith("/xl/externalLinks/",StringComparison.Ordinal) || ((string?)e.Attribute("PartName")??"").StartsWith("/xl/printerSettings/",StringComparison.Ordinal)).Remove();changed=true;}
            using var target=clean.CreateEntry(name).Open();
            if(changed)doc.Save(target);else {using var source=entry.Open();source.CopyTo(target);}
        }
        return output.ToArray();
    }
}

