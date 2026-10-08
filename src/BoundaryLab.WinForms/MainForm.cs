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
    private readonly DataGridView _symbols = Grid();
    private readonly DataGridView _ripple = Grid();
    private readonly DataGridView _timeline = Grid();

    public MainForm()
    {
        Text = "Incremental Boundary Lab — phonetic-first + RCR v0.4";
        Width = 1380;
        Height = 920;
        MinimumSize = new Size(1040, 740);
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
            Text = "v0.4: ①音写 → ②局所再解釈 → ③矛盾が出たらSoftFrozenだけ局所解凍",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        });

        _input.Dock = DockStyle.Top;
        _input.Font = new Font("Consolas", 15);
        _input.PlaceholderText = "node.jswotukau / kyouzyuuni...";
        _input.TextChanged += (_, _) => AnalyzeInput();
        _input.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) &&
                !InputSyntax.IsAllowed(e.KeyChar))
                e.Handled = true;
        };
        root.Controls.Add(_input);

        root.Controls.Add(MakeLabeled("Frozen prefix", _committed));
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
            Text = "SoftFrozenは近傍のUnknown/記号で解凍可能。HardFrozenは十分な先読み後のみ固定。ピリオドやアンダースコア等はLatin構造の証拠として利用します。"
        });

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(Page("Stage 1 phonetic", _phoneticUnits));
        tabs.TabPages.Add(Page("Segments / freeze", _segments));
        tabs.TabPages.Add(Page("Stage 2 candidates", _candidates));
        tabs.TabPages.Add(Page("Orthographic evidence", _symbols));
        tabs.TabPages.Add(Page("RCR events", _ripple));
        tabs.TabPages.Add(Page("Workload", _timeline));
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

        var node = new Button { Text = "node.js例", AutoSize = true };
        node.Click += (_, _) => _input.Text = "node.jswotukau";
        actions.Controls.Add(node);

        var ripple = new Button { Text = "Ripple例", AutoSize = true };
        ripple.Click += (_, _) => _input.Text = "kyouzyuunitaiousiteoitayo";
        actions.Controls.Add(ripple);

        var export = new Button { Text = "RCR研究JSONを書き出す", AutoSize = true };
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
        var filtered = new string(
            _input.Text
                .Where(InputSyntax.IsAllowed)
                .ToArray());

        filtered = InputSyntax.Normalize(filtered);

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
            _symbols.DataSource = null;
            _ripple.DataSource = null;
            _timeline.DataSource = null;
            return;
        }

        var last = _result.Frames[^1];
        _committed.Text = string.Join(
            " | ",
            _result.CommittedSegments.Select(s =>
                s.Raw + ":" +
                (s.FreezeState == FreezeState.HardFrozen ? "H" : "S")));
        _active.Text = last.ActiveRaw;
        _phonetic.Text = last.Stage1.Preview;
        _groups.Text = string.Join(
            " | ",
            _result.CommittedSegments
                .Concat(_result.ActiveSegments)
                .Select(s => s.Raw));
        _converted.Text = _result.Output;

        var total = last.TotalAnalyzedCharacters;
        var naive =
            (long)_result.Input.Length *
            (_result.Input.Length + 1) / 2;
        var reduction =
            naive == 0 ? 0 : 1.0 - (double)total / naive;

        var soft = _result.CommittedSegments.Count(s =>
            s.FreezeState == FreezeState.SoftFrozen);
        var hard = _result.CommittedSegments.Count(s =>
            s.FreezeState == FreezeState.HardFrozen);

        _summary.Text =
            "入力 " + _result.Input.Length +
            " / Active " + last.ActiveRaw.Length +
            " / Soft " + soft +
            " / Hard " + hard +
            " / Ripple " + last.RippleEvents.Count +
            " / Thaw " + last.ThawEvents.Count +
            " / 今回解析 " + last.AnalyzedCharactersThisStep +
            " / 累積 " + total +
            " / 全文prefix比 " + reduction.ToString("P1") + "削減";

        _phoneticUnits.DataSource = last.Stage1.Units.Select(u => new
        {
            Range = u.Start + ".." + u.End,
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
                Range = s.Start + ".." + s.End,
                s.Raw,
                s.Output,
                Language = s.Language.ToString(),
                Freeze = s.FreezeState.ToString(),
                Confidence = s.Confidence.ToString("P1"),
                ContextPenalty = s.ContextPenalty.ToString("P1"),
                s.DecisionReason
            }).ToList();

        _candidates.DataSource = last.Stage2Candidates.Select(c => new
        {
            Range = c.Start + ".." + c.End,
            c.Raw,
            c.Output,
            Language = c.Language.ToString(),
            Score = c.Score.ToString("F2"),
            Phonetic = c.PhoneticConfidence.ToString("P1"),
            Lexical = c.LexicalConfidence.ToString("P1"),
            c.Reason
        }).ToList();

        _symbols.DataSource = last.SymbolEvidence.Select(e => new
        {
            Range = e.Start + ".." + e.End,
            e.Raw,
            e.Kind,
            Confidence = e.Confidence.ToString("P1"),
            e.Complete,
            e.Reason
        }).ToList();

        var eventRows = new List<RcrEventRow>();
        eventRows.AddRange(last.RippleEvents.Select(e => new RcrEventRow(
            "Ripple",
            e.SourceStart + ".." + e.SourceEnd,
            e.SourceKind + " -> " + e.AffectedStart + ".." + e.AffectedEnd,
            e.Strength.ToString("P0"),
            e.Reason)));
        eventRows.AddRange(last.ThawEvents.Select(e => new RcrEventRow(
            "Thaw",
            e.Start + ".." + e.End,
            string.Join("|", e.RawSegments),
            "",
            e.Reason)));
        eventRows.AddRange(last.FreezeTransitions.Select(e => new RcrEventRow(
            "Freeze",
            e.Start + ".." + e.End,
            e.Raw + ": " + e.From + " -> " + e.To,
            "",
            e.Reason)));
        _ripple.DataSource = eventRows;

        _timeline.DataSource = _result.Frames.Select(f => new
        {
            f.Step,
            f.CommittedRawLength,
            ActiveLength = f.ActiveRaw.Length,
            f.Output,
            Work = f.AnalyzedCharactersThisStep,
            TotalWork = f.TotalAnalyzedCharacters,
            Ripple = f.RippleEvents.Count,
            Thaw = f.ThawEvents.Count,
            FreezeChanges = f.FreezeTransitions.Count,
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
            FileName = "boundary-lab-rcr-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json",
            AddExtension = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var report =
            PhoneticFirstResearchExporter.CreateReport(
                _result,
                _session.Parameters);
        File.WriteAllText(
            dialog.FileName,
            PhoneticFirstResearchExporter.ToJson(report),
            new UTF8Encoding(false));

        MessageBox.Show(
            "RCR研究データを書き出しました。",
            "Incremental Boundary Lab");
    }

    private sealed record RcrEventRow(
        string Type,
        string Range,
        string Detail,
        string Strength,
        string Reason);
}
