using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Inventory.Core;

public static class ImageScanner
{
    public static InventoryReport Scan(string image, RegistrySnapshot snapshot,
        CancellationToken cancellation, IProgress<string>? progress = null, bool includeFileInventory = true)
    {
        var report = new InventoryReport { Source = image, Windows = new(snapshot.Windows),
            Applications = snapshot.Applications.Select(app => new AppItem {
                Id = app.Id, Name = app.Name, Kind = app.Kind, Version = app.Version,
                Publisher = app.Publisher, Location = app.Location, IconPath = app.IconPath,
                UninstallCommand = app.UninstallCommand, Dependencies = app.Dependencies.ToList()
            }).ToList(), Services = snapshot.Services.ToList() };
        void Warn(string message)
        {
            report.WarningCount++;
            if (report.Warnings.Count < 1000) report.Warnings.Add(message);
        }
        foreach (var warning in snapshot.Warnings) Warn(warning);
        var fs = new ImageFiles(image, Warn, snapshot.Windows.GetValueOrDefault("SystemRoot"));
        var files = new Dictionary<string, FileItem>(StringComparer.OrdinalIgnoreCase);
        void Add(ResolvedImageFile resolved, string owner, string evidence)
        {
            if (!files.TryGetValue(resolved.Path, out var item))
            {
                long? size = null;
                var exists = resolved.PhysicalPath is not null && File.Exists(resolved.PhysicalPath);
                if (exists)
                    try { size = new FileInfo(resolved.PhysicalPath!).Length; }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Warn(e.Message); }
                item = new FileItem { Path = resolved.Path, Found = exists, Bytes = size };
                files[resolved.Path] = item;
            }
            if (!item.Owners.Contains(owner)) item.Owners.Add(owner);
            var note = evidence + "; " + resolved.Note;
            if (!item.Evidence.Contains(note)) item.Evidence.Add(note);
        }

        progress?.Report("Lendo serviços e drivers...");
        foreach (var service in includeFileInventory ? report.Services : Enumerable.Empty<ServiceItem>())
        {
            cancellation.ThrowIfCancellationRequested();
            var owner = "Serviço: " + service.Name;
            if (service.ImagePath.Length != 0)
            {
                var command = ImageFiles.ExecutableFromCommand(service.ImagePath);
                if (command.File.Length != 0) Add(fs.ResolveWindows(command.File), owner, command.Evidence);
            }
            else if ((service.Type & 3) != 0)
                Add(fs.ResolveWindows(@"%SystemRoot%\System32\drivers\" + service.Name + ".sys"), owner,
                    "Padrão de nome do driver (estimado; ImagePath ausente)");
            if (service.ServiceDll.Length != 0)
                Add(fs.ResolveWindows(service.ServiceDll), owner, "Parameters/ServiceDll do registro offline");
        }

        foreach (var app in includeFileInventory ? report.Applications : Enumerable.Empty<AppItem>())
        {
            cancellation.ThrowIfCancellationRequested();
            var owner = "Programa: " + app.Name + " [" + app.Id + "]";
            if (app.Location.Length != 0)
            {
                var directory = fs.ResolveWindows(app.Location);
                var parts = directory.Path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                if (directory.PhysicalPath is not null && Directory.Exists(directory.PhysicalPath) &&
                    parts.Length >= 2 && !parts[0].Equals("Windows", StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report("Inventariando a pasta declarada de " + app.Name + "...");
                    foreach (var file in fs.WalkFiles(directory.PhysicalPath, cancellation))
                    {
                        var resolved = new ResolvedImageFile(Path.GetRelativePath(fs.Root, file).Replace('/', '\\'),
                            file, "Contido na pasta InstallLocation declarada; associação estimada.");
                        Add(resolved, owner, "Registro InstallLocation (não prova de propriedade exclusiva)");
                        app.FileCount++;
                        if (files[resolved.Path].Bytes is { } bytes) app.LogicalBytes += bytes;
                    }
                }
                else Warn(app.Name + ": InstallLocation não enumerado (ausente, inacessível ou raiz compartilhada).");
            }
            if (app.IconPath.Length != 0)
            {
                var icon = Regex.Replace(app.IconPath.Trim(), @",\s*-?\d+$", "").Trim('"');
                Add(fs.ResolveWindows(icon), owner, "DisplayIcon (referência de metadado, não prova de propriedade)");
            }
            if (app.UninstallCommand.Length != 0)
            {
                var command = ImageFiles.ExecutableFromCommand(app.UninstallCommand);
                if (command.File.Length != 0)
                    Add(fs.ResolveWindows(command.File), owner, "Desinstalador: " + command.Evidence);
            }
        }

        foreach (var relative in new[] { @"Program Files\WindowsApps", @"Windows\SystemApps", @"Windows\ImmersiveControlPanel" })
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report("Lendo pacotes em " + relative + "...");
            var parent = fs.ResolveRelative(relative).PhysicalPath;
            if (parent is null || !Directory.Exists(parent)) continue;
            List<string> packages;
            try
            {
                packages = Directory.GetDirectories(parent).ToList();
                if (relative.EndsWith("ImmersiveControlPanel")) packages = new() { parent };
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Warn(relative + ": " + e.Message); continue; }
            foreach (var package in packages)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    if (ImageFiles.IsLink(package)) { Warn(package + ": link/junção não seguido."); continue; }
                    var manifest = fs.ResolveRelative(Path.GetRelativePath(fs.Root, package) + "/AppxManifest.xml").PhysicalPath;
                    if (manifest is null) continue;
                    using var reader = XmlReader.Create(manifest,
                        new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                    var xml = XDocument.Load(reader);
                    var identity = xml.Root?.Elements().FirstOrDefault(x => x.Name.LocalName == "Identity");
                    if (identity?.Attribute("Name") is not { } name) { Warn(manifest + ": identidade ausente."); continue; }
                    var app = new AppItem
                    {
                        Id = Path.GetFileName(package), Name = name.Value,
                        Kind = relative.StartsWith("Program Files") ? "AppX/MSIX" : "App do sistema",
                        Version = (string?)identity.Attribute("Version") ?? "",
                        Publisher = (string?)identity.Attribute("Publisher") ?? "",
                        Location = Path.GetRelativePath(fs.Root, package).Replace('/', '\\'),
                        Dependencies = xml.Descendants().Where(x => x.Name.LocalName == "PackageDependency")
                            .Select(x => (string?)x.Attribute("Name") ?? "").Where(x => x.Length != 0).ToList()
                    };
                    progress?.Report("Inventariando " + app.Name + "...");
                    var owner = "App: " + app.Name + " [" + app.Id + "]";
                    foreach (var file in includeFileInventory ? fs.WalkFiles(package, cancellation) : Enumerable.Empty<string>())
                    {
                        var resolved = new ResolvedImageFile(Path.GetRelativePath(fs.Root, file).Replace('/', '\\'),
                            file, "Arquivo contido na pasta do pacote; não é prova de dependência exclusiva.");
                        Add(resolved, owner, "Pasta do pacote identificado por AppxManifest.xml");
                        app.FileCount++;
                        if (files[resolved.Path].Bytes is { } bytes) app.LogicalBytes += bytes;
                    }
                    report.Applications.Add(app);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException)
                { Warn(Path.GetRelativePath(fs.Root, package) + ": " + e.Message); }
            }
        }
        report.Files = files.Values.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
        report.Applications = report.Applications.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        report.Services = report.Services.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Warn("Escopo: AppX nas pastas padrão, programas HKLM e serviços do ControlSet selecionado. Pastas InstallLocation são associações estimadas. Apps por usuário e arquivos de componentes CBS podem exigir análise adicional.");
        return report;
    }
}
