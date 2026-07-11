using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Saenggibu;

namespace Gui;

/// <summary>생기부 도우미 — Avalonia 데스크톱 셸. 이식된 Core(변형 엔진)+Kiwi로 실제 변형 수행.</summary>
public class MainWindow : Window
{
    private KiwiNative? _kiwi;

    public MainWindow()
    {
        Title = "생기부 도우미 (Avalonia · C# 포팅)";
        Width = 760; Height = 560;

        var input = new TextBox
        {
            Watermark = "생기부 문장을 입력하세요 (예: 실험을 설계하고 결과를 분석함. 논리적 사고력을 보임)",
            AcceptsReturn = true, Height = 90, TextWrapping = TextWrapping.Wrap,
        };
        var count = new NumericUpDown { Value = 6, Minimum = 1, Maximum = 30, Width = 110 };
        var btn = new Button { Content = "변형 생성", Padding = new Thickness(16, 6) };
        var status = new TextBlock { Foreground = Brushes.Gray };
        var output = new ListBox { Height = 300 };

        btn.Click += (_, _) =>
        {
            var sent = (input.Text ?? "").Trim();
            output.Items.Clear();
            if (sent.Length == 0) { status.Text = "문장을 입력하세요."; return; }
            try
            {
                _kiwi ??= new KiwiNative(
                    Environment.GetEnvironmentVariable("SGB_KIWI_MODEL")
                    ?? throw new InvalidOperationException("환경변수 SGB_KIWI_MODEL(모델 경로) 필요"));
                int n = (int)(count.Value ?? 6);
                var vars = Paraphrase.RecombineParaphrase(sent, n, _kiwi,
                    Array.Empty<string>(), Array.Empty<string>());
                foreach (var v in vars) output.Items.Add(v);
                status.Text = $"{vars.Count}개 변형 생성 (Kiwi {KiwiNative.Version()})";
            }
            catch (Exception ex) { status.Text = "오류: " + ex.Message; }
        };

        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        top.Children.Add(new TextBlock { Text = "개수", VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(count);
        top.Children.Add(btn);

        var root = new StackPanel { Margin = new Thickness(18), Spacing = 10 };
        root.Children.Add(new TextBlock { Text = "원문", FontWeight = FontWeight.Bold });
        root.Children.Add(input);
        root.Children.Add(top);
        root.Children.Add(new TextBlock { Text = "변형 결과", FontWeight = FontWeight.Bold });
        root.Children.Add(output);
        root.Children.Add(status);
        Content = root;
    }
}
