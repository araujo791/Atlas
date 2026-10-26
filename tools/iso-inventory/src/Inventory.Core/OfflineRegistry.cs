using DiscUtils.Registry;
using Microsoft.Win32;
using System.Text.RegularExpressions;
using HiveKey = DiscUtils.Registry.RegistryKey;

namespace Inventory.Core;

// DiscUtils' net5 registry types carry Windows annotations because of their ACL
// creation APIs. This reader only uses managed stream reads; binary REGF tests
// verify those paths on Linux. The native fallback remains OS-guarded.
#pragma warning disable CA1416
public static class OfflineRegistry
{
    private static object? Value(HiveKey? key, string name) =>
        key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    private static string Text(HiveKey? key, string name) => Value(key, name)?.ToString() ?? "";
    private static string[] Strings(HiveKey? key, string name) => Value(key, name) switch {
        string[] values => values, string value when value.Length != 0 => new[] { value }, _ => Array.Empty<string>()
    };
    private static int Number(HiveKey? key, string name, int fallback = -1) => Value(key, name) switch {
        int value => value, uint value when value <= int.MaxValue => (int)value,
        long value when value >= 0 && value <= int.MaxValue => (int)value, _ => fallback
    };

    public static RegistrySnapshot Read(string image, CancellationToken cancellation)
    {
        var snapshot = new RegistrySnapshot();
        var fs = new ImageFiles(image, snapshot.Warnings.Add);
        foreach (var name in new[] { "SOFTWARE", "SYSTEM" })
        {
            cancellation.ThrowIfCancellationRequested();
            var path = fs.ResolveRelative(@"Windows\System32\config\" + name).PhysicalPath;
            if (path is null) { snapshot.Warnings.Add(name + ": hive ausente ou inacessível."); continue; }
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var hive = new DiscUtils.Registry.RegistryHive(stream);
                if (name == "SOFTWARE") ReadSoftware(hive.Root, snapshot, cancellation);
                else ReadSystem(hive.Root, snapshot, cancellation);
                snapshot.Windows[name + "Reader"] = "DiscUtils, leitura direta";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
                InvalidOperationException or NotSupportedException or NotImplementedException or IndexOutOfRangeException)
            { snapshot.Warnings.Add(name + ": leitura direta falhou: " + error.Message); }
        }
        if (OperatingSystem.IsWindows() &&
            (snapshot.Services.Count == 0 || !snapshot.Windows.ContainsKey("CurrentBuildNumber")))
        {
            var native = NativeOfflineRegistry.Read(image, cancellation);
            if (snapshot.Services.Count == 0 && native.Services.Count != 0)
            {
                snapshot.Services = native.Services;
                snapshot.Windows["SYSTEMReader"] = "API nativa, cópia temporária";
            }
            if (!snapshot.Windows.ContainsKey("CurrentBuildNumber"))
            {
                foreach (var pair in native.Windows) snapshot.Windows[pair.Key] = pair.Value;
                snapshot.Applications = native.Applications;
            }
            snapshot.Warnings.AddRange(native.Warnings);
        }
        if (snapshot.Services.Count == 0)
            snapshot.Warnings.Add("Nenhum serviço pôde ser lido do SYSTEM. Isso não significa que a imagem não possui serviços.");
        return snapshot;
    }

    private static void ReadSoftware(HiveKey root, RegistrySnapshot snapshot, CancellationToken cancellation)
    {
        var version = root.OpenSubKey(@"Microsoft\Windows NT\CurrentVersion");
        foreach (var name in new[] { "ProductName", "DisplayVersion", "CurrentBuildNumber", "UBR",
            "EditionID", "InstallationType", "SystemRoot", "BuildLabEx" })
            if (Value(version, name) is not null) snapshot.Windows[name] = Text(version, name);
        foreach (var path in new[] { @"Microsoft\Windows\CurrentVersion\Uninstall",
            @"WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            var uninstall = root.OpenSubKey(path);
            if (uninstall is null) continue;
            foreach (var name in uninstall.GetSubKeyNames())
            {
                cancellation.ThrowIfCancellationRequested();
                var key = uninstall.OpenSubKey(name);
                if (Text(key, "DisplayName").Length == 0) continue;
                snapshot.Applications.Add(new() {
                    Id = path + "\\" + name, Name = Text(key, "DisplayName"), Kind = "Programa (registro HKLM)",
                    Version = Text(key, "DisplayVersion"), Publisher = Text(key, "Publisher"),
                    Location = Text(key, "InstallLocation"), IconPath = Text(key, "DisplayIcon"),
                    UninstallCommand = Text(key, "UninstallString")
                });
            }
        }
    }

    private static void ReadSystem(HiveKey root, RegistrySnapshot snapshot, CancellationToken cancellation)
    {
        var controlSets = root.GetSubKeyNames().Where(name => Regex.IsMatch(name, "^ControlSet[0-9]{3}$",
            RegexOptions.IgnoreCase)).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        var select = root.OpenSubKey("Select");
        string? primary = null;
        foreach (var value in new[] { Number(select, "Default"), Number(select, "Current") })
        {
            var candidate = value > 0 ? "ControlSet" + value.ToString("000") : "";
            primary = controlSets.FirstOrDefault(name => name.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            if (primary is not null) break;
        }
        primary ??= controlSets.FirstOrDefault();
        if (primary is null) { snapshot.Warnings.Add("SYSTEM: nenhum ControlSet real encontrado."); return; }
        snapshot.Windows["ControlSet"] = primary;
        snapshot.Windows["ControlSetsFound"] = string.Join(", ", controlSets);
        var collected = new Dictionary<string, ServiceItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var controlSet in new[] { primary }.Concat(controlSets.Where(name => name != primary)))
        {
            var services = root.OpenSubKey(controlSet + @"\Services");
            if (services is null) continue;
            foreach (var name in services.GetSubKeyNames())
            {
                cancellation.ThrowIfCancellationRequested();
                if (collected.TryGetValue(name, out var existing))
                { existing.ControlSets.Add(controlSet); continue; }
                var key = services.OpenSubKey(name);
                var parameters = key?.OpenSubKey("Parameters");
                int type = Number(key, "Type", 0);
                collected[name] = new() {
                    Name = name, DisplayName = Text(key, "DisplayName"), Type = type,
                    Kind = (type & 3) != 0 ? "Driver" : (type & 0x20) != 0 ? "Serviço compartilhado" : "Serviço",
                    Start = Number(key, "Start") switch { 0 => "Boot", 1 => "Sistema", 2 => "Automático",
                        3 => "Manual", 4 => "Desativado", _ => "Não informado" },
                    ImagePath = Text(key, "ImagePath"),
                    ServiceDll = Text(parameters, "ServiceDll") is { Length: > 0 } dll ? dll : Text(key, "ServiceDll"),
                    Account = Text(key, "ObjectName"), Description = Text(key, "Description"),
                    Dependencies = Strings(key, "DependOnService").Concat(Strings(key, "DependOnGroup").Select(group => "Grupo: " + group)).ToList(),
                    ControlSets = new() { controlSet }, IsPrimaryControlSet = controlSet == primary
                };
            }
        }
        snapshot.Services = collected.Values.ToList();
    }
}
