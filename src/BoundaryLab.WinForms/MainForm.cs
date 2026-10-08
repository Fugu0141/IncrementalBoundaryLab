using System.Text;
using BoundaryLab.Core;

namespace BoundaryLab.WinForms;

public sealed class MainForm : Form
{
    private readonly IncrementalRecognizer _recognizer = new();
    private AnalysisResult _result = new("", "", [], [], []);

    private readonly TextBox _input = new();
    private readonly TextBox _rawGroups = new();
    private readonly TextBox _converted = new();
    private readonly Label _summary = new();
    private readonly DataGridView _segments = Grid();
    private readonly DataGridView _boundaries = Grid();
    private readonly DataGridView _timeline = Grid();
    private readonly DataGridView _hypotheses = Grid();

    public MainForm()
    {
        Text = "Incremental Boundary Lab — consensus v0.2";
        Width = 1280;
        Height = 860;
        MinimumSize = new Size(980, 680);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            Text = "ASCII letters only — ローマ字/英単語を空白なしで入力",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        });

        _input.Dock = DockStyle.Top;
        _input.Font = new Font("Consolas", 15);
        _input.PlaceholderText = "kyouhacommitsimasita";
        _input.TextChanged += (_, _) => AnalyzeInput();
        _input.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsAsciiLetter(e.KeyChar))
                e.Handled = true;
        };
        root.Controls.Add(_input);

        root.Controls.Add(MakeLabeled("推測されたまとまり", _rawGroups));
        root.Controls.Add(MakeLabeled("変換結果", _converted));

        _summary.AutoSize = true;
        _summary.Padding = new Padding(0, 4, 0, 4);
        root.Controls.Add(_summary);

        var hint = new Label
        {
            AutoSize = true,
            Text = "確定 = Beam / 双方向構造 / 辞書局所 / 時系列安定性の複数系統が合意し、解釈側も独立証拠が2つ以上一致した場合のみ。"
        };
        root.Controls.Add(hint);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(Page("Segments / interpretation", _segments));
        tabs.TabPages.Add(Page("Boundary consensus", _boundaries));
        tabs.TabPages.Add(Page("Incremental timeline", _timeline));
        tabs.TabPages.Add(Page("Top hypotheses", _hypotheses));
        root.Controls.Add(tabs);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Fill
        };

        var example = new Button { Text = "基本例", AutoSize = true };
        example.Click += (_, _) => _input.Text = "kyouhacommitsimasita";
        actions.Controls.Add(example);

        var researchExample = new Button { Text = "長文研究例", AutoSize = true };
        researchExample.Click += (_, _) =>
            _input.Text = "kyouhacommitasitanisitakunakattakaraimamergesityattayo";
        actions.Controls.Add(researchExample);

        var export = new Button { Text = "研究データをJSONに書き出す", AutoSize = true };
        export.Click += (_, _) => ExportJson();
        actions.Controls.Add(export);

        root.Controls.Add(actions);
        Controls.Add(root);
        AnalyzeInput();
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

        _result = _recognizer.Analyze(filtered);
        Render();
    }

    private void Render()
    {
        _rawGroups.Text = string.Join(" | ", _result.Segments.Select(s => s.Raw));
        _converted.Text = _result.Converted;

        var confirmed = _result.Segments.Count(s => s.Confirmed);
        _summary.Text = _result.Input.Length == 0
            ? "入力待ち"
            : $"文字数: {_result.Input.Length} / セグメント: {_result.Segments.Count} / 確定: {confirmed} / " +
              $"採用仮説のBeam確率: {_result.Frames[^1].BestHypothesisProbability:P1} / " +
              $"Ensemble: {_result.Frames[^1].BestEnsembleScore:F2} / " +
              $"Entropy: {_result.Frames[^1].EntropyBits:F2} bit";

        _segments.DataSource = _result.Segments.Select(s => new
        {
            Range = $"{s.Start}..{s.End}",
            s.Raw,
            s.Converted,
            Language = s.Language.ToString(),
            Status = s.Confirmed ? "確定" : "推測",
            BoundaryConsensus = s.BoundaryConfidence.ToString("P1"),
            InterpretationConsensus = s.InterpretationConfidence.ToString("P1"),
            Beam = s.BeamInterpretationConfidence.ToString("P1"),
            Lexical = s.LexicalInterpretationConfidence.ToString("P1"),
            Stability = ShowOptional(s.StabilityInterpretationConfidence),
            Votes = s.IndependentSupport,
            Class = CertaintyText(s.Certainty)
        }).ToList();

        _boundaries.DataSource = _result.Boundaries.Select(b => new
        {
            b.Position,
            Cut = $"{Short(b.Left)} | {Short(b.Right)}",
            Consensus = b.Probability.ToString("P1"),
            Beam = b.BeamProbability.ToString("P1"),
            Bidirectional = b.BidirectionalProbability.ToString("P1"),
            Lexical = b.LexicalProbability.ToString("P1"),
            Stability = ShowOptional(b.StabilityProbability),
            Votes = b.IndependentSupport,
            Status = b.IsInputEnd ? "入力末尾" : b.Confirmed ? "複数確認済み" : "未確定"
        }).ToList();

        _timeline.DataSource = _result.Frames.Select(f => new
        {
            f.Step,
            f.Prefix,
            Segmentation = f.BestSegmentation,
            f.Converted,
            Beam = f.BestHypothesisProbability.ToString("P1"),
            Ensemble = f.BestEnsembleScore.ToString("F2"),
            EntropyBits = f.EntropyBits.ToString("F2")
        }).ToList();

        var last = _result.Frames.LastOrDefault();
        _hypotheses.DataSource = last?.TopHypotheses.Select(h => new
        {
            BeamProbability = h.Probability.ToString("P2"),
            Ensemble = h.EnsembleScore.ToString("F2"),
            BoundaryAgreement = h.BoundaryAgreement.ToString("P1"),
            LexicalAgreement = h.LexicalAgreement.ToString("P1"),
            h.Segmentation,
            h.Converted,
            Score = h.Score.ToString("F2")
        }).ToList();
    }

    private static string ShowOptional(double value) =>
        value < 0 ? "n/a" : value.ToString("P1");

    private static string CertaintyText(CertaintyClass value) => value switch
    {
        CertaintyClass.ClearBoundaryClearInterpretation => "境界○ / 解釈○",
        CertaintyClass.ClearBoundaryAmbiguousInterpretation => "境界○ / 解釈△",
        CertaintyClass.AmbiguousBoundaryClearInterpretation => "境界△ / 解釈○",
        _ => "境界△ / 解釈△"
    };

    private static string Short(string value)
    {
        const int max = 18;
        if (value.Length <= max)
            return value;
        return "…" + value[^max..];
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
            FileName = $"boundary-lab-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            AddExtension = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var report = ResearchExporter.CreateReport(_result, _recognizer.Parameters);
        File.WriteAllText(dialog.FileName, ResearchExporter.ToJson(report), new UTF8Encoding(false));
        MessageBox.Show("research-v2 JSONを書き出しました。", "Incremental Boundary Lab");
    }
}
