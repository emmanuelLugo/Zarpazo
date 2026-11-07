using System.Windows.Media;
using Xunit;
using Zarpazo.Recording;

namespace Zarpazo.Tests;

public sealed class AudioLevelDisplayTests
{
    [Fact]
    public void Silence_fills_nothing_and_full_scale_fills_everything()
    {
        Assert.Equal(0, AudioLevelDisplay.Fraction(0));
        Assert.Equal(1, AudioLevelDisplay.Fraction(1));
        Assert.Equal(1, AudioLevelDisplay.Fraction(3)); // past full scale is still a full bar
    }

    [Fact]
    public void Quiet_speech_still_shows_on_the_bar()
    {
        Assert.True(AudioLevelDisplay.Fraction(0.01f) > 0.09); // -40 dB would be invisible on a straight scale
    }

    [Fact]
    public void Color_goes_from_green_to_amber_to_red_as_it_gets_louder()
    {
        Brush quiet = AudioLevelDisplay.Color(0.3);
        Brush loud = AudioLevelDisplay.Color(0.8);
        Brush clipping = AudioLevelDisplay.Color(0.95);

        Assert.NotEqual(quiet, loud);
        Assert.NotEqual(loud, clipping);
        Assert.NotEqual(quiet, clipping);
    }
}
