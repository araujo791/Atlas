using Inventory.Core;
using Xunit;

namespace Inventory.Tests;

public sealed class ReferenceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "preset-test-" + Guid.NewGuid().ToString("N"));
    public ReferenceTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Fact]
    public void EmbeddedPresetContainsThe372DistinctUploadedIds()
    {
        Assert.Equal(372, PresetReference.Data.Entries.Count);
        Assert.Equal(372, PresetReference.Data.Entries.Select(entry => entry.Id).Distinct().Count());
        Assert.Equal(64, PresetReference.Data.SourceSha256.Length);
    }

    [Fact]
    public void ReferencesNeverInventInstalledItemsOnAnEmptyImage()
    {
        var report = new InventoryReport();
        var references = PresetReference.Build(root, report);
        Assert.Equal(372, references.Count);
        Assert.All(references, item => {
            Assert.True(item.IsReference);
            Assert.StartsWith("NÃO VERIFICADO", item.State);
        });
        Assert.Empty(report.Services); Assert.Empty(report.Components); Assert.Empty(report.Applications);
    }

    [Fact]
    public void RecognizesUserServiceTemplateWithoutSubstringFalsePositives()
    {
        var report = new InventoryReport { Services = new() {
            new() { Name = "cbdhsvc_a12f" }, new() { Name = "NotRemoteRegistry" }
        }};
        var references = PresetReference.Build(root, report);
        Assert.Equal("Registro de serviço identificado",
            Assert.Single(references, item => item.Id == "ntlite-reference:clipboardusersvc").State);
        Assert.StartsWith("NÃO VERIFICADO",
            Assert.Single(references, item => item.Id == "ntlite-reference:remoteregistry").State);
    }

    [Fact]
    public void FileEvidenceIsOnlyPartialAndNotConfirmationOfTheWholeComponent()
    {
        Directory.CreateDirectory(Path.Combine(root, "Windows", "System32"));
        File.WriteAllText(Path.Combine(root, "Windows", "System32", "Magnify.exe"), "fixture");
        var references = PresetReference.Build(root, new());
        Assert.Equal("Evidência parcial por arquivo",
            Assert.Single(references, item => item.Id == "ntlite-reference:accessibility_magnifier").State);
    }

    [Fact]
    public void FirewallAndDefenderHaveFriendlyServiceNames()
    {
        Assert.Equal("Firewall do Windows (MpsSvc)", PresetReference.FriendlyServiceName(new() { Name = "MpsSvc" }));
        Assert.Equal("Microsoft Defender Antivirus (WinDefend)", PresetReference.FriendlyServiceName(new() { Name = "WinDefend" }));
    }

    [Theory]
    [InlineData(0x80000017u, false)] // WOF data compression
    [InlineData(0x80000008u, false)] // WIM data
    [InlineData(0xA000000Cu, true)] // symbolic link
    [InlineData(0xA0000003u, true)] // junction/mount-point redirect
    public void CompressedImageFilesAreNotConfusedWithNameRedirects(uint tag, bool redirect)
    {
        Assert.Equal(redirect, WindowsReparse.RedirectsName(tag));
    }

    [Fact]
    public void FirewallBuiltinUsesRealServiceEvidence()
    {
        var report = new InventoryReport { Services = new() { new() { Name = "MpsSvc" }, new() { Name = "BFE" } } };
        InventoryCatalog.AddImageComponents(root, report, CancellationToken.None);
        Assert.Contains(report.Components, item => item.Name == "Firewall do Windows");
        Assert.Contains(report.Components, item => item.Name == "Mecanismo de Filtragem Básica (BFE)");
    }
}
