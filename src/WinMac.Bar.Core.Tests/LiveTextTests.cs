using WinMac.Bar.Features;
using Xunit;

namespace WinMac.Bar.Core.Tests;

public class LiveTextTests
{
    [Fact] public void Line_breaks_are_kept_or_joined_and_empty_lines_are_dropped()
    {
        string[] l = ["Primera línea  ", "", "   ", "Segunda"];
        Assert.Equal("Primera línea" + Environment.NewLine + "Segunda", LiveText.Join(l, true));
        Assert.Equal("Primera línea Segunda", LiveText.Join(l, false)); Assert.Equal("", LiveText.Join([], true)); Assert.Equal("", LiveText.Join(["  "], false));
    }

    [Fact] public void Words_split_with_a_hyphen_at_the_end_of_a_line_are_rejoined_only_when_it_looks_like_a_split_word()
    {
        Assert.Equal("la información llegó", LiveText.Join(["la informa-", "ción llegó"], false));
        Assert.Equal("fecha 2020- Nueva", LiveText.Join(["fecha 2020-", "Nueva"], false));   // un guion tras un número no se toca
        Assert.Equal("anti- Estado", LiveText.Join(["anti-", "Estado"], false));            // mayúscula: es otra palabra
    }

    [Fact] public void Language_choice_prefers_the_exact_one_then_the_same_base_then_the_profile_then_anything()
    {
        string[] av = ["en-US", "es-ES", "ja-JP"];
        Assert.Equal("es-ES", LiveText.PickLanguage(av, "es-ES", ["en-US"])); Assert.Equal("es-ES", LiveText.PickLanguage(av, "es-CL", ["en-US"]));
        Assert.Equal("ja-JP", LiveText.PickLanguage(av, "", ["ja-JP", "en-US"])); Assert.Equal("en-US", LiveText.PickLanguage(av, "fr-FR", ["de-DE"]));
        Assert.Null(LiveText.PickLanguage([], "es-ES", ["es-ES"]));
    }

    [Fact] public void The_preview_limits_lines_and_characters_and_says_how_many_lines_are_left()
    {
        var (t, more) = LiveText.Preview(string.Join('\n', Enumerable.Range(1, 30)), 5); Assert.Equal(25, more); Assert.Equal(5, t.Split('\n').Length);
        Assert.EndsWith("…", LiveText.Preview(new string('x', 5000)).Text);
    }

    [Fact] public void Live_text_defaults_are_off_with_auto_copy_and_language_is_sanitized()
    {
        var c = new BarConfig(); Assert.False(c.LiveTextEnabled); Assert.True(c.LtAutoCopy); Assert.True(c.LtKeepLineBreaks);
        Assert.Equal("", new BarConfig { LtLanguage = new string('x', 40) }.Sanitize().LtLanguage); Assert.Equal("es-ES", new BarConfig { LtLanguage = " es-ES " }.Sanitize().LtLanguage);
    }
}
