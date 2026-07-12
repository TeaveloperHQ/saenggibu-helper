using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Saenggibu;

namespace Gui;

/// <summary>생기부 도우미 — 전체 화면(생성·학습·학급시트·수업메모·과정안내) 탭.</summary>
public class MainWindow : Window
{
    private LlamaEngine? _engine;
    private KiwiNative? _kiwi;
    private readonly string _dataDir;
    private readonly Settings _settings;
    private readonly Glossary _glossary;

    public MainWindow()
    {
        _dataDir = DataDir();
        Directory.CreateDirectory(_dataDir);
        _settings = new Settings(_dataDir);
        _glossary = new Glossary(_dataDir);

        Title = "생기부 도우미 (C# · Avalonia)";
        Width = 900; Height = 680;

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "생성 모드", Content = BuildGenerate() });
        tabs.Items.Add(new TabItem { Header = "학습 모드", Content = BuildLearn() });
        tabs.Items.Add(new TabItem { Header = "학급 시트", Content = BuildRoster() });
        tabs.Items.Add(new TabItem { Header = "수업 메모", Content = BuildMemo() });
        tabs.Items.Add(new TabItem { Header = "과정 안내", Content = BuildProcess() });
        Content = tabs;
    }

    // ── 생성 모드(내 문장 변형) ────────────────────────────────────────────
    private Control BuildGenerate()
    {
        var area = new ComboBox { Width = 190 };
        foreach (var a in Prompts.Areas) area.Items.Add(a.Title);
        area.SelectedIndex = 0;
        var subject = new TextBox { Watermark = "과목(세특)", Width = 130 };
        var count = new NumericUpDown { Value = 5, Minimum = 1, Maximum = 20, Width = 90 };
        var input = new TextBox { Watermark = "변형할 문장 입력", AcceptsReturn = true, Height = 90, TextWrapping = TextWrapping.Wrap };
        var btn = new Button { Content = "변형 생성", Padding = new Thickness(18, 6), FontWeight = FontWeight.Bold };
        var status = new TextBlock { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
        var output = new ListBox { Height = 300 };
        void Sync() => subject.IsVisible = Prompts.Areas[area.SelectedIndex].Key == "seteuk";
        area.SelectionChanged += (_, _) => Sync(); Sync();

        btn.Click += async (_, _) =>
        {
            string sent = (input.Text ?? "").Trim();
            if (sent.Length == 0) { status.Text = "문장을 입력하세요."; return; }
            btn.IsEnabled = false; output.Items.Clear();
            status.Text = "모델 준비·생성 중… (최초 로딩 수십 초)";
            int n = (int)(count.Value ?? 5);
            string subj = subject.IsVisible ? (subject.Text ?? "").Trim() : "";
            var terms = _glossary.AllTerms();
            try
            {
                var vars = await Task.Run(() =>
                {
                    EnsureEngines();
                    return Paraphrase.LlmParaphrase(sent, n, _engine!, _kiwi!, terms, Array.Empty<string>(), subj);
                });
                foreach (var v in vars) output.Items.Add(v);
                status.Text = vars.Count > 0 ? $"{vars.Count}개 변형 완료" : "변형 실패 — 문장을 바꿔 다시.";
            }
            catch (Exception ex) { status.Text = "오류: " + ex.Message; }
            finally { btn.IsEnabled = true; }
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (Control c in new Control[] { new TextBlock { Text = "영역", VerticalAlignment = VerticalAlignment.Center }, area, subject, new TextBlock { Text = "개수", VerticalAlignment = VerticalAlignment.Center }, count, btn })
            row.Children.Add(c);
        return Pad(new StackPanel { Spacing = 10 }.With(new Control[] {
            new TextBlock { Text = "생성 모드 — 내 문장 변형", FontSize = 17, FontWeight = FontWeight.Bold },
            row, new TextBlock { Text = "원문", FontWeight = FontWeight.Bold }, input,
            new TextBlock { Text = "변형 결과", FontWeight = FontWeight.Bold }, output, status }));
    }

    // ── 학습 모드(용어 등록 + 모델) ────────────────────────────────────────
    private Control BuildLearn()
    {
        var list = new ListBox { Height = 240 };
        void Refresh() { list.Items.Clear(); foreach (var t in _glossary.AllTerms().OrderBy(x => x, StringComparer.Ordinal)) list.Items.Add(t); }
        Refresh();
        var edit = new TextBox { Watermark = "예) 아이오딘화 칼륨", Width = 260 };
        var add = new Button { Content = "추가" };
        var del = new Button { Content = "선택 삭제" };
        var msg = new TextBlock { Foreground = Brushes.Gray };
        add.Click += (_, _) => { if (_glossary.Add(edit.Text ?? "")) { edit.Text = ""; Refresh(); msg.Text = "등록됨"; } };
        del.Click += (_, _) => { if (list.SelectedItem is string s && _glossary.Remove(s)) { Refresh(); msg.Text = "삭제됨"; } };
        string model = _settings.Get<string>("active_model") ?? "(자동)";
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (Control c in new Control[] { edit, add, del }) bar.Children.Add(c);
        return Pad(new StackPanel { Spacing = 10 }.With(new Control[] {
            new TextBlock { Text = "학습 모드 — 용어 등록(변형 시 철자 보존)", FontSize = 17, FontWeight = FontWeight.Bold },
            new TextBlock { Text = $"기본 모델: {model}", Foreground = Brushes.Gray },
            list, bar, msg }));
    }

    // ── 학급 시트(명단 + 엑셀 가져오기) ────────────────────────────────────
    private Control BuildRoster()
    {
        var areaBox = new ComboBox { Width = 190 };
        foreach (var a in Prompts.Areas) areaBox.Items.Add(a.Title);
        areaBox.SelectedIndex = 0;
        var listing = new ListBox { Height = 300 };
        void Load()
        {
            listing.Items.Clear();
            string area = Prompts.Areas[areaBox.SelectedIndex].Key;
            foreach (var kv in RosterData.ClassesAndStudents(_dataDir, area))
                listing.Items.Add($"[{kv.Key}] {string.Join(", ", kv.Value)}");
        }
        areaBox.SelectionChanged += (_, _) => Load(); Load();
        var import = new Button { Content = "엑셀(.xlsx) 불러오기" };
        var msg = new TextBlock { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
        import.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            { Title = "생기부 엑셀", AllowMultiple = false });
            var f = files?.FirstOrDefault();
            if (f == null) return;
            try
            {
                var recs = Importer.ParseXlsx(f.Path.LocalPath);
                listing.Items.Clear();
                foreach (var r in recs) listing.Items.Add(r);
                msg.Text = $"{recs.Count}개 기록 추출(본문 열 자동 판별).";
            }
            catch (Exception ex) { msg.Text = "오류: " + ex.Message; }
        };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (Control c in new Control[] { new TextBlock { Text = "영역", VerticalAlignment = VerticalAlignment.Center }, areaBox, import }) bar.Children.Add(c);
        return Pad(new StackPanel { Spacing = 10 }.With(new Control[] {
            new TextBlock { Text = "학급 시트 — 명단·엑셀 가져오기", FontSize = 17, FontWeight = FontWeight.Bold },
            bar, listing, msg }));
    }

    // ── 수업 메모(명단에 기록) ─────────────────────────────────────────────
    private Control BuildMemo()
    {
        var area = new ComboBox { Width = 170 };
        foreach (var a in Prompts.Areas) area.Items.Add(a.Title);
        area.SelectedIndex = 0;
        var klass = new TextBox { Watermark = "학급(예:1반)", Width = 100 };
        var num = new TextBox { Watermark = "번호", Width = 70 };
        var name = new TextBox { Watermark = "이름", Width = 100 };
        var text = new TextBox { Watermark = "관찰 메모", AcceptsReturn = true, Height = 90, TextWrapping = TextWrapping.Wrap };
        var save = new Button { Content = "저장(명단 반영)", FontWeight = FontWeight.Bold, Padding = new Thickness(14, 6) };
        var msg = new TextBlock { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
        save.Click += (_, _) =>
        {
            string ar = Prompts.Areas[area.SelectedIndex].Key;
            var res = RosterData.AddMemoToRoster(_dataDir, ar, (klass.Text ?? "").Trim(),
                (num.Text ?? "").Trim(), (name.Text ?? "").Trim(), (text.Text ?? "").Trim());
            msg.Text = res switch
            {
                "append" => "✓ 기존 학생 내용에 추가됨", "insert" => "＋ 새 학생 행 추가됨",
                "no_class" => "등록된 학급이 아닙니다(학급 시트 먼저 등록).", _ => "학급·이름/번호·메모를 확인하세요."
            };
            if (res is "append" or "insert") text.Text = "";
        };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (Control c in new Control[] { area, klass, num, name }) bar.Children.Add(c);
        return Pad(new StackPanel { Spacing = 10 }.With(new Control[] {
            new TextBlock { Text = "수업 메모 — 관찰을 명단에 기록", FontSize = 17, FontWeight = FontWeight.Bold },
            bar, text, save, msg }));
    }

    private Control BuildProcess() => Pad(new StackPanel { Spacing = 12 }.With(new Control[] {
        new TextBlock { Text = "과정 안내", FontSize = 17, FontWeight = FontWeight.Bold },
        new TextBlock { TextWrapping = TextWrapping.Wrap, Text =
            "① 키워드/문장 입력 →  ② 형태소 분석(Kiwi)으로 보존할 명사 파악 →  " +
            "③ 언어모델(오프라인)이 표현을 바꿔 여러 후보 생성 →  ④ 검증(용어 보존·비문·유사도) →  " +
            "⑤ 부족분은 사전·어순 규칙으로 보충.\n\n모든 처리는 인터넷 없이 내 PC에서 이뤄지며, " +
            "학생 정보는 외부로 나가지 않습니다(모델 다운로드·선택적 맞춤법 검사 제외)." } }));

    private static Control Pad(Control c) => new ScrollViewer { Content = new Border { Padding = new Thickness(18), Child = c } };

    private void EnsureEngines()
    {
        _kiwi ??= new KiwiNative(Environment.GetEnvironmentVariable("SGB_KIWI_MODEL")
            ?? throw new InvalidOperationException("환경변수 SGB_KIWI_MODEL(형태소 모델)이 필요합니다."));
        _engine ??= new LlamaEngine(Environment.GetEnvironmentVariable("SGB_GGUF")
            ?? throw new InvalidOperationException("환경변수 SGB_GGUF(언어모델 GGUF)가 필요합니다."));
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
