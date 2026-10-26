using Inventory.Core;
using Xunit;

namespace Inventory.Tests;

public sealed class CatalogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "catalog-test-" + Guid.NewGuid().ToString("N"));
    public CatalogTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private void Put(string path, string value = "fixture")
    {
        var full = Path.Combine(root, path.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, value);
    }

    [Fact]
    public void DetectsDefenderAndEdgeFromActualImageEvidence()
    {
        Put(@"Program Files\Windows Defender\MsMpEng.exe");
        Put(@"Program Files (x86)\Microsoft\Edge\Application\msedge.exe");
        var report = new InventoryReport();
        InventoryCatalog.AddImageComponents(root, report, CancellationToken.None);
        var catalog = InventoryCatalog.Build(report);
        Assert.Contains(catalog, item => item.Name == "Microsoft Defender Antivirus" && item.State.Contains("Arquivo/binário"));
        Assert.Contains(catalog, item => item.Name == "Microsoft Edge");
    }

    [Fact]
    public void DoesNotInventBuiltinsOnAnEmptyImage()
    {
        var report = new InventoryReport();
        InventoryCatalog.AddImageComponents(root, report, CancellationToken.None);
        Assert.Empty(InventoryCatalog.Build(report));
    }

    [Fact]
    public void RegistryOnlyEvidenceIsNotReportedAsConfirmedBinary()
    {
        var report = new InventoryReport { Services = new() { new() { Name = "WinDefend", Start = "Automático" } } };
        InventoryCatalog.AddImageComponents(root, report, CancellationToken.None);
        var item = Assert.Single(report.Components, component => component.Name == "Microsoft Defender Antivirus");
        Assert.Contains("não confirmado", item.State);
    }

    [Fact]
    public void EmptyPlatformDirectoryDoesNotProveInstalledDefender()
    {
        Directory.CreateDirectory(Path.Combine(root, "ProgramData", "Microsoft", "Windows Defender", "Platform"));
        var report = new InventoryReport();
        InventoryCatalog.AddImageComponents(root, report, CancellationToken.None);
        Assert.Contains("instalação não confirmada", Assert.Single(report.Components).State);
    }

    [Fact]
    public void NativePackageStateOverridesManifestFallback()
    {
        Put(@"Windows\servicing\Packages\Demo-Package.mum");
        var report = new InventoryReport();
        InventoryCatalog.AddImageComponents(root, report, CancellationToken.None);
        report.Components.Add(new() { Name = "Demo-Package", Kind = "Pacote CBS", State = "Installed" });
        var item = Assert.Single(InventoryCatalog.Build(report));
        Assert.Equal("Installed", item.State);
    }

    [Fact]
    public void IncludesDriverInfAndDeferredScriptsWithoutExecutingThem()
    {
        Put(@"Windows\INF\demo.inf");
        Put(@"Windows\Setup\Scripts\SetupComplete.cmd", "this must never be executed");
        var report = new InventoryReport();
        InventoryCatalog.AddImageComponents(root, report, CancellationToken.None);
        Assert.Contains(report.Components, item => item.Name == "demo.inf" && item.Kind == "Pacote de driver");
        Assert.Contains(report.Components, item => item.Name == "SetupComplete.cmd" && item.State.Contains("não determinados"));
    }

    [Fact]
    public void ServicesAndDriverServicesRemainDistinctAndDisabledMeansDisabled()
    {
        var report = new InventoryReport { Services = new() {
            new() { Name = "Spooler", Start = "Desativado", Type = 0x10 },
            new() { Name = "Driver", Start = "Boot", Type = 1 }
        }};
        var catalog = InventoryCatalog.Build(report);
        Assert.Contains(catalog, item => item.Id == "service:Spooler" && item.Category == "Serviços" && item.State == "Desativado");
        Assert.Contains(catalog, item => item.Id == "service:Driver" && item.Category == "Drivers (serviços)");
    }

    [Fact]
    public void CatalogModeListsAppMetadataWithoutFileInventory()
    {
        Put(@"Program Files\WindowsApps\Demo_1\AppxManifest.xml",
            "<Package><Identity Name='Demo' Version='1.0.0.0' Publisher='CN=Demo'/></Package>");
        Put(@"Program Files\WindowsApps\Demo_1\app.exe");
        var report = ImageScanner.Scan(root, new RegistrySnapshot(), CancellationToken.None, includeFileInventory: false);
        Assert.Empty(report.Files);
        Assert.Equal("Demo", Assert.Single(report.Applications).Name);
        Assert.Equal(0, report.Applications[0].FileCount);
    }

    [Fact]
    public void DriverQueryRetainsProviderDetailsWithoutInventingActiveState()
    {
        var result = DismInventory.Components("Published Name : demo.inf\nProvider Name : Contoso\nClass Name : System\nThe operation completed successfully.",
            "Published Name", "Pacote de driver");
        var driver = Assert.Single(result);
        Assert.Contains("Contoso", driver.Details);
        Assert.Equal("Listado no inventário DISM", driver.State);
    }

    [Fact]
    public void DuplicateCatalogIdsAreCollapsedAndSelectionIsEmptyByDefault()
    {
        var report = new InventoryReport { Services = new() { new() { Name = "Same" }, new() { Name = "Same" } } };
        Assert.Single(InventoryCatalog.Build(report));
        Assert.Empty(report.SelectedCatalogIds);
        Assert.Contains("Futura remoção", report.SelectionIntent);
    }

    [Fact]
    public void ReadsWinSxsAssemblyIdentityWithoutCollapsingDifferentVersions()
    {
        Put(@"Windows\WinSxS\Manifests\one.manifest",
            "<assembly><assemblyIdentity name='Demo.Component' version='1.0.0.0' processorArchitecture='amd64' language='neutral'/></assembly>");
        Put(@"Windows\WinSxS\Manifests\two.manifest",
            "<assembly><assemblyIdentity name='Demo.Component' version='2.0.0.0' processorArchitecture='amd64' language='neutral'/></assembly>");
        var report = new InventoryReport();
        InventoryCatalog.AddImageComponents(root, report, CancellationToken.None);
        var catalog = InventoryCatalog.Build(report);
        Assert.Equal(2, catalog.Count);
        Assert.All(catalog, item => Assert.Equal("Componentes WinSxS", item.Category));
        Assert.Contains(catalog, item => item.Name.Contains("2.0.0.0"));
    }

    [Fact]
    public void UndecodableWinSxsManifestIsVisibleAndNotClaimedToBeActive()
    {
        Put(@"Windows\WinSxS\Manifests\compressed.manifest", "not readable XML");
        var report = new InventoryReport();
        InventoryCatalog.AddImageComponents(root, report, CancellationToken.None);
        var item = Assert.Single(InventoryCatalog.Build(report));
        Assert.Equal("compressed", item.Name);
        Assert.Contains("não confirmadas", item.State);
        Assert.Equal(1, report.WarningCount);
    }
}
