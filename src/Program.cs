using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

#if DEBUG
await Cmd("./test.pdb", "./meta/metadata.json", "./meta");
#else
await Cmd(args[0], args[1], args[2]);
#endif

static async Task Cmd(string input, string metadata, string output)
{
    Directory.CreateDirectory(output);

    MetadataReader reader;

    using FileStream fs = File.OpenRead(input);
    if (Path.GetExtension(input) == ".pdb")
    {
        MetadataReaderProvider readerProvider = MetadataReaderProvider.FromPortablePdbStream(fs);
        reader = readerProvider.GetMetadataReader();
    }
    else
    {
        using var pe = new PEReader(fs);
        reader = pe.GetMetadataReader();
    }

    await ParseSourceLink(reader, metadata, output);
}

static async Task ParseSourceLink(MetadataReader reader, string metadata, string output)
{
    SourceLink? link = GetSourceLink(reader);

    foreach (var assemblyHandle in reader.AssemblyFiles)
    {
        AssemblyFile assembly = reader.GetAssemblyFile(assemblyHandle);
        string name = reader.GetString(assembly.Name);
        byte[] hash = reader.GetBlobBytes(assembly.HashValue);
    }

    Ctx ctx = new(output, reader, link, new());
    Meta meta = new(link, new());
    foreach (DocumentHandle documentHandle in reader.Documents)
    {
        Doc doc = await ParseDocument(ctx, documentHandle);
        if (doc != default)
        {
            meta.docs.Add(doc);
        }
    }
    await meta.WriteTo(metadata);
}

static async Task<Doc> ParseDocument(Ctx ctx, DocumentHandle documentHandle)
{
    Document document = ctx.reader.GetDocument(documentHandle);
    if (document.Name.IsNil || document.Language.IsNil || document.Hash.IsNil || document.HashAlgorithm.IsNil)
    {
        Console.WriteLine($"Document not found for handle {documentHandle}");
        return default;
    }

    Doc doc = new(ctx.reader.GetString(document.Name), ctx.reader.GetGuid(document.Language), ctx.reader.GetGuid(document.HashAlgorithm), ctx.reader.GetBlobBytes(document.Hash));
    byte[]? content = GetEmbeddedDocumentContent(ctx.reader, documentHandle);

    if (content == null && ctx.link != null)
    {
        var url = ctx.link.GetUrl(doc.name);
        if (url != null)
        {
            Console.WriteLine($"Downloading source from {url}");
            content = await ctx.http.GetByteArrayAsync(url);
        }
    }

    if (content == null)
    {
        Console.WriteLine($"No SourceLink or Embedded document for {doc.name}");
        return doc;
    }

    if (doc.name.StartsWith("/_/", StringComparison.Ordinal))
    {
        doc.name = doc.name.Substring(3);
    }
    else if (doc.name.StartsWith("/", StringComparison.Ordinal))
    {
        doc.name = doc.name.Substring(1);
    }

    string path = Path.Combine(ctx.output, doc.name);
    Console.WriteLine($"Writing source to {path}");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllBytesAsync(path, content);
    return doc;
}

static SourceLink? GetSourceLink(MetadataReader reader)
{
    BlobHandle blobHandle = default;
    foreach (CustomDebugInformationHandle handle in reader.GetCustomDebugInformation(EntityHandle.ModuleDefinition))
    {
        CustomDebugInformation cdi = reader.GetCustomDebugInformation(handle);
        if (reader.GetGuid(cdi.Kind) == SourceLink.SourceLinkId)
        {
            blobHandle = cdi.Value;
        }
    }

    if (blobHandle.IsNil)
    {
        return null;
    }

    return JsonSerializer.Deserialize<SourceLink>(reader.GetBlobBytes(blobHandle));
}

static byte[]? GetEmbeddedDocumentContent(MetadataReader reader, DocumentHandle documentHandle)
{
    foreach (CustomDebugInformationHandle handle in reader.GetCustomDebugInformation(documentHandle))
    {
        CustomDebugInformation cdi = reader.GetCustomDebugInformation(handle);
        if (reader.GetGuid(cdi.Kind) == SourceLink.EmbeddedSourceId)
        {
            return reader.GetBlobBytes(cdi.Value);
        }
    }

    return null;
}

readonly record struct Ctx(string output, MetadataReader reader, SourceLink? link, HttpClient http);

record struct Meta(SourceLink? link, List<Doc> docs);

record struct Doc(string name, Guid lang, Guid algo, byte[] hash);

class SourceLink
{
    public static Guid EmbeddedSourceId { get; } = new("0E8A571B-6926-466E-B4AD-8AB04611F5FE");
    public static Guid SourceLinkId { get; } = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

    [JsonPropertyName("documents")]
    public Dictionary<string, string> Documents { get; set; }

    public string? GetUrl(string file)
    {
        if (Documents is null)
        {
            return null;
        }

        foreach (string key in Documents.Keys)
        {
            if (key.Contains('*', StringComparison.Ordinal))
            {
                string pattern = Regex.Escape(key).Replace(@"\*", "(.+)");
                Match m = Regex.Match(file, pattern);
                if (!m.Success)
                {
                    continue;
                }

                string url = Documents[key];
                string path = m.Groups[1].Value.Replace(@"\", "/", StringComparison.Ordinal);
                return url.Replace("*", path);
            }

            if (key.Equals(file, StringComparison.Ordinal))
            {
                return Documents[key];
            }
        }

        return null;
    }
}

static class MetaExt
{
    public static async Task WriteTo(this Meta link, string path)
    {
        using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, link);
    }
}
