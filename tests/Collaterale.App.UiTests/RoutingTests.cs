using Avalonia.Headless.XUnit;
using Collaterale.App.ViewModels;
using Collaterale.Core.Imaging;

namespace Collaterale.App.UiTests;

public class RoutingTests
{
    private static (string Left, string Right) Folders(Action<string, string> fill)
    {
        var root = Path.Combine(Path.GetTempPath(), "collaterale-route-" + Guid.NewGuid().ToString("N")[..8]);
        var (l, r) = (Path.Combine(root, "L"), Path.Combine(root, "R"));
        Directory.CreateDirectory(l); Directory.CreateDirectory(r);
        fill(l, r);
        return (l, r);
    }

    private static void Png(string path, byte shade)
        => File.WriteAllBytes(path, ImageLoader.EncodePng(RgbaImage.Create(16, 16, (_, _) => (shade, shade, shade, 255))));

    [AvaloniaFact]
    public async Task Double_clicking_files_in_a_folder_compare_opens_the_matching_view()
    {
        var (l, r) = Folders((a, b) =>
        {
            Png(Path.Combine(a, "logo.png"), 10); Png(Path.Combine(b, "logo.png"), 200);
            File.WriteAllText(Path.Combine(a, "data.csv"), "id,v\n1,a\n"); File.WriteAllText(Path.Combine(b, "data.csv"), "id,v\n1,b\n");
            File.WriteAllBytes(Path.Combine(a, "blob.bin"), [1, 0, 2]); File.WriteAllBytes(Path.Combine(b, "blob.bin"), [1, 0, 9]);
            File.WriteAllText(Path.Combine(a, "readme.txt"), "hello"); File.WriteAllText(Path.Combine(b, "readme.txt"), "hello world");
        });
        var shell = TextCompareTests.NewShell();
        var folders = shell.NewFolderCompare();
        folders.Criteria = Collaterale.Core.Folders.CompareCriteria.Content;
        await folders.OpenAsync(l, r);

        folders.Activate(folders.Rows.Single(x => x.Name == "logo.png"));
        Assert.IsType<ImageCompareViewModel>(shell.SelectedTab!.Content);
        folders.Activate(folders.Rows.Single(x => x.Name == "data.csv"));
        Assert.IsType<TableCompareViewModel>(shell.SelectedTab!.Content);
        folders.Activate(folders.Rows.Single(x => x.Name == "blob.bin"));
        Assert.IsType<HexCompareViewModel>(shell.SelectedTab!.Content);
        folders.Activate(folders.Rows.Single(x => x.Name == "readme.txt"));
        Assert.IsType<TextCompareViewModel>(shell.SelectedTab!.Content);
    }

    [AvaloniaFact]
    public void Dropping_two_pictures_opens_a_picture_compare()
    {
        var (l, r) = Folders((a, b) => { Png(Path.Combine(a, "x.png"), 1); Png(Path.Combine(b, "x.png"), 2); });
        var shell = TextCompareTests.NewShell();
        shell.Drop([Path.Combine(l, "x.png"), Path.Combine(r, "x.png")]);
        Assert.IsType<ImageCompareViewModel>(shell.SelectedTab!.Content);
    }

    [AvaloniaFact]
    public void Recent_entries_reopen_in_the_same_kind_of_view()
    {
        var (l, r) = Folders((a, b) => { Png(Path.Combine(a, "x.png"), 1); Png(Path.Combine(b, "x.png"), 2); });
        var shell = TextCompareTests.NewShell();
        shell.OpenFilePair(Path.Combine(l, "x.png"), Path.Combine(r, "x.png"));
        var recent = shell.Settings.Recent[0];
        Assert.Equal(Collaterale.Core.Cli.LaunchKind.ImageCompare, recent.Kind);
        shell.OpenRecentCommand.Execute(recent);
        Assert.IsType<ImageCompareViewModel>(shell.SelectedTab!.Content);
    }

    [AvaloniaFact]
    public async Task Opening_two_archives_compares_their_contents_as_folders()
    {
        var (l, r) = Folders((_, _) => { });
        string Zip(string dir, string text)
        {
            var p = Path.Combine(dir, "pack.zip");
            using var z = System.IO.Compression.ZipFile.Open(p, System.IO.Compression.ZipArchiveMode.Create);
            using var w = new StreamWriter(z.CreateEntry("inside/doc.txt").Open());
            w.Write(text);
            return p;
        }

        var shell = TextCompareTests.NewShell();
        var vm = Assert.IsType<FolderCompareViewModel>(shell.OpenFilePair(Zip(l, "one"), Zip(r, "three")));
        for (var i = 0; i < 100 && vm.Comparison is null; i++) { await Task.Delay(50); }
        Assert.NotNull(vm.Comparison);
        Assert.Equal(1, vm.Comparison!.Summary.Different);
    }
}
