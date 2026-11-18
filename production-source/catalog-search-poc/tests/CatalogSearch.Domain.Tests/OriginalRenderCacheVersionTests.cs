using CatalogSearch.Infrastructure;
using Xunit;

namespace CatalogSearch.Domain.Tests;

public sealed class OriginalRenderCacheVersionTests
{
    [Fact]
    public void LinuxSeparatesLegacyAndBothCodeChangesWithoutDeletingOldCache()
    {
        var root = Path.Combine(Path.GetTempPath(), "lips-cache-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "render.py"), "dispatcher-a");
            File.WriteAllText(Path.Combine(root, "libreoffice_backend.py"), "policy-a");
            var initial = OriginalRenderCacheVersion.Resolve(".xlsx", false, root);
            Assert.NotEqual("v1", initial);
            Assert.Equal(initial, OriginalRenderCacheVersion.Resolve(".xlsx", false, root));
            Directory.CreateDirectory(Path.Combine(root, initial));
            var oldImage = Path.Combine(root, initial, "base.png");
            File.WriteAllText(oldImage, "old-image");
            File.WriteAllText(Path.Combine(root, "libreoffice_backend.py"), "policy-b");
            var changedPolicy = OriginalRenderCacheVersion.Resolve(".xlsx", false, root);
            Assert.NotEqual(initial, changedPolicy);
            File.WriteAllText(Path.Combine(root, "render.py"), "dispatcher-b");
            Assert.NotEqual(changedPolicy, OriginalRenderCacheVersion.Resolve(".xlsx", false, root));
            Assert.Equal("old-image", File.ReadAllText(oldImage));
            File.Delete(Path.Combine(root, "libreoffice_backend.py"));
            Assert.Throws<FileNotFoundException>(() => OriginalRenderCacheVersion.Resolve(".xlsx", false, root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(".xlsx", true, "v2-native-density")]
    [InlineData(".pdf", false, "v1")]
    [InlineData(".pdf", true, "v1")]
    public void UnaffectedPathsDoNotRequireLinuxFiles(string extension, bool windows, string expected)
    {
        Assert.Equal(expected, OriginalRenderCacheVersion.Resolve(extension, windows, "nonexistent-directory"));
    }
}
