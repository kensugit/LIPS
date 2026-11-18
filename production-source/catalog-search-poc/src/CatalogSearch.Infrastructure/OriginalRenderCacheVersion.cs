using System.Security.Cryptography;
using System.Text;

namespace CatalogSearch.Infrastructure;

public static class OriginalRenderCacheVersion
{
    // Changing either dispatcher or policy implementation invalidates Linux XLSX images.
    public static string Resolve(string extension, bool windows, string scriptDirectory)
    {
        if (extension != ".xlsx") return "v1";
        if (windows) return "v2-native-density";
        var dispatcher = SHA256.HashData(File.ReadAllBytes(Path.Combine(scriptDirectory, "render.py")));
        var backend = SHA256.HashData(File.ReadAllBytes(Path.Combine(scriptDirectory, "libreoffice_backend.py")));
        var identity = "linux-xlsx-policy-v1:" + Convert.ToHexString(dispatcher) + ":" + Convert.ToHexString(backend);
        return "v3-linux-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
}
