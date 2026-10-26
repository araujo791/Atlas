namespace Inventory.Core;

public sealed record ImageEdition(int Index, string Name)
{
    public override string ToString() => Index + " — " + Name;
}

public static class DismInventory
{
    public static List<AppItem> ProvisionedApps(string text)
    {
        var apps = new List<AppItem>();
        AppItem? app = null;
        foreach (var line in text.Split('\n'))
        {
            var pair = line.Split(':', 2);
            if (pair.Length != 2) continue;
            var key = pair[0].Trim();
            var value = pair[1].Trim();
            if (key == "DisplayName")
            {
                if (app is not null && app.Id.Length == 0) throw new FormatException("Registro de AppX incompleto.");
                app = new() { Name = value, Kind = "AppX provisionado (DISM)" };
                apps.Add(app);
            }
            else if (app is not null)
            {
                if (key == "Version") app.Version = value;
                if (key == "PackageName") app.Id = value;
            }
        }
        if (!text.Contains("The operation completed successfully.", StringComparison.Ordinal) ||
            apps.Any(x => x.Name.Length == 0 || x.Id.Length == 0))
            throw new FormatException("Inventário de AppX provisionados incompleto.");
        return apps;
    }

    public static List<ImageEdition> Editions(string text)
    {
        var result = new List<ImageEdition>();
        int index = 0;
        string name = "";
        foreach (var line in text.Split('\n'))
        {
            var pair = line.Split(':', 2);
            if (pair.Length != 2) continue;
            var key = pair[0].Trim();
            var value = pair[1].Trim();
            if (key == "Index")
            {
                if (index > 0) result.Add(new(index, name));
                if (!int.TryParse(value, out index) || index < 1) throw new FormatException("Índice de imagem inválido.");
                name = "";
            }
            else if (key == "Name") name = value;
        }
        if (index > 0) result.Add(new(index, name));
        if (result.Count == 0 || result.Any(x => x.Name.Length == 0) ||
            result.Select(x => x.Index).Distinct().Count() != result.Count)
            throw new FormatException("O DISM não retornou uma lista reconhecida de edições.");
        return result;
    }

    public static List<ComponentItem> Components(string text, string firstKey, string kind)
    {
        var result = new List<ComponentItem>();
        ComponentItem? item = null;
        foreach (var line in text.Split('\n'))
        {
            var pair = line.Split(':', 2);
            if (pair.Length != 2) continue;
            var key = pair[0].Trim();
            var value = pair[1].Trim();
            if (key.Equals(firstKey, StringComparison.OrdinalIgnoreCase))
            {
                item = new() { Name = value, Kind = kind };
                result.Add(item);
            }
            else if (key.Equals("State", StringComparison.OrdinalIgnoreCase) && item is not null)
                item.State = value;
        }
        if (!text.Contains("The operation completed successfully.", StringComparison.Ordinal))
            throw new FormatException("Saída de inventário DISM incompleta.");
        return result;
    }
}
