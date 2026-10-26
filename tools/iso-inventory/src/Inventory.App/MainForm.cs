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
    private readonly Button export = new() { Text = "Exportar seleção / JSON", AutoSize = true, Enabled = false };
    private readonly TextBox search = new() { Width = 390, PlaceholderText = "Buscar componentes..." };
    private readonly Label status = new() { AutoSize = true, Text = "Escolha uma ISO ou uma pasta Windows offline." };
    private readonly Label count = new() { AutoSize = true };
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly TextBox warnings = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
    private readonly System.Windows.Forms.Timer filterTimer = new() { Interval = 250 };
    private readonly TreeView catalogTree = new() { Dock = DockStyle.Fill, CheckBoxes = true, HideSelection = false, ShowNodeToolTips = true };
    private readonly TextBox details = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false };
    private readonly HashSet<string> selected = new(StringComparer.OrdinalIgnoreCase);
    private bool updatingTree;
    private sealed record CatalogGroup(string Name, IReadOnlyList<CatalogItem> Items);
    private CancellationTokenSource? cancellation;
    private InventoryReport? report;
    private bool busy;
    private readonly DiagnosticLog log = new();
    private readonly ProgressBar activity = new() { Width = 120, Height = 18, Style = ProgressBarStyle.Marquee, Visible = false };
    private readonly Label nextStep = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Selecione a origem para iniciar. Imagens com várias edições abrem uma escolha de edição." };
    private bool closeRequested;

    public MainForm()
    {
        Text = "ISO Inventory 0.2.0 — componentes da imagem";
        Width = 1280; Height = 820; MinimumSize = new(850, 550);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9);
        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 152, Padding = new Padding(8), ColumnCount = 1, RowCount = 4 };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        var first = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        first.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        first.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        first.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        first.Controls.Add(source); first.Controls.Add(chooseIso); first.Controls.Add(chooseFolder);
        var second = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        second.Controls.AddRange(new Control[] { editions, analyze, cancel, export, activity });
        var third = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var clearFilter = new Button { Text = "Limpar filtro", AutoSize = true };
        clearFilter.Click += (_, _) =>
        {
            search.PlaceholderText = "Buscar componentes...";
            search.Clear(); RefreshGrid();
        };
        var openLog = new Button { Text = "Abrir log", AutoSize = true };
        openLog.Click += (_, _) =>
        {
            log.Write("Log solicitado pela interface.");
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(log.FilePath) { UseShellExecute = true }); }
            catch (Exception error) { ShowError(error); }
        };
        third.Controls.Add(search); third.Controls.Add(clearFilter); third.Controls.Add(openLog); third.Controls.Add(count);
        header.Controls.Add(first, 0, 0); header.Controls.Add(second, 0, 1); header.Controls.Add(third, 0, 2);
        header.Controls.Add(nextStep, 0, 3);
        var catalogPage = new TabPage("Componentes da imagem");
        var split = new SplitContainer { Dock = DockStyle.Fill, Width = 1050, SplitterDistance = 520 };
        split.Panel1.Controls.Add(catalogTree); split.Panel2.Controls.Add(details);
        catalogPage.Controls.Add(split); tabs.TabPages.Add(catalogPage);
        var warningsPage = new TabPage("Avisos e limites"); warningsPage.Controls.Add(warnings); tabs.TabPages.Add(warningsPage);
        catalogTree.BeforeExpand += (_, e) => { if (e.Node is not null) PopulateGroup(e.Node); };
        catalogTree.AfterCheck += (_, e) => UpdateSelection(e.Node);
        catalogTree.AfterSelect += (_, e) =>
        {
            if (e.Node?.Tag is CatalogItem item)
                details.Text = item.Name + Environment.NewLine + item.Category + Environment.NewLine +
                    "Estado: " + item.State + Environment.NewLine + Environment.NewLine + item.Details +
                    Environment.NewLine + Environment.NewLine + "Marcação: planejamento de futura remoção.";
            else if (e.Node?.Tag is CatalogGroup group)
                details.Text = group.Name + Environment.NewLine + group.Items.Count +
                    " itens. Expandir carrega a lista; marcar o grupo seleciona os itens deste grupo/filtro.";
        };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(8), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        footer.Controls.Add(status);
        footer.Controls.Add(new Label { AutoSize = true, Text = "Marque os itens para futura remoção e exporte a seleção. Estado e dependências aparecem nos detalhes." });
        Controls.Add(tabs); Controls.Add(header); Controls.Add(footer);
        chooseIso.Click += async (_, _) =>
        {
            try
            {
                using var dialog = new OpenFileDialog { Filter = "Imagens Windows|*.iso;*.wim;*.esd", CheckFileExists = true };
                if (dialog.ShowDialog(this) == DialogResult.OK) await SelectSource(dialog.FileName);
            }
            catch (Exception error) { ShowError(error); }
        };
        chooseFolder.Click += async (_, _) =>
        {
            try
            {
                using var dialog = new FolderBrowserDialog { Description = "Escolha a raiz da imagem (contendo a pasta Windows), ou a unidade da ISO montada." };
                if (dialog.ShowDialog(this) == DialogResult.OK) await SelectSource(dialog.SelectedPath);
            }
            catch (Exception error) { ShowError(error); }
        };
        analyze.Click += async (_, _) => await Analyze();
        cancel.Click += (_, _) => { cancellation?.Cancel(); status.Text = "Cancelando após a operação nativa em andamento; aguarde a desmontagem..."; };
        export.Click += (_, _) => Export();
        search.TextChanged += (_, _) => { filterTimer.Stop(); filterTimer.Start(); };
        filterTimer.Tick += (_, _) => { filterTimer.Stop(); RefreshGrid(); };
        tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedIndex == 0) RefreshGrid(); };
        FormClosing += (_, e) =>
        {
            if (busy) { e.Cancel = true; closeRequested = true; cancellation?.Cancel(); status.Text = "Encerrando após cancelar e desmontar; aguarde..."; }
        };
        FormClosed += (_, _) => { filterTimer.Dispose(); cancellation?.Dispose(); };
        WindowsImageSession.CommandLog = log.Write;
        log.Write("ISO Inventory 0.2.0 iniciado; apartment=" + Thread.CurrentThread.GetApartmentState());
    }

    private void SetBusy(bool value)
    {
        busy = value; chooseIso.Enabled = chooseFolder.Enabled = editions.Enabled = !value;
        analyze.Enabled = !value && editions.Items.Count != 0;
        export.Enabled = !value && report is not null;
        cancel.Enabled = value && cancellation is not null;
        activity.Visible = value;
        UseWaitCursor = value;
    }

    private async Task SelectSource(string path)
    {
        log.Write("Origem selecionada: " + path);
        source.Text = path; editions.Items.Clear(); report = null;
        selected.Clear(); catalogTree.Nodes.Clear(); details.Clear();
        cancellation?.Dispose(); cancellation = new();
        var token = cancellation.Token;
        warnings.Clear(); count.Text = ""; SetBusy(true);
        status.Text = "Lendo edições (sem alterar a imagem)...";
        nextStep.Text = "Carregando a origem... O progresso aparece no rodapé; use Cancelar se necessário.";
        bool loaded = false;
        try
        {
            var list = await Task.Run(() => WindowsImageSession.ListEditions(path, token), token);
            token.ThrowIfCancellationRequested();
            editions.Items.AddRange(list.Cast<object>().ToArray());
            editions.SelectedIndex = 0;
            loaded = true;
            log.Write("Origem carregada: " + list.Count + " edição(ões).");
            status.Text = "Origem carregada: " + list.Count + " edição(ões).";
        }
        catch (OperationCanceledException) { status.Text = "Carregamento cancelado."; log.Write("Carregamento cancelado."); }
        catch (Exception e) { ShowError(e); }
        finally
        {
            cancellation.Dispose(); cancellation = null; SetBusy(false);
        }
        if (closeRequested) { Close(); return; }
        if (!loaded) { nextStep.Text = "Não foi possível carregar a origem. Consulte Avisos ou Abrir log."; return; }
        if (editions.Items.Count > 1 && !ChooseEdition())
        {
            nextStep.Text = "Origem carregada. Selecione a edição na lista e clique em Analisar.";
            analyze.Focus();
            return;
        }
        nextStep.Text = "Iniciando a análise da edição selecionada...";
        await Analyze();
    }

    private bool ChooseEdition()
    {
        using var dialog = new Form { Text = "Escolha a edição para analisar", Width = 640, Height = 200,
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, Padding = new Padding(12) };
        var label = new Label { Dock = DockStyle.Top, Height = 30, Text = "A imagem contém várias edições. Escolha uma para iniciar o inventário." };
        var options = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        options.Items.AddRange(editions.Items.Cast<object>().ToArray()); options.SelectedIndex = 0;
        var start = new Button { Dock = DockStyle.Bottom, Height = 34, Text = "Iniciar análise", DialogResult = DialogResult.OK };
        dialog.Controls.Add(options); dialog.Controls.Add(label); dialog.Controls.Add(start);
        dialog.AcceptButton = start;
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        editions.SelectedIndex = options.SelectedIndex;
        return true;
    }

    private async Task Analyze()
    {
        if (editions.SelectedItem is not ImageEdition edition) return;
        cancellation?.Dispose(); cancellation = new();
        var token = cancellation.Token;
        IProgress<string> progress = new Progress<string>(message => { status.Text = message; nextStep.Text = message; log.Write(message); });
        InventoryReport? result = null;
        bool finished = false;
        WindowsImageSession? session = null;
        report = null; SetBusy(true);
        log.Write("Análise iniciada: " + edition);
        try
        {
            var input = source.Text;
            session = await Task.Run(() => WindowsImageSession.Open(input, edition.Index, token, progress), token);
            var root = session.ImageRoot;
            progress.Report("Lendo cópias temporárias dos hives offline...");
            var registry = await Task.Run(() => OfflineRegistry.Read(root, token), token);
            result = await Task.Run(() =>
            {
                var inventory = ImageScanner.Scan(root, registry, token, progress, includeFileInventory: false);
                progress.Report("Detectando componentes integrados, manifestos CBS e drivers...");
                InventoryCatalog.AddImageComponents(root, inventory, token, progress);
                return inventory;
            }, token);
            result.Source = source.Text; result.Edition = edition.ToString();
            result.Components.AddRange(await session.ReadComponents(token, progress));
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
                selected.Clear(); report.Catalog = InventoryCatalog.Build(report);
                report.Limits = "Catálogo do conteúdo identificado na imagem. Marcar planeja futura remoção e salva a escolha no JSON. A presença não garante que o recurso esteja ativo ou seja removível. Instalações posteriores por scripts/downloads não são determinadas por completo.";
                search.Clear(); if (finished) tabs.SelectedIndex = 0; RefreshGrid();
                status.Text = (finished ? "Coleta finalizada: " : "Inventário parcial: ") +
                    $"{report.Applications.Count} aplicativos, {report.Services.Count} serviços/drivers, {report.Catalog.Count} itens no catálogo. Consulte os avisos.";
                log.Write(status.Text);
            }
            else status.Text = token.IsCancellationRequested ? "Cancelado. Consulte os avisos de limpeza." : "Análise não concluída. Consulte os avisos.";
            cancellation.Dispose(); cancellation = null; SetBusy(false);
            nextStep.Text = status.Text;
            if (closeRequested) Close();
        }
    }

    private void ShowError(Exception error)
    {
        log.Write(error.ToString());
        warnings.Text = error.Message; tabs.SelectedIndex = 1;
        status.Text = "Não foi possível concluir a operação.";
        MessageBox.Show(this, error.Message.Length > 4000 ? error.Message[..4000] : error.Message,
            "Inventário não concluído", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void RefreshGrid()
    {
        if (report is null) return;
        var query = search.Text;
        var matches = report.Catalog.Where(item => query.Length == 0 ||
            (item.Name + " " + item.Category + " " + item.State + " " + item.Details)
                .Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        updatingTree = true;
        catalogTree.BeginUpdate();
        try
        {
            catalogTree.Nodes.Clear();
            foreach (var group in matches.GroupBy(item => item.Category))
                catalogTree.Nodes.Add(GroupNode(group.Key, group.ToList()));
            foreach (TreeNode node in catalogTree.Nodes)
                if (((CatalogGroup)node.Tag!).Name == "Componentes integrados") node.Expand();
        }
        finally { catalogTree.EndUpdate(); updatingTree = false; }
        UpdateCount(matches.Count);
    }

    private TreeNode GroupNode(string name, IReadOnlyList<CatalogItem> items)
    {
        var marked = items.Count(item => selected.Contains(item.Id));
        var node = new TreeNode(name + " (" + items.Count + "; " + marked + " marcados)") {
            Tag = new CatalogGroup(name, items), Checked = items.Count != 0 && marked == items.Count
        };
        node.Nodes.Add(new TreeNode("Expandir para listar..."));
        return node;
    }

    private void PopulateGroup(TreeNode node)
    {
        if (node.Tag is not CatalogGroup group || node.Nodes.Count != 1 || node.Nodes[0].Tag is not null) return;
        bool previous = updatingTree; updatingTree = true;
        try
        {
            node.Nodes.Clear();
            if (group.Items.Count > 250)
            {
                for (int offset = 0; offset < group.Items.Count; offset += 250)
                    node.Nodes.Add(GroupNode("Itens " + (offset + 1) + "–" + Math.Min(offset + 250, group.Items.Count),
                        group.Items.Skip(offset).Take(250).ToList()));
            }
            else foreach (var item in group.Items)
                node.Nodes.Add(new TreeNode(item.Name + " — " + item.State) {
                    Tag = item, Checked = selected.Contains(item.Id), ToolTipText = item.Id
                });
        }
        finally { updatingTree = previous; }
    }

    private void UpdateSelection(TreeNode? node)
    {
        if (updatingTree || node is null) return;
        if (node.Tag is CatalogItem item)
        {
            if (node.Checked) selected.Add(item.Id); else selected.Remove(item.Id);
        }
        else if (node.Tag is CatalogGroup group)
            foreach (var entry in group.Items)
                if (node.Checked) selected.Add(entry.Id); else selected.Remove(entry.Id);
        else return;
        updatingTree = true;
        try
        {
            void Sync(TreeNode current)
            {
                if (current.Tag is CatalogItem entry) current.Checked = selected.Contains(entry.Id);
                if (current.Tag is CatalogGroup entries)
                {
                    int marked = entries.Items.Count(entry => selected.Contains(entry.Id));
                    current.Checked = marked == entries.Items.Count;
                    current.Text = entries.Name + " (" + entries.Items.Count + "; " + marked + " marcados)";
                }
                foreach (TreeNode child in current.Nodes) Sync(child);
            }
            foreach (TreeNode root in catalogTree.Nodes) Sync(root);
        }
        finally { updatingTree = false; }
        UpdateCount();
    }

    private void UpdateCount(int? matches = null)
    {
        count.Text = (matches is null ? "" : matches + " encontrados • ") + selected.Count + " selecionados para futura remoção";
    }

    private void Export()
    {
        if (report is null) return;
        report.SelectedCatalogIds = selected.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
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
