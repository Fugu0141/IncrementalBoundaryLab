using System.Text;
using BoundaryLab.Core;

namespace BoundaryLab.WinForms;

public sealed class MainForm : Form
{
    private readonly IMozcConversionOracle _mozc;
    private readonly IResearchSession _session;
    private EvidenceLatticeResult _result =
        new("", "", 0, [], [], []);

    private readonly TextBox _input = new();
    private readonly TextBox _committed = new();
    private readonly TextBox _active = new();
    private readonly TextBox _phonetic = new();
    private readonly TextBox _phoneticStructure = new();
    private readonly TextBox _bestPath = new();
    private readonly TextBox _converted = new();
    private readonly Label _summary = new();

    private readonly DataGridView _paths = Grid();
    private readonly DataGridView _edges = Grid();
    private readonly DataGridView _timeline = Grid();
    private readonly DataGridView _fourState = Grid();

    public MainForm()
    {
        _mozc = MozcBridgeOracle.TryCreateDefault();
        var hybrid = Environment.GetEnvironmentVariable(
            "BOUNDARYLAB_PHONETIC_HYBRID") == "1";
        var engine = Environment.GetEnvironmentVariable("BOUNDARYLAB_ENGINE");
        _session = engine == "v1"
            ? new StreamHybridSession(_mozc)
            : new EvidenceLatticeSession(
                new EvidenceLatticeParameters(
                    UsePhoneticFirstHybrid: hybrid),
                _mozc);

        Text = "Incremental Boundary Lab — " +
            (_session is StreamHybridSession
                ? "Stream Hybrid v1.2 (shared code/phonetic boundaries)"
                : _session.Parameters.UsePhoneticFirstHybrid
                    ? "Four-state Phonetic Hybrid v0.8 (research diagnostics)"
                    : "Mozc Responsibility IME v0.6 baseline");
        Width = 1460;
        Height = 940;
        MinimumSize = new Size(1060, 760);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;

        FormClosed += (_, _) => _mozc.Dispose();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 12,
            Padding = new Padding(12)
        };

        for (var i = 0; i < 10; i++)
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            Text = _session is StreamHybridSession
                ? "v1.1: 連続かな語の過分割を防止し、未知の英語の語尾まで境界候補を復元"
                : _session.Parameters.UsePhoneticFirstHybrid
                    ? "v0.8: かなのまとまり/切れ目を確定・曖昧に分類"
                    : "v0.6 baseline: 日本語/英語の責務境界をEvidence Latticeで推定",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        });

        _input.Dock = DockStyle.Top;
        _input.Font = new Font("Consolas", 15);
        _input.PlaceholderText =
            "commitsitade-tawogithubnipushsitekudasai";
        _input.TextChanged += (_, _) => AnalyzeInput();
        _input.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) &&
                !InputSyntax.IsAllowed(e.KeyChar))
                e.Handled = true;
        };
        root.Controls.Add(_input);

        root.Controls.Add(MakeLabeled("Committed prefix", _committed));
        root.Controls.Add(MakeLabeled("Active Window", _active));
        root.Controls.Add(MakeLabeled("Phonetic preview", _phonetic));
        root.Controls.Add(MakeLabeled("Phonetic 4-state", _phoneticStructure));
        root.Controls.Add(MakeLabeled("Best responsibility path", _bestPath));
        root.Controls.Add(MakeLabeled("Mozc-aware preview", _converted));

        _summary.AutoSize = true;
        _summary.Padding = new Padding(0, 4, 0, 4);
        root.Controls.Add(_summary);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "OpenPrefix / Unknown は通常commit禁止。'-' はLatin固定せずMozc候補とも競合させます。Mozc bridgeが無い場合はv0.5互換のヒューリスティックのみで動作します。"
        });

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(Page("Top hypotheses", _paths));
        tabs.TabPages.Add(Page("Responsibility / Mozc edges", _edges));
        tabs.TabPages.Add(Page("Commit / workload", _timeline));
        tabs.TabPages.Add(Page("Phonetic four-state", _fourState));
        root.Controls.Add(tabs);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Fill
        };

        var mixed = new Button { Text = "混合入力例", AutoSize = true };
        mixed.Click += (_, _) => _input.Text =
            "commitsitade-tawogithubnipushsitekudasai";
        actions.Controls.Add(mixed);

        var node = new Button { Text = "node.js例", AutoSize = true };
        node.Click += (_, _) => _input.Text =
            "demotyottomonndainanoganode.jstoiukotoba";
        actions.Controls.Add(node);

        var open = new Button { Text = "OpenPrefix例", AutoSize = true };
        open.Click += (_, _) => _input.Text = "commi";
        actions.Controls.Add(open);

        var export = new Button { Text = "Mozc責務研究JSONを書き出す", AutoSize = true };
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
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
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
            _phoneticStructure.Text = "";
            _bestPath.Text = "";
            _converted.Text = "";
            _summary.Text =
                _mozc.IsAvailable
                    ? "入力待ち / Mozc bridge: connected"
                    : "入力待ち / Mozc bridge: unavailable";
            _paths.DataSource = null;
            _edges.DataSource = null;
            _timeline.DataSource = null;
            _fourState.DataSource = null;
            return;
        }

        var last = _result.Frames[^1];
        var best = last.TopPaths.FirstOrDefault();

        _committed.Text = string.Join(
            " | ",
            _result.CommittedSegments.Select(s => s.Raw));
        _active.Text = last.ActiveRaw;
        _phonetic.Text = last.PhoneticPreview;
        var structure = last.PhoneticStructure;
        _phoneticStructure.Text = structure is null
            ? "(disabled in baseline mode)"
            : $"group 確定 {structure.Groups.Count(g => g.Status == PhoneticEvidenceStatus.Confirmed)} / " +
              $"曖昧 {structure.Groups.Count(g => g.Status == PhoneticEvidenceStatus.Tentative)}, " +
              $"cut 確定 {structure.Boundaries.Count(b => b.Status == PhoneticEvidenceStatus.Confirmed)} / " +
              $"曖昧 {structure.Boundaries.Count(b => b.Status == PhoneticEvidenceStatus.Tentative)}, " +
              $"前後再検討 {structure.ReviewWindows.Count}";
        _bestPath.Text = best?.Segmentation ?? "";
        _converted.Text = _result.Output;

        var altCount = last.TopPaths.Count(p =>
            p.RelativeScore >= -_session.Parameters.AlternativeScoreWindow);

        _summary.Text =
            $"Mode {(_session is StreamHybridSession ? "stream-v1" :
                _session.Parameters.UsePhoneticFirstHybrid ? "hybrid-v0.8" : "baseline")} / " +
            $"Mozc {(_mozc.IsAvailable ? "connected" : "offline")} / " +
            $"入力 {_result.Input.Length} / committed {_result.CommittedRawLength} / " +
            $"active {last.ActiveRaw.Length} / edges {last.CandidateEdges.Count} / " +
            $"paths {altCount} / Mozc probes {last.MozcProbesThisStep} / " +
            $"consensus {last.ConsensusEnd} / stable {last.ConsensusStableFrames}";

        _paths.DataSource = last.TopPaths
            .Take(12)
            .Select((p, i) => new
            {
                Rank = i + 1,
                Score = p.Score.ToString("F2"),
                Delta = p.RelativeScore.ToString("F2"),
                p.Segmentation,
                p.Output
            })
            .ToList();

        _edges.DataSource = last.CandidateEdges
            .OrderBy(e => e.Start)
            .ThenByDescending(e => e.LocalScore)
            .Select(e => new
            {
                Range = $"{e.Start}..{e.End}",
                e.Raw,
                e.Output,
                Language = e.Language.ToString(),
                Kind = e.Kind.ToString(),
                Score = e.LocalScore.ToString("F2"),
                JP = e.JapaneseProfile.ToString("F2"),
                EN = e.EnglishProfile.ToString("F2"),
                Mozc = e.MozcQuality < 0
                    ? "n/a"
                    : e.MozcQuality.ToString("P0"),
                e.MozcTopCandidate,
                e.Evidence
            })
            .ToList();

        _timeline.DataSource = _result.Frames
            .Select(f => new
            {
                f.Step,
                f.CommittedRawLength,
                Active = f.ActiveRaw.Length,
                Paths = f.TopPaths.Count,
                Edges = f.CandidateEdges.Count,
                f.MozcProbesThisStep,
                f.ConsensusEnd,
                f.ConsensusStableFrames,
                Commits = f.CommitEvents.Count,
                Work = f.ExpandedEdgesThisStep,
                Total = f.TotalExpandedEdges,
                f.Output
            })
            .ToList();

        _fourState.DataSource = structure is null
            ? null
            : structure.Groups.Select(g => new
            {
                Position = g.Start,
                End = g.End,
                Category = g.Status == PhoneticEvidenceStatus.Confirmed
                    ? "確定したまとまり" : "曖昧なまとまり",
                g.Raw,
                g.Preview,
                g.Reason
            })
            .Concat(structure.Boundaries.Select(b => new
            {
                Position = b.Position,
                End = b.Position,
                Category = b.Status == PhoneticEvidenceStatus.Confirmed
                    ? "確定した切れ目" : "曖昧な切れ目",
                Raw = "|",
                Preview = "",
                b.Reason
            }))
            .OrderBy(x => x.Position)
            .ThenBy(x => x.End)
            .ToList();
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
            FileName = "boundary-lab-mozc-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json",
            AddExtension = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var report = EvidenceLatticeResearchExporter.CreateReport(
            _result,
            _session.Parameters);

        File.WriteAllText(
            dialog.FileName,
            EvidenceLatticeResearchExporter.ToJson(report),
            new UTF8Encoding(false));

        MessageBox.Show(
            "Mozc Responsibility研究データを書き出しました。",
            "Incremental Boundary Lab");
    }
}
