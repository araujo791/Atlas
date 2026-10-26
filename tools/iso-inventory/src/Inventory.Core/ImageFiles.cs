using System.Text.RegularExpressions;

namespace Inventory.Core;

public sealed record ResolvedImageFile(string Path, string? PhysicalPath, string Note);

public sealed class ImageFiles
{
    public string Root { get; }
    private readonly Action<string> warn;
    private readonly Dictionary<string, Dictionary<string, string>> directories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SystemRoot"] = @"C:\Windows", ["windir"] = @"C:\Windows",
        ["SystemDrive"] = "C:", ["ProgramFiles"] = @"C:\Program Files",
        ["ProgramFiles(x86)"] = @"C:\Program Files (x86)", ["ProgramData"] = @"C:\ProgramData"
    };

    public ImageFiles(string root, Action<string> warn, string? systemRoot = null)
    {
        Root = Path.GetFullPath(root);
        this.warn = warn;
        if (systemRoot is not null && Regex.IsMatch(systemRoot, @"^[A-Za-z]:\\[^%]+$"))
        {
            variables["SystemRoot"] = variables["windir"] = systemRoot.TrimEnd('\\');
            variables["SystemDrive"] = systemRoot[..2];
            variables["ProgramFiles"] = systemRoot[..2] + @"\Program Files";
            variables["ProgramFiles(x86)"] = systemRoot[..2] + @"\Program Files (x86)";
            variables["ProgramData"] = systemRoot[..2] + @"\ProgramData";
        }
    }

    public static bool IsLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) return false;
        // WOF/WIM compression is a data reparse point, not a link to another path.
        // Blocking every reparse point can hide real Windows image content.
        return !OperatingSystem.IsWindows() || WindowsReparse.IsNameRedirect(path);
    }

    public ResolvedImageFile ResolveWindows(string input)
    {
        var raw = input.Trim().Trim('"').Replace('/', '\\');
        if (raw.StartsWith(@"\??\") || raw.StartsWith(@"\\?\"))
            raw = raw[4..];
        if (raw.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
            raw = variables["SystemRoot"] + raw[11..];
        raw = Regex.Replace(raw, "%([^%]+)%", m =>
            variables.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);
        if (raw.Contains('%'))
            return new(input, null, "Variável offline desconhecida; não expandida no Windows do computador.");
        if (raw.StartsWith(@"\\"))
            return new(input, null, "Caminho de rede não consultado.");
        if (Regex.IsMatch(raw, @"^[A-Za-z]:\\"))
        {
            if (!raw[..2].Equals(variables["SystemDrive"], StringComparison.OrdinalIgnoreCase))
                return new(input, null, "Outra unidade: não é possível associá-la à raiz desta imagem.");
            raw = raw[3..];
        }
        else
        {
            raw = raw.TrimStart('\\');
            if (raw.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase))
                raw = variables["SystemRoot"][3..] + "\\" + raw;
            else if (!raw.Contains('\\'))
                raw = variables["SystemRoot"][3..] + @"\System32\" + raw;
        }
        return ResolveRelative(raw);
    }

    public ResolvedImageFile ResolveRelative(string relative)
    {
        var parts = relative.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(p => p == ".." || p.Contains(':') || p.Contains('\0') || p.Contains('"')))
            return new(relative, null, "Caminho inválido ou fora da imagem.");
        parts = parts.Where(p => p != ".").ToArray();
        string logical = string.Join("\\", parts);
        string current = Root;
        try
        {
            foreach (var part in parts)
            {
                if (!Directory.Exists(current))
                    return new(logical, null, "Não encontrado na imagem.");
                if (!directories.TryGetValue(current, out var entries))
                {
                    entries = new(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                        if (!entries.TryAdd(Path.GetFileName(entry), entry))
                            throw new IOException("Nomes ambíguos por maiúsculas/minúsculas: " + current);
                    directories[current] = entries;
                }
                if (!entries.TryGetValue(part, out var found))
                    return new(logical, null, "Não encontrado na imagem.");
                if (IsLink(found))
                    return new(logical, null, "Link/junção não seguido.");
                current = found;
            }
            return new(logical, current, "Encontrado na imagem.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warn(logical + ": " + e.Message);
            return new(logical, null, "Sem acesso.");
        }
    }

    public IEnumerable<string> WalkFiles(string directory, CancellationToken cancellation)
    {
        var stack = new Stack<string>();
        stack.Push(directory);
        while (stack.TryPop(out var current))
        {
            cancellation.ThrowIfCancellationRequested();
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(current); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { warn(Path.GetRelativePath(Root, current) + ": " + e.Message); continue; }
            foreach (var path in entries)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    if (IsLink(path))
                    { warn(Path.GetRelativePath(Root, path) + ": link/junção não seguido."); continue; }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { warn(path + ": " + e.Message); continue; }
                if (Directory.Exists(path)) stack.Push(path);
                else if (File.Exists(path)) yield return path;
            }
        }
    }

    public static (string File, string Evidence) ExecutableFromCommand(string command)
    {
        var text = command.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? (text[1..end], "ImagePath delimitado por aspas") :
                ("", "ImagePath com aspas incompletas");
        }
        var match = Regex.Match(text, @"^(.+?\.(?:exe|sys|dll))(?=\s|$)", RegexOptions.IgnoreCase);
        if (match.Success)
            return (match.Groups[1].Value, match.Groups[1].Value.Contains(' ') ?
                "ImagePath sem aspas: associação estimada, execução pode ser ambígua" : "ImagePath");
        return (text.Split(' ', 2)[0], "ImagePath sem extensão reconhecida: estimado");
    }
}
