using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Saenggibu;

namespace Gui;

/// <summary>생기부 도우미 — 생성 화면. 영역·과목·키워드 → LlmParaphrase(변형 생성).
/// 모델 경로는 환경변수 SGB_GGUF(언어모델)·SGB_KIWI_MODEL(형태소 모델).</summary>
public class MainWindow : Window
{
    private LlamaEngine? _engine;
    private KiwiNative? _kiwi;

    public MainWindow()
    {
        Title = "생기부 도우미 (C# · Avalonia)";
        Width = 820; Height = 640;

        var area = new ComboBox { Width = 200 };
        foreach (var a in Prompts.Areas) area.Items.Add(a.Title);
        area.SelectedIndex = 0;

        var subject = new TextBox { Watermark = "과목 (세특)", Width = 140 };
        var count = new NumericUpDown { Value = 5, Minimum = 1, Maximum = 20, Width = 90 };
        var input = new TextBox
        {
            Watermark = "변형할 문장을 입력하세요 (예: 지시약 실험을 직접 설계하고 결과를 그래프로 정리함)",
            AcceptsReturn = true, Height = 100, TextWrapping = TextWrapping.Wrap,
        };
        var btn = new Button { Content = "변형 생성", Padding = new Thickness(20, 8), FontWeight = FontWeight.Bold };
        var status = new TextBlock { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap };
        var output = new ListBox { Height = 320 };

        void SyncSubject() => subject.IsVisible = area.SelectedIndex >= 0
            && Prompts.Areas[area.SelectedIndex].Key == "seteuk";
        area.SelectionChanged += (_, _) => SyncSubject();
        SyncSubject();

        btn.Click += async (_, _) =>
        {
            string sent = (input.Text ?? "").Trim();
            if (sent.Length == 0) { status.Text = "문장을 입력하세요."; return; }
            btn.IsEnabled = false;
            output.Items.Clear();
            status.Text = "모델 준비·생성 중… (최초 1회 모델 로딩에 수십 초)";
            int n = (int)(count.Value ?? 5);
            string subj = subject.IsVisible ? (subject.Text ?? "").Trim() : "";
            try
            {
                var vars = await Task.Run(() =>
                {
                    EnsureEngines();
                    return Paraphrase.LlmParaphrase(sent, n, _engine!, _kiwi!,
                        Array.Empty<string>(), Array.Empty<string>(), subj);
                });
                foreach (var v in vars) output.Items.Add(v);
                status.Text = vars.Count > 0
                    ? $"{vars.Count}개 변형 생성 완료 (Kiwi {KiwiNative.Version()})"
                    : "변형을 만들지 못했습니다. 문장을 바꿔 다시 시도하세요.";
            }
            catch (Exception ex) { status.Text = "오류: " + ex.Message; }
            finally { btn.IsEnabled = true; }
        };

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row1.Children.Add(new TextBlock { Text = "영역", VerticalAlignment = VerticalAlignment.Center });
        row1.Children.Add(area);
        row1.Children.Add(subject);
        row1.Children.Add(new TextBlock { Text = "개수", VerticalAlignment = VerticalAlignment.Center });
        row1.Children.Add(count);
        row1.Children.Add(btn);

        var root = new StackPanel { Margin = new Thickness(18), Spacing = 10 };
        root.Children.Add(new TextBlock { Text = "생성 모드 — 내 문장 변형", FontSize = 18, FontWeight = FontWeight.Bold });
        root.Children.Add(row1);
        root.Children.Add(new TextBlock { Text = "원문", FontWeight = FontWeight.Bold });
        root.Children.Add(input);
        root.Children.Add(new TextBlock { Text = "변형 결과", FontWeight = FontWeight.Bold });
        root.Children.Add(output);
        root.Children.Add(status);
        Content = root;
    }

    private void EnsureEngines()
    {
        _kiwi ??= new KiwiNative(Environment.GetEnvironmentVariable("SGB_KIWI_MODEL")
            ?? throw new InvalidOperationException("환경변수 SGB_KIWI_MODEL(형태소 모델 경로)이 필요합니다."));
        _engine ??= new LlamaEngine(Environment.GetEnvironmentVariable("SGB_GGUF")
            ?? throw new InvalidOperationException("환경변수 SGB_GGUF(언어모델 GGUF 경로)가 필요합니다."));
    }
}
