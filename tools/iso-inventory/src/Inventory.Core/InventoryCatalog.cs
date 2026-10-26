using System.Xml;
using System.Xml.Linq;

namespace Inventory.Core;

public static class InventoryCatalog
{
    public static void AddImageComponents(string image, InventoryReport report, CancellationToken cancellation,
        IProgress<string>? progress = null)
    {
        cancellation.ThrowIfCancellationRequested();
        var fs = new ImageFiles(image, message => { report.Warnings.Add(message); report.WarningCount++; },
            report.Windows.GetValueOrDefault("SystemRoot"));
        void Builtin(string id, string name, string[] candidates, string[] services)
        {
            var evidence = candidates.Select(path => fs.ResolveRelative(path))
                .Where(path => path.PhysicalPath is not null).ToList();
            var found = evidence.Select(path => path.Path).ToList();
            var registered = report.Services.Where(service => services.Contains(service.Name, StringComparer.OrdinalIgnoreCase))
                .Select(service => service.Name + " (" + service.Start + ")").ToList();
            if (found.Count == 0 && registered.Count == 0) return;
            report.Components.Add(new()
            {
                Name = name, Kind = "Componentes integrados",
                State = evidence.Any(path => File.Exists(path.PhysicalPath)) ? "Arquivo/binário presente na imagem" :
                    found.Count != 0 ? "Diretório presente; instalação não confirmada" : "Registro de serviço presente; conteúdo não confirmado",
                Details = "ID: " + id + "\r\nEvidências na imagem:\r\n" +
                    string.Join("\r\n", found.Concat(registered)) +
                    "\r\nPresença não equivale a recurso ativo, remoção suportada ou ausência de dependências."
            });
        }
        Builtin("defender", "Microsoft Defender Antivirus", new[] {
            @"Program Files\Windows Defender\MsMpEng.exe", @"Program Files\Windows Defender\MpCmdRun.exe",
            @"ProgramData\Microsoft\Windows Defender\Platform"
        }, new[] { "WinDefend", "WdNisSvc", "WdFilter" });
        Builtin("securityhealth", "Segurança do Windows", new[] {
            @"Windows\System32\SecurityHealthService.exe",
            @"Windows\SystemApps\Microsoft.Windows.SecHealthUI_cw5n1h2txyewy"
        }, new[] { "SecurityHealthService", "wscsvc" });
        Builtin("firewall", "Firewall do Windows", new[] {
            @"Windows\System32\mpssvc.dll", @"Windows\System32\FirewallAPI.dll"
        }, new[] { "MpsSvc", "BFE", "mpsdrv" });
        Builtin("bfe", "Mecanismo de Filtragem Básica (BFE)", new[] {
            @"Windows\System32\bfe.dll"
        }, new[] { "BFE" });
        Builtin("edge", "Microsoft Edge", new[] {
            @"Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"Program Files\Microsoft\Edge\Application\msedge.exe",
            @"Windows\SystemApps\Microsoft.MicrosoftEdge_8wekyb3d8bbwe\MicrosoftEdge.exe"
        }, new[] { "edgeupdate", "edgeupdatem" });
        Builtin("webview2", "Microsoft Edge WebView2 Runtime", new[] {
            @"Program Files (x86)\Microsoft\EdgeWebView\Application",
            @"Program Files\Microsoft\EdgeWebView\Application"
        }, Array.Empty<string>());
        Builtin("recovery", "Ambiente de recuperação do Windows (WinRE)", new[] {
            @"Windows\System32\Recovery\Winre.wim", @"Recovery\WindowsRE\Winre.wim"
        }, Array.Empty<string>());
        Builtin("onedrive", "Microsoft OneDrive (instalador integrado)", new[] {
            @"Windows\SysWOW64\OneDriveSetup.exe", @"Windows\System32\OneDriveSetup.exe"
        }, Array.Empty<string>());

        foreach (var location in new[] { @"Windows\servicing\Packages", @"Windows\INF", @"Windows\Setup\Scripts" })
        {
            cancellation.ThrowIfCancellationRequested();
            var directory = fs.ResolveRelative(location).PhysicalPath;
            if (directory is null || !Directory.Exists(directory)) continue;
            string[] entries;
            try { entries = Directory.GetFiles(directory); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { report.Warnings.Add(location + ": " + e.Message); report.WarningCount++; continue; }
            foreach (var path in entries)
            {
                cancellation.ThrowIfCancellationRequested();
                try { if (ImageFiles.IsLink(path)) continue; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { report.Warnings.Add(path + ": " + e.Message); report.WarningCount++; continue; }
                var extension = Path.GetExtension(path);
                if (extension.Equals(".mum", StringComparison.OrdinalIgnoreCase))
                    report.Components.Add(new() {
                        Name = Path.GetFileNameWithoutExtension(path), Kind = "Pacote CBS",
                        State = "Manifesto presente; estado de instalação não confirmado",
                        Details = "Manifesto: " + location + "\\" + Path.GetFileName(path)
                    });
                else if (location.EndsWith("INF") && extension.Equals(".inf", StringComparison.OrdinalIgnoreCase))
                    report.Components.Add(new() {
                        Name = Path.GetFileName(path), Kind = "Pacote de driver",
                        State = "INF presente; instalação/uso não confirmado",
                        Details = "Arquivo INF: " + location + "\\" + Path.GetFileName(path)
                    });
                else if (location.EndsWith("Scripts") && new[] { ".cmd", ".bat", ".ps1", ".vbs" }.Contains(extension.ToLowerInvariant()))
                    report.Components.Add(new() {
                        Name = Path.GetFileName(path), Kind = "Scripts pós-instalação",
                        State = "Script presente; efeitos futuros não determinados",
                        Details = "Caminho: " + location + "\\" + Path.GetFileName(path) + "\r\nO script não foi executado."
                    });
            }
        }

        var manifests = fs.ResolveRelative(@"Windows\WinSxS\Manifests").PhysicalPath;
        if (manifests is null || !Directory.Exists(manifests)) return;
        string[] files;
        try { files = Directory.GetFiles(manifests, "*.manifest"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { report.Warnings.Add("WinSxS: " + e.Message); report.WarningCount++; return; }
        int scanned = 0, undecoded = 0;
        foreach (var path in files)
        {
            cancellation.ThrowIfCancellationRequested();
            if (scanned++ % 250 == 0) progress?.Report("Lendo componentes WinSxS: " + scanned + "/" + files.Length);
            var name = Path.GetFileNameWithoutExtension(path);
            string detail = @"Manifesto: Windows\WinSxS\Manifests\" + Path.GetFileName(path);
            try
            {
                if (ImageFiles.IsLink(path)) continue;
                using var reader = XmlReader.Create(path, new XmlReaderSettings {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_000_000
                });
                var xml = XDocument.Load(reader);
                var identity = xml.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "assemblyIdentity");
                if (identity?.Attribute("name") is { } identityName)
                {
                    name = identityName.Value + " [" + (string?)identity.Attribute("processorArchitecture") +
                        ", " + (string?)identity.Attribute("version") + ", " + (string?)identity.Attribute("language") + "]";
                    detail += "\r\nIdentidade: " + identity.ToString(SaveOptions.DisableFormatting);
                }
                else { undecoded++; detail += "\r\nIdentidade não encontrada; nome exibido é o identificador do arquivo."; }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException)
            { undecoded++; detail += "\r\nIdentidade não decodificada; o manifesto pode ser comprimido ou inacessível."; }
            report.Components.Add(new() {
                Name = name, Kind = "Componentes WinSxS",
                State = "Manifesto presente; ativação/dependências não confirmadas", Details = detail
            });
        }
        if (undecoded != 0)
        {
            report.Warnings.Add(undecoded + " manifestos WinSxS sem identidade decodificada; exibidos pelo identificador do arquivo.");
            report.WarningCount++;
        }
    }

    public static List<CatalogItem> Build(InventoryReport report)
    {
        var items = new List<CatalogItem>();
        foreach (var app in report.Applications)
            items.Add(new() {
                Id = "app:" + app.Kind + ":" + app.Id,
                Category = app.Kind.StartsWith("Programa") ? "Programas" :
                    app.Kind.Contains("sistema") ? "Aplicativos do sistema" : "Aplicativos AppX / provisionados",
                Name = app.Name, State = app.Kind,
                Details = "ID: " + app.Id + "\r\nVersão: " + app.Version + "\r\nFabricante: " + app.Publisher +
                    "\r\nLocal: " + app.Location + "\r\nDependências declaradas: " + string.Join(", ", app.Dependencies)
            });
        foreach (var service in report.Services)
            items.Add(new() {
                Id = "service:" + service.Name, Category = (service.Type & 3) != 0 ? "Drivers (serviços)" : "Serviços",
                Name = PresetReference.FriendlyServiceName(service), State = service.Start,
                Details = service.DisplayName + "\r\n" + service.Description +
                    "\r\nImagePath: " + service.ImagePath + "\r\nServiceDll: " + service.ServiceDll +
                    "\r\nConta: " + service.Account + "\r\nControlSets: " + string.Join(", ", service.ControlSets) +
                    "\r\nControlSet de inicialização: " + service.IsPrimaryControlSet +
                    "\r\nDependências: " + string.Join(", ", service.Dependencies)
            });
        // Native DISM state takes precedence over a filesystem manifest fallback.
        foreach (var component in report.Components.GroupBy(c => c.Kind + "\0" + c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last()))
            items.Add(new() {
                Id = "component:" + component.Kind + ":" + component.Name, Category = component.Kind,
                Name = component.Name, State = component.State, Details = component.Details
            });
        items.AddRange(report.PresetReferences);
        return items.GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase).Select(group => group.Last())
            .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
