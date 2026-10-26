using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace Inventory.Core;

[SupportedOSPlatform("windows")]
public sealed class WindowsImageSession : IAsyncDisposable
{
    public static Action<string>? CommandLog { get; set; }
    public string ImageRoot { get; private set; } = "";
    public List<string> CleanupWarnings { get; } = new();
    private string? workspace;
    private string? mountedIso;
    private string? mountDirectory;
    private bool mountAttempted;

    private sealed record CommandResult(int ExitCode, string Output, string Error);
    private static string SystemTool(string relative) => Path.Combine(Environment.SystemDirectory, relative);
    private static async Task<string> ReadOutput(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return NativeOutput.Decode(buffer.ToArray());
    }
    private static async Task<CommandResult> Run(string executable, IEnumerable<string> args,
        Dictionary<string, string>? variables = null)
    {
        var arguments = args.ToArray();
        CommandLog?.Invoke("Iniciando " + executable + " " + string.Join(" ", arguments));
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        if (variables is not null)
            foreach (var (key, value) in variables) info.Environment[key] = value;
        using var process = Process.Start(info) ?? throw new IOException("Não foi possível iniciar " + executable);
        var output = ReadOutput(process.StandardOutput.BaseStream);
        var error = ReadOutput(process.StandardError.BaseStream);
        // Do not kill DISM mid-mount. Cancellation takes effect after the native
        // operation finishes, and the session can then discard its own mount.
        await process.WaitForExitAsync();
        var result = new CommandResult(process.ExitCode, await output, await error);
        CommandLog?.Invoke("Processo encerrado: " + process.ExitCode);
        if (result.Output.Length != 0) CommandLog?.Invoke(result.Output[..Math.Min(result.Output.Length, 16000)]);
        if (result.Error.Length != 0) CommandLog?.Invoke(result.Error[..Math.Min(result.Error.Length, 16000)]);
        return result;
    }

    private static async Task<string> Dism(params string[] args)
    {
        var result = await Run(SystemTool("dism.exe"), new[] { "/English" }.Concat(args));
        if (result.ExitCode != 0)
            throw new IOException("DISM (" + result.ExitCode + "): " + result.Output + result.Error);
        return result.Output;
    }

    private async Task<string> LocateImage(string input)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("A montagem de ISO exige Windows.");
        input = Path.GetFullPath(input);
        if (Directory.Exists(input))
        {
            var windows = new ImageFiles(input, _ => { }).ResolveRelative(@"Windows\System32");
            if (windows.PhysicalPath is not null && Directory.Exists(windows.PhysicalPath))
            {
                var live = Directory.GetParent(Environment.GetFolderPath(Environment.SpecialFolder.Windows))?.FullName;
                if (Path.TrimEndingDirectorySeparator(input).Equals(live, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Escolha uma imagem offline, não a instalação Windows em execução.");
                ImageRoot = input; return "";
            }
        }
        if (Path.GetExtension(input).Equals(".iso", StringComparison.OrdinalIgnoreCase) && File.Exists(input))
        {
            const string script = """
                $ErrorActionPreference='Stop'
                [Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false)
                $owned=$false
                try {
                  $disk=Get-DiskImage -ImagePath $env:ISO_INVENTORY_SOURCE -ErrorAction SilentlyContinue
                  if (!$disk -or !$disk.Attached) {
                    $disk=Mount-DiskImage -ImagePath $env:ISO_INVENTORY_SOURCE -Access ReadOnly -PassThru
                    $owned=$true
                  }
                  $volume=$null
                  for ($attempt=0; $attempt -lt 20; $attempt++) {
                    $volume=$disk | Get-Volume | Where-Object DriveLetter | Select-Object -First 1
                    if ($volume) { break }
                    Start-Sleep -Milliseconds 500
                  }
                  if (!$volume) { throw 'ISO montada sem letra de unidade acessível.' }
                  @{Root=($volume.DriveLetter+':\'); Owned=$owned} | ConvertTo-Json -Compress
                } catch {
                  if ($owned) { Dismount-DiskImage -ImagePath $env:ISO_INVENTORY_SOURCE -ErrorAction SilentlyContinue | Out-Null }
                  throw
                }
                """;
            var result = await Run(SystemTool(@"WindowsPowerShell\v1.0\powershell.exe"),
                new[] { "-NoProfile", "-NonInteractive", "-Command", script },
                new() { ["ISO_INVENTORY_SOURCE"] = input });
            if (result.ExitCode != 0) throw new IOException("Não foi possível montar a ISO: " + result.Error);
            using var json = JsonDocument.Parse(result.Output);
            if (json.RootElement.GetProperty("Owned").GetBoolean()) mountedIso = input;
            input = json.RootElement.GetProperty("Root").GetString() ?? throw new IOException("Unidade ISO ausente.");
        }
        if (File.Exists(input) && new[] { ".wim", ".esd" }.Contains(Path.GetExtension(input).ToLowerInvariant()))
            return input;
        foreach (var file in new[] { Path.Combine("sources", "install.wim"), Path.Combine("sources", "install.esd") })
        {
            var candidate = new ImageFiles(input, _ => { }).ResolveRelative(file).PhysicalPath;
            if (candidate is not null && File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("Não encontrei sources/install.wim ou install.esd. Imagens divididas em SWM ainda não são suportadas.");
    }

    public static async Task<List<ImageEdition>> ListEditions(string input, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        await using var session = new WindowsImageSession();
        var source = await session.LocateImage(input);
        cancellation.ThrowIfCancellationRequested();
        if (source.Length == 0) return new() { new(1, "Pasta Windows offline") };
        var output = await Dism("/Get-WimInfo", "/WimFile:" + source);
        cancellation.ThrowIfCancellationRequested();
        return DismInventory.Editions(output);
    }

    public static async Task<WindowsImageSession> Open(string input, int index,
        CancellationToken cancellation, IProgress<string>? progress = null)
    {
        if (index < 1) throw new ArgumentOutOfRangeException(nameof(index));
        var session = new WindowsImageSession();
        try
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report("Abrindo origem somente para leitura...");
            var source = await session.LocateImage(input);
            cancellation.ThrowIfCancellationRequested();
            if (source.Length == 0) return session;
            var editions = DismInventory.Editions(await Dism("/Get-WimInfo", "/WimFile:" + source));
            cancellation.ThrowIfCancellationRequested();
            if (!editions.Any(x => x.Index == index)) throw new IOException("Edição não encontrada na imagem.");
            session.workspace = Path.Combine(Path.GetTempPath(), "ISOInventory-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(session.workspace);
            if (Path.GetExtension(source).Equals(".esd", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report("Exportando a edição ESD para WIM temporário (pode demorar)...");
                var exported = Path.Combine(session.workspace, "edition.wim");
                await Dism("/Export-Image", "/SourceImageFile:" + source, "/SourceIndex:" + index,
                    "/DestinationImageFile:" + exported, "/Compress:fast", "/CheckIntegrity");
                source = exported;
                index = 1;
            }
            cancellation.ThrowIfCancellationRequested();
            session.mountDirectory = Path.Combine(session.workspace, "mount");
            Directory.CreateDirectory(session.mountDirectory);
            session.mountAttempted = true;
            progress?.Report("Montando a edição WIM com /ReadOnly...");
            await Dism("/Mount-Image", "/ImageFile:" + source, "/Index:" + index,
                "/MountDir:" + session.mountDirectory, "/ReadOnly");
            session.ImageRoot = session.mountDirectory;
            cancellation.ThrowIfCancellationRequested();
            return session;
        }
        catch (OperationCanceledException)
        {
            await session.DisposeAsync();
            foreach (var warning in session.CleanupWarnings) progress?.Report(warning);
            throw;
        }
        catch (Exception error)
        {
            await session.DisposeAsync();
            throw new IOException(error.Message + (session.CleanupWarnings.Count == 0 ? "" :
                Environment.NewLine + string.Join(Environment.NewLine, session.CleanupWarnings)), error);
        }
    }

    public async Task<List<ComponentItem>> ReadComponents(CancellationToken cancellation, IProgress<string>? progress)
    {
        var items = new List<ComponentItem>();
        foreach (var query in new[] {
            (new[] { "/Get-Features" }, "Feature Name", "Recursos opcionais"),
            (new[] { "/Get-Capabilities" }, "Capability Identity", "Recursos sob demanda (Capabilities)"),
            (new[] { "/Get-Packages" }, "Package Identity", "Pacote CBS"),
            (new[] { "/Get-Drivers", "/All" }, "Published Name", "Pacote de driver")
        })
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Report("Consultando " + query.Item3 + "...");
            try
            {
                var entries = DismInventory.Components(
                    await Dism(new[] { "/Image:" + ImageRoot }.Concat(query.Item1).Append("/Format:List").ToArray()), query.Item2, query.Item3);
                items.AddRange(entries);
                if (entries.Count == 0) CleanupWarnings.Add(query.Item3 + ": nenhum registro retornado; inventário pode estar vazio ou o formato não foi reconhecido.");
            }
            catch (Exception e) when (e is IOException or FormatException)
            { CleanupWarnings.Add(query.Item3 + " não inventariado: " + e.Message); }
        }
        return items;
    }

    public async Task<List<AppItem>> ReadProvisionedApps(CancellationToken cancellation, IProgress<string>? progress)
    {
        cancellation.ThrowIfCancellationRequested();
        progress?.Report("Consultando os AppX provisionados pelo DISM...");
        try
        {
            var apps = DismInventory.ProvisionedApps(await Dism("/Image:" + ImageRoot, "/Get-ProvisionedAppxPackages"));
            if (apps.Count == 0) CleanupWarnings.Add("Nenhum AppX provisionado foi retornado pelo DISM.");
            return apps;
        }
        catch (Exception e) when (e is IOException or FormatException)
        { CleanupWarnings.Add("AppX provisionados não inventariados: " + e.Message); return new(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (mountAttempted && mountDirectory is not null)
        {
            try { await Dism("/Unmount-Image", "/MountDir:" + mountDirectory, "/Discard"); mountAttempted = false; }
            catch (Exception e) when (e is IOException or Win32Exception or InvalidOperationException)
            { CleanupWarnings.Add("Montagem temporária preservada para recuperação em " + mountDirectory + ": " + e.Message); }
        }
        if (!mountAttempted && mountedIso is not null)
        {
            const string script = "$ErrorActionPreference='Stop'; Dismount-DiskImage -ImagePath $env:ISO_INVENTORY_SOURCE | Out-Null";
            try
            {
                var result = await Run(SystemTool(@"WindowsPowerShell\v1.0\powershell.exe"),
                    new[] { "-NoProfile", "-NonInteractive", "-Command", script },
                    new() { ["ISO_INVENTORY_SOURCE"] = mountedIso });
                if (result.ExitCode == 0) mountedIso = null;
                else CleanupWarnings.Add("A ISO permanece montada: " + result.Error);
            }
            catch (Exception e) when (e is IOException or Win32Exception or InvalidOperationException)
            { CleanupWarnings.Add("A ISO permanece montada: " + e.Message); }
        }
        if (!mountAttempted && workspace is not null)
        {
            try { Directory.Delete(workspace, true); workspace = null; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { CleanupWarnings.Add("Pasta temporária preservada: " + workspace + ": " + e.Message); }
        }
    }
}
