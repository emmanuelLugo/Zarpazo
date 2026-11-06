using Microsoft.Win32;
using Zarpazo.Shell;

namespace Zarpazo.Tests;

/// <summary>Runs against a throwaway key under HKCU, never the real startup list.</summary>
public sealed class StartupRegistrationTests : IDisposable
{
    private const string ExePath = @"C:\Program Files\Zarpazo\Zarpazo.exe";
    private readonly string _testRoot = $@"Software\Zarpazo.Tests\{Guid.NewGuid():N}";

    private string RunKey => $@"{_testRoot}\Run";

    private StartupRegistration Create(string exe = ExePath) => new(RunKey, "Zarpazo", exe);

    private string? StoredValue()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("Zarpazo") as string;
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_testRoot, throwOnMissingSubKey: false);
        try
        {
            Registry.CurrentUser.DeleteSubKey(@"Software\Zarpazo.Tests", throwOnMissingSubKey: false);
        }
        catch (InvalidOperationException)
        {
            // Another test still has its own key under it; that one will remove the parent when it finishes.
        }
    }

    [Fact]
    public void Is_off_when_nothing_was_ever_written()
    {
        Assert.False(Create().IsEnabled);
    }

    [Fact]
    public void Enabling_writes_the_quoted_path_because_it_contains_spaces()
    {
        Create().SetEnabled(true);

        Assert.Equal($"\"{ExePath}\"", StoredValue());
        Assert.True(Create().IsEnabled);
    }

    [Fact]
    public void Disabling_removes_the_entry()
    {
        StartupRegistration startup = Create();
        startup.SetEnabled(true);

        startup.SetEnabled(false);

        Assert.Null(StoredValue());
        Assert.False(startup.IsEnabled);
    }

    [Fact]
    public void Disabling_when_already_off_does_not_fail()
    {
        Create().SetEnabled(false);
        Create().SetEnabled(false);

        Assert.Null(StoredValue());
    }

    [Fact]
    public void Enabling_twice_leaves_a_single_entry()
    {
        Create().SetEnabled(true);
        Create().SetEnabled(true);

        using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey)!;
        Assert.Single(key.GetValueNames());
    }

    [Fact]
    public void An_entry_left_by_another_copy_counts_as_off_and_enabling_replaces_it()
    {
        Create(@"D:\old\bin\Debug\Zarpazo.exe").SetEnabled(true);

        StartupRegistration current = Create();
        Assert.False(current.IsEnabled);

        current.SetEnabled(true);

        Assert.True(current.IsEnabled);
        Assert.Equal($"\"{ExePath}\"", StoredValue());
    }

    [Fact]
    public void Leaves_other_programs_entries_alone()
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            key.SetValue("OtherApp", @"""C:\other.exe""");
        }

        StartupRegistration startup = Create();
        startup.SetEnabled(true);
        startup.SetEnabled(false);

        using RegistryKey after = Registry.CurrentUser.OpenSubKey(RunKey)!;
        Assert.Equal(@"""C:\other.exe""", after.GetValue("OtherApp"));
    }
}
