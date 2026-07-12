using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Saenggibu;

namespace Gui;

public class RowVm
{
    public string Num { get; set; } = "";
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
}

/// <summary>생기부 도우미 — 파이썬 앱 구조·아이콘에 맞춘 데스크톱 UI.</summary>
public class MainWindow : Window
{
    private LlamaEngine? _engine;
    private KiwiNative? _kiwi;
    private readonly string _dataDir, _modelsDir;
    private readonly Settings _settings;
    private readonly Glossary _glossary;
    private readonly MemoryStore _store;
    private TextBlock? _learnStatus;

    private static Bitmap? Asset(string name)
    {
        try { return new Bitmap(AssetLoader.Open(new Uri($"avares://Gui/Assets/{name}"))); } catch { return null; }
    }

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
        Width = 980; Height = 780;
        var ic = Asset("appicon.png"); if (ic != null) Icon = new WindowIcon(ic);

        var mark = new Image { Width = 34, Height = 34, Source = Asset("brandmark.png") };
        var header = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#4f46e5"), 0), new GradientStop(Color.Parse("#7c3aed"), 0.5), new GradientStop(Color.Parse("#06b6d4"), 1) },
            },
            Padding = new Thickness(18, 10),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = {
                mark, new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = {
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

    private Control BuildGenerate()
    {
        var area = new ComboBox { Width = 180 };
        foreach (var a in Prompts.Areas) area.Items.Add(a.Title);
        area.SelectedIndex = 0;
        var subject = new TextBox { Watermark = "과목(세특)", Width = 110 };
        var mode = new ComboBox { Width = 150 };
        mode.Items.Add("내 문장 변형"); mode.Items.Add("키워드로 새로 생성"); mode.SelectedIndex = 0;
        var count = new NumericUpDown { Value = 5, Minimum = 1, Maximum = 20, Width = 84 };
        var genBtn = new Button { Content = "생성", Padding = new Thickness(16, 6), FontWeight = FontWeight.Bold };
        var input = new TextBox { Watermark = "문장(변형) 또는 키워드(생성) 입력", AcceptsReturn = true, Height = 70, TextWrapping = TextWrapping.Wrap };
        var warn = new TextBlock { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
        var results = new ListBox { Height = 140 };
        var status = new TextBlock { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
        void SyncSubj() => subject.IsVisible = Prompts.Areas[area.SelectedIndex].Key == "seteuk";
        area.SelectionChanged += (_, _) => SyncSubj(); SyncSubj();

        genBtn.Click += async (_, _) =>
        {
            string text = (input.Text ?? "").Trim();
            if (text.Length == 0) { status.Text = "입력하세요."; return; }
            var viol = Compliance.Summary(text);
            warn.Text = viol.Length > 0 ? "⚠ 규정 확인: " + viol : "";
            genBtn.IsEnabled = false; results.Items.Clear();
            status.Text = "모델 준비·생성 중… (최초 로딩 수십 초)";
            int n = (int)(count.Value ?? 5);
            string subj = subject.IsVisible ? (subject.Text ?? "").Trim() : "";
            var areaSpec = Prompts.Areas[area.SelectedIndex];
            bool gen = mode.SelectedIndex == 1;
            var terms = _glossary.AllTerms();
            try
            {
                var vars = await Task.Run(() =>
                {
                    EnsureEngines();
                    return gen
                        ? Paraphrase.GenerateFromKeywords(areaSpec, _store, _engine!, _kiwi!, subj, text, "", "", n, terms)
                        : Paraphrase.LlmParaphrase(text, n, _engine!, _kiwi!, terms, Array.Empty<string>(), subj);
                });
                foreach (var v in vars) results.Items.Add(v);
                status.Text = vars.Count > 0 ? $"{vars.Count}개 생성 — '저장'하면 학습에 반영됩니다." : "생성 실패 — 입력을 바꿔 다시.";
            }
            catch (Exception ex) { status.Text = "오류: " + ex.Message; }
            finally { genBtn.IsEnabled = true; }
        };

        var spellBtn = new Button { Content = "맞춤법 검사" };
        spellBtn.Click += async (_, _) =>
        {
            string t = results.SelectedItem as string ?? (input.Text ?? "").Trim();
            if (t.Length == 0) return;
            status.Text = "네이버 맞춤법 검사 중…(온라인)";
            var r = await Task.Run(() => Spellcheck.NaverSpellcheck(t));
            status.Text = r == null ? "맞춤법 검사 실패(오프라인/차단)." : $"교정: {r.Value.corrected} (오류 {r.Value.errata})";
        };
        var saveBtn = new Button { Content = "저장(학습)", FontWeight = FontWeight.Bold };
        saveBtn.Click += (_, _) =>
        {
            var areaSpec = Prompts.Areas[area.SelectedIndex];
            string subj = subject.IsVisible ? (subject.Text ?? "").Trim() : "";
            string kw = (input.Text ?? "").Trim();
            int saved = 0;
            foreach (var it in results.Items) if (it is string s && s.Trim().Length > 0) { _store.AddExample(areaSpec.Key, subj, kw, s); saved++; }
            status.Text = $"{saved}개 저장(학습). 누적 예시 {_store.Count()}건.";
            if (_learnStatus != null) _learnStatus.Text = $"학습 예시: 총 {_store.Count()}건";
        };

        var ctrl = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (Control c in new Control[] { new TextBlock { Text = "영역", VerticalAlignment = VerticalAlignment.Center }, area, subject, mode, new TextBlock { Text = "개수", VerticalAlignment = VerticalAlignment.Center }, count, genBtn })
            ctrl.Children.Add(c);
        var resultBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (Control c in new Control[] { spellBtn, saveBtn }) resultBar.Children.Add(c);

        // 학급 시트(생성 결과 아래)
        var classBox = new AutoCompleteBox { Width = 130, Watermark = "학급(예:1반)", FilterMode = AutoCompleteFilterMode.Contains };
        var rows = new ObservableCollection<RowVm>();
        var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, IsReadOnly = false, GridLinesVisibility = DataGridGridLinesVisibility.All };
        grid.Columns.Add(new DataGridTextColumn { Header = "학번", Binding = new Binding("Num"), Width = new DataGridLength(70) });
        grid.Columns.Add(new DataGridTextColumn { Header = "이름", Binding = new Binding("Name"), Width = new DataGridLength(90) });
        grid.Columns.Add(new DataGridTextColumn { Header = "내용", Binding = new Binding("Content"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var sheetMsg = new TextBlock { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
        string AreaKey() => Prompts.Areas[area.SelectedIndex].Key;
        void LoadRows() { rows.Clear(); foreach (var (nu, na, co) in RosterData.ReadRows(_dataDir, AreaKey(), classBox.Text ?? "")) rows.Add(new RowVm { Num = nu, Name = na, Content = co }); for (int i = rows.Count; i < 8; i++) rows.Add(new RowVm()); }
        void ReloadSheet() { var names = RosterData.ClassNames(_dataDir, AreaKey()); classBox.ItemsSource = names; if (string.IsNullOrEmpty(classBox.Text)) classBox.Text = names.Count > 0 ? names[0] : "1반"; LoadRows(); }
        classBox.SelectionChanged += (_, _) => LoadRows();
        area.SelectionChanged += (_, _) => { classBox.Text = ""; ReloadSheet(); };
        var addRow = new Button { Content = "행 추가" }; addRow.Click += (_, _) => rows.Add(new RowVm());
        var saveSheet = new Button { Content = "시트 저장", FontWeight = FontWeight.Bold };
        saveSheet.Click += (_, _) => { string k = (classBox.Text ?? "").Trim(); if (k.Length == 0) { sheetMsg.Text = "학급명 입력"; return; } RosterData.WriteRows(_dataDir, AreaKey(), k, rows.Select(r => (r.Num, r.Name, r.Content))); sheetMsg.Text = $"'{k}' 저장됨."; classBox.ItemsSource = RosterData.ClassNames(_dataDir, AreaKey()); };
        var importX = new Button { Content = "엑셀 불러오기" };
        importX.Click += async (_, _) => { var f = (await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false }))?.FirstOrDefault(); if (f == null) return; try { rows.Clear(); foreach (var r in Importer.ParseXlsx(f.Path.LocalPath)) rows.Add(new RowVm { Content = r }); sheetMsg.Text = "엑셀 본문 추출됨."; } catch (Exception ex) { sheetMsg.Text = "오류:" + ex.Message; } };
        var exportX = new Button { Content = "엑셀 내보내기" };
        exportX.Click += async (_, _) => { var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = "명단.xlsx", DefaultExtension = "xlsx" }); if (f == null) return; try { Importer.WriteXlsx(f.Path.LocalPath, rows.Select(r => (r.Num, r.Name, r.Content))); sheetMsg.Text = "내보냄: " + f.Name; } catch (Exception ex) { sheetMsg.Text = "오류:" + ex.Message; } };
        var sheetBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (Control c in new Control[] { new TextBlock { Text = "학급", VerticalAlignment = VerticalAlignment.Center }, classBox, addRow, saveSheet, importX, exportX }) sheetBar.Children.Add(c);
        ReloadSheet();

        var g = new Grid { Margin = new Thickness(16), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,*") };
        void Put(Control c, int r) { Grid.SetRow(c, r); g.Children.Add(c); }
        Put(new TextBlock { Text = "생성 모드", FontSize = 16, FontWeight = FontWeight.Bold }, 0);
        Put(ctrl, 1);
        Put(new TextBlock { Text = "입력(원문/키워드)", FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6, 0, 0) }, 2);
        Put(input, 3); Put(warn, 4);
        Put(new TextBlock { Text = "생성 결과", FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6, 0, 0) }, 5);
        Put(results, 6); Put(resultBar, 7); Put(status, 8);
        Put(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 8, 0, 6),
            Child = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = "학급 시트 (엑셀식 편집)", FontWeight = FontWeight.Bold, Margin = new Thickness(0, 6, 0, 0) }, sheetBar, sheetMsg } } }, 9);
        Put(grid, 10);
        return g;
    }

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
            dl.IsEnabled = false;
            string dest = Path.Combine(_modelsDir, Config.ModelFilename);
            try
            {
                await Downloader.DownloadModelAsync(Config.ModelUrl, dest, Config.ModelApproxBytes,
                    (d, t) => Avalonia.Threading.Dispatcher.UIThread.Post(() => status.Text = $"내려받는 중… {d / 1024 / 1024}MB / {t / 1024 / 1024}MB"));
                status.Text = "모델 다운로드 완료."; LoadModels();
            }
            catch (Exception ex) { status.Text = "다운로드 오류: " + ex.Message; }
            finally { dl.IsEnabled = true; }
        };

        _learnStatus = new TextBlock { Text = $"학습 예시: 총 {_store.Count()}건 (씨드 {_store.SeedCount()})", Foreground = Brushes.Gray };
        var backup = new Button { Content = "학습 백업" };
        backup.Click += async (_, _) => { var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = "saenggibu_backup.sqlite3" }); if (f == null) return; try { _store.Backup(f.Path.LocalPath); status.Text = "백업 완료: " + f.Name; } catch (Exception ex) { status.Text = "오류:" + ex.Message; } };
        var restore = new Button { Content = "학습 복원" };
        restore.Click += async (_, _) => { var f = (await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false }))?.FirstOrDefault(); if (f == null) return; try { int a = _store.ImportMerge(f.Path.LocalPath); status.Text = $"복원: {a}건 추가."; _learnStatus.Text = $"학습 예시: 총 {_store.Count()}건"; } catch (Exception ex) { status.Text = "오류:" + ex.Message; } };

        var list = new ListBox { Height = 200 };
        void Refresh() { list.Items.Clear(); foreach (var t in _glossary.AllTerms().OrderBy(x => x, StringComparer.Ordinal)) list.Items.Add(t); }
        Refresh();
        var edit = new TextBox { Watermark = "예) 아이오딘화 칼륨", Width = 240 };
        var add = new Button { Content = "추가" }; var del = new Button { Content = "선택 삭제" };
        add.Click += (_, _) => { if (_glossary.Add(edit.Text ?? "")) { edit.Text = ""; Refresh(); } };
        del.Click += (_, _) => { if (list.SelectedItem is string s && _glossary.Remove(s)) Refresh(); };

        var modelBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (Control c in new Control[] { new TextBlock { Text = "기본 모델", VerticalAlignment = VerticalAlignment.Center }, modelCombo, dl }) modelBar.Children.Add(c);
        var bkBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (Control c in new Control[] { backup, restore }) bkBar.Children.Add(c);
        var termBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (Control c in new Control[] { edit, add, del }) termBar.Children.Add(c);
        return Pad(new StackPanel { Spacing = 10 }.With(new Control[] {
            new TextBlock { Text = "학습 모드", FontSize = 16, FontWeight = FontWeight.Bold },
            modelBar, _learnStatus, bkBar,
            new TextBlock { Text = "등록 용어(변형 시 철자 보존)", FontWeight = FontWeight.Bold, Margin = new Thickness(0,8,0,0) },
            list, termBar, status }));
    }

    private Control BuildProcess() => Pad(new StackPanel { Spacing = 12 }.With(new Control[] {
        new TextBlock { Text = "과정 안내", FontSize = 16, FontWeight = FontWeight.Bold },
        new TextBlock { TextWrapping = TextWrapping.Wrap, Text =
            "① 입력 →  ② 형태소 분석(Kiwi)으로 보존 명사 파악 →  ③ 오프라인 언어모델이 표현 변경(여러 후보) →  " +
            "④ 검증(용어 보존·비문·유사도·규정) →  ⑤ 부족분 사전·어순 규칙 보충 →  ⑥ 학급 시트 정리 → 저장 시 학습 반영.\n\n" +
            "모든 처리는 인터넷 없이 내 PC에서. 학생 정보는 외부로 나가지 않습니다(모델 다운로드·선택적 맞춤법 검사 제외)." } }));

    private static Control Pad(Control c) => new ScrollViewer { Content = new Border { Padding = new Thickness(18), Child = c } };

    private void EnsureEngines()
    {
        _kiwi ??= new KiwiNative(Environment.GetEnvironmentVariable("SGB_KIWI_MODEL") ?? throw new InvalidOperationException("SGB_KIWI_MODEL 필요"));
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
