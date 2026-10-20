using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAXCom.Engine;

namespace AVAXCom.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAXCom/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    // The command centre's colours: cool steel and cyan, amber for warnings, red for the enemy.
    private const uint White = 0xFFE6F0F4, Cyan = 0xFF5AE0FF, Teal = 0xFF2E7A9A, Amber = 0xFFFFC040, Red = 0xFFFF4A3A, Green = 0xFF5AE07A, Dim = 0xFF7A94A8, Violet = 0xFFB07AFF;
    private const uint PanelFill = 0xEC08121E, PanelEdge = 0xFF2E6A8A;

    private static FormattedText Format(string text, double size, uint colour) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = Format(text, size, colour);
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(Format(text, size, 0xC0000000), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static double TextWidth(string text, double size) => Format(text, size, White).Width;

    /// <summary>Words wrapped to a width; returns the lines.</summary>
    private static List<string> Wrap(string text, double size, double width)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string next = line.Length == 0 ? word : line + " " + word;
            if (TextWidth(next, size) > width && line.Length > 0)
            {
                lines.Add(line);
                line = word;
            }
            else line = next;
        }
        if (line.Length > 0) lines.Add(line);
        return lines;
    }

    private static void Paragraph(DrawingContext dc, string text, Rect r, double size, uint colour, double spacing = 1.05)
    {
        double y = r.Y + size * 0.5;
        foreach (var line in Wrap(text, size, r.Width))
        {
            if (y > r.Bottom) break;
            Text(dc, line, new Point(r.X, y), size, colour);
            y += size * spacing;
        }
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static void Line(DrawingContext dc, uint colour, double width, params Point[] pts)
    {
        if (pts.Length < 2) return;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], false);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(false);
        }
        dc.DrawGeometry(null, Pen(colour, width), g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);
    private static uint Blend(uint a, uint b, double t) => Pix.Mix(a, b, t);
    private static uint Shade(uint c, double k) => Pix.Scale(c, k);

    /// <summary>A panel: dark glass with a lit edge and corner brackets, as on a command console.</summary>
    private static void Panel(DrawingContext dc, Rect r, uint edge = PanelEdge, uint fill = PanelFill)
    {
        dc.FillRectangle(Brush(fill), r, 4);
        dc.DrawRectangle(null, Pen(Alpha(edge, 0.7), 1.5), r.Deflate(1), 4, 4);
        dc.DrawRectangle(null, Pen(Alpha(0xFFFFFFFF, 0.05), 1), r.Deflate(4), 3, 3);
        double k = Math.Min(14, Math.Min(r.Width, r.Height) / 4);
        foreach (var (cx, cy, sx, sy) in new[] { (r.X, r.Y, 1, 1), (r.Right, r.Y, -1, 1), (r.X, r.Bottom, 1, -1), (r.Right, r.Bottom, -1, -1) })
        {
            dc.DrawLine(Pen(edge, 2.5), new Point(cx, cy), new Point(cx + sx * k, cy));
            dc.DrawLine(Pen(edge, 2.5), new Point(cx, cy), new Point(cx, cy + sy * k));
        }
    }

    /// <summary>A heading bar across the top of a panel.</summary>
    private static void Heading(DrawingContext dc, Rect r, string title, uint colour = Cyan)
    {
        dc.FillRectangle(Brush(0xFF0E2A3E), new Rect(r.X + 6, r.Y + 6, r.Width - 12, 34), 3);
        dc.FillRectangle(Brush(Alpha(colour, 0.8)), new Rect(r.X + 6, r.Y + 38, r.Width - 12, 2));
        Text(dc, title, new Point(r.X + 18, r.Y + 23), 30, colour);
    }

    /// <summary>A button: lit when the mouse is over it.</summary>
    private void Button(DrawingContext dc, Rect r, string label, bool on = true, double size = 20, bool lit = false, uint accent = Cyan)
    {
        bool over = r.Contains(_mouse) && on;
        dc.FillRectangle(Brush(!on ? 0xFF0C141C : lit ? Shade(accent, 0.35) : over ? 0xFF16364A : 0xFF0E2232), r, 3);
        dc.DrawRectangle(null, Pen(!on ? 0xFF1E2C38 : over || lit ? accent : Shade(accent, 0.55), 1.5), r, 3, 3);
        Text(dc, label, r.Center, size, on ? (lit ? 0xFFFFFFFF : White) : 0xFF4A5A68, center: true);
    }

    /// <summary>A bar of a value against its most.</summary>
    private static void Bar(DrawingContext dc, Rect r, double value, double max, uint colour, uint back = 0xFF101820)
    {
        dc.FillRectangle(Brush(back), r);
        if (max > 0) dc.FillRectangle(Brush(colour), new Rect(r.X, r.Y, r.Width * Math.Clamp(value / max, 0, 1), r.Height));
        dc.DrawRectangle(null, Pen(0x60FFFFFF, 1), r);
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.Geoscape: DrawGeoscape(dc); break;
            case GameState.Attract:
                if (g.Battle is not null) DrawBattle(dc);
                else DrawGeoscape(dc);
                dc.FillRectangle(Brush(0xB0000000), new Rect(0, 4, 760, 34));
                if ((int)(_clock * 2) % 2 == 0) Text(dc, "DEMONSTRATION - PRESS ENTER", new Point(380, 21), 26, Amber, center: true);
                break;
            case GameState.Briefing: DrawBriefing(dc); break;
            case GameState.Battle: DrawBattle(dc); break;
            case GameState.Debrief: DrawDebrief(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            case GameState.HighScores: DrawBackdrop(dc); DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawBackdrop(dc); DrawInitials(dc); break;
        }
        if (g.Message is { } msg && g.State is GameState.Geoscape or GameState.Battle or GameState.Attract && !g.Inventory && !g.BattleMenu)
        {
            double y = g.State == GameState.Geoscape ? 56 : 60;
            double w = TextWidth(msg, 26) + 40;
            double cx = g.State == GameState.Geoscape ? GlobeArea.Center.X : ScreenWidth / 2;
            dc.FillRectangle(Brush(0xC8000000), new Rect(cx - w / 2, y - 18, w, 36), 4);
            Text(dc, msg, new Point(cx, y), 26, Amber, center: true);
        }
        if (_padActive) DrawPadCursor(dc);
    }

    private void DrawPadCursor(DrawingContext dc)
    {
        var p = _padCursor;
        dc.DrawLine(Pen(0xFF000000, 4), new Point(p.X - 10, p.Y), new Point(p.X + 10, p.Y));
        dc.DrawLine(Pen(0xFF000000, 4), new Point(p.X, p.Y - 10), new Point(p.X, p.Y + 10));
        dc.DrawLine(Pen(Amber, 2), new Point(p.X - 9, p.Y), new Point(p.X + 9, p.Y));
        dc.DrawLine(Pen(Amber, 2), new Point(p.X, p.Y - 9), new Point(p.X, p.Y + 9));
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillEllipse(this DrawingContext dc, double x, double y, double rx, double ry, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), rx, ry);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
