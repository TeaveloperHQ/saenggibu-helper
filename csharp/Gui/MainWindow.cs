using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Saenggibu;

namespace Gui;

public class RowVm
{
    public bool Checked { get; set; }
    public string Num { get; set; } = "";
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
}

/// <summary>생기부 도우미 — 파이썬 완성본 UI를 따른 데스크톱 앱.</summary>
public class MainWindow : Window
{
    private static readonly string[] TonePresets = { "기본", "간결하게", "구체적 사례 중심", "따뜻한 어조", "성장·변화 강조" };
    private static readonly string[] LengthPresets = { "보통(3~4문장)", "짧게(1~2문장)", "자세히(5문장 이상)" };

    private LlamaEngine? _engine;
    private KiwiNative? _kiwi;
    private readonly string _dataDir, _modelsDir;
    private readonly Settings _settings;
    private readonly Glossary _glossary;
    private readonly MemoryStore _store;
    private TextBlock? _learnStatus;

    private static Bitmap? Asset(string name)
    { try { return new Bitmap(AssetLoader.Open(new Uri($"avares://Gui/Assets/{name}"))); } catch { return null; } }

    public MainWindow()
    {
        _dataDir = DataDir();
        _modelsDir = Path.Combine(_dataDir, "models");
        Directory.CreateDirectory(_modelsDir);
        _settings = new Settings(_dataDir);
        _glossary = new Glossary(_dataDir);
        _store = new MemoryStore(Path.Combine(_dataDir, "memory.sqlite3"));
        var seed = Environment.GetEnvironmentVariable("SGB_SEED");
        if (seed != null) try { _store.LoadSeedCorpus(seed); } catch { }

        Title = "생기부 도우미";
        Width = 1000; Height = 800;
        var ic = Asset("appicon.png"); if (ic != null) Icon = new WindowIcon(ic);

        var header = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#4f46e5"), 0), new GradientStop(Color.Parse("#7c3aed"), 0.5), new GradientStop(Color.Parse("#06b6d4"), 1) },
            },
            Padding = new Thickness(18, 10),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = {
                new Image { Width = 34, Height = 34, Source = Asset("brandmark.png") },
                new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = {
                    new TextBlock { Text = "생기부 도우미", FontSize = 17, FontWeight = FontWeight.Bold, Foreground = Brushes.White },
                    new TextBlock { Text = "교사용 오프라인 생기부 작성 · 완전 오프라인", FontSize = 11, Foreground = Brushes.White, Opacity = 0.85 } } } } },
        };

        var tabs = new TabControl { Margin = new Thickness(6, 4, 6, 6) };
        tabs.Items.Add(new TabItem { Header = "생성 모드", Content = BuildGenerate() });
        tabs.Items.Add(new TabItem { Header = "학습 모드", Content = BuildLearn() });
        tabs.Items.Add(new TabItem { Header = "과정 안내", Content = BuildProcess() });
        Content = new DockPanel { Children = { Docked(header, Dock.Top), tabs } };
    }

    private static Control Docked(Control c, Dock d) { DockPanel.SetDock(c, d); return c; }

    // ── 생성 모드 = 영역 탭 + 입력/옵션/형태소/규정 + 체크박스 시트(파이썬 동일) ──
    private Control BuildGenerate()
    {
        var areaStrip = new ListBox { SelectionMode = SelectionMode.Single, Background = Brushes.Transparent };
        areaStrip.ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Orientation.Horizontal });
        foreach (var a in Prompts.Areas) areaStrip.Items.Add(a.Title);
        areaStrip.SelectedIndex = 0;
        AreaSpec Area() => Prompts.Areas[Math.Max(0, areaStrip.SelectedIndex)];

        var subject = new AutoCompleteBox { Width = 220, Watermark = "예) 수학, 통합과학 (선택 필수)", FilterMode = AutoCompleteFilterMode.Contains };
        void ReloadSubjects() { subject.ItemsSource = (_settings.Get<string[]>("subjects") ?? Array.Empty<string>()).ToList(); }
        ReloadSubjects();
        var tone = new ComboBox { Width = 150 }; foreach (var t in TonePresets) tone.Items.Add(t); tone.SelectedIndex = 0;
        var length = new ComboBox { Width = 160 }; foreach (var l in LengthPresets) length.Items.Add(l); length.SelectedIndex = 0;
        var genOpts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = {
            new TextBlock { Text = "톤", VerticalAlignment = VerticalAlignment.Center }, tone,
            new TextBlock { Text = "분량", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8,0,0,0) }, length } };
        var learnLabel = new TextBlock { Foreground = Brush.Parse("#2e7d32") };
        void RefreshLearn() => learnLabel.Text = _store.Count(Area().Key) > 0 ? $"이 영역 학습 예시 {_store.Count(Area().Key)}건 반영" : "";

        var input = new TextBox { Watermark = Area().InputHint, AcceptsReturn = true, Height = 80, TextWrapping = TextWrapping.Wrap };
        var morph = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var morphBox = new Border { Background = Brush.Parse("#fafafa"), BorderBrush = Brush.Parse("#e0e0e0"), BorderThickness = new Thickness(1), Height = 46, Padding = new Thickness(6, 4), Child = new ScrollViewer { Content = morph } };
        var compliance = new TextBlock { Foreground = Brush.Parse("#c62828"), FontSize = 11, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var legend = new TextBlock { FontSize = 11, Foreground = Brush.Parse("#666"), Inlines = {
            new Run { Text = "형태소 점검: " }, new Run { Text = "■ 사전에 없는 말", Foreground = Brush.Parse("#e65100"), FontWeight = FontWeight.Bold },
            new Run { Text = "  " }, new Run { Text = "■ 영문/한자", Foreground = Brush.Parse("#1565c0"), FontWeight = FontWeight.Bold },
            new Run { Text = "  ■ 정상·조사", Foreground = Brush.Parse("#999") } } };
        var termBtn = new Button { Content = "선택 단어 용어 등록" };
        termBtn.Click += (_, _) => { var sel = (input.SelectedText ?? "").Trim(); if (sel.Length >= 2 && _glossary.Add(sel)) { UpdateMorph(input.Text ?? "", morph); RefreshLearn(); } };

        // 형태소 실시간 점검(디바운스) — kiwi 지연 로드
        DispatcherTimer? morphTimer = null;
        input.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            compliance.IsVisible = false;
            var v = Compliance.Summary(input.Text ?? "");
            if (v.Length > 0) { compliance.Text = "⚠ " + v; compliance.IsVisible = true; }
            morphTimer?.Stop();
            morphTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            morphTimer.Tick += (_, _) => { morphTimer!.Stop(); _ = UpdateMorphAsync(input.Text ?? "", morph); };
            morphTimer.Start();
        };

        var mode = new ComboBox { Width = 200 };
        mode.Items.Add("내 문장 변형(같은 의미)"); mode.Items.Add("키워드로 새로 생성"); mode.SelectedIndex = 0;
        void SyncMode() { genOpts.IsVisible = mode.SelectedIndex == 1; }
        mode.SelectionChanged += (_, _) => SyncMode(); SyncMode();
        var genBtn = new Button { Content = "체크된 행 채우기", FontWeight = FontWeight.Bold, Padding = new Thickness(14, 6) };
        var status = new TextBlock { Foreground = Brush.Parse("#666"), Text = "행을 체크하고 '체크된 행 채우기'를 누르세요.", VerticalAlignment = VerticalAlignment.Center };

        // 시트(체크박스 + 학번/이름/내용)
        var classBox = new AutoCompleteBox { Width = 130, Watermark = "학급(예:1반)", FilterMode = AutoCompleteFilterMode.Contains };
        var rows = new ObservableCollection<RowVm>();
        var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, IsReadOnly = false, GridLinesVisibility = DataGridGridLinesVisibility.All };
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "✓", Binding = new Binding("Checked") { Mode = BindingMode.TwoWay }, Width = new DataGridLength(40) });
        grid.Columns.Add(new DataGridTextColumn { Header = "학번", Binding = new Binding("Num"), Width = new DataGridLength(64) });
        grid.Columns.Add(new DataGridTextColumn { Header = "이름", Binding = new Binding("Name"), Width = new DataGridLength(84) });
        grid.Columns.Add(new DataGridTextColumn { Header = "내용", Binding = new Binding("Content"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var sheetMsg = new TextBlock { Foreground = Brush.Parse("#666") };
        void LoadRows() { rows.Clear(); foreach (var (nu, na, co) in RosterData.ReadRows(_dataDir, Area().Key, classBox.Text ?? "")) rows.Add(new RowVm { Num = nu, Name = na, Content = co }); for (int i = rows.Count; i < 10; i++) rows.Add(new RowVm()); }
        void ReloadSheet() { var names = RosterData.ClassNames(_dataDir, Area().Key); classBox.ItemsSource = names; if (string.IsNullOrEmpty(classBox.Text)) classBox.Text = names.Count > 0 ? names[0] : "1반"; LoadRows(); }
        classBox.SelectionChanged += (_, _) => LoadRows();

        areaStrip.SelectionChanged += (_, _) => { subject.IsVisible = Area().SubjectField; input.Watermark = Area().InputHint; RefreshLearn(); classBox.Text = ""; ReloadSheet(); };
        subject.IsVisible = Area().SubjectField;
        RefreshLearn(); ReloadSheet();

        genBtn.Click += async (_, _) =>
        {
            var checkedRows = rows.Where(r => r.Checked).ToList();
            if (checkedRows.Count == 0) { status.Text = "채울 행을 먼저 체크하세요."; return; }
            string text = (input.Text ?? "").Trim();
            if (text.Length == 0) { status.Text = "입력하세요."; return; }
            genBtn.IsEnabled = false; status.Text = $"{checkedRows.Count}개 생성 중… (최초 로딩 수십 초)";
            int n = checkedRows.Count;
            var areaSpec = Area(); bool gen = mode.SelectedIndex == 1;
            string subj = subject.IsVisible ? (subject.Text ?? "").Trim() : "";
            string toneT = tone.SelectedItem as string ?? ""; if (toneT == "기본") toneT = "";
            string lenT = length.SelectedIndex == 0 ? "" : (length.SelectedItem as string ?? "");
            var terms = _glossary.AllTerms();
            if (gen && subj.Length == 0 && areaSpec.SubjectField) { status.Text = "세특은 과목을 입력하세요."; genBtn.IsEnabled = true; return; }
            try
            {
                var outv = await Task.Run(() =>
                {
                    EnsureEngines();
                    return gen
                        ? Paraphrase.GenerateFromKeywords(areaSpec, _store, _engine!, _kiwi!, subj, text, toneT, lenT, n, terms)
                        : Paraphrase.LlmParaphrase(text, n, _engine!, _kiwi!, terms, Array.Empty<string>(), subj);
                });
                for (int i = 0; i < checkedRows.Count && i < outv.Count; i++) checkedRows[i].Content = outv[i];
                grid.ItemsSource = null; grid.ItemsSource = rows;   // 표 갱신
                RememberSubject(subj);
                status.Text = $"{Math.Min(outv.Count, checkedRows.Count)}개 채움. '시트 저장'하면 학습에 반영됩니다.";
            }
            catch (Exception ex) { status.Text = "오류: " + ex.Message; }
            finally { genBtn.IsEnabled = true; }
        };

        var addRow = new Button { Content = "행 추가" }; addRow.Click += (_, _) => rows.Add(new RowVm());
        var saveSheet = new Button { Content = "시트 저장(학습)", FontWeight = FontWeight.Bold };
        saveSheet.Click += (_, _) =>
        {
            string k = (classBox.Text ?? "").Trim(); if (k.Length == 0) { sheetMsg.Text = "학급명 입력"; return; }
            RosterData.WriteRows(_dataDir, Area().Key, k, rows.Select(r => (r.Num, r.Name, r.Content)));
            int learned = 0; foreach (var r in rows) if (r.Content.Trim().Length > 0) { _store.AddExample(Area().Key, subject.IsVisible ? (subject.Text ?? "").Trim() : "", r.Name, r.Content); learned++; }
            classBox.ItemsSource = RosterData.ClassNames(_dataDir, Area().Key);
            sheetMsg.Text = $"'{k}' 저장 · {learned}건 학습 반영."; RefreshLearn();
            if (_learnStatus != null) _learnStatus.Text = $"학습 예시: 총 {_store.Count()}건";
        };
        var importX = new Button { Content = "엑셀 불러오기" };
        importX.Click += async (_, _) => { var f = (await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false }))?.FirstOrDefault(); if (f == null) return; try { rows.Clear(); foreach (var r in Importer.ParseXlsx(f.Path.LocalPath)) rows.Add(new RowVm { Content = r }); sheetMsg.Text = "엑셀 본문 추출됨."; } catch (Exception ex) { sheetMsg.Text = "오류:" + ex.Message; } };
        var exportX = new Button { Content = "엑셀 내보내기" };
        exportX.Click += async (_, _) => { var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = "명단.xlsx", DefaultExtension = "xlsx" }); if (f == null) return; try { Importer.WriteXlsx(f.Path.LocalPath, rows.Select(r => (r.Num, r.Name, r.Content))); sheetMsg.Text = "내보냄: " + f.Name; } catch (Exception ex) { sheetMsg.Text = "오류:" + ex.Message; } };

        // 레이아웃
        Control HRow(params Control[] cs) { var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; foreach (var c in cs) p.Children.Add(c); return p; }
        var g = new Grid { Margin = new Thickness(14), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,*") };
        void Put(Control c, int r) { Grid.SetRow(c, r); g.Children.Add(c); }
        Put(areaStrip, 0);
        Put(HRow(new TextBlock { Text = "과목", VerticalAlignment = VerticalAlignment.Center }, subject, genOpts, new Control { Width = 12 }, learnLabel), 1);
        Put(new TextBlock { Text = "입력 (키워드·관찰 메모)", Margin = new Thickness(0, 6, 0, 0) }, 2);
        Put(input, 3);
        Put(morphBox, 4);
        Put(compliance, 5);
        Put(HRow(legend, termBtn), 6);
        Put(HRow(mode, genBtn, status), 7);
        Put(new TextBlock { Text = "학급 표 (체크된 행에 채워짐 · 엑셀 가져오기/내보내기)", FontWeight = FontWeight.Bold, Margin = new Thickness(0, 8, 0, 0) }, 8);
        Put(HRow(new TextBlock { Text = "학급", VerticalAlignment = VerticalAlignment.Center }, classBox, addRow, saveSheet, importX, exportX), 9);
        Put(sheetMsg, 10);
        Put(grid, 11);
        return g;
    }

    private void RememberSubject(string subj)
    {
        if (subj.Length == 0) return;
        var list = (_settings.Get<string[]>("subjects") ?? Array.Empty<string>()).Where(s => s != subj).ToList();
        list.Insert(0, subj);
        _settings.Set("subjects", list.Take(5).ToArray());
    }

    private async Task UpdateMorphAsync(string text, TextBlock morph)
    {
        if (text.Trim().Length == 0) { morph.Inlines?.Clear(); return; }
        var terms = _glossary.AllTerms();
        try
        {
            var toks = await Task.Run(() => { EnsureKiwi(); return _kiwi!.Tokenize(text).ToList(); });
            UpdateMorphInlines(toks, terms, morph);
        }
        catch (Exception ex) { morph.Inlines?.Clear(); morph.Inlines?.Add(new Run { Text = "(형태소 분석 불가: " + ex.Message + ")", Foreground = Brushes.Gray }); }
    }

    private static void UpdateMorph(string text, TextBlock morph) { }

    private void UpdateMorphInlines(System.Collections.Generic.List<(string form, string tag)> toks, System.Collections.Generic.HashSet<string> terms, TextBlock morph)
    {
        morph.Inlines?.Clear();
        foreach (var (form, tag) in toks)
        {
            IBrush color;
            if (tag is "SL" or "SH" or "SW") color = Brush.Parse("#1565c0");             // 영문/한자
            else if (tag == "NNP" && !terms.Contains(form)) color = Brush.Parse("#e65100"); // 사전에 없는 고유어(오타·용어)
            else color = Brush.Parse("#999");                                             // 정상·조사
            morph.Inlines?.Add(new Run { Text = form + " ", Foreground = color });
        }
    }

    // ── 학습 모드 ─────────────────────────────────────────────────────────
    private Control BuildLearn()
    {
        var modelCombo = new ComboBox { Width = 320 };
        void LoadModels()
        {
            modelCombo.Items.Clear();
            if (Directory.Exists(_modelsDir)) foreach (var f in Directory.GetFiles(_modelsDir, "*.gguf")) modelCombo.Items.Add(Path.GetFileName(f));
            var envG = Environment.GetEnvironmentVariable("SGB_GGUF");
            if (envG != null && File.Exists(envG) && !modelCombo.Items.Contains(Path.GetFileName(envG))) modelCombo.Items.Add(Path.GetFileName(envG));
            var act = _settings.Get<string>("active_model");
            if (act != null && modelCombo.Items.Contains(act)) modelCombo.SelectedItem = act; else if (modelCombo.ItemCount > 0) modelCombo.SelectedIndex = 0;
        }
        LoadModels();
        modelCombo.SelectionChanged += (_, _) => { if (modelCombo.SelectedItem is string m) _settings.Set("active_model", m); };
        var dl = new Button { Content = "모델 내려받기" };
        var status = new TextBlock { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
        dl.Click += async (_, _) =>
        {
            dl.IsEnabled = false; string dest = Path.Combine(_modelsDir, Config.ModelFilename);
            try { await Downloader.DownloadModelAsync(Config.ModelUrl, dest, Config.ModelApproxBytes, (d, t) => Dispatcher.UIThread.Post(() => status.Text = $"내려받는 중… {d / 1024 / 1024}MB / {t / 1024 / 1024}MB")); status.Text = "완료."; LoadModels(); }
            catch (Exception ex) { status.Text = "오류: " + ex.Message; } finally { dl.IsEnabled = true; }
        };
        _learnStatus = new TextBlock { Text = $"학습 예시: 총 {_store.Count()}건 (씨드 {_store.SeedCount()})", Foreground = Brushes.Gray };
        var backup = new Button { Content = "학습 백업" };
        backup.Click += async (_, _) => { var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = "saenggibu_backup.sqlite3" }); if (f == null) return; try { _store.Backup(f.Path.LocalPath); status.Text = "백업 완료."; } catch (Exception ex) { status.Text = "오류:" + ex.Message; } };
        var restore = new Button { Content = "학습 복원" };
        restore.Click += async (_, _) => { var f = (await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false }))?.FirstOrDefault(); if (f == null) return; try { int a = _store.ImportMerge(f.Path.LocalPath); status.Text = $"복원 {a}건."; _learnStatus.Text = $"학습 예시: 총 {_store.Count()}건"; } catch (Exception ex) { status.Text = "오류:" + ex.Message; } };
        var list = new ListBox { Height = 200 };
        void Refresh() { list.Items.Clear(); foreach (var t in _glossary.AllTerms().OrderBy(x => x, StringComparer.Ordinal)) list.Items.Add(t); }
        Refresh();
        var edit = new TextBox { Watermark = "예) 아이오딘화 칼륨", Width = 240 };
        var add = new Button { Content = "추가" }; var del = new Button { Content = "선택 삭제" };
        add.Click += (_, _) => { if (_glossary.Add(edit.Text ?? "")) { edit.Text = ""; Refresh(); } };
        del.Click += (_, _) => { if (list.SelectedItem is string s && _glossary.Remove(s)) Refresh(); };
        Control HRow(params Control[] cs) { var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; foreach (var c in cs) p.Children.Add(c); return p; }
        return Pad(new StackPanel { Spacing = 10 }.With(new Control[] {
            new TextBlock { Text = "학습 모드", FontSize = 16, FontWeight = FontWeight.Bold },
            HRow(new TextBlock { Text = "기본 모델", VerticalAlignment = VerticalAlignment.Center }, modelCombo, dl),
            _learnStatus, HRow(backup, restore),
            new TextBlock { Text = "등록 용어(변형 시 철자 보존)", FontWeight = FontWeight.Bold, Margin = new Thickness(0,8,0,0) },
            list, HRow(edit, add, del), status }));
    }

    private Control BuildProcess() => Pad(new StackPanel { Spacing = 12 }.With(new Control[] {
        new TextBlock { Text = "과정 안내", FontSize = 16, FontWeight = FontWeight.Bold },
        new TextBlock { TextWrapping = TextWrapping.Wrap, Text =
            "① 입력 →  ② 형태소 분석(Kiwi)으로 보존 명사 파악 →  ③ 오프라인 언어모델이 표현 변경 →  " +
            "④ 검증(용어 보존·비문·유사도·규정) →  ⑤ 부족분 사전·어순 보충 →  ⑥ 체크한 행에 채움 → 시트 저장 시 학습 반영.\n\n" +
            "모든 처리는 인터넷 없이 내 PC에서. 학생 정보는 외부로 나가지 않습니다(모델 다운로드·선택적 맞춤법 검사 제외)." } }));

    private static Control Pad(Control c) => new ScrollViewer { Content = new Border { Padding = new Thickness(18), Child = c } };

    private void EnsureKiwi() => _kiwi ??= new KiwiNative(Environment.GetEnvironmentVariable("SGB_KIWI_MODEL") ?? throw new InvalidOperationException("SGB_KIWI_MODEL 필요"));
    private void EnsureEngines()
    {
        EnsureKiwi();
        string gguf = Environment.GetEnvironmentVariable("SGB_GGUF") ?? "";
        if (gguf.Length == 0)
        {
            var act = _settings.Get<string>("active_model");
            var cand = act != null ? Path.Combine(_modelsDir, act) : Path.Combine(_modelsDir, Config.ModelFilename);
            if (File.Exists(cand)) gguf = cand;
        }
        if (gguf.Length == 0) throw new InvalidOperationException("모델 없음 — 학습 모드에서 내려받거나 SGB_GGUF 설정");
        _engine ??= new LlamaEngine(gguf);
    }

    private static string DataDir()
    {
        var env = Environment.GetEnvironmentVariable("SGB_DATA");
        if (!string.IsNullOrEmpty(env)) return env;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, OperatingSystem.IsWindows() ? "SaenggibuHelper" : "saenggibu-helper");
    }
}

internal static class PanelExt
{
    public static T With<T>(this T panel, Control[] children) where T : Panel
    { foreach (var c in children) panel.Children.Add(c); return panel; }
}
