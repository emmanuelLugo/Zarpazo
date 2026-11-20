using System.Drawing;
using System.IO;
using Zarpazo.Diagnostics;

namespace Zarpazo.Tests;

public sealed class ErrorLogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    private string LogPath => Path.Combine(_folder, "Logs", "zarpazo.log");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Writes_the_source_and_the_exception()
    {
        var log = new ErrorLog(LogPath);
        log.Write("Test", new InvalidOperationException("boom"));

        string text = File.ReadAllText(LogPath);
        Assert.Contains("Test", text);
        Assert.Contains("boom", text);
    }

    [Fact]
    public void Rolls_over_once_past_the_limit_and_keeps_writing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        File.WriteAllText(LogPath, new string('x', (int)ErrorLog.MaxBytes + 1));
        var log = new ErrorLog(LogPath);
        log.Write("Test", new Exception("after"));

        Assert.True(File.Exists(LogPath + ".1"));
        Assert.True(new FileInfo(LogPath).Length < ErrorLog.MaxBytes);
        Assert.Contains("after", log.Tail(10));
    }

    [Fact]
    public void Never_throws_when_the_folder_cannot_be_used()
    {
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "Zarpazo.Tests.file"), "");
        new ErrorLog(Path.Combine(Path.GetTempPath(), "Zarpazo.Tests.file", "x.log")).Write("Test", new Exception());
    }

    [Fact]
    public void Report_has_version_screens_and_log_tail_but_no_capture_paths()
    {
        var log = new ErrorLog(LogPath);
        log.Write("Test", new Exception("boom"));

        string report = ErrorReport.Build(log, [(new Rectangle(-1920, 0, 1920, 1080), 1.5)]);

        Assert.Contains("Zarpazo ", report);
        Assert.Contains("1920x1080 en (-1920, 0), escala 150", report);
        Assert.Contains("boom", report);
    }
}
