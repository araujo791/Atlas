namespace Inventory.App;

internal sealed class DiagnosticLog
{
    public string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ISOInventory", "Logs", "inventory-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] + ".log");
    private readonly object sync = new();

    public void Write(string message)
    {
        lock (sync)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, DateTime.Now.ToString("O") + " " + message + Environment.NewLine);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { System.Diagnostics.Debug.WriteLine(e); }
        }
    }
}
