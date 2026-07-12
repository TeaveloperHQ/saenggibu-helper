using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Gui;

/// <summary>
/// app/theme.py _UI_ICONS 이식 — 동일한 SVG(24×24, stroke, Feather 스타일) path를
/// Avalonia Shape로 그대로 렌더한다. 컬러 이모지 대신 브랜드 라인 아이콘 유지.
/// </summary>
public static class SgbIcon
{
    public const string Accent = "#4f46e5";   // theme.ACCENT

    private static readonly Dictionary<string, string> Bodies = new()
    {
        ["edit"] = "<path d='M12 20h9'/><path d='M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4 12.5-12.5z'/>",
        ["book"] = "<path d='M4 19.5A2.5 2.5 0 0 1 6.5 17H20'/><path d='M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z'/>",
        ["steps"] = "<circle cx='5' cy='6' r='1.6'/><path d='M10 6h10'/><circle cx='5' cy='12' r='1.6'/><path d='M10 12h10'/><circle cx='5' cy='18' r='1.6'/><path d='M10 18h10'/><path d='M5 7.6v2.8'/><path d='M5 13.6v2.8'/>",
        ["copy"] = "<rect x='9' y='9' width='11' height='11' rx='2'/><path d='M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1'/>",
        ["user"] = "<path d='M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2'/><circle cx='12' cy='7' r='4'/>",
        ["lock"] = "<rect x='3' y='11' width='18' height='11' rx='2'/><path d='M7 11V7a5 5 0 0 1 10 0v4'/>",
        ["down"] = "<path d='M12 5v14'/><path d='M19 12l-7 7-7-7'/>",
        ["clipboard"] = "<rect x='8' y='3' width='8' height='4' rx='1'/><path d='M8 5H6a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2'/><path d='M9 12h6'/><path d='M9 16h4'/>",
        ["save"] = "<path d='M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4'/><path d='M7 10l5 5 5-5'/><path d='M12 15V3'/>",
        ["maximize"] = "<path d='M8 3H5a2 2 0 0 0-2 2v3'/><path d='M21 8V5a2 2 0 0 0-2-2h-3'/><path d='M3 16v3a2 2 0 0 0 2 2h3'/><path d='M16 21h3a2 2 0 0 0 2-2v-3'/>",
        ["plus"] = "<path d='M12 5v14'/><path d='M5 12h14'/>",
        ["trash"] = "<path d='M3 6h18'/><path d='M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2'/><path d='M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6'/>",
        ["check"] = "<path d='M20 6L9 17l-5-5'/>",
        ["import"] = "<path d='M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4'/><path d='M7 10l5 5 5-5'/><path d='M12 15V3'/>",
        ["export"] = "<path d='M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4'/><path d='M17 8l-5-5-5 5'/><path d='M12 3v12'/>",
        ["tag"] = "<path d='M20.59 13.41l-7.17 7.17a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z'/><circle cx='7' cy='7' r='1.2'/>",
    };

    private static readonly Regex PathRe = new(@"<path\s+d='([^']*)'", RegexOptions.Compiled);
    private static readonly Regex CircleRe = new(@"<circle\s+cx='([^']*)'\s+cy='([^']*)'\s+r='([^']*)'", RegexOptions.Compiled);
    private static readonly Regex RectRe = new(@"<rect\s+x='([^']*)'\s+y='([^']*)'\s+width='([^']*)'\s+height='([^']*)'(?:\s+rx='([^']*)')?", RegexOptions.Compiled);

    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    public static Control Make(string name, double size = 18, string color = Accent)
    {
        var brush = Brush.Parse(color);
        var canvas = new Canvas { Width = 24, Height = 24 };
        if (Bodies.TryGetValue(name, out var body))
        {
            foreach (Match m in PathRe.Matches(body))
                canvas.Children.Add(Stroke(new Path { Data = Geometry.Parse(m.Groups[1].Value) }, brush));
            foreach (Match m in CircleRe.Matches(body))
            {
                double cx = D(m.Groups[1].Value), cy = D(m.Groups[2].Value), r = D(m.Groups[3].Value);
                var e = Stroke(new Ellipse { Width = 2 * r, Height = 2 * r }, brush);
                Canvas.SetLeft(e, cx - r); Canvas.SetTop(e, cy - r); canvas.Children.Add(e);
            }
            foreach (Match m in RectRe.Matches(body))
            {
                double x = D(m.Groups[1].Value), y = D(m.Groups[2].Value), w = D(m.Groups[3].Value), h = D(m.Groups[4].Value);
                double rx = m.Groups[5].Success && m.Groups[5].Value.Length > 0 ? D(m.Groups[5].Value) : 0;
                var rc = Stroke(new Rectangle { Width = w, Height = h, RadiusX = rx, RadiusY = rx }, brush);
                Canvas.SetLeft(rc, x); Canvas.SetTop(rc, y); canvas.Children.Add(rc);
            }
        }
        return new Viewbox { Width = size, Height = size, Child = canvas, Stretch = Stretch.Uniform };
    }

    private static T Stroke<T>(T shape, IBrush brush) where T : Shape
    {
        shape.Stroke = brush; shape.StrokeThickness = 2;
        shape.StrokeLineCap = PenLineCap.Round; shape.StrokeJoin = PenLineJoin.Round;
        shape.Fill = null;
        return shape;
    }
}
