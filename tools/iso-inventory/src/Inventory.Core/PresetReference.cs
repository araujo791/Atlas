using System.Text.Json;
using System.Text.RegularExpressions;

namespace Inventory.Core;

public sealed class PresetReferenceData
{
    public string SourceSha256 { get; set; } = "";
    public List<PresetEntry> Entries { get; set; } = new();
}
public sealed class PresetEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public static class PresetReference
{
    private static readonly Lazy<PresetReferenceData> source = new(() =>
    {
        using var stream = typeof(PresetReference).Assembly.GetManifestResourceStream("Inventory.Core.NtlitePresetReference.json")
            ?? throw new IOException("Recurso de referência do preset não foi incluído no aplicativo.");
        return JsonSerializer.Deserialize<PresetReferenceData>(stream) ?? throw new IOException("Preset de referência inválido.");
    });
    public static PresetReferenceData Data => source.Value;
    private static readonly Dictionary<string, string[]> serviceAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aarsvc"] = new[] { "AarSvc" }, ["alg"] = new[] { "ALG" }, ["axinstall"] = new[] { "AxInstSV" },
        ["asimov"] = new[] { "DiagTrack" }, ["biometricservice"] = new[] { "WbioSrvc" },
        ["cellulartime"] = new[] { "autotimesvc" }, ["clipboardusersvc"] = new[] { "cbdhsvc" },
        ["compluseventsystem"] = new[] { "EventSystem" }, ["complussysapp"] = new[] { "COMSysApp" },
        ["dashost"] = new[] { "DeviceAssociationService" }, ["desktopactivitymoderator"] = new[] { "dam" },
        ["diagnostics"] = new[] { "DPS", "WdiServiceHost", "WdiSystemHost" },
        ["domainclientssvc"] = new[] { "PolicyAgent" }, ["eaphost"] = new[] { "EapHost" },
        ["enterpriseclientsync"] = new[] { "workfolderssvc" }, ["errorreporting"] = new[] { "WerSvc" },
        ["fdphost"] = new[] { "fdPHost" }, ["filehistory"] = new[] { "fhsvc" },
        ["gameinputsvc"] = new[] { "GameInputSvc" }, ["graphicsperfsvc"] = new[] { "GraphicsPerfSvc" },
        ["mediaplayernetworksharing"] = new[] { "WMPNetworkSvc" }, ["messagingsvc"] = new[] { "MessagingService" },
        ["msdtc"] = new[] { "MSDTC" }, ["ncb"] = new[] { "NcbService" }, ["ncdauto"] = new[] { "NcdAutoSetup" },
        ["ndu"] = new[] { "Ndu" }, ["nettopology"] = new[] { "lltdsvc" }, ["offlinefiles"] = new[] { "CscService" },
        ["phonesvc"] = new[] { "PhoneSvc" }, ["pimindexmaintenancesvc"] = new[] { "PimIndexMaintenanceSvc" },
        ["pla"] = new[] { "pla" }, ["pushnotificationssvc"] = new[] { "WpnService" },
        ["pushtoinstall"] = new[] { "PushToInstall" }, ["rasauto"] = new[] { "RasAuto" },
        ["rasmans"] = new[] { "RasMan" }, ["remoteregistry"] = new[] { "RemoteRegistry" },
        ["retaildemo"] = new[] { "RetailDemo" }, ["rpclocator"] = new[] { "RpcLocator" },
        ["securitycenter"] = new[] { "wscsvc" }, ["sens"] = new[] { "SENS" }, ["sharedaccess"] = new[] { "SharedAccess" },
        ["srumon"] = new[] { "DusmSvc" }, ["trkwks"] = new[] { "TrkWks" },
        ["troubleshootingsvc"] = new[] { "TroubleshootingSvc" }, ["userchoiceprotection"] = new[] { "UCPD" },
        ["userdatasvc"] = new[] { "UserDataSvc" }, ["vss"] = new[] { "VSS", "swprv" },
        ["waasassessment"] = new[] { "WaaSMedicSvc" }, ["wallet"] = new[] { "WalletService" },
        ["warpjitsvc"] = new[] { "WarpJITSvc" }, ["wcn"] = new[] { "wcncsvc" },
        ["webclient"] = new[] { "WebClient" }, ["webthreatdefense"] = new[] { "webthreatdefsvc", "webthreatdefusersvc" },
        ["wfds"] = new[] { "WFDSConMgrSvc" }, ["winrm"] = new[] { "WinRM" },
        ["wiredautoconfig"] = new[] { "dot3svc" }, ["wpdbusenum"] = new[] { "WPDBusEnum" }, ["wwanautoconfig"] = new[] { "WwanSvc" }
    };
    private static readonly Dictionary<string, string[]> appAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["calculator"] = new[] { "Microsoft.WindowsCalculator" }, ["bingsearch"] = new[] { "Microsoft.BingSearch" },
        ["copilotpwa"] = new[] { "Microsoft.Copilot" }, ["outlookpwa"] = new[] { "Microsoft.OutlookForWindows" },
        ["microsoft.sechealthui"] = new[] { "Microsoft.SecHealthUI", "Microsoft.Windows.SecHealthUI" },
        ["widgetsplatformruntimeapp"] = new[] { "Microsoft.WidgetsPlatformRuntime" }
    };
    private static readonly Dictionary<string, string[]> fileRules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["accessibility_magnifier"] = new[] { @"Windows\System32\Magnify.exe" },
        ["accessibility_narrator"] = new[] { @"Windows\System32\Narrator.exe" },
        ["charmap"] = new[] { @"Windows\System32\charmap.exe" },
        ["eudcedit"] = new[] { @"Windows\System32\eudcedit.exe" },
        ["osk_acc"] = new[] { @"Windows\System32\osk.exe" },
        ["stepsrecorder"] = new[] { @"Windows\System32\psr.exe" },
        ["winsat"] = new[] { @"Windows\System32\WinSAT.exe" },
        ["perfmon"] = new[] { @"Windows\System32\perfmon.exe" },
        ["regedit32"] = new[] { @"Windows\SysWOW64\regedit.exe" },
        ["wfmmc32"] = new[] { @"Windows\SysWOW64\wf.msc" },
        ["isoburn"] = new[] { @"Windows\System32\isoburn.exe" },
        ["memorydiagnostic"] = new[] { @"Windows\System32\MdSched.exe" },
        ["systemrestore"] = new[] { @"Windows\System32\rstrui.exe" },
        ["systemreset"] = new[] { @"Windows\System32\systemreset.exe" },
        ["winre"] = new[] { @"Windows\System32\Recovery\Winre.wim" },
        ["winresume"] = new[] { @"Windows\System32\winresume.exe" },
        ["zipfolder"] = new[] { @"Windows\System32\zipfldr.dll" },
        ["directml"] = new[] { @"Windows\System32\DirectML.dll" }
    };
    private static readonly Lazy<Dictionary<string, string>> serviceLabels = new(() =>
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Data.Entries)
            foreach (var alias in serviceAliases.GetValueOrDefault(entry.Id, new[] { entry.Id }))
                if (entry.Name != entry.Id) labels.TryAdd(alias, entry.Name);
        return labels;
    });
    private static string TemplateName(string name) =>
        Regex.Replace(name, "_[0-9a-f]+$", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string FriendlyServiceName(ServiceItem service)
    {
        var known = service.Name.ToLowerInvariant() switch {
            "windefend" => "Microsoft Defender Antivirus", "wdnissvc" => "Inspeção de rede do Microsoft Defender",
            "mpssvc" => "Firewall do Windows", "bfe" => "Mecanismo de Filtragem Básica (BFE)",
            "mpsdrv" => "Driver do Firewall do Windows", "edgeupdate" => "Atualização do Microsoft Edge",
            "edgeupdatem" => "Atualização do Microsoft Edge (manual)", _ => ""
        };
        if (known.Length != 0) return known + " (" + service.Name + ")";
        if (service.DisplayName.Length != 0 && !service.DisplayName.StartsWith('@'))
            return service.DisplayName + " (" + service.Name + ")";
        if (serviceLabels.Value.TryGetValue(service.Name, out var label) ||
            serviceLabels.Value.TryGetValue(TemplateName(service.Name), out label))
            return label + " (" + service.Name + ")";
        return service.Name;
    }

    public static List<CatalogItem> Build(string image, InventoryReport report)
    {
        var fs = new ImageFiles(image, _ => { }, report.Windows.GetValueOrDefault("SystemRoot"));
        var serviceIndex = report.Services.SelectMany(service => new[] { service.Name, TemplateName(service.Name) }
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(name => (Name: name, Service: service)))
            .ToLookup(pair => pair.Name, pair => pair.Service, StringComparer.OrdinalIgnoreCase);
        var appIndex = report.Applications.SelectMany(app => new[] { app.Name, app.Id.Split('_')[0] }
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(name => (Name: name, App: app)))
            .ToLookup(pair => pair.Name, pair => pair.App, StringComparer.OrdinalIgnoreCase);
        var result = new List<CatalogItem>();
        foreach (var entry in Data.Entries)
        {
            var evidence = new List<string>();
            var aliases = serviceAliases.GetValueOrDefault(entry.Id, new[] { entry.Id });
            var services = aliases.SelectMany(alias => serviceIndex[alias]).DistinctBy(service => service.Name).ToList();
            evidence.AddRange(services.Select(service => "Registro de serviço: " + service.Name));
            var apps = appAliases.GetValueOrDefault(entry.Id, new[] { entry.Id });
            var matches = apps.SelectMany(name => appIndex[name]).DistinctBy(app => app.Id).ToList();
            evidence.AddRange(matches.Select(app => "Aplicativo identificado: " + app.Name));
            string state = services.Count != 0 ? "Registro de serviço identificado" :
                matches.Count != 0 ? "Aplicativo identificado" : "NÃO VERIFICADO nesta imagem";
            var paths = fileRules.GetValueOrDefault(entry.Id, Array.Empty<string>()).ToList();
            if (entry.Id.StartsWith("driver_", StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(entry.Id[7..], @"^[A-Za-z0-9_.-]+\.inf$"))
                paths.Add(@"Windows\INF\" + entry.Id[7..]);
            foreach (var path in paths)
            {
                var found = fs.ResolveRelative(path);
                if (found.PhysicalPath is null || !File.Exists(found.PhysicalPath)) continue;
                evidence.Add("Arquivo de referência: " + found.Path);
                if (state.StartsWith("NÃO")) state = "Evidência parcial por arquivo";
            }
            result.Add(new() {
                Id = "ntlite-reference:" + entry.Id, Name = entry.Name + " [" + entry.Id + "]",
                Category = "Preset NTLite — referência", State = state, IsReference = true,
                Details = "ID do preset: " + entry.Id + "\r\nOrigem: XML fornecido pelo usuário, seção RemoveComponents.\r\n" +
                    string.Join("\r\n", evidence) +
                    "\r\nReferência de comparação. Não comprova que o componente inteiro está instalado ou seja removível. " +
                    "NÃO VERIFICADO não significa ausente. Marcações deste grupo são planejamento de comparação, não remoções implementadas."
            });
        }
        return result;
    }
}
