using Collaterale.Core.Cli;

namespace Collaterale.Core.Tests;

public class CompareRouterTests
{
    private static string Write(string name, byte[] bytes)
    {
        var dir = Path.Combine(Path.GetTempPath(), "collaterale-rt-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, name);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    private static string Text(string name, string content) => Write(name, System.Text.Encoding.UTF8.GetBytes(content));

    [Fact]
    public void Pictures_go_to_picture_compare_by_extension_ignoring_case()
    {
        Assert.Equal(LaunchKind.ImageCompare, CompareRouter.ForFiles(Write("a.PNG", [1, 2]), Write("b.jpeg", [3, 4])));
        Assert.Equal(LaunchKind.ImageCompare, CompareRouter.ForFiles(Write("a.webp", [1]), Write("b.ico", [2])));
    }

    [Fact]
    public void Delimited_files_go_to_table_compare()
    {
        Assert.Equal(LaunchKind.TableCompare, CompareRouter.ForFiles(Text("a.csv", "a,b\n1,2\n"), Text("b.tsv", "a\tb\n1\t2\n")));
    }

    [Fact]
    public void Binary_files_go_to_hex_compare_and_text_stays_text()
    {
        Assert.Equal(LaunchKind.HexCompare, CompareRouter.ForFiles(Write("a.bin", [1, 0, 2]), Write("b.bin", [1, 0, 3])));
        Assert.Equal(LaunchKind.HexCompare, CompareRouter.ForFiles(Text("a.txt", "plain"), Write("b.dat", [0, 0, 0])));   // one binary side is enough
        Assert.Equal(LaunchKind.TextCompare, CompareRouter.ForFiles(Text("a.cs", "class A {}"), Text("b.cs", "class B {}")));
    }

    [Fact]
    public void Utf16_text_is_not_mistaken_for_binary()
    {
        var utf16 = System.Text.Encoding.Unicode.GetPreamble().Concat(System.Text.Encoding.Unicode.GetBytes("hello\nworld")).ToArray();
        Assert.Equal(LaunchKind.TextCompare, CompareRouter.ForFiles(Write("a.txt", utf16), Write("b.txt", utf16)));
    }

    [Fact]
    public void A_picture_extension_on_only_one_side_falls_back_to_content_sniffing()
    {
        Assert.Equal(LaunchKind.TextCompare, CompareRouter.ForFiles(Text("a.png", "i am text"), Text("b.txt", "me too")));
    }

    [Fact]
    public void Unreadable_or_missing_files_default_to_text_so_the_view_can_explain()
        => Assert.Equal(LaunchKind.TextCompare, CompareRouter.ForFiles("/no/such/a.txt", "/no/such/b.txt"));

    [Fact]
    public void The_command_line_routes_automatically_and_flags_override()
    {
        var (a, b) = (Write("x.png", [1]), Write("y.png", [2]));
        Assert.Equal(LaunchKind.ImageCompare, LaunchParser.Parse([a, b]).Kind);
        Assert.Equal(LaunchKind.TextCompare, LaunchParser.Parse(["--text", a, b]).Kind);
        Assert.Equal(LaunchKind.HexCompare, LaunchParser.Parse(["--hex", a, b]).Kind);
        var (c, d) = (Text("p.csv", "a,b\n"), Text("q.csv", "a,b\n"));
        Assert.Equal(LaunchKind.TableCompare, LaunchParser.Parse([c, d]).Kind);
        Assert.Equal(LaunchKind.TextCompare, LaunchParser.Parse(["--text", c, d]).Kind);
    }
}
