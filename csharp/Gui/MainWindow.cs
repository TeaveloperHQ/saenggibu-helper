using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Saenggibu;

namespace Gui;

public class RowVm
{
    public string Num { get; set; } = "";
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
    // 사용자 추가 열(열 삽입) — 열 id로 값 보관/편집(엑셀식)
    public Dictionary<string, string> Extra { get; set; } = new();
    public string this[string key]
    {
        get => Extra.TryGetValue(key, out var v) ? v : "";
        set => Extra[key] = value ?? "";
    }
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
        ApplyStyles();

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

    // 인라인 이름 입력 팝업(엑셀식 탭/열 이름 변경·추가)
    private void ShowPrompt(Control anchor, string initial, string title, Action<string> onOk)
    {
        var tb = new TextBox { Text = initial, Width = 180 };
        var ok = new Button { Content = "확인", IsDefault = true };
        var fly = new Flyout { Content = new StackPanel { Spacing = 6, Margin = new Thickness(4), Children = { new TextBlock { Text = title }, tb, ok } } };
        void Commit() { var v = (tb.Text ?? "").Trim(); fly.Hide(); if (v.Length > 0) onOk(v); }
        ok.Click += (_, _) => Commit();
        tb.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Commit(); };
        fly.ShowAt(anchor);
        Dispatcher.UIThread.Post(() => { tb.SelectAll(); tb.Focus(); });
    }

    private void ApplyStyles()
    {
        Style St(Func<Selector?, Selector> sel, params (Avalonia.AvaloniaProperty p, object? v)[] setters)
        {
            var s = new Style(sel);
            foreach (var (p, v) in setters) s.Setters.Add(new Setter(p, v));
            return s;
        }
        const double H = 32.0;
        Styles.Add(St(x => x.OfType<Button>(), (Button.MinHeightProperty, H), (Button.PaddingProperty, new Thickness(12, 4)),
            (Button.VerticalAlignmentProperty, VerticalAlignment.Center), (Button.VerticalContentAlignmentProperty, VerticalAlignment.Center)));
        Styles.Add(St(x => x.OfType<ComboBox>(), (ComboBox.MinHeightProperty, H), (ComboBox.VerticalAlignmentProperty, VerticalAlignment.Center)));
        Styles.Add(St(x => x.OfType<AutoCompleteBox>(), (AutoCompleteBox.MinHeightProperty, H), (AutoCompleteBox.VerticalAlignmentProperty, VerticalAlignment.Center)));
        Styles.Add(St(x => x.OfType<NumericUpDown>(), (NumericUpDown.MinHeightProperty, H), (NumericUpDown.VerticalAlignmentProperty, VerticalAlignment.Center)));
        Styles.Add(St(x => x.OfType<TextBox>(), (TextBox.MinHeightProperty, H)));
        Styles.Add(St(x => x.OfType<TextBox>().Class("multiline"), (TextBox.MinHeightProperty, 80.0)));
        // 탭 스트립(영역/학급): 아이템을 탭처럼 여백
        Styles.Add(St(x => x.OfType<ListBox>().Class("tabs").Descendant().OfType<ListBoxItem>(),
            (ListBoxItem.PaddingProperty, new Thickness(12, 6)), (ListBoxItem.MarginProperty, new Thickness(0, 0, 3, 3))));
        // 엑셀식 그리드 머리글(열·행) 회색 강조
        Styles.Add(St(x => x.OfType<DataGridColumnHeader>(),
            (DataGridColumnHeader.BackgroundProperty, Brush.Parse("#eef0f3")),
            (DataGridColumnHeader.FontWeightProperty, FontWeight.SemiBold),
            (DataGridColumnHeader.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
            (DataGridColumnHeader.VerticalContentAlignmentProperty, VerticalAlignment.Center),
            (DataGridColumnHeader.PaddingProperty, new Thickness(6, 0)),
            (DataGridColumnHeader.ClipToBoundsProperty, false),
            (DataGridColumnHeader.SeparatorBrushProperty, Brush.Parse("#c8ccd2"))));
        // 헤더 안 TextBlock: 잘림 방지 + 세로 중앙(테마 기본 clip으로 글자 하단이 잘리던 문제)
        Styles.Add(St(x => x.OfType<DataGridColumnHeader>().Descendant().OfType<TextBlock>(),
            (TextBlock.TextTrimmingProperty, TextTrimming.None), (TextBlock.MarginProperty, new Thickness(0)),
            (TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center)));
        Styles.Add(St(x => x.OfType<DataGridCell>(), (DataGridCell.PaddingProperty, new Thickness(4, 0)),
            (DataGridCell.MinHeightProperty, 0.0),
            (DataGridCell.VerticalContentAlignmentProperty, VerticalAlignment.Center)));
        Styles.Add(St(x => x.OfType<DataGridRow>(), (DataGridRow.MinHeightProperty, 0.0)));
        // 셀 안 TextBlock 자체 여백 제거(엑셀식 밀착) — DataGridTextColumn 기본 좌측 여백 상쇄
        Styles.Add(St(x => x.OfType<DataGridCell>().Descendant().OfType<TextBlock>(),
            (TextBlock.MarginProperty, new Thickness(0)), (TextBlock.PaddingProperty, new Thickness(0)),
            (TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center)));
    }

    // ── 생성 모드 = 영역 탭 + 입력/옵션/형태소/규정 + 체크박스 시트(파이썬 동일) ──
    private Control BuildGenerate()
    {
        var areaStrip = new ListBox { SelectionMode = SelectionMode.Single, Background = Brushes.Transparent };
        areaStrip.Classes.Add("tabs");
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
        var genBtn = new Button { Content = "선택한 행 채우기", FontWeight = FontWeight.Bold, Padding = new Thickness(16, 6), Background = Brush.Parse("#4f46e5"), Foreground = Brushes.White };
        var status = new TextBlock { Foreground = Brush.Parse("#666"), Text = "행 번호(라벨)를 클릭해 선택하고 '선택한 행 채우기'를 누르세요.", VerticalAlignment = VerticalAlignment.Center };

        // 시트(학급 서브탭 + 체크박스 + 학번/이름/내용)
        var classStrip = new ListBox { SelectionMode = SelectionMode.Single, Background = Brushes.Transparent, MinWidth = 100 };
        classStrip.Classes.Add("tabs");
        classStrip.ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Orientation.Horizontal });
        string CurClass() => classStrip.SelectedItem as string ?? "";
        bool suppress = false;
        var rows = new ObservableCollection<RowVm>();
        var grid = new DataGrid
        {
            ItemsSource = rows, AutoGenerateColumns = false, IsReadOnly = false,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            HeadersVisibility = DataGridHeadersVisibility.All,   // 열 + 행 머리글(엑셀식)
            RowHeight = 22, RowHeaderWidth = 34, ColumnHeaderHeight = 32,
            CanUserResizeColumns = true, CanUserSortColumns = false,
            ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader,   // Ctrl+C 복사
            SelectionMode = DataGridSelectionMode.Extended,
            BorderThickness = new Thickness(1), BorderBrush = Brush.Parse("#b5b5b5"),
            HorizontalGridLinesBrush = Brush.Parse("#d9d9d9"), VerticalGridLinesBrush = Brush.Parse("#d9d9d9"),
        };
        grid.LoadingRow += (_, e) => e.Row.Header = (e.Row.GetIndex() + 1).ToString();  // 행 번호(라벨)
        // TemplateColumn: DataGridTextColumn이 셀 TextBlock에 넣는 로컬 여백을 우회 → 엑셀식 밀착 여백 완전 통제
        DataGridColumn Col(string header, string bindPath, DataGridLength w, bool wrap = false) => new DataGridTemplateColumn
        {
            Header = header, Width = w, CanUserResize = true,
            CellTemplate = new FuncDataTemplate<RowVm>((_, _) => new TextBlock
            {
                [!TextBlock.TextProperty] = new Binding(bindPath),
                Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            }),
            CellEditingTemplate = new FuncDataTemplate<RowVm>((_, _) => new TextBox
            {
                [!TextBox.TextProperty] = new Binding(bindPath) { Mode = BindingMode.TwoWay },
                Margin = new Thickness(0), Padding = new Thickness(3, 0), MinHeight = 0,
                BorderThickness = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center,
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            }),
        };
        // 파이썬 class_tab.py 구조: 학번·이름 = 고정(FIXED=2), 그 뒤 '내용' 열들(첫 내용 열 = Content)
        const int Fixed = 2;        // 학번·이름 (삭제 불가)
        const int CoreCols = 3;     // 학번·이름·내용(첫 content 열) — 그리드 인덱스 0,1,2
        string contentLabel = "내용";
        grid.Columns.Add(Col("학번", "Num", new DataGridLength(68)));
        grid.Columns.Add(Col("이름", "Name", new DataGridLength(90)));
        grid.Columns.Add(Col(contentLabel, "Content", new DataGridLength(1, DataGridLengthUnitType.Star), wrap: true));
        var extraCols = new List<(string id, string label)>();
        int extraSeq = 0;
        var sheetMsg = new TextBlock { Foreground = Brush.Parse("#666") };
        void RebuildExtraColumns()   // 첫 content 열(내용) 뒤 추가 content 열 재구성
        {
            while (grid.Columns.Count > CoreCols) grid.Columns.RemoveAt(grid.Columns.Count - 1);
            foreach (var (id, label) in extraCols) grid.Columns.Add(Col(label, $"[{id}]", new DataGridLength(160)));
        }
        void LoadRows()
        {
            rows.Clear(); extraCols.Clear(); contentLabel = "내용";
            if (CurClass() is { Length: > 0 } cc && cc != "＋")
            {
                var (clbl, ext, rr) = RosterData.ReadRowsExtended(_dataDir, Area().Key, cc);
                contentLabel = clbl; extraCols.AddRange(ext);
                foreach (var (nu, na, co, ev) in rr)
                {
                    var vm = new RowVm { Num = nu, Name = na, Content = co };
                    for (int i = 0; i < ext.Count && i < ev.Count; i++) vm.Extra[ext[i].id] = ev[i];
                    rows.Add(vm);
                }
            }
            for (int i = rows.Count; i < 50; i++) rows.Add(new RowVm());   // 기본 50행(학급 25명+ 여유)
            grid.Columns[2].Header = contentLabel;
            RebuildExtraColumns();
        }
        void ReloadSheet()
        {
            var names = RosterData.ClassNames(_dataDir, Area().Key).ToList(); names.Add("＋");   // 엑셀식 ＋ 탭
            suppress = true; classStrip.ItemsSource = names; if (classStrip.SelectedIndex < 0 && names.Count > 1) classStrip.SelectedIndex = 0; suppress = false;
            LoadRows();
        }
        classStrip.SelectionChanged += (_, _) =>
        {
            if (suppress) return;
            if (CurClass() == "＋")   // ＋ 탭 = 새 학급 추가(엑셀식)
            {
                ShowPrompt(classStrip, "", "새 학급 이름", name => { RosterData.WriteRows(_dataDir, Area().Key, name, Array.Empty<(string, string, string)>()); ReloadSheet(); classStrip.SelectedItem = name; });
                suppress = true; classStrip.SelectedIndex = 0; suppress = false;
            }
            else LoadRows();
        };
        classStrip.DoubleTapped += (_, _) => { var cur = CurClass(); if (cur.Length > 0 && cur != "＋") ShowPrompt(classStrip, cur, "학급 이름 변경", nn => { if (RosterData.RenameClass(_dataDir, Area().Key, cur, nn)) { ReloadSheet(); classStrip.SelectedItem = nn; } }); };

        areaStrip.SelectionChanged += (_, _) => { subject.IsVisible = Area().SubjectField; input.Watermark = Area().InputHint; RefreshLearn(); classStrip.SelectedIndex = -1; ReloadSheet(); };
        subject.IsVisible = Area().SubjectField;
        RefreshLearn(); ReloadSheet();

        genBtn.Click += async (_, _) =>
        {
            var selRows = grid.SelectedItems.Cast<RowVm>().OrderBy(r => rows.IndexOf(r)).ToList();
            if (selRows.Count == 0) { status.Text = "채울 행을 먼저 선택하세요(왼쪽 행 번호 클릭 · Ctrl/Shift로 여러 개)."; return; }
            string text = (input.Text ?? "").Trim();
            if (text.Length == 0) { status.Text = "입력하세요."; return; }
            genBtn.IsEnabled = false; status.Text = $"{selRows.Count}개 생성 중… (최초 로딩 수십 초)";
            int n = selRows.Count;
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
                    var rej = _store.RejectedTexts(areaSpec.Key);
                    return gen
                        ? Paraphrase.GenerateFromKeywords(areaSpec, _store, _engine!, _kiwi!, subj, text, toneT, lenT, n, terms)
                        : Paraphrase.LlmParaphrase(text, n, _engine!, _kiwi!, terms, rej, subj);
                });
                for (int i = 0; i < selRows.Count && i < outv.Count; i++) selRows[i].Content = outv[i];
                grid.ItemsSource = null; grid.ItemsSource = rows;   // 표 갱신
                RememberSubject(subj);
                status.Text = $"{Math.Min(outv.Count, selRows.Count)}개 채움. '저장'하면 학습에 반영됩니다.";
            }
            catch (Exception ex) { status.Text = "오류: " + ex.Message; }
            finally { genBtn.IsEnabled = true; }
        };

        var addRow = new Button { Content = "행 추가" }; addRow.Click += (_, _) => rows.Add(new RowVm());
        var saveSheet = new Button { Content = "저장", FontWeight = FontWeight.Bold, Background = Brush.Parse("#2e7d32"), Foreground = Brushes.White };
        saveSheet.Click += (_, _) =>
        {
            string k = CurClass(); if (k.Length == 0 || k == "＋") { sheetMsg.Text = "저장할 학급 탭을 선택하세요(없으면 ＋ 로 추가)."; return; }
            RosterData.WriteRowsExtended(_dataDir, Area().Key, k, contentLabel, extraCols,
                rows.Select(r => (r.Num, r.Name, r.Content, (IReadOnlyList<string>)extraCols.Select(c => r[c.id]).ToList())));
            int learned = 0; foreach (var r in rows) if (r.Content.Trim().Length > 0) { _store.AddExample(Area().Key, subject.IsVisible ? (subject.Text ?? "").Trim() : "", r.Name, r.Content); learned++; }
            sheetMsg.Text = $"'{k}' 저장 · {learned}건 학습 반영."; RefreshLearn();
            if (_learnStatus != null) _learnStatus.Text = $"학습 예시: 총 {_store.Count()}건";
        };
        var importX = new Button { Content = "엑셀 불러오기" };
        importX.Click += async (_, _) => { var f = (await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false }))?.FirstOrDefault(); if (f == null) return; try { rows.Clear(); foreach (var r in Importer.ParseXlsx(f.Path.LocalPath)) rows.Add(new RowVm { Content = r }); sheetMsg.Text = "엑셀 본문 추출됨."; } catch (Exception ex) { sheetMsg.Text = "오류:" + ex.Message; } };
        var exportX = new Button { Content = "엑셀 내보내기" };
        exportX.Click += async (_, _) => { var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = "명단.xlsx", DefaultExtension = "xlsx" }); if (f == null) return; try { Importer.WriteXlsx(f.Path.LocalPath, rows.Select(r => (r.Num, r.Name, r.Content))); sheetMsg.Text = "내보냄: " + f.Name; } catch (Exception ex) { sheetMsg.Text = "오류:" + ex.Message; } };

        // ── 우클릭 메뉴(파이썬 class_tab.py 기준): 열머리글 / 행머리글 / 셀 로 분리 ──
        string GetCell(RowVm r, int di) => di == 0 ? r.Num : di == 1 ? r.Name : di == 2 ? r.Content : ((di - CoreCols) is var e2 && e2 >= 0 && e2 < extraCols.Count ? r[extraCols[e2].id] : "");
        void SetCell(RowVm r, int di, string v) { switch (di) { case 0: r.Num = v; break; case 1: r.Name = v; break; case 2: r.Content = v; break; default: int ei = di - CoreCols; if (ei >= 0 && ei < extraCols.Count) r[extraCols[ei].id] = v; break; } }
        void Refresh() { grid.ItemsSource = null; grid.ItemsSource = rows; }   // 행 번호 재정렬 + 표 갱신
        // 우클릭 지점 포착: 종류(열머리글/행머리글/셀) + 대상 행·열
        int VisualIndex(Visual v) { var p = v.GetVisualParent(); if (p == null) return -1; int i = 0; foreach (var c in p.GetVisualChildren()) { if (ReferenceEquals(c, v)) return i; i++; } return -1; }
        string ctxKind = ""; RowVm? ctxRow = null; int ctxRowIdx = -1, ctxColIdx = -1;
        grid.AddHandler(InputElement.PointerPressedEvent, (object? _, PointerPressedEventArgs e) =>
        {
            if (!e.GetCurrentPoint(grid).Properties.IsRightButtonPressed) return;
            var src = e.Source as Visual;
            ctxRow = src?.FindAncestorOfType<DataGridRow>()?.DataContext as RowVm;
            ctxRowIdx = ctxRow != null ? rows.IndexOf(ctxRow) : -1;
            var chdr = src?.FindAncestorOfType<DataGridColumnHeader>();
            var rhdr = src?.FindAncestorOfType<DataGridRowHeader>();
            var cell = src?.FindAncestorOfType<DataGridCell>();
            ctxColIdx = chdr != null ? VisualIndex(chdr) : (cell != null ? VisualIndex(cell) : -1);
            if (ctxColIdx >= grid.Columns.Count) ctxColIdx = -1;
            ctxKind = chdr != null ? "col" : rhdr != null ? "row" : cell != null ? "cell" : "";
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);

        int ContentCount() => 1 + extraCols.Count;   // '내용' 열 개수(첫 내용 + 추가)
        void InsertRow(int at) { rows.Insert(Math.Clamp(at, 0, rows.Count), new RowVm()); Refresh(); }
        void InsertCol(int di)   // 파이썬: 자동 이름 '내용N', 위치는 FIXED 뒤로 클램프
        {
            extraSeq++; string id = "c" + extraSeq; string label = $"내용{ContentCount() + 1}";
            int ei = Math.Clamp(di - CoreCols, 0, extraCols.Count);
            extraCols.Insert(ei, (id, label)); RebuildExtraColumns();
        }
        void RenameCol(int di)
        {
            if (di < 0 || di >= grid.Columns.Count) return;
            string cur = grid.Columns[di].Header as string ?? "";
            ShowPrompt(grid, cur, "열 이름 변경", nn =>
            {
                grid.Columns[di].Header = nn;
                if (di == 2) contentLabel = nn;
                else if (di >= CoreCols) { int ei = di - CoreCols; if (ei >= 0 && ei < extraCols.Count) extraCols[ei] = (extraCols[ei].id, nn); }
            });
        }
        void DeleteCol(int di)   // 파이썬 remove_content_column: 내용 열 1개 이상 유지, 학번·이름 불가
        {
            if (di < Fixed) { sheetMsg.Text = "학번·이름 열은 삭제할 수 없습니다."; return; }
            if (ContentCount() <= 1) { sheetMsg.Text = "내용 열은 최소 1개 있어야 합니다."; return; }
            if (di == 2)   // 첫 내용 열 삭제 → 다음 내용 열을 첫 내용 열로 승격
            {
                var promote = extraCols[0];
                foreach (var r in rows) r.Content = r[promote.id];
                contentLabel = promote.label; grid.Columns[2].Header = contentLabel; extraCols.RemoveAt(0);
            }
            else { int ei = di - CoreCols; if (ei >= 0 && ei < extraCols.Count) extraCols.RemoveAt(ei); }
            RebuildExtraColumns(); Refresh();
        }

        // 열머리글 메뉴
        var miColRename = new MenuItem { Header = "이름 변경" };
        miColRename.Click += (_, _) => RenameCol(ctxColIdx);
        var miColLeft = new MenuItem { Header = "◀ 왼쪽에 열 삽입" };
        miColLeft.Click += (_, _) => InsertCol(Math.Max(Fixed, ctxColIdx));
        var miColRight = new MenuItem { Header = "오른쪽에 열 삽입 ▶" };
        miColRight.Click += (_, _) => InsertCol(Math.Max(Fixed, ctxColIdx + 1));
        var miColDel = new MenuItem { Header = "열 삭제" };
        miColDel.Click += (_, _) => DeleteCol(ctxColIdx);
        var colItems = new Control[] { miColRename, miColLeft, miColRight, miColDel };

        // 행머리글 메뉴
        var miRowAbove = new MenuItem { Header = "▲ 위에 행 삽입" };
        miRowAbove.Click += (_, _) => InsertRow(ctxRowIdx >= 0 ? ctxRowIdx : rows.Count);
        var miRowBelow = new MenuItem { Header = "아래에 행 삽입 ▼" };
        miRowBelow.Click += (_, _) => InsertRow(ctxRowIdx >= 0 ? ctxRowIdx + 1 : rows.Count);
        var miRowDel = new MenuItem { Header = "행 삭제" };
        miRowDel.Click += (_, _) =>
        {
            var sel = grid.SelectedItems.Cast<RowVm>().ToList();
            if (sel.Count == 0 && ctxRow != null) sel.Add(ctxRow);
            foreach (var r in sel) rows.Remove(r);
            if (rows.Count == 0) rows.Add(new RowVm());
            Refresh();
        };
        var miSelAll = new MenuItem { Header = "전체 선택" };
        miSelAll.Click += (_, _) => grid.SelectAll();
        var miClear = new MenuItem { Header = "전체 해제" };
        miClear.Click += (_, _) => grid.SelectedItems.Clear();
        var rowItems = new Control[] { miRowAbove, miRowBelow, miRowDel, new Separator(), miSelAll, miClear };

        // 셀 메뉴(파이썬: 내용 열이며 텍스트 있을 때만 '버리기')
        var reject = new MenuItem { Header = "이 표현 버리기(다음부터 안 나오게)" };
        reject.Click += (_, _) =>
        {
            if (ctxRow == null || ctxColIdx < 2) return;
            string t = GetCell(ctxRow, ctxColIdx).Trim(); if (t.Length == 0) return;
            _store.AddRejection(Area().Key, subject.IsVisible ? (subject.Text ?? "") : "", t);
            SetCell(ctxRow, ctxColIdx, ""); Refresh(); sheetMsg.Text = "버림 — 다음 생성에서 회피합니다.";
        };
        var cellItems = new Control[] { reject };

        var menu = new ContextMenu();
        menu.Opening += (_, ce) =>
        {
            if (ctxKind == "col")
            {
                miColDel.IsEnabled = ctxColIdx >= Fixed && ContentCount() > 1;   // 파이썬 규칙
                menu.ItemsSource = colItems;
            }
            else if (ctxKind == "row") menu.ItemsSource = rowItems;
            else if (ctxKind == "cell" && ctxRow != null && ctxColIdx >= 2 && GetCell(ctxRow, ctxColIdx).Trim().Length > 0)
                menu.ItemsSource = cellItems;
            else ce.Cancel = true;   // 그 외(빈 셀·학번/이름 셀)엔 메뉴 없음
        };
        grid.ContextMenu = menu;

        // ── 엑셀식 키보드: Delete=선택 셀 비우기, Ctrl+V=붙여넣기(TSV) ──
        grid.KeyDown += async (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Delete && grid.SelectedItems.Count > 0)
            {
                foreach (var r in grid.SelectedItems.Cast<RowVm>()) { r.Num = r.Name = r.Content = ""; foreach (var c in extraCols) r[c.id] = ""; }
                Refresh(); e.Handled = true;
            }
            else if (e.Key == Avalonia.Input.Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                var cb = TopLevel.GetTopLevel(grid)?.Clipboard; if (cb == null) return;
                string txt = await cb.TryGetTextAsync() ?? ""; if (txt.Length == 0) return;
                int startRow = ctxRow != null ? ctxRowIdx : (grid.SelectedItem is RowVm sr ? rows.IndexOf(sr) : 0);
                if (startRow < 0) startRow = 0;
                int startCol = ctxColIdx >= 0 ? ctxColIdx : (grid.CurrentColumn != null ? grid.Columns.IndexOf(grid.CurrentColumn) : 0);
                if (startCol < 0) startCol = 0;
                var lines = txt.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    int ri = startRow + i; while (ri >= rows.Count) rows.Add(new RowVm());
                    var cells = lines[i].Split('\t');
                    for (int j = 0; j < cells.Length; j++) { int cj = startCol + j; if (cj < grid.Columns.Count) SetCell(rows[ri], cj, cells[j]); }
                }
                Refresh(); e.Handled = true;
            }
        };

        // ── 엑셀식 Ctrl+휠 = 시트 화면 확대/축소(줌) ──
        var sheetZoom = new ScaleTransform(1, 1);
        grid.RenderTransform = sheetZoom;
        grid.RenderTransformOrigin = RelativePoint.TopLeft;
        // 터널+handledEventsToo: 확대 후 내부 스크롤뷰가 휠을 삼켜 '축소가 안 되던' 문제 해결
        grid.AddHandler(InputElement.PointerWheelChangedEvent, (object? _, PointerWheelEventArgs e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            double s = Math.Clamp(sheetZoom.ScaleX + (e.Delta.Y > 0 ? 0.1 : -0.1), 0.5, 2.5);
            sheetZoom.ScaleX = s; sheetZoom.ScaleY = s; e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);

        // ── 엑셀식 행 높이 = 행번호 라벨(행 머리글) 하단 경계 드래그 ──
        var rowResize = new Cursor(StandardCursorType.SizeNorthSouth);
        bool rowDragging = false; double dragStartY = 0, dragStartH = 0;
        const double edge = 4;
        grid.AddHandler(InputElement.PointerMovedEvent, (object? _, PointerEventArgs e) =>
        {
            if (rowDragging)
            {
                grid.RowHeight = Math.Clamp(dragStartH + (e.GetPosition(grid).Y - dragStartY) / sheetZoom.ScaleY, 16, 200);
                e.Handled = true; return;
            }
            var hdr = (e.Source as Visual)?.FindAncestorOfType<DataGridRowHeader>();
            grid.Cursor = (hdr != null && e.GetPosition(hdr).Y >= hdr.Bounds.Height - edge) ? rowResize : Cursor.Default;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        grid.AddHandler(InputElement.PointerPressedEvent, (object? _, PointerPressedEventArgs e) =>
        {
            if (!e.GetCurrentPoint(grid).Properties.IsLeftButtonPressed) return;   // 좌클릭만 리사이즈
            var hdr = (e.Source as Visual)?.FindAncestorOfType<DataGridRowHeader>();
            if (hdr != null && e.GetPosition(hdr).Y >= hdr.Bounds.Height - edge)
            { rowDragging = true; dragStartY = e.GetPosition(grid).Y; dragStartH = grid.RowHeight; e.Pointer.Capture(grid); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        grid.AddHandler(InputElement.PointerReleasedEvent, (object? _, PointerReleasedEventArgs e) =>
        {
            if (rowDragging) { rowDragging = false; e.Pointer.Capture(null); grid.Cursor = Cursor.Default; e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // 열 머리글 더블클릭 = 이름 변경(엑셀식) — RenameCol로 contentLabel·추가열 라벨 동기화
        grid.DoubleTapped += (_, e) =>
        {
            if (e.Source is Avalonia.Visual vv && vv.FindAncestorOfType<DataGridColumnHeader>() is { } hdr)
            {
                int di = VisualIndex(hdr);
                if (di >= 0 && di < grid.Columns.Count) { RenameCol(di); e.Handled = true; }
            }
        };

        // 맞춤법 검사(선택 행 또는 입력) + 생성 대상 열
        var spellBtn = new Button { Content = "맞춤법 검사" };
        spellBtn.Click += async (_, _) => { string t = grid.SelectedItem is RowVm rr && rr.Content.Trim().Length > 0 ? rr.Content : (input.Text ?? "").Trim(); if (t.Length == 0) return; sheetMsg.Text = "네이버 맞춤법 검사 중…(온라인)"; var res = await Task.Run(() => Spellcheck.NaverSpellcheck(t)); sheetMsg.Text = res == null ? "맞춤법 검사 실패(오프라인/차단)." : $"교정: {res.Value.corrected} (오류 {res.Value.errata})"; };
        var colCombo = new ComboBox { MinWidth = 100 }; colCombo.Items.Add("내용"); colCombo.SelectedIndex = 0;
        // 학적(학생 레코드)이 있다고 판단되는 행 능동 판별 — 학번=숫자 / 이름=한글 2~5자
        static bool LooksLikeStudent(RowVm r)
        {
            string num = r.Num.Trim(), name = r.Name.Trim();
            bool numOk = num.Length > 0 && num.All(char.IsDigit);                       // 학번은 숫자
            bool nameOk = name.Length is >= 2 and <= 5 && name.All(Hangul.IsSyllable);  // 이름은 한글 2~5자
            return numOk || nameOk;
        }
        // 엑셀식 좌상단 코너 전체선택 버튼(라벨 없음) — 학적 있는 행만 선택
        var cornerCheck = new CheckBox { Padding = new Thickness(0), MinWidth = 0, MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        bool cornerSync = false;
        cornerCheck.IsCheckedChanged += (_, _) =>
        {
            if (cornerSync) return;
            grid.SelectedItems.Clear();
            if (cornerCheck.IsChecked == true)
                foreach (var r in rows) if (LooksLikeStudent(r)) grid.SelectedItems.Add(r);
        };
        grid.SelectionChanged += (_, _) =>
        {
            cornerSync = true;
            var idRows = rows.Where(LooksLikeStudent).ToList();
            cornerCheck.IsChecked = idRows.Count > 0 && idRows.All(r => grid.SelectedItems.Contains(r));
            cornerSync = false;
        };
        var cornerBox = new Border
        {
            Width = 34, Height = 32, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(1, 1, 0, 0), Background = Brush.Parse("#eef0f3"), Child = cornerCheck,
        };
        ToolTip.SetTip(cornerBox, "학적(학번·이름) 있는 행 전체 선택/해제");

        // 레이아웃(파이썬 동일): 위 패널(입력·옵션) + 시트(학급탭줄 / 툴바 / 그리드)
        Control HRow(params Control[] cs) { var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; foreach (var c in cs) p.Children.Add(c); return p; }
        Control Bar(Control left, Control right) { var d = new DockPanel(); d.Children.Add(Docked(right, Dock.Right)); d.Children.Add(left); return d; }
        var fsBtn = new Button { Content = "전체화면" };
        var topPanel = new StackPanel { Spacing = 6, Children = {
            areaStrip,
            HRow(new TextBlock { Text = "과목", VerticalAlignment = VerticalAlignment.Center }, subject, genOpts, new Control { Width = 12 }, learnLabel),
            new TextBlock { Text = "입력 (키워드·관찰 메모)", Margin = new Thickness(0, 6, 0, 0) }, input, morphBox, compliance,
            HRow(legend, termBtn), HRow(mode, genBtn, status) } };
        bool fs = false;
        fsBtn.Click += (_, _) => { fs = !fs; topPanel.IsVisible = !fs; fsBtn.Content = fs ? "원래대로" : "전체화면"; };

        var hint = new TextBlock { Text = "행번호 클릭=선택 · Ctrl/Shift=여러 개 · 우클릭=버리기 · Ctrl+휠=화면 확대/축소 · 열머리글·행번호 경계 드래그=너비/높이", Foreground = Brush.Parse("#999"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        Control MRow(double top, Control c) { c.Margin = new Thickness(0, top, 0, 0); return c; }

        // 시트 영역(파이썬 순서): 라벨 → 툴바(대상열·힌트 | 맞춤법·엑셀·저장) → 학급 탭(＋ · 전체화면) → 그리드
        var sheetLabel = new TextBlock { Text = "학급 표 (학급 탭별 · 선택한 행에 채워짐 · 엑셀 가져오기/내보내기)", FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#444"), Margin = new Thickness(0, 12, 0, 6) };
        var toolbar = Bar(HRow(new TextBlock { Text = "생성 대상 열", VerticalAlignment = VerticalAlignment.Center }, colCombo, hint), HRow(spellBtn, importX, exportX, saveSheet));
        var classRow = Bar(HRow(new TextBlock { Text = "학급", VerticalAlignment = VerticalAlignment.Center }, classStrip), fsBtn);

        var sheet = new DockPanel();
        sheet.Children.Add(Docked(sheetLabel, Dock.Top));
        sheet.Children.Add(Docked(toolbar, Dock.Top));
        sheet.Children.Add(Docked(MRow(6, classRow), Dock.Top));
        sheet.Children.Add(Docked(MRow(2, sheetMsg), Dock.Top));
        var gridHost = new Grid();   // 그리드 + 좌상단 코너 전체선택 오버레이(엑셀식)
        gridHost.Children.Add(grid);
        gridHost.Children.Add(cornerBox);
        sheet.Children.Add(MRow(4, gridHost));

        var root = new DockPanel { Margin = new Thickness(16, 12, 16, 12) };
        root.Children.Add(Docked(topPanel, Dock.Top));
        root.Children.Add(sheet);
        return root;
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
