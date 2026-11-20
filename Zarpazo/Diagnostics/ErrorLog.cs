using System.IO;

namespace Zarpazo.Diagnostics;

/// <summary>Appends unexpected errors to a text file under LocalAppData; the file is rolled over once it passes <see cref="MaxBytes"/>.</summary>
internal sealed class ErrorLog
{
    public const long MaxBytes = 512 * 1024;

    private readonly string _path;
    private readonly object _lock = new();

    public static ErrorLog Default { get; } = new(Update.TestOverrides.Data(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zarpazo", "Logs", "zarpazo.log"), "Logs", "zarpazo.log"));

    public ErrorLog(string path) => _path = path;

    public string FilePath => _path;

    /// <summary>Never throws: logging a failure must not cause another one.</summary>
    public void Write(string source, Exception exception)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
                {
                    File.Move(_path, _path + ".1", overwrite: true); // keeps one old file: about 1 MB at most
                }
                File.AppendAllText(_path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public string Tail(int lines)
    {
        try
        {
            lock (_lock)
            {
                return File.Exists(_path) ? string.Join(Environment.NewLine, File.ReadLines(_path).TakeLast(lines)) : "";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
