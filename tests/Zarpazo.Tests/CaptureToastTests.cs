using System.Windows;
using Xunit;
using Zarpazo.Shell;

namespace Zarpazo.Tests;

public class CaptureToastTests
{
    [Fact]
    public void Shows_the_file_only_when_one_was_written_and_never_takes_the_focus()
    {
        Sta.Run(() =>
        {
            var copied = new CaptureToast("Captura copiada");
            var saved = new CaptureToast("Captura guardada", @"C:\Capturas\uno.png");

            Assert.Equal(Visibility.Collapsed, copied.Detail.Visibility);
            Assert.Equal(@"uno.png · C:\Capturas", saved.Detail.Text);
            Assert.False(saved.ShowActivated);
            copied.Close();
            saved.Close();
        });
    }
}
