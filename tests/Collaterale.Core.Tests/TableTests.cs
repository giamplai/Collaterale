using Collaterale.Core.Table;

namespace Collaterale.Core.Tests;

public class CsvParserTests
{
    [Fact]
    public void Plain_rows_and_both_newline_styles()
    {
        Assert.Equal([["a", "b"], ["c", "d"]], CsvParser.ParseRows("a,b\nc,d\n", ','));
        Assert.Equal([["a", "b"], ["c", "d"]], CsvParser.ParseRows("a,b\r\nc,d", ','));
        Assert.Equal([["a"], ["b"]], CsvParser.ParseRows("a\rb\r", ','));
    }

    [Fact]
    public void Quoted_fields_keep_delimiters_quotes_and_line_breaks()
    {
        var rows = CsvParser.ParseRows("id,note\n1,\"hello, \"\"world\"\"\"\n2,\"line one\nline two\"\n", ',');
        Assert.Equal(3, rows.Count);
        Assert.Equal("hello, \"world\"", rows[1][1]);
        Assert.Equal("line one\nline two", rows[2][1]);
    }

    [Fact]
    public void Empty_fields_and_ragged_rows_are_preserved()
    {
        var rows = CsvParser.ParseRows("a,,c\nx\n,,\n", ',');
        Assert.Equal(["a", "", "c"], rows[0]);
        Assert.Equal(["x"], rows[1]);
        Assert.Equal(["", "", ""], rows[2]);
    }

    [Fact]
    public void A_final_line_without_newline_and_an_empty_file()
    {
        Assert.Equal([["a", "b"]], CsvParser.ParseRows("a,b", ','));
        Assert.Empty(CsvParser.ParseRows("", ','));
        Assert.Equal([[""]], CsvParser.ParseRows("\"\"", ','));   // a quoted empty field is a real (empty) cell
    }

    [Theory]
    [InlineData("a,b,c\n1,2,3\n4,5,6\n", ',')]
    [InlineData("a;b;c\n1;2;3\n4;5;6\n", ';')]
    [InlineData("a\tb\tc\n1\t2\t3\n", '\t')]
    [InlineData("a|b|c\n1|2|3\n", '|')]
    [InlineData("name;price\nWidget, large;9,50\nGadget;3,20\n", ';')]   // commas inside the data must not win over the consistent semicolon
    public void Delimiter_is_detected(string text, char expected) => Assert.Equal(expected, CsvParser.Detect(text));

    [Fact]
    public void Parse_returns_the_delimiter_and_column_count()
    {
        var t = CsvParser.Parse("a;b;c\n1;2\n");
        Assert.Equal(';', t.Delimiter);
        Assert.Equal(3, t.ColumnCount);
    }

    [Fact]
    public void Escaping_roundtrips_through_the_parser()
    {
        foreach (var v in new[] { "plain", "with,comma", "with \"quotes\"", "multi\nline", "" })
        {
            var csv = CsvParser.Escape(v, ',') + ",tail\n";
            Assert.Equal([v, "tail"], CsvParser.ParseRows(csv, ',')[0]);
        }
    }
}

public class TableComparisonTests
{
    private static ParsedTable T(string csv, char d = ',') => CsvParser.Parse(csv, d);

    private const string Customers =
        "id,name,city,balance\n1,Ann,Rome,100.0\n2,Bob,Milan,250.5\n3,Cid,Turin,75\n";

    [Fact]
    public void Identical_tables_are_equal()
    {
        var c = TableComparison.Compare(T(Customers), T(Customers));
        Assert.True(c.AreEqual);
        Assert.Equal(["id", "name", "city", "balance"], c.ColumnNames);
        Assert.All(c.Rows, r => Assert.Equal(TableRowKind.Same, r.Kind));
    }

    [Fact]
    public void Changed_cells_are_flagged_per_column()
    {
        var right = "id,name,city,balance\n1,Ann,Rome,100.0\n2,Bob,Naples,260\n3,Cid,Turin,75\n";
        var c = TableComparison.Compare(T(Customers), T(right));
        var changed = Assert.Single(c.Rows, r => r.Kind == TableRowKind.Changed);
        Assert.Equal([false, false, true, true], changed.CellDiffs);
        Assert.Equal(2, c.ChangedCells);
        Assert.Equal("Naples", c.Cell(Array.IndexOf(c.Rows.ToArray(), changed), 2, left: false));
    }

    [Fact]
    public void Inserted_and_deleted_rows_are_aligned_by_content()
    {
        var right = "id,name,city,balance\n1,Ann,Rome,100.0\n1b,New,Pisa,5\n2,Bob,Milan,250.5\n";
        var c = TableComparison.Compare(T(Customers), T(right));
        Assert.Equal([TableRowKind.Same, TableRowKind.RightOnly, TableRowKind.Same, TableRowKind.LeftOnly], c.Rows.Select(r => r.Kind));
        Assert.Equal((1, 1, 1), (c.LeftOnlyRows, c.RightOnlyRows, 1));
    }

    [Fact]
    public void Key_columns_match_rows_regardless_of_order()
    {
        var shuffled = "id,name,city,balance\n3,Cid,Turin,75\n1,Ann,Rome,100.0\n2,Bob,Milan,999\n";
        var byKey = TableComparison.Compare(T(Customers), T(shuffled), new TableOptions { KeyColumns = [0] });
        Assert.Equal(1, byKey.ChangedRows);                     // only Bob's balance differs
        Assert.Equal(0, byKey.LeftOnlyRows + byKey.RightOnlyRows);
        var bob = byKey.Rows.Single(r => r.Kind == TableRowKind.Changed);
        Assert.Equal([false, false, false, true], bob.CellDiffs);

        var byContent = TableComparison.Compare(T(Customers), T(shuffled));
        Assert.True(byContent.ChangedRows + byContent.LeftOnlyRows + byContent.RightOnlyRows > 1);   // without a key the reorder is noise
    }

    [Fact]
    public void Key_mode_reports_rows_missing_on_either_side_and_handles_duplicate_keys_in_order()
    {
        var left = "id,v\n1,a\n2,b\n2,c\n9,z\n";
        var right = "id,v\n1,a\n2,b\n2,CHANGED\n5,new\n";
        var c = TableComparison.Compare(T(left), T(right), new TableOptions { KeyColumns = [0] });
        Assert.Equal(1, c.LeftOnlyRows);                       // id 9
        Assert.Equal(1, c.RightOnlyRows);                      // id 5
        Assert.Equal(1, c.ChangedRows);                        // the second "2" row
    }

    [Fact]
    public void Numeric_tolerance_treats_equal_numbers_as_equal()
    {
        var right = "id,name,city,balance\n1,Ann,Rome,100\n2,Bob,Milan,250.5004\n3,Cid,Turin,75\n";
        Assert.Equal(2, TableComparison.Compare(T(Customers), T(right)).ChangedRows);                                       // exact text
        Assert.Equal(1, TableComparison.Compare(T(Customers), T(right), new TableOptions { NumericTolerance = 0 }).ChangedRows);       // 100.0 == 100
        Assert.Equal(0, TableComparison.Compare(T(Customers), T(right), new TableOptions { NumericTolerance = 0.001 }).ChangedRows);   // 250.5 ~ 250.5004
    }

    [Fact]
    public void Case_and_whitespace_options_apply_to_cells()
    {
        var right = "id,name,city,balance\n1,ANN,Rome ,100.0\n2,Bob,Milan,250.5\n3,Cid,Turin,75\n";
        Assert.Equal(1, TableComparison.Compare(T(Customers), T(right)).ChangedRows);
        Assert.Equal(1, TableComparison.Compare(T(Customers), T(right), new TableOptions { IgnoreCase = true }).ChangedRows);   // "Rome " still differs
        Assert.Equal(0, TableComparison.Compare(T(Customers), T(right), new TableOptions { IgnoreCase = true, TrimWhitespace = true }).ChangedRows);
    }

    [Fact]
    public void Ignored_columns_are_not_compared()
    {
        var right = "id,name,city,balance\n1,Ann,Rome,999\n2,Bob,Milan,250.5\n3,Cid,Turin,75\n";
        Assert.Equal(1, TableComparison.Compare(T(Customers), T(right)).ChangedRows);
        Assert.Equal(0, TableComparison.Compare(T(Customers), T(right), new TableOptions { IgnoredColumns = new HashSet<int> { 3 } }).ChangedRows);
    }

    [Fact]
    public void Columns_can_be_matched_by_header_when_they_are_reordered_or_missing()
    {
        var right = "balance,id,name,extra\n100.0,1,Ann,x\n250.5,2,Bob,y\n75,3,Cid,z\n";
        var byPos = TableComparison.Compare(T(Customers), T(right));
        Assert.True(byPos.ChangedRows > 0);                     // positions do not line up

        var byHeader = TableComparison.Compare(T(Customers), T(right), new TableOptions { Columns = ColumnMatch.Header });
        Assert.Equal(["id", "name", "city", "balance", "extra"], byHeader.ColumnNames);
        Assert.True(byHeader.ColumnsDiffer);                    // "city" only on the left, "extra" only on the right
        Assert.Null(byHeader.LeftColumn[4]);
        Assert.Null(byHeader.RightColumn[2]);
        Assert.Equal(0, byHeader.ChangedRows);                  // every shared column matches once aligned by name
    }

    [Fact]
    public void Without_a_header_columns_are_named_by_letter()
    {
        var c = TableComparison.Compare(T("1,2\n3,4\n"), T("1,2\n3,5\n"), new TableOptions { HasHeader = false });
        Assert.Equal(["A", "B"], c.ColumnNames);
        Assert.Equal(1, c.ChangedRows);
        Assert.Equal(2, c.Rows.Count);                          // no row was swallowed as a header
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void Spreadsheet_style_column_letters(int index, string expected) => Assert.Equal(expected, TableComparison.ColumnLetter(index));

    [Fact]
    public void Tables_with_different_widths_compare_missing_cells_as_blank()
    {
        var c = TableComparison.Compare(T("a,b,c\n1,2,3\n"), T("a,b\n1,2\n"));
        Assert.Equal(3, c.ColumnCount);
        Assert.Equal(1, c.ChangedRows);                         // the third column exists only on the left
        Assert.Null(c.Cell(0, 2, left: false));
    }

    [Fact]
    public void Large_tables_compare_quickly()
    {
        var sb1 = new System.Text.StringBuilder("id,a,b,c\n");
        var sb2 = new System.Text.StringBuilder("id,a,b,c\n");
        for (var i = 0; i < 50_000; i++)
        {
            sb1.Append($"{i},x{i},{i * 2},y\n");
            sb2.Append($"{i},x{i},{(i % 5000 == 0 ? -1 : i * 2)},y\n");
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var c = TableComparison.Compare(T(sb1.ToString()), T(sb2.ToString()), new TableOptions { KeyColumns = [0] });
        sw.Stop();
        Assert.Equal(10, c.ChangedRows);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"took {sw.ElapsedMilliseconds} ms");
    }
}
