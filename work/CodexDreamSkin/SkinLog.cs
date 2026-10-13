using System.IO;

namespace CodexDreamSkin;

internal static class SkinLog
{
    private static readonly object gate = new();
    internal static void Write(string message)
    {
        try
        {
            lock (gate)
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexDreamSkin");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "skin.log"),
                    $"{DateTimeOffset.Now:O} [{Environment.ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
    }
}
