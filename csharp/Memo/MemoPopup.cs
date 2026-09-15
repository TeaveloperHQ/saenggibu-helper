using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
            Child = new DockPanel { VerticalAlignment = VerticalAlignment.Center },
        };
        // 왼쪽 선택 칸·오른쪽 버튼은 고정, 메모 칸이 남는 폭을 채움(예전: 전부 고정 폭이라 창보다 넓어 오른쪽이 잘렸음)
        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 7, Margin = new Thickness(0, 0, 13, 0), VerticalAlignment = VerticalAlignment.Center,
            Children = { new Image { Width = 24, Height = 24, Source = icon, VerticalAlignment = VerticalAlignment.Center }, _class, _num, _name, _area, _subject },
        };
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 7, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Children = { _status, save, close },
        };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        var dock = (DockPanel)bar.Child;
        dock.Children.Add(left); dock.Children.Add(right); dock.Children.Add(_memo);
        _memo.MinWidth = 160;
        Content = bar;

        foreach (var b in new[] { _class, _num, _name })
            b.TextChanged += (_, _) => { if (b == _class || b == _num || b == _name) SyncFrom(b); };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
            else if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { Save(); e.Handled = true; }
        };
    }

    private static AutoCompleteBox MkBox(string ph, double w)
    {
        var b = new AutoCompleteBox { Width = w, Watermark = ph, MinimumPrefixLength = 0, FilterMode = AutoCompleteFilterMode.Custom, MinHeight = 0 };
        // 칸에 이미 완성된 값(예: 번호 3)이 있으면 전체 후보를 보여 다른 학생을 고를 수 있게, 입력 중인 부분 값이면 포함 검색
        b.ItemFilter = (search, item) =>
            string.IsNullOrEmpty(search) || (b.ItemsSource as List<string>)?.Contains(search) == true
            || (item?.ToString() ?? "").Contains(search, StringComparison.Ordinal);
        // 칸을 클릭·탭 이동만 해도 후보 목록을 펼친다(기본은 글자를 쳐야만 열려서 학생이 안 불러와진 것처럼 보였음).
        // 목록에서 항목을 고른 직후 포커스가 돌아올 때 다시 열리지 않게 닫힌 직후는 건너뜀.
        var closedAt = DateTime.MinValue;
        b.DropDownClosed += (_, _) => closedAt = DateTime.UtcNow;
        void Open() { if (b.IsKeyboardFocusWithin && !b.IsDropDownOpen && DateTime.UtcNow - closedAt > TimeSpan.FromMilliseconds(300)) b.IsDropDownOpen = true; }
        b.GotFocus += (_, _) => Dispatcher.UIThread.Post(Open);
        b.AddHandler(InputElement.PointerReleasedEvent, (_, e) => { if (e.Source is Visual v && b.IsVisualAncestorOf(v)) Dispatcher.UIThread.Post(Open); },
            RoutingStrategies.Bubble, handledEventsToo: true);
        return b;
    }

    // ── 표시 ──
    public void PopupBar()
    {
        ReloadAll();
        _memo.Text = ""; _status.Text = "";
        // WorkingArea·Position은 물리 픽셀, Width·Height는 DIP → 화면 배율(예: 125%)로 환산해야 오른쪽이 화면 밖으로 안 나감
        var scr = Screens.Primary;
        var screen = scr?.WorkingArea ?? new PixelRect(0, 0, 1280, 800);
        double scale = scr?.Scaling ?? 1.0;
        double w = Math.Min(1060, screen.Width / scale - 28);
        Width = w;
        int x = screen.X + screen.Width - (int)Math.Ceiling((w + 14) * scale);
        int y = screen.Y + screen.Height - (int)Math.Ceiling((Height + 12) * scale);
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

    private static void SetItems(AutoCompleteBox box, List<string> opts)
    {
        if (box.ItemsSource is List<string> cur && cur.SequenceEqual(opts)) return;   // 같은 목록이면 그대로(타이핑 중 드롭다운 재설정 방지)
        box.ItemsSource = opts;
    }

    /// <summary>후보: 학급=전체, 번호·이름=고른 학급 안(학급 미확정이면 전체). 칸끼리 서로 좁히지 않고, 하나가 정해지면 나머지를 채운다.
    /// (예전: 세 칸이 서로를 필터 → 학생 한 명이 채워지면 번호·이름 목록이 그 한 명으로 줄어 다른 학생을 못 골랐음)</summary>
    private void RefreshOptions(AutoCompleteBox? source)
    {
        static string V(AutoCompleteBox b) => (b.Text ?? "").Trim();
        List<(string klass, string num, string name)> Pool()
        {
            var inClass = _records.Where(r => r.klass == V(_class)).ToList();
            return inClass.Count > 0 ? inClass : _records;
        }
        bool classOk = _records.Any(r => r.klass == V(_class));
        var pool = Pool();
        (string klass, string num, string name)? Only(Func<(string klass, string num, string name), bool> pred)
        {
            var m = pool.Where(pred).Take(2).ToList();
            return m.Count == 1 ? m[0] : null;
        }
        string num = V(_num), name = V(_name);

        if (source == _num && num.Length > 0 && Only(r => r.num == num) is { } byNum)
        { _name.Text = byNum.name; if (!classOk) _class.Text = byNum.klass; }
        else if (source == _name && name.Length > 0 && Only(r => r.name == name) is { } byName)
        { _num.Text = byName.num; if (!classOk) _class.Text = byName.klass; }
        else if (source == _class && classOk)   // 학급을 바꾸면 그 반의 같은 번호(없으면 같은 이름) 학생으로, 둘 다 없으면 비움
        {
            if (num.Length > 0 && Only(r => r.num == num) is { } c1) _name.Text = c1.name;
            else if (name.Length > 0 && Only(r => r.name == name) is { } c2) _num.Text = c2.num;
            else { _num.Text = ""; _name.Text = ""; }
        }

        pool = Pool();   // 위에서 학급이 채워졌을 수 있음
        SetItems(_class, Distinct("klass", _records));
        SetItems(_num, Distinct("num", pool));
        SetItems(_name, Distinct("name", pool));
    }

    private void SyncFrom(AutoCompleteBox source)
    {
        if (_syncing) return;
        _syncing = true;
        try { RefreshOptions(source); }
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
