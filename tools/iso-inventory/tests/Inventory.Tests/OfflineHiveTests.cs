using Inventory.Core;
using Xunit;

namespace Inventory.Tests;

public sealed class OfflineHiveTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hive-test-" + Guid.NewGuid().ToString("N"));
    public OfflineHiveTests() => Directory.CreateDirectory(Path.Combine(root, "Windows", "System32", "config"));
    public void Dispose() => Directory.Delete(root, true);
    private void Put(string name, BinaryHiveFixture fixture) =>
        File.WriteAllBytes(Path.Combine(root, "Windows", "System32", "config", name), fixture.Build());

    [Fact]
    public void ReadsServicesFromBinaryHiveWithoutSelectKeyAndPreservesSourceBytes()
    {
        var hive = new BinaryHiveFixture();
        var service = hive.Root.Sub(@"ControlSet002\Services\WinDefend");
        service.Values["Start"] = 2; service.Values["Type"] = 0x10;
        service.Values["ImagePath"] = @"%SystemRoot%\System32\MsMpEng.exe";
        Put("SYSTEM", hive);
        string path = Path.Combine(root, "Windows", "System32", "config", "SYSTEM");
        var original = File.ReadAllBytes(path);
        var snapshot = OfflineRegistry.Read(root, CancellationToken.None);
        var actual = Assert.Single(snapshot.Services);
        Assert.Equal("WinDefend", actual.Name);
        Assert.Equal("Automático", actual.Start);
        Assert.Equal("ControlSet002", snapshot.Windows["ControlSet"]);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void PrefersDefaultAndRetainsServicesFromOtherActualControlSets()
    {
        var hive = new BinaryHiveFixture();
        hive.Root.Sub("Select").Values["Default"] = 2;
        hive.Root.Sub("Select").Values["Current"] = 1;
        hive.Root.Sub(@"ControlSet001\Services\OnlyOther").Values["Start"] = 3;
        hive.Root.Sub(@"ControlSet001\Services\Shared").Values["Start"] = 4;
        hive.Root.Sub(@"ControlSet002\Services\Shared").Values["Start"] = 2;
        Put("SYSTEM", hive);
        var snapshot = OfflineRegistry.Read(root, CancellationToken.None);
        Assert.Equal(2, snapshot.Services.Count);
        var shared = Assert.Single(snapshot.Services, service => service.Name == "Shared");
        Assert.Equal("Automático", shared.Start);
        Assert.Equal(2, shared.ControlSets.Count);
        Assert.False(Assert.Single(snapshot.Services, service => service.Name == "OnlyOther").IsPrimaryControlSet);
    }

    [Fact]
    public void ReadsSoftwareAndDoesNotExpandOfflineVariablesAgainstTheHost()
    {
        var hive = new BinaryHiveFixture();
        hive.Root.Sub(@"Microsoft\Windows NT\CurrentVersion").Values["CurrentBuildNumber"] = "26300";
        var app = hive.Root.Sub(@"Microsoft\Windows\CurrentVersion\Uninstall\Demo");
        app.Values["DisplayName"] = "Demo"; app.Values["InstallLocation"] = new BinaryHiveFixture.Expand(@"%SystemDrive%\Demo");
        Put("SOFTWARE", hive);
        var snapshot = OfflineRegistry.Read(root, CancellationToken.None);
        Assert.Equal("26300", snapshot.Windows["CurrentBuildNumber"]);
        Assert.Equal(@"%SystemDrive%\Demo", Assert.Single(snapshot.Applications).Location);
    }

    [Fact]
    public void CorruptHiveChecksumIsReportedRatherThanIgnoredOrRewritten()
    {
        var hive = new BinaryHiveFixture();
        hive.Root.Sub(@"ControlSet001\Services\Example").Values["Start"] = 2;
        Put("SYSTEM", hive);
        var path = Path.Combine(root, "Windows", "System32", "config", "SYSTEM");
        var bytes = File.ReadAllBytes(path); bytes[508] ^= 0xff; File.WriteAllBytes(path, bytes);
        var snapshot = OfflineRegistry.Read(root, CancellationToken.None);
        Assert.Contains(snapshot.Warnings, warning => warning.Contains("checksum", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void ReadsServiceDllAndMultiStringDependenciesFromActualBinaryValues()
    {
        var hive = new BinaryHiveFixture();
        var service = hive.Root.Sub(@"ControlSet001\Services\MpsSvc");
        service.Values["Start"] = 2; service.Values["Type"] = 0x20;
        service.Values["DependOnService"] = new[] { "BFE", "RpcSs" };
        service.Sub("Parameters").Values["ServiceDll"] = new BinaryHiveFixture.Expand(@"%SystemRoot%\System32\mpssvc.dll");
        Put("SYSTEM", hive);
        var actual = Assert.Single(OfflineRegistry.Read(root, CancellationToken.None).Services);
        Assert.Equal(@"%SystemRoot%\System32\mpssvc.dll", actual.ServiceDll);
        Assert.Equal(new[] { "BFE", "RpcSs" }, actual.Dependencies);
    }
}
