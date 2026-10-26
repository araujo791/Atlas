using System.Text.Json;
using Inventory.Core;
using Xunit;

namespace Inventory.Tests;

public class InventoryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "iso-inventory-test-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> warnings = new();
    public InventoryTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    private string Put(string relative, string data = "binary")
    {
        var path = Path.Combine(root, relative.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, data);
        return path;
    }

    [Theory]
    [InlineData(@"%SystemRoot%\System32\drivers\demo.sys")]
    [InlineData(@"\SystemRoot\System32\drivers\demo.sys")]
    [InlineData(@"\??\C:\Windows\System32\drivers\demo.sys")]
    [InlineData(@"System32\drivers\demo.sys")]
    [InlineData(@"C:\WINDOWS\SYSTEM32\DRIVERS\DEMO.SYS")]
    public void ResolvesOfflinePathsWithoutUsingHostEnvironment(string input)
    {
        var expected = Put(@"Windows\System32\drivers\demo.sys");
        var resolved = new ImageFiles(root, warnings.Add).ResolveWindows(input);
        Assert.Equal(expected, resolved.PhysicalPath);
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData(@"D:\Applications\app.exe")]
    [InlineData(@"%UNKNOWN_OFFLINE_VARIABLE%\app.exe")]
    [InlineData(@"\\server\share\app.exe")]
    [InlineData(@"C:\Windows\..\..\outside.exe")]
    public void NeverReadsOutsideTheImage(string input)
    {
        Put(@"Windows\System32\demo.sys");
        var resolved = new ImageFiles(root, warnings.Add).ResolveWindows(input);
        Assert.Null(resolved.PhysicalPath);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Demo\\service.exe\" -service", @"C:\Program Files\Demo\service.exe")]
    [InlineData(@"C:\Program Files\Demo\service.exe -service", @"C:\Program Files\Demo\service.exe")]
    [InlineData(@"%SystemRoot%\System32\svchost.exe -k netsvcs", @"%SystemRoot%\System32\svchost.exe")]
    public void SeparatesBinaryReferencesFromCommandArguments(string command, string expected)
    {
        Assert.Equal(expected, ImageFiles.ExecutableFromCommand(command).File);
    }

    [Fact]
    public void UnquotedSpaceContainingPathIsExplicitlyAnEstimate()
    {
        Assert.Contains("estimada", ImageFiles.ExecutableFromCommand(@"C:\Program Files\Demo\service.exe -s").Evidence);
    }

    [Fact]
    public void SharedServiceHostsAndServiceDllsAreRecordedSeparately()
    {
        Put(@"Windows\System32\svchost.exe");
        Put(@"Windows\System32\a.dll");
        var registry = new RegistrySnapshot { Services = new() {
            new() { Name = "Alpha", ImagePath = @"%SystemRoot%\System32\svchost.exe -k group", ServiceDll = @"%SystemRoot%\System32\a.dll" },
            new() { Name = "Beta", ImagePath = @"C:\Windows\System32\svchost.exe -k other" }
        }};
        var report = ImageScanner.Scan(root, registry, CancellationToken.None);
        var host = Assert.Single(report.Files, x => x.Path.EndsWith("svchost.exe"));
        Assert.True(host.Shared);
        Assert.Equal(2, host.Owners.Count);
        var dll = Assert.Single(report.Files, x => x.Path.EndsWith("a.dll"));
        Assert.False(dll.Shared);
        Assert.Contains("Serviço: Alpha", dll.Owners);
    }

    [Fact]
    public void MissingDriverIsNotReportedAsAnExistingFile()
    {
        var registry = new RegistrySnapshot { Services = new() { new() { Name = "Demo", Type = 1 } } };
        var file = Assert.Single(ImageScanner.Scan(root, registry, CancellationToken.None).Files);
        Assert.False(file.Found);
        Assert.Null(file.Bytes);
        Assert.Contains("estimado", string.Join(";", file.Evidence));
    }

    [Fact]
    public void EnumeratesADeclaredProgramDirectoryAsAnEstimate()
    {
        Put(@"Program Files\Demo\app.exe", "demo");
        var snapshot = new RegistrySnapshot { Applications = new() {
            new() { Name = "Demo", Id = "registry-id", Kind = "Programa (registro HKLM)", Location = @"C:\Program Files\Demo" }
        }};
        var report = ImageScanner.Scan(root, snapshot, CancellationToken.None);
        Assert.Equal(1, Assert.Single(report.Applications).FileCount);
        Assert.Contains("estimada", string.Join(";", Assert.Single(report.Files).Evidence));
        Assert.Equal(0, snapshot.Applications[0].FileCount);
        Assert.Equal(1, Assert.Single(ImageScanner.Scan(root, snapshot, CancellationToken.None).Applications).FileCount);
    }

    [Fact]
    public void DoesNotAttributeASharedWindowsRootToOneProgram()
    {
        Put(@"Windows\System32\system.dll");
        var snapshot = new RegistrySnapshot { Applications = new() {
            new() { Name = "Invalid broad location", Location = @"C:\Windows" }
        }};
        var report = ImageScanner.Scan(root, snapshot, CancellationToken.None);
        Assert.Empty(report.Files);
        Assert.Contains(report.Warnings, w => w.Contains("raiz compartilhada"));
    }

    [Fact]
    public void AppxManifestAndEveryPackageFileAreReadWithoutChangingTheInput()
    {
        var manifest = """
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
            <Identity Name="Contoso.Demo" Version="1.2.3.4" Publisher="CN=Contoso"/>
            <Dependencies><PackageDependency Name="Microsoft.VCLibs.140.00"/></Dependencies>
            </Package>
            """;
        var file = Put(@"Program Files\WindowsApps\Contoso.Demo_1\AppxManifest.xml", manifest);
        Put(@"Program Files\WindowsApps\Contoso.Demo_1\app.exe", "application");
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(x => x, File.ReadAllBytes);
        var report = ImageScanner.Scan(root, new(), CancellationToken.None);
        var app = Assert.Single(report.Applications);
        Assert.Equal("Contoso.Demo", app.Name);
        Assert.Equal(2, app.FileCount);
        Assert.Contains("Microsoft.VCLibs.140.00", app.Dependencies);
        Assert.Equal(2, report.Files.Count);
        foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(before.Count, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
        var serialized = JsonSerializer.Serialize(report);
        Assert.Contains("Contoso.Demo", serialized);
        Assert.Equal(manifest, File.ReadAllText(file));
    }

    [Fact]
    public void ExternalXmlEntitiesAreNotRead()
    {
        Put(@"Windows\SystemApps\Untrusted\AppxManifest.xml",
            "<!DOCTYPE Package [<!ENTITY xxe SYSTEM 'file:///not-an-image-file'>]><Package>&xxe;</Package>");
        var report = ImageScanner.Scan(root, new(), CancellationToken.None);
        Assert.Empty(report.Applications);
        Assert.True(report.WarningCount > 1);
        Assert.Contains(report.Warnings, w => w.Contains("DTD", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CancellationIsPropagated()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => ImageScanner.Scan(root,
            new RegistrySnapshot { Services = new() { new() { Name = "Test" } } }, cancel.Token));
    }

    [Fact]
    public void ReadsEditionNamesContainingColons()
    {
        var editions = DismInventory.Editions("Index : 1\nName : Windows 11: Pro\nIndex : 2\nName : Windows 11 Home\n");
        Assert.Equal(2, editions.Count);
        Assert.Equal("Windows 11: Pro", editions[0].Name);
    }

    [Theory]
    [InlineData("The operation completed successfully.")]
    [InlineData("Index : 1\nName : One\nIndex : 1\nName : Two")]
    [InlineData("Index : invalid\nName : Test")]
    public void InvalidEditionInventoriesCannotLookSuccessful(string output)
    {
        Assert.Throws<FormatException>(() => DismInventory.Editions(output));
    }

    [Fact]
    public void ReadsComponentStatesWithoutGuessingFiles()
    {
        var components = DismInventory.Components("Feature Name : NetFx3\nState : Disabled\nThe operation completed successfully.",
            "Feature Name", "Recurso");
        Assert.Equal("NetFx3", Assert.Single(components).Name);
        Assert.Equal("Disabled", components[0].State);
    }

    [Fact]
    public void IncompleteComponentOutputFails()
    {
        Assert.Throws<FormatException>(() => DismInventory.Components("Feature Name : NetFx3\n",
            "Feature Name", "Recurso"));
    }

    [Fact]
    public void ProvisioningInventoryDoesNotInventAFileLocation()
    {
        var apps = DismInventory.ProvisionedApps("DisplayName : Microsoft.WindowsCalculator\nVersion : 11.0.0.0\nPackageName : Microsoft.WindowsCalculator_11.0.0.0_neutral_~_8wekyb3d8bbwe\nThe operation completed successfully.");
        var app = Assert.Single(apps);
        Assert.Equal("Microsoft.WindowsCalculator", app.Name);
        Assert.Empty(app.Location);
        Assert.Equal(0, app.FileCount);
    }

    [Fact]
    public void IncompleteProvisioningRecordsCannotLookSuccessful()
    {
        Assert.Throws<FormatException>(() => DismInventory.ProvisionedApps("DisplayName : Test\nThe operation completed successfully."));
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16")]
    [InlineData("utf16-bom")]
    public void NativeOutputSupportsUnicodeAndBomlessDismText(string format)
    {
        const string value = "Index : 1\r\nName : Edição de avaliação\r\n";
        var encoding = format == "utf8" ? System.Text.Encoding.UTF8 : System.Text.Encoding.Unicode;
        var bytes = encoding.GetBytes(value);
        if (format == "utf16-bom") bytes = encoding.GetPreamble().Concat(bytes).ToArray();
        Assert.Equal(value, NativeOutput.Decode(bytes));
        Assert.Equal("Edição de avaliação", Assert.Single(DismInventory.Editions(NativeOutput.Decode(bytes))).Name);
    }

    [UnixSymlinkFact]
    public void FilesAndDirectoriesLinkedElsewhereAreNeverFollowed()
    {
        var original = Put("outside/payload.dll", "do not read through a link");
        Directory.CreateDirectory(Path.Combine(root, "Windows", "System32"));
        File.CreateSymbolicLink(Path.Combine(root, "Windows", "System32", "linked.dll"), original);
        Directory.CreateSymbolicLink(Path.Combine(root, "Windows", "linked"), Path.Combine(root, "outside"));
        var fs = new ImageFiles(root, warnings.Add);
        Assert.Null(fs.ResolveRelative(@"Windows\System32\linked.dll").PhysicalPath);
        Assert.Null(fs.ResolveRelative(@"Windows\linked\payload.dll").PhysicalPath);
        Assert.Empty(fs.WalkFiles(Path.Combine(root, "Windows"), CancellationToken.None));
        Assert.NotEmpty(warnings);
    }
}

public sealed class UnixSymlinkFactAttribute : FactAttribute
{
    public UnixSymlinkFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "Requires Unix symlink fixture creation without developer-mode privileges.";
    }
}
