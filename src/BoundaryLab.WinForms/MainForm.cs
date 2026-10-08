using System.Text;
using BoundaryLab.Core;

namespace BoundaryLab.WinForms;

public sealed class MainForm : Form
{
    private readonly PhoneticFirstSession _session = new();
    private PhoneticFirstAnalysisResult _result =
        new("", "", 0, [], [], []);

    private readonly TextBox _input = new();
    private readonly TextBox _committed = new();
    private readonly TextBox _active = new();
    private readonly TextBox _phonetic = new();
    private readonly TextBox _groups = new();
    private readonly TextBox _converted = new();
    private readonly Label _summary = new();

    private readonly DataGridView _phoneticUnits = Grid();
    private readonly DataGridView _segments = Grid();
    private readonly DataGridView _candidates = Grid();
    private readonly DataGridView _timeline = Grid();

    public MainForm()
    {
        Text = "Incremental Boundary Lab — phonetic-first v0.3";
        Width = 1320;
        Height = 900;
        MinimumSize = new Size(1000, 720);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 11,
            Padding = new Padding(12)
        };

        for (var i = 0; i < 9; i++)
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            Text = "Phonetic-first: ①まず音として読む → ②読みにくい部分を英語/日本語へ再解釈 → ③確定prefixを凍結",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        });

        _input.Dock = DockStyle.Top;
        _input.Font = new Font("Consolas", 15);
        _input.PlaceholderText = "seidohasugokuiikannzininattakaramethodtositeha...";
        _input.TextChanged += (_, _) => AnalyzeInput();
        _input.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsAsciiLetter(e.KeyChar))
                e.Handled = true;
        };
        root.Controls.Add(_input);

        root.Controls.Add(MakeLabeled("確定済みprefix", _committed));
        root.Controls.Add(MakeLabeled("Active Window", _active));
        root.Controls.Add(MakeLabeled("Stage 1 音写", _phonetic));
        root.Controls.Add(MakeLabeled("Stage 2 まとまり", _groups));
        root.Controls.Add(MakeLabeled("最終表示", _converted));

        _summary.AutoSize = true;
        _summary.Padding = new Padding(0, 4, 0, 4);
        root.Controls.Add(_summary);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "確定済みprefixは末尾追加時に再解析しません。削除/途中編集時のみ安全のため全履歴を再構築します。"
        });

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(Page("Stage 1 phonetic units", _phoneticUnits));
        tabs.TabPages.Add(Page("Stage 2 segments", _segments));
        tabs.TabPages.Add(Page("Stage 2 candidates", _candidates));
        tabs.TabPages.Add(Page("Incremental workload", _timeline));
        root.Controls.Add(tabs);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Fill
        };

        var basic = new Button { Text = "基本例", AutoSize = true };
        basic.Click += (_, _) => _input.Text = "kyouhacommitsimasita";
        actions.Controls.Add(basic);

        var method = new Button { Text = "method研究例", AutoSize = true };
        method.Click += (_, _) =>
            _input.Text = "seidohasugokuiikannzininattakaramethodtositehakonnnakannzideiikamo";
        actions.Controls.Add(method);

        var longExample = new Button { Text = "長文例", AutoSize = true };
        longExample.Click += (_, _) =>
            _input.Text = "kyouhacommitasitanimotikosunogamenndoudattakarakousitayo";
        actions.Controls.Add(longExample);

        var export = new Button { Text = "phonetic-first研究JSONを書き出す", AutoSize = true };
        export.Click += (_, _) => ExportJson();
        actions.Controls.Add(export);

        root.Controls.Add(actions);
        Controls.Add(root);
        Render();
    }

    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false
    };

    private static Control MakeLabeled(string label, TextBox box)
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 2
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        panel.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 7, 0, 0)
        }, 0, 0);

        box.ReadOnly = true;
        box.Dock = DockStyle.Fill;
        box.Font = new Font("Consolas", 11);
        panel.Controls.Add(box, 1, 0);
        return panel;
    }

    private static TabPage Page(string title, Control control)
    {
        var page = new TabPage(title);
        page.Controls.Add(control);
        return page;
    }

    private void AnalyzeInput()
    {
        var caret = _input.SelectionStart;
        var filtered = new string(_input.Text
            .Where(char.IsAsciiLetter)
            .Select(char.ToLowerInvariant)
            .ToArray());

        if (_input.Text != filtered)
        {
            _input.Text = filtered;
            _input.SelectionStart = Math.Min(caret, filtered.Length);
            return;
        }

        _result = _session.Update(filtered);
        Render();
    }

    private void Render()
    {
        if (_result.Frames.Count == 0)
        {
            _committed.Text = "";
            _active.Text = "";
            _phonetic.Text = "";
            _groups.Text = "";
            _converted.Text = "";
            _summary.Text = "入力待ち";
            _phoneticUnits.DataSource = null;
            _segments.DataSource = null;
            _candidates.DataSource = null;
            _timeline.DataSource = null;
            return;
        }

        var last = _result.Frames[^1];
        _committed.Text =
            $"{_result.Input[.._result.CommittedRawLength]}  →  " +
            string.Concat(_result.CommittedSegments.Select(s => s.Output));
        _active.Text = last.ActiveRaw;
        _phonetic.Text = last.Stage1.Preview;
        _groups.Text = string.Join(
            " | ",
            _result.CommittedSegments.Concat(_result.ActiveSegments).Select(s => s.Raw));
        _converted.Text = _result.Output;

        var total = last.TotalAnalyzedCharacters;
        var naive = (long)_result.Input.Length * (_result.Input.Length + 1) / 2;
        var reduction = naive == 0 ? 0 : 1.0 - (double)total / naive;

        _summary.Text =
            $"入力 {_result.Input.Length}文字 / 確定prefix {_result.CommittedRawLength}文字 / " +
            $"Active {last.ActiveRaw.Length}文字 / 今回解析 {last.AnalyzedCharactersThisStep}文字 / " +
            $"累積解析 {total}文字 / 全prefix再解析比 {reduction:P1}削減";

        _phoneticUnits.DataSource = last.Stage1.Units.Select(u => new
        {
            Range = $"{u.Start}..{u.End}",
            u.Raw,
            u.Preview,
            Kind = u.Kind.ToString(),
            Confidence = u.Confidence.ToString("P0"),
            u.Reason
        }).ToList();

        _segments.DataSource = _result.CommittedSegments
            .Concat(_result.ActiveSegments)
            .Select(s => new
            {
                Range = $"{s.Start}..{s.End}",
                s.Raw,
                s.Output,
                Language = s.Language.ToString(),
                Status = s.Confirmed ? "Frozen" : "Active",
                Confidence = s.Confidence.ToString("P1"),
                s.DecisionReason
            }).ToList();

        _candidates.DataSource = last.Stage2Candidates.Select(c => new
        {
            Range = $"{c.Start}..{c.End}",
            c.Raw,
            c.Output,
            Language = c.Language.ToString(),
            Score = c.Score.ToString("F2"),
            Phonetic = c.PhoneticConfidence.ToString("P1"),
            Lexical = c.LexicalConfidence.ToString("P1"),
            c.Reason
        }).ToList();

        _timeline.DataSource = _result.Frames.Select(f => new
        {
            f.Step,
            f.CommittedRawLength,
            ActiveLength = f.ActiveRaw.Length,
            Stage1 = f.Stage1.Preview,
            f.Output,
            Work = f.AnalyzedCharactersThisStep,
            TotalWork = f.TotalAnalyzedCharacters,
            Frozen = f.FrozenThisStep.Count,
            f.Rebuilt
        }).ToList();
    }

    private void ExportJson()
    {
        if (_result.Input.Length == 0)
        {
            MessageBox.Show("先に文字列を入力してください。", "Incremental Boundary Lab");
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "Research JSON (*.json)|*.json",
            FileName = $"boundary-lab-phonetic-first-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            AddExtension = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var report = PhoneticFirstResearchExporter.CreateReport(
            _result,
            _session.Parameters);
        File.WriteAllText(
            dialog.FileName,
            PhoneticFirstResearchExporter.ToJson(report),
            new UTF8Encoding(false));
        MessageBox.Show(
            "phonetic-first研究データを書き出しました。",
            "Incremental Boundary Lab");
    }
}
