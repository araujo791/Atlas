using Inventory.Core;
using System.Text.Json;

namespace Inventory.App;

public sealed class MainForm : Form
{
    private readonly TextBox source = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly ComboBox editions = new() { Width = 390, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button chooseIso = new() { Text = "Selecionar ISO/WIM/ESD", AutoSize = true };
    private readonly Button chooseFolder = new() { Text = "Pasta offline", AutoSize = true };
    private readonly Button analyze = new() { Text = "Analisar", AutoSize = true, Enabled = false };
    private readonly Button cancel = new() { Text = "Cancelar", AutoSize = true, Enabled = false };
    private readonly Button export = new() { Text = "Salvar JSON completo", AutoSize = true, Enabled = false };
    private readonly TextBox search = new() { Width = 390, PlaceholderText = "Filtrar a aba atual..." };
    private readonly Label status = new() { AutoSize = true, Text = "Escolha uma ISO ou uma pasta Windows offline." };
    private readonly Label count = new() { AutoSize = true };
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly TextBox warnings = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
    private readonly System.Windows.Forms.Timer filterTimer = new() { Interval = 250 };
    private readonly List<DataGridView> grids = new();
    private CancellationTokenSource? cancellation;
    private InventoryReport? report;
    private bool busy;
    private string? fileOwnerFilter;

    private sealed record AppRow(string Nome, string Tipo, string Versão, string Fabricante,
        string Localização, int Arquivos, string TamanhoLógico, string Id, string Owner);
    private sealed record ServiceRow(string Nome, string Descrição, string Tipo, string Início,
        string Executável, string DLL, string Conta, string Dependências, string Owner);
    private sealed record FileRow(string Caminho, bool Encontrado, string BytesLógicos,
        bool Compartilhado, string VinculadoA, string Evidência);
    private sealed record ComponentRow(string Nome, string Tipo, string Estado);

    public MainForm()
    {
        Text = "ISO Inventory 0.1.0 — experimental • somente leitura";
        Width = 1280; Height = 820; MinimumSize = new(850, 550);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9);
        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 112, Padding = new Padding(8), ColumnCount = 1, RowCount = 3 };
        var first = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        first.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        first.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        first.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        first.Controls.Add(source); first.Controls.Add(chooseIso); first.Controls.Add(chooseFolder);
        var second = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        second.Controls.AddRange(new Control[] { editions, analyze, cancel, export });
        var third = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var clearFilter = new Button { Text = "Limpar filtro", AutoSize = true };
        clearFilter.Click += (_, _) =>
        {
            fileOwnerFilter = null; search.PlaceholderText = "Filtrar a aba atual...";
            search.Clear(); RefreshGrid();
        };
        third.Controls.Add(search); third.Controls.Add(clearFilter); third.Controls.Add(count);
        header.Controls.Add(first); header.Controls.Add(second); header.Controls.Add(third);
        foreach (var title in new[] { "Aplicativos", "Serviços e drivers", "Arquivos vinculados", "Componentes", "Avisos e limites" })
        {
            var page = new TabPage(title);
            if (grids.Count < 4)
            {
                var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true,
                    AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
                    RowHeadersVisible = false, BackgroundColor = SystemColors.Window,
                    ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText };
                grids.Add(grid); page.Controls.Add(grid);
            }
            else page.Controls.Add(warnings);
            tabs.TabPages.Add(page);
        }
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(8), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        footer.Controls.Add(status);
        footer.Controls.Add(new Label { AutoSize = true, Text = "Somente leitura • Tamanhos lógicos não representam espaço removível • Duplo clique em um app/serviço para ver seus arquivos" });
        Controls.Add(tabs); Controls.Add(header); Controls.Add(footer);
        chooseIso.Click += async (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Imagens Windows|*.iso;*.wim;*.esd", CheckFileExists = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) await SelectSource(dialog.FileName);
        };
        chooseFolder.Click += async (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Escolha a raiz da imagem (contendo a pasta Windows), ou a unidade da ISO montada." };
            if (dialog.ShowDialog(this) == DialogResult.OK) await SelectSource(dialog.SelectedPath);
        };
        analyze.Click += async (_, _) => await Analyze();
        cancel.Click += (_, _) => { cancellation?.Cancel(); status.Text = "Cancelando após a operação nativa em andamento; aguarde a desmontagem..."; };
        export.Click += (_, _) => Export();
        search.TextChanged += (_, _) => { filterTimer.Stop(); filterTimer.Start(); };
        filterTimer.Tick += (_, _) => { filterTimer.Stop(); RefreshGrid(); };
        tabs.SelectedIndexChanged += (_, _) => RefreshGrid();
        grids[0].CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && grids[0].Rows[e.RowIndex].DataBoundItem is AppRow row) ShowOwner(row.Owner);
        };
        grids[1].CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && grids[1].Rows[e.RowIndex].DataBoundItem is ServiceRow row) ShowOwner(row.Owner);
        };
        FormClosing += (_, e) =>
        {
            if (busy) { e.Cancel = true; cancellation?.Cancel(); status.Text = "Aguarde a conclusão/cancelamento e a desmontagem antes de fechar."; }
        };
        FormClosed += (_, _) => { filterTimer.Dispose(); cancellation?.Dispose(); };
    }

    private void ShowOwner(string owner)
    {
        fileOwnerFilter = owner; search.Clear();
        search.PlaceholderText = "Arquivos de " + owner;
        tabs.SelectedIndex = 2; RefreshGrid();
    }
    private void SetBusy(bool value)
    {
        busy = value; chooseIso.Enabled = chooseFolder.Enabled = editions.Enabled = !value;
        analyze.Enabled = !value && editions.Items.Count != 0;
        export.Enabled = !value && report is not null;
        cancel.Enabled = value && cancellation is not null;
        UseWaitCursor = value;
    }

    private async Task SelectSource(string path)
    {
        source.Text = path; editions.Items.Clear(); report = null;
        foreach (var grid in grids) grid.DataSource = null;
        warnings.Clear(); count.Text = ""; SetBusy(true);
        status.Text = "Lendo edições (sem alterar a imagem)...";
        try
        {
            var list = await WindowsImageSession.ListEditions(path);
            editions.Items.AddRange(list.Cast<object>().ToArray());
            editions.SelectedIndex = 0;
            status.Text = "Selecione a edição e clique em Analisar.";
        }
        catch (Exception e) { ShowError(e); }
        finally { SetBusy(false); }
    }

    private async Task Analyze()
    {
        if (editions.SelectedItem is not ImageEdition edition) return;
        cancellation?.Dispose(); cancellation = new();
        var token = cancellation.Token;
        IProgress<string> progress = new Progress<string>(message => status.Text = message);
        InventoryReport? result = null;
        bool finished = false;
        WindowsImageSession? session = null;
        report = null; SetBusy(true);
        try
        {
            session = await WindowsImageSession.Open(source.Text, edition.Index, token, progress);
            var root = session.ImageRoot;
            progress.Report("Lendo cópias temporárias dos hives offline...");
            var registry = await Task.Run(() => OfflineRegistry.Read(root, token), token);
            result = await Task.Run(() => ImageScanner.Scan(root, registry, token, progress), token);
            result.Source = source.Text; result.Edition = edition.ToString();
            result.Components = await session.ReadComponents(token, progress);
            foreach (var provisioned in await session.ReadProvisionedApps(token, progress))
            {
                var physical = result.Applications.FirstOrDefault(app => app.Id.Equals(provisioned.Id, StringComparison.OrdinalIgnoreCase));
                if (physical is not null) physical.Kind += " • provisionado";
                else result.Applications.Add(provisioned);
            }
            result.Applications = result.Applications.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase).ToList();
            result.CollectionStatus = "Coleta finalizada; consulte limites e avisos.";
            finished = true;
        }
        catch (OperationCanceledException) { status.Text = "Análise cancelada."; }
        catch (Exception e)
        {
            if (result is not null)
            {
                result.CollectionStatus = "Coleta interrompida; dados parciais.";
                result.Warnings.Add(e.Message); result.WarningCount++;
            }
            ShowError(e);
        }
        finally
        {
            if (session is not null)
            {
                status.Text = "Desmontando as imagens abertas pelo aplicativo...";
                await session.DisposeAsync();
                if (result is not null)
                    foreach (var warning in session.CleanupWarnings)
                    { result.Warnings.Add(warning); result.WarningCount++; }
                else if (session.CleanupWarnings.Count != 0)
                    warnings.AppendText(Environment.NewLine + string.Join(Environment.NewLine, session.CleanupWarnings));
            }
            if (result is not null && !token.IsCancellationRequested)
            {
                report = result;
                warnings.Text = report.Limits + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, report.Warnings);
                if (report.WarningCount > report.Warnings.Count)
                    warnings.AppendText(Environment.NewLine + "Avisos omitidos: " + (report.WarningCount - report.Warnings.Count));
                fileOwnerFilter = null; search.Clear(); RefreshGrid();
                status.Text = (finished ? "Coleta finalizada: " : "Inventário parcial: ") +
                    $"{report.Applications.Count} aplicativos, {report.Services.Count} serviços/drivers, {report.Files.Count} arquivos vinculados. Consulte os avisos.";
            }
            else status.Text = token.IsCancellationRequested ? "Cancelado. Consulte os avisos de limpeza." : "Análise não concluída. Consulte os avisos.";
            cancellation.Dispose(); cancellation = null; SetBusy(false);
        }
    }

    private void ShowError(Exception error)
    {
        warnings.Text = error.Message; tabs.SelectedIndex = 4;
        status.Text = "Não foi possível concluir a operação.";
        MessageBox.Show(this, error.Message.Length > 4000 ? error.Message[..4000] : error.Message,
            "Inventário não concluído", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static string FormatSize(long? bytes) => bytes is null ? "" :
        bytes >= 1048576 ? (bytes.Value / 1048576.0).ToString("N2") + " MiB" : bytes.Value.ToString("N0") + " B";
    private bool Match(params string[] text) => text.Any(value => value.Contains(search.Text, StringComparison.OrdinalIgnoreCase));

    private void RefreshGrid()
    {
        if (report is null || tabs.SelectedIndex > 3) return;
        var grid = grids[tabs.SelectedIndex];
        int total;
        switch (tabs.SelectedIndex)
        {
            case 0:
                var apps = report.Applications.Where(a => Match(a.Name, a.Id, a.Kind, a.Publisher, a.Location)).ToList();
                total = apps.Count;
                grid.DataSource = apps.Take(5000).Select(a => new AppRow(a.Name, a.Kind, a.Version, a.Publisher,
                    a.Location, a.FileCount, a.FileCount == 0 ? "" : FormatSize(a.LogicalBytes), a.Id,
                    (a.Kind.StartsWith("Programa") ? "Programa: " + a.Name : "App: " + a.Name) + " [" + a.Id + "]")).ToList();
                break;
            case 1:
                var services = report.Services.Where(s => Match(s.Name, s.DisplayName, s.Kind, s.ImagePath, s.ServiceDll, s.Start)).ToList();
                total = services.Count;
                grid.DataSource = services.Take(5000).Select(s => new ServiceRow(s.Name, s.DisplayName,
                    s.Kind, s.Start, s.ImagePath, s.ServiceDll, s.Account, string.Join("; ", s.Dependencies), "Serviço: " + s.Name)).ToList();
                break;
            case 2:
                var files = report.Files.Where(f => (fileOwnerFilter is null || f.Owners.Contains(fileOwnerFilter)) &&
                    Match(f.Path, string.Join("; ", f.Owners), string.Join("; ", f.Evidence))).ToList();
                total = files.Count;
                grid.DataSource = files.Take(5000).Select(f => new FileRow(f.Path, f.Found, FormatSize(f.Bytes),
                    f.Shared, string.Join("; ", f.Owners), string.Join("; ", f.Evidence))).ToList();
                break;
            default:
                var components = report.Components.Where(c => Match(c.Name, c.Kind, c.State)).ToList();
                total = components.Count;
                grid.DataSource = components.Take(5000).Select(c => new ComponentRow(c.Name, c.Kind, c.State)).ToList();
                break;
        }
        if (grid.Columns.Contains("Owner")) grid.Columns["Owner"].Visible = false;
        count.Text = $"{total:N0} resultados" + (total > 5000 ? " • exibindo 5.000; refine o filtro ou salve o JSON completo" : "");
    }

    private void Export()
    {
        if (report is null) return;
        using var dialog = new SaveFileDialog { Filter = "Inventário JSON|*.json", DefaultExt = "json",
            AddExtension = true, FileName = "inventario-windows.json", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (!Path.GetExtension(dialog.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Salve o relatório com a extensão .json.");
            if (File.Exists(report.Source) && Path.GetFullPath(report.Source).Equals(Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase))
                throw new IOException("O relatório não pode substituir a imagem de origem.");
            if (Directory.Exists(report.Source) && Path.GetFullPath(dialog.FileName).StartsWith(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(report.Source)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Escolha uma pasta fora da imagem para salvar o relatório.");
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            status.Text = "Relatório completo salvo em " + dialog.FileName;
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "Não foi possível salvar", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
