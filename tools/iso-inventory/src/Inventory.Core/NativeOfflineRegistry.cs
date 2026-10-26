using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Inventory.Core;

[SupportedOSPlatform("windows")]
internal static class NativeOfflineRegistry
{
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegLoadAppKey(string file, out SafeRegistryHandle handle,
        uint access, uint options, uint reserved);

    private static RegistryKey OpenCopy(string source, string copy)
    {
        File.Copy(source, copy);
        File.SetAttributes(copy, FileAttributes.Normal);
        foreach (var suffix in new[] { ".LOG", ".LOG1", ".LOG2" })
        {
            var log = source + suffix;
            if (File.Exists(log) && !ImageFiles.IsLink(log))
            {
                File.Copy(log, copy + suffix);
                File.SetAttributes(copy + suffix, FileAttributes.Normal);
            }
        }
        // Only the temporary copy is loaded; no hive is attached to host HKLM.
        var result = RegLoadAppKey(copy, out var handle, 0x20019, 1, 0);
        if (result != 0) { handle?.Dispose(); throw new Win32Exception(result); }
        return RegistryKey.FromHandle(handle, RegistryView.Registry64);
    }

    private static object? Value(RegistryKey? key, string name) =>
        key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    private static string Text(RegistryKey? key, string name) => Value(key, name)?.ToString() ?? "";
    private static string[] Strings(RegistryKey? key, string name) =>
        Value(key, name) is string[] list ? list : Array.Empty<string>();
    private static int Number(RegistryKey? key, string name, int fallback = -1) =>
        Value(key, name) is int value ? value : fallback;

    public static RegistrySnapshot Read(string image, CancellationToken cancellation)
    {
        var snapshot = new RegistrySnapshot();
        var temporary = Path.Combine(Path.GetTempPath(), "ISOInventory-hives-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var files = new ImageFiles(image, snapshot.Warnings.Add);
            foreach (var hive in new[] { "SOFTWARE", "SYSTEM" })
            {
                cancellation.ThrowIfCancellationRequested();
                var source = files.ResolveRelative(@"Windows\System32\config\" + hive).PhysicalPath;
                if (source is null)
                { snapshot.Warnings.Add(hive + ": hive não encontrado ou inacessível."); continue; }
                try
                {
                    using var root = OpenCopy(source, Path.Combine(temporary, hive));
                    if (hive == "SOFTWARE") ReadSoftware(root, snapshot, cancellation);
                    else ReadSystem(root, snapshot, cancellation);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception)
                { snapshot.Warnings.Add(hive + ": " + e.Message); }
            }
        }
        finally
        {
            try { Directory.Delete(temporary, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { snapshot.Warnings.Add("Cópias temporárias dos hives não puderam ser removidas: " + temporary); }
        }
        return snapshot;
    }

    private static void ReadSoftware(RegistryKey root, RegistrySnapshot snapshot, CancellationToken cancellation)
    {
        using (var version = root.OpenSubKey(@"Microsoft\Windows NT\CurrentVersion"))
            foreach (var name in new[] { "ProductName", "DisplayVersion", "CurrentBuildNumber", "UBR",
                "EditionID", "InstallationType", "SystemRoot", "BuildLabEx" })
                snapshot.Windows[name] = Text(version, name);
        foreach (var location in new[] { @"Microsoft\Windows\CurrentVersion\Uninstall",
            @"WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            using var uninstall = root.OpenSubKey(location);
            if (uninstall is null) continue;
            foreach (var name in uninstall.GetSubKeyNames())
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    using var key = uninstall.OpenSubKey(name);
                    var displayName = Text(key, "DisplayName");
                    if (displayName.Length == 0) continue;
                    snapshot.Applications.Add(new AppItem
                    {
                        Id = location + "\\" + name, Name = displayName, Kind = "Programa (registro HKLM)",
                        Version = Text(key, "DisplayVersion"), Publisher = Text(key, "Publisher"),
                        Location = Text(key, "InstallLocation"), IconPath = Text(key, "DisplayIcon"),
                        UninstallCommand = Text(key, "UninstallString")
                    });
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { snapshot.Warnings.Add("Programa " + name + ": " + e.Message); }
            }
        }
    }

    private static void ReadSystem(RegistryKey root, RegistrySnapshot snapshot, CancellationToken cancellation)
    {
        using var select = root.OpenSubKey("Select");
        var selected = Number(select, "Default", Number(select, "Current"));
        if (selected < 1)
        { snapshot.Warnings.Add("SYSTEM: ControlSet não identificado; serviços não inventariados."); return; }
        var controlSet = "ControlSet" + selected.ToString("000");
        snapshot.Windows["ControlSet"] = controlSet;
        using var services = root.OpenSubKey(controlSet + @"\Services");
        if (services is null)
        { snapshot.Warnings.Add("SYSTEM: chave Services ausente."); return; }
        foreach (var name in services.GetSubKeyNames())
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                using var key = services.OpenSubKey(name);
                using var parameters = key?.OpenSubKey("Parameters");
                var type = Number(key, "Type", 0);
                snapshot.Services.Add(new ServiceItem
                {
                    Name = name, DisplayName = Text(key, "DisplayName"), Type = type,
                    Kind = (type & 3) != 0 ? "Driver" : (type & 0x20) != 0 ? "Serviço compartilhado" : "Serviço",
                    Start = Number(key, "Start") switch { 0 => "Boot", 1 => "Sistema", 2 => "Automático",
                        3 => "Manual", 4 => "Desativado", _ => "Não informado" },
                    ImagePath = Text(key, "ImagePath"), ServiceDll = Text(parameters, "ServiceDll"),
                    Account = Text(key, "ObjectName"), Description = Text(key, "Description"),
                    Dependencies = Strings(key, "DependOnService").Concat(Strings(key, "DependOnGroup").Select(x => "Grupo: " + x)).ToList()
                });
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { snapshot.Warnings.Add("Serviço " + name + ": " + e.Message); }
        }
    }
}
