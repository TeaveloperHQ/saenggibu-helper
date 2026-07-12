using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Saenggibu;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Memo;

/// <summary>
/// 한 줄 팝업 입력 바(화면 하단·항상 위). 학급·번호·이름을 등록 명단으로 자동완성·동기화하고
/// Ctrl+S 저장 시 해당 영역 학급 시트에 반영(기존 학생=이어붙임 / 없으면 행 삽입).
/// app/ui/quicknote.py 이식.
/// </summary>
public class MemoPopup : Window
{
    private readonly string _dataDir;
    private readonly Settings _settings;
    private readonly AutoCompleteBox _class, _num, _name;
    private readonly ComboBox _area, _subject;
    private readonly TextBox _memo;
    private readonly TextBlock _status;
    private List<(string klass, string num, string name)> _records = new();
    private bool _syncing;

    public MemoPopup()
    {
        _dataDir = DataDir();
        _settings = new Settings(_dataDir);

        SystemDecorations = Avalonia.Controls.WindowDecorations.None;
        Topmost = true; CanResize = false; ShowInTaskbar = false;
        Width = 880; Height = 56; Background = Brushes.Transparent;
        Avalonia.Media.Imaging.Bitmap? icon = null;
        try { icon = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Memo/Assets/appicon.png"))); Icon = new WindowIcon(icon); } catch { }

        _class = MkBox("학급", 84);
        _num = MkBox("번호", 66);
        _name = MkBox("이름", 110);
        _area = new ComboBox { Width = 150 };
        foreach (var a in Prompts.Areas) _area.Items.Add(a.Title);
        _area.SelectionChanged += (_, _) => { ReloadRecords(); SyncSubject(); };
        _subject = new ComboBox { Width = 100, IsVisible = false };
        _memo = new TextBox { Watermark = "관찰 메모 입력 · Ctrl+S 저장 · Enter 줄바꿈", AcceptsReturn = true, Height = 40, MinHeight = 0, VerticalContentAlignment = VerticalAlignment.Center };
        _status = new TextBlock { Width = 20, FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Foreground = Brush.Parse("#16a34a") };

        var save = new Button { Content = "저장", Background = Brush.Parse("#2e7d32"), Foreground = Brushes.White };
        save.Click += (_, _) => Save();
        var close = new Button { Content = "✕", Background = Brushes.Transparent, Foreground = Brush.Parse("#9ca3af") };
        close.Click += (_, _) => Hide();

        var bar = new Border
        {
            Background = Brushes.White, BorderBrush = Brush.Parse("#c7c9d1"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 6, 10, 6),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center,
                Children = { new Image { Width = 24, Height = 24, Source = icon, VerticalAlignment = VerticalAlignment.Center }, _class, _num, _name, _area, _subject, new Panel { Width = 6 }, _memo, _status, save, close },
            },
        };
        _memo.Width = 300;
        Content = bar;

        foreach (var b in new[] { _class, _num, _name })
            b.TextChanged += (_, _) => { if (b == _class || b == _num || b == _name) SyncFrom(b); };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
            else if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { Save(); e.Handled = true; }
        };
    }

    private static AutoCompleteBox MkBox(string ph, double w) => new()
    {
        Width = w, Watermark = ph, MinimumPrefixLength = 0, FilterMode = AutoCompleteFilterMode.Contains, MinHeight = 0,
    };

    // ── 표시 ──
    public void PopupBar()
    {
        ReloadAll();
        _memo.Text = ""; _status.Text = "";
        var screen = Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1280, 800);
        int w = Math.Min(880, screen.Width - 40);
        Width = w;
        int x = screen.X + screen.Width - w - 14;
        int y = screen.Y + screen.Height - (int)Height - 12;
        Position = new PixelPoint(x, y);
        Show(); Activate();
        Dispatcher.UIThread.Post(() => _memo.Focus());
    }

    // ── 데이터(상속: 영역 → 학급·번호·이름) ──
    private void ReloadAll()
    {
        _syncing = true;
        var lastArea = _settings.Get<string>("quicknote_last_area") ?? "seteuk";
        int idx = Prompts.Areas.FindIndex(a => a.Key == lastArea);
        _area.SelectedIndex = idx >= 0 ? idx : 0;
        _syncing = false;
        ReloadRecords();
        SyncSubject();
    }

    private AreaSpec Area() => Prompts.Areas[Math.Max(0, _area.SelectedIndex)];

    private void ReloadRecords()
    {
        _records = RosterData.RosterRecords(_dataDir, Area().Key);
        _syncing = true;
        _class.Text = _num.Text = _name.Text = "";
        _syncing = false;
        RefreshOptions(null);
        var last = _settings.Get<string>("quicknote_last_class") ?? "";
        if (last.Length > 0 && _records.Any(r => r.klass == last))
        {
            _syncing = true; _class.Text = last; _syncing = false;
            SyncFrom(_class);
        }
    }

    private static List<string> Distinct(string field, IEnumerable<(string klass, string num, string name)> recs)
    {
        var vals = recs.Select(r => field == "klass" ? r.klass : field == "num" ? r.num : r.name)
                       .Where(v => v.Length > 0).Distinct();
        return field == "num"
            ? vals.OrderBy(s => s.Length).ThenBy(s => s, StringComparer.Ordinal).ToList()
            : vals.OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    private void RefreshOptions(AutoCompleteBox? source)
    {
        var boxes = new (AutoCompleteBox box, string field)[] { (_class, "klass"), (_num, "num"), (_name, "name") };
        string Val(string f) => (f == "klass" ? _class.Text : f == "num" ? _num.Text : _name.Text ?? "").Trim();
        var known = boxes.ToDictionary(b => b.field, b => Val(b.field).Length > 0 && _records.Any(r => Get(r, b.field) == Val(b.field)));
        foreach (var (box, field) in boxes)
        {
            var others = boxes.Where(o => o.field != field && known[o.field]).Select(o => o.field).ToList();
            var cand = _records.Where(r => others.All(g => Get(r, g) == Val(g)));
            var opts = Distinct(field, cand);
            box.ItemsSource = opts;
            if (box == source) continue;
            string cur = (box.Text ?? "").Trim();
            if (opts.Count == 1) { box.Text = opts[0]; }
            else if (cur.Length > 0 && !opts.Contains(cur)) box.Text = "";
        }
    }

    private static string Get((string klass, string num, string name) r, string f) => f == "klass" ? r.klass : f == "num" ? r.num : r.name;

    private void SyncFrom(AutoCompleteBox source)
    {
        if (_syncing) return;
        _syncing = true;
        try { RefreshOptions(source); RefreshOptions(source); }   // 유일값 연쇄 확정(2회 수렴)
        finally { _syncing = false; }
    }

    private void SyncSubject()
    {
        bool seteuk = Area().Key == "seteuk";
        _subject.IsVisible = seteuk;
        if (seteuk)
        {
            _subject.Items.Clear();
            foreach (var s in _settings.Get<string[]>("subjects") ?? Array.Empty<string>()) if (s.Length > 0) _subject.Items.Add(s);
        }
    }

    // ── 저장 ──
    private void Save()
    {
        string t = (_memo.Text ?? "").Trim();
        if (t.Length == 0) { _memo.Watermark = "메모를 입력한 뒤 Ctrl+S 하세요"; return; }
        var area = Area();
        string klass = (_class.Text ?? "").Trim(), num = (_num.Text ?? "").Trim(), name = (_name.Text ?? "").Trim();
        if (klass.Length == 0) { Flash("!", "#dc2626"); _status.Text = "학급?"; return; }
        if (num.Length == 0 && name.Length == 0) { Flash("!", "#dc2626"); return; }
        string result = RosterData.AddMemoToRoster(_dataDir, area.Key, klass, num, name, t);
        if (result is "" or "no_class") { Flash("!", "#dc2626"); return; }
        _settings.Set("quicknote_last_class", klass);
        _settings.Set("quicknote_last_area", area.Key);
        _memo.Text = "";
        Flash(result == "insert" ? "＋" : "✓", "#16a34a");
        Dispatcher.UIThread.Post(() => _memo.Focus());
    }

    private void Flash(string mark, string color)
    {
        _status.Foreground = Brush.Parse(color); _status.Text = mark;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        t.Tick += (_, _) => { t.Stop(); _status.Text = ""; };
        t.Start();
    }

    protected override void OnClosing(Avalonia.Controls.WindowClosingEventArgs e) { e.Cancel = true; Hide(); }   // 닫기=숨김(트레이 상주)

    private static string DataDir()
    {
        var env = Environment.GetEnvironmentVariable("SGB_DATA");
        if (!string.IsNullOrEmpty(env)) return env;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, OperatingSystem.IsWindows() ? "SaenggibuHelper" : "saenggibu-helper");
    }
}
