namespace Inventory.Core;

public sealed class InventoryReport
{
    public int SchemaVersion { get; set; } = 2;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string Source { get; set; } = "";
    public string Edition { get; set; } = "";
    public string CollectionStatus { get; set; } = "Varredura de arquivos; consulte limites e avisos.";
    public Dictionary<string, string> Windows { get; set; } = new();
    public List<AppItem> Applications { get; set; } = new();
    public List<ServiceItem> Services { get; set; } = new();
    public List<FileItem> Files { get; set; } = new();
    public List<ComponentItem> Components { get; set; } = new();
    public List<CatalogItem> Catalog { get; set; } = new();
    public List<CatalogItem> PresetReferences { get; set; } = new();
    public string SelectionIntent { get; set; } = "Futura remoção (planejamento)";
    public List<string> SelectedCatalogIds { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public int WarningCount { get; set; }
    public string Limits { get; set; } = "Inventário somente de leitura. Vínculos por registro, manifesto ou pasta; não é um grafo completo de dependências do NTLite. Tamanhos são lógicos, não espaço recuperável.";
}

public sealed class AppItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Version { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Location { get; set; } = "";
    public string UninstallCommand { get; set; } = "";
    public string IconPath { get; set; } = "";
    public List<string> Dependencies { get; set; } = new();
    public long LogicalBytes { get; set; }
    public int FileCount { get; set; }
}

public sealed class ServiceItem
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Start { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public string ServiceDll { get; set; } = "";
    public string Account { get; set; } = "";
    public string Description { get; set; } = "";
    public int Type { get; set; }
    public List<string> ControlSets { get; set; } = new();
    public bool IsPrimaryControlSet { get; set; } = true;
    public List<string> Dependencies { get; set; } = new();
}

public sealed class FileItem
{
    public string Path { get; set; } = "";
    public bool Found { get; set; }
    public long? Bytes { get; set; }
    public List<string> Owners { get; set; } = new();
    public List<string> Evidence { get; set; } = new();
    public bool Shared => Owners.Count > 1;
}

public sealed class ComponentItem
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string State { get; set; } = "";
    public string Details { get; set; } = "";
}

public sealed class CatalogItem
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public string State { get; set; } = "";
    public string Details { get; set; } = "";
    public bool IsReference { get; set; }
}

public sealed class RegistrySnapshot
{
    public Dictionary<string, string> Windows { get; set; } = new();
    public List<AppItem> Applications { get; set; } = new();
    public List<ServiceItem> Services { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
