namespace CatalogSearch.Infrastructure;

// Reuses the enrichment store's reviewed path, bounded read, hash and directory fsync primitives.
public static class ProtectedExportFiles
{
    public static void CheckRoot(string root)
    {
        if(!Path.IsPathFullyQualified(root)) throw new InvalidDataException("EXPORT_ROOT_INVALID");
        EnrichmentFiles.RootSafe(root);
    }
    public static byte[] ReadPinned(string path,int limit,string sha)
    {
        if(!EnrichmentFiles.Hash(sha)) throw new InvalidDataException("EXPORT_PIN_INVALID");
        var bytes=EnrichmentFiles.Read(path,limit);
        if(EnrichmentFiles.Digest(bytes)!=sha) throw new InvalidDataException("EXPORT_PIN_MISMATCH");
        return bytes;
    }
    public static string CreateNew(string path,byte[] bytes,int maximum)
    {
        if(bytes.Length is 0 || bytes.Length>maximum) throw new InvalidDataException("EXPORT_SIZE_INVALID");
        CheckRoot(Path.GetDirectoryName(path)!);
        var options=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None};
        if(!OperatingSystem.IsWindows()) options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
        using(var stream=new FileStream(path,options)){stream.Write(bytes);stream.Flush(true);}
        EnrichmentFiles.SyncDirectory(Path.GetDirectoryName(path)!);
        var actual=EnrichmentFiles.Read(path,maximum);
        if(!actual.AsSpan().SequenceEqual(bytes)) throw new IOException("EXPORT_READBACK_FAILED");
        return EnrichmentFiles.Digest(actual);
    }
}
