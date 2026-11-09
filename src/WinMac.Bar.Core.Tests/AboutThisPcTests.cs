using WinMac.Bar;
using Xunit;

/// <summary>It7: «Acerca de este PC» muestra datos REALES y dice «No disponible» cuando Windows no los da; nada inventado.</summary>
public class AboutThisPcTests
{
    private static AboutRaw Raw(string? man = "LENOVO", string? model = "82XB", string? cpu = "AMD Ryzen 5 5600X 6-Core Processor", ulong? ram = 16UL << 30, ulong? vis = null, GpuInfo[]? gpus = null,
        string? prod = "Windows 10 Pro", string? dv = "25H2", int? build = 26300, int? rev = 9711)
        => new(man, model, null, "PC-DE-MUESTRA", cpu, 6, 12, ram, vis, gpus ?? [new("NVIDIA GeForce RTX 3060")], prod, dv, build, rev, "x64", "F.12", "2.0.0");

    [Fact]
    public void A_laptop_shows_its_real_model_and_the_pc_name_as_subtitle()
    {
        var v = AboutFormatter.Build(Raw()); Assert.Equal("LENOVO 82XB", v.Title); Assert.Equal("PC-DE-MUESTRA", v.Subtitle);
    }

    [Theory]
    [InlineData("To Be Filled By O.E.M.")] [InlineData("System Product Name")] [InlineData("Default string")] [InlineData("  ")] [InlineData(null)]
    public void BIOS_placeholders_are_not_a_model_the_pc_name_is_used_instead(string? model)
    {
        var v = AboutFormatter.Build(Raw(man: "ASUSTeK COMPUTER INC.", model: model)); Assert.Equal("PC-DE-MUESTRA", v.Title); Assert.Null(v.Subtitle);
    }

    [Fact]
    public void The_manufacturer_is_not_repeated_when_the_model_already_has_it()
        => Assert.Equal("Dell Inc. XPS 13", AboutFormatter.ModelName("Dell Inc.", "Dell Inc. XPS 13", null));

    [Fact]
    public void Without_model_or_name_the_title_is_generic_not_invented()
    {
        var v = AboutFormatter.Build(Raw(model: null) with { ComputerName = null }); Assert.Equal("Este PC", v.Title);
    }

    [Theory]
    [InlineData(16UL << 30, "16 GB")] [InlineData(17075000000UL, "16 GB")] [InlineData(8UL << 30, "8 GB")] [InlineData(32UL << 30, "32 GB")]
    [InlineData(536870912UL, "512 MB")]
    public void RAM_is_rounded_the_way_it_is_sold(ulong bytes, string expected) => Assert.Equal(expected, AboutFormatter.FormatRam(bytes));

    [Fact]
    public void Installed_memory_is_preferred_over_the_visible_figure_and_a_missing_one_is_not_available()
    {
        Assert.Equal("16 GB", AboutFormatter.Build(Raw(ram: 16UL << 30, vis: 15UL << 30)).Main.Single(r => r.Label == "Memoria").Value);
        Assert.Equal("16 GB", AboutFormatter.Build(Raw(ram: null, vis: 17075000000UL)).Main.Single(r => r.Label == "Memoria").Value);
        var none = AboutFormatter.Build(Raw(ram: null, vis: null)).Main.Single(r => r.Label == "Memoria"); Assert.False(none.Available); Assert.Equal("No disponible", none.Value);
    }

    [Fact]
    public void The_dedicated_gpu_is_primary_over_the_integrated_one_and_the_others_are_counted()
    {
        var (p, o) = AboutFormatter.PrimaryGpu([new("AMD Radeon(TM) Graphics"), new("NVIDIA GeForce RTX 4070 Laptop GPU")]); Assert.Equal("NVIDIA GeForce RTX 4070 Laptop GPU", p); Assert.Equal(1, o);
        var (p2, _) = AboutFormatter.PrimaryGpu([new("Intel(R) UHD Graphics 770"), new("AMD Radeon RX 6700 XT")]); Assert.Equal("AMD Radeon RX 6700 XT", p2);
        var (p3, _) = AboutFormatter.PrimaryGpu([new("Intel(R) Arc(TM) A770 Graphics"), new("Intel(R) UHD Graphics 770")]); Assert.Equal("Intel(R) Arc(TM) A770 Graphics", p3);
    }

    [Fact]
    public void Virtual_and_remote_adapters_are_ignored_and_no_gpu_means_not_available()
    {
        var (p, o) = AboutFormatter.PrimaryGpu([new("Microsoft Basic Display Adapter"), new("Parsec Virtual Display Adapter"), new("Intel(R) UHD Graphics 630")]); Assert.Equal("Intel(R) UHD Graphics 630", p); Assert.Equal(0, o);
        Assert.Equal((null, 0), AboutFormatter.PrimaryGpu([new("Microsoft Remote Display Adapter")]));
        Assert.False(AboutFormatter.Build(Raw(gpus: [])).Main.Single(r => r.Label == "Gráficos").Available);
    }

    [Fact]
    public void Duplicate_gpu_entries_are_counted_once()
        => Assert.Equal(0, AboutFormatter.PrimaryGpu([new("NVIDIA GeForce GTX 1650"), new("NVIDIA GeForce GTX 1650")]).Others);

    [Theory]
    [InlineData("Windows 10 Pro", 26300, "Windows 11 Pro")] [InlineData("Windows 10 Pro", 19045, "Windows 10 Pro")] [InlineData("Windows 10 Home", 22631, "Windows 11 Home")] [InlineData(null, 26100, "Windows 11")]
    public void The_registry_says_Windows_10_even_on_Windows_11(string? product, int build, string expected) => Assert.Equal(expected, AboutFormatter.WindowsName(product, build));

    [Fact]
    public void Windows_and_build_lines_use_the_real_numbers()
    {
        var v = AboutFormatter.Build(Raw()); Assert.Equal("Windows 11 Pro 25H2", v.Main.Single(r => r.Label == "Windows").Value); Assert.Equal("26300.9711", v.More.Single(r => r.Label == "Compilación de Windows").Value);
        Assert.Equal("2.0.0", v.Main.Single(r => r.Label == "Hatsu Desktop").Value);
    }

    [Theory]
    [InlineData("AMD Ryzen 5 5600X 6-Core Processor", "AMD Ryzen 5 5600X")]
    [InlineData("Intel(R) Core(TM) i7-12700H", "Intel Core i7-12700H")]
    [InlineData("13th Gen Intel(R) Core(TM) i5-13600K", "13th Gen Intel Core i5-13600K")]
    [InlineData("Intel(R) Core(TM) i7-8550U CPU @ 1.80GHz", "Intel Core i7-8550U @ 1.80GHz")]
    public void The_processor_name_is_tidied_without_losing_information(string raw, string expected) => Assert.Equal(expected, AboutFormatter.CleanCpu(raw));

    [Fact]
    public void Missing_values_are_marked_not_available_everywhere_and_never_filled_in()
    {
        var empty = new AboutRaw(null, null, null, null, null, null, null, null, null, [], null, null, null, null, null, null, "2.0.0");
        var v = AboutFormatter.Build(empty);
        Assert.Equal("Este PC", v.Title);
        Assert.All(v.Main.Where(r => r.Label != "Hatsu Desktop"), r => { Assert.False(r.Available); Assert.Equal("No disponible", r.Value); });
        Assert.All(v.More, r => Assert.False(r.Available));
    }

    [Fact]
    public void The_view_never_contains_sensitive_identifiers()
    {
        var all = string.Join("|", AboutFormatter.Build(Raw()).Main.Concat(AboutFormatter.Build(Raw()).More).Select(r => r.Label.ToLowerInvariant()));
        Assert.DoesNotContain("serie", all); Assert.DoesNotContain("serial", all); Assert.DoesNotContain("uuid", all); Assert.DoesNotContain("id. del producto", all);
    }
}

public class AboutLayoutTests
{
    [Theory]
    [InlineData(5, false)] [InlineData(5, true)] [InlineData(11, true)]
    public void Rows_do_not_overlap_and_everything_fits_inside_the_window(int rows, bool sub)
    {
        var f = AboutLayout.Compute(28, sub, Enumerable.Repeat(22f, rows).ToList());
        Assert.True(f.Icon.Bottom <= f.Title.Y); Assert.True(f.Title.Bottom <= (sub ? f.Subtitle.Y : f.Rows[0].Y));
        for (int i = 1; i < f.Rows.Count; i++) Assert.True(f.Rows[i - 1].Bottom < f.Rows[i].Y);
        Assert.True(f.Rows[^1].Bottom < f.SeparatorY); Assert.True(f.SeparatorY < f.MoreButton.Y); Assert.True(f.MoreButton.Bottom < f.WindowsLink.Y); Assert.True(f.WindowsLink.Bottom < f.Height);
        Assert.True(f.MoreButton.Right <= f.Width);
    }

    [Fact]
    public void A_wrapped_value_makes_its_row_and_the_window_taller()
    {
        var a = AboutLayout.Compute(28, true, [22, 22, 22]); var b = AboutLayout.Compute(28, true, [22, 40, 22]);
        Assert.Equal(a.Height + 18, b.Height, 2); Assert.True(b.Rows[2].Y > a.Rows[2].Y);
    }

    [Fact]
    public void The_window_is_compact()
    {
        var f = AboutLayout.Compute(28, true, Enumerable.Repeat(22f, 5).ToList()); Assert.InRange(f.Height, 300, 460); Assert.InRange(f.Width, 340, 440);   // «Acerca de este Mac»: pequeña y sin saturar
    }
}
