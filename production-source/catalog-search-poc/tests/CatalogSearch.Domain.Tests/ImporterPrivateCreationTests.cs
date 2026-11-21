using System.Reflection;
using CatalogSearch.Infrastructure;
using Xunit;
namespace CatalogSearch.Domain.Tests;
public sealed class ImporterPrivateCreationTests
{
    static object? Call(string method,string path) => typeof(FileImporterEnrichmentReader).Assembly
        .GetType("CatalogSearch.Infrastructure.EnrichmentFiles")!.GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[path]);
    [Fact]
    public void NewPrivatePathsAndExistingPathsAreNotRepairedOrOverwritten()
    {
        var root=Path.Combine(Path.GetTempPath(),"lips-private-create-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var directory=Path.Combine(root,"new");Call("CreatePrivateDirectory",directory);Assert.True(Directory.Exists(directory));
        var file=Path.Combine(directory,"record.json");using(var stream=(FileStream)Call("CreatePrivateFile",file)!){stream.Write([1,2,3]);stream.Flush(true);}
        Assert.Equal(new byte[]{1,2,3},File.ReadAllBytes(file));
        var ex=Assert.Throws<TargetInvocationException>(()=>Call("CreatePrivateFile",file));Assert.IsAssignableFrom<IOException>(ex.InnerException);
        Assert.Equal(new byte[]{1,2,3},File.ReadAllBytes(file));
        if(!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead|UnixFileMode.UserWrite,File.GetUnixFileMode(file));
            Assert.Equal(UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute,File.GetUnixFileMode(directory));
            var existing=Path.Combine(root,"existing");
            Directory.CreateDirectory(existing,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute|UnixFileMode.GroupRead);
            var before=File.GetUnixFileMode(existing);Call("CreatePrivateDirectory",existing);Assert.Equal(before,File.GetUnixFileMode(existing));
        }
        Call("CreatePrivateDirectory",directory);Assert.Equal(new byte[]{1,2,3},File.ReadAllBytes(file));
        var notDirectory=Path.Combine(root,"not-directory");File.WriteAllText(notDirectory,"original");
        Assert.Throws<TargetInvocationException>(()=>Call("CreatePrivateDirectory",notDirectory));Assert.Equal("original",File.ReadAllText(notDirectory));
    }
}
