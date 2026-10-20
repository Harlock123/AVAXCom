using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AVAXCom.Engine;

namespace AVAXCom.Views;

public sealed partial class GameCanvas
{
    public static readonly Rect GlobeArea = new(0, 0, 780, 768);
    private const double GlobeCx = 390, GlobeCy = 392;
    private static GlobePainter? _globe;
    private static WriteableBitmap? _starBitmap;

    // ───────────────────────────── the globe ─────────────────────────────

    private (Point At, bool Front) Project(GeoPoint p)
    {
        var g = Engine;
        var (x, y, front) = Geo.Project(p, g.View);
        return (new Point(GlobeCx + x * g.Zoom, GlobeCy - y * g.Zoom), front);
    }

    /// <summary>The place under a point of the screen, or null off the globe.</summary>
    public GeoPoint? GlobePoint(Point at) => Geo.Unproject((at.X - GlobeCx) / Engine.Zoom, -(at.Y - GlobeCy) / Engine.Zoom, Engine.View);

    /// <summary>Where a place is drawn on the screen (for tests: clicking it).</summary>
    public Point ScreenOf(GeoPoint p) => Project(p).At;

    private void DrawGeoscape(DrawingContext dc)
    {
        var g = Engine;
        var c = g.Campaign;
        using (dc.PushClip(GlobeArea))
        {
            _starBitmap ??= GlobePainter.Stars((int)GlobeArea.Width, (int)GlobeArea.Height).ToBitmap();
            dc.DrawImage(_starBitmap, GlobeArea);
            _globe ??= new GlobePainter((int)GlobeArea.Width, (int)GlobeArea.Height);
            var bmp = _globe.Render(g.View, g.Zoom, GlobeCx, GlobeCy, c?.Now ?? Geo.Start);
            dc.DrawImage(bmp, GlobeArea);
            if (c is not null) DrawGlobeMarks(dc, c);
            if (g.PlacingBase && GlobeArea.Contains(_mouse) && GlobePoint(_mouse) is { } at)
            {
                bool land = LandMask.IsLand(at);
                dc.DrawEllipse(null, Pen(land ? Green : Red, 2), _mouse, 14, 14);
                dc.DrawLine(Pen(land ? Green : Red, 1.5), new Point(_mouse.X - 20, _mouse.Y), new Point(_mouse.X + 20, _mouse.Y));
                dc.DrawLine(Pen(land ? Green : Red, 1.5), new Point(_mouse.X, _mouse.Y - 20), new Point(_mouse.X, _mouse.Y + 20));
                Text(dc, land ? Campaign.BaseName(at) : "OPEN SEA", new Point(_mouse.X + 22, _mouse.Y - 18), 22, land ? Green : Red);
            }
            DrawTicker(dc);
        }
        DrawCommandPanel(dc);
        switch (g.Screen)
        {
            case GeoScreen.Event: DrawEvent(dc); break;
            case GeoScreen.Intercept: DrawIntercept(dc); break;
            case GeoScreen.AirCombat: DrawAirCombat(dc); break;
            case GeoScreen.Base: DrawBase(dc); break;
            case GeoScreen.Research: DrawResearch(dc); break;
            case GeoScreen.Manufacture: DrawManufacture(dc); break;
            case GeoScreen.Purchase: DrawPurchase(dc); break;
            case GeoScreen.Sell: DrawSell(dc); break;
            case GeoScreen.Soldiers: DrawSoldiers(dc); break;
            case GeoScreen.Funding: DrawFunding(dc); break;
            case GeoScreen.Memorial: DrawMemorial(dc); break;
            case GeoScreen.Files: DrawFiles(dc); break;
        }
    }

    /// <summary>A circle of a given radius (nautical miles) round a place, as a line on the globe's near side.</summary>
    private void GlobeCircle(DrawingContext dc, GeoPoint centre, double nm, uint colour, double width)
    {
        var pts = new List<Point>();
        var c = Geo.ToVec(centre);
        // Two directions at right angles to the centre, to swing round it.
        var up = Math.Abs(c.Y) > 0.9 ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0);
        var e1 = new Vec3(up.Y * c.Z - up.Z * c.Y, up.Z * c.X - up.X * c.Z, up.X * c.Y - up.Y * c.X).Unit;
        var e2 = new Vec3(c.Y * e1.Z - c.Z * e1.Y, c.Z * e1.X - c.X * e1.Z, c.X * e1.Y - c.Y * e1.X);
        double a = nm / Geo.EarthNm;
        for (int i = 0; i <= 72; i++)
        {
            double t = i * Math.PI * 2 / 72;
            var v = c * Math.Cos(a) + (e1 * Math.Cos(t) + e2 * Math.Sin(t)) * Math.Sin(a);
            var (p, front) = Project(Geo.FromVec(v));
            if (front) pts.Add(p);
            else if (pts.Count > 1) { Line(dc, colour, width, [.. pts]); pts.Clear(); }
            else pts.Clear();
        }
        if (pts.Count > 1) Line(dc, colour, width, [.. pts]);
    }

    /// <summary>A great-circle line from one place to another, on the near side.</summary>
    private void GlobeLine(DrawingContext dc, GeoPoint a, GeoPoint b, uint colour, double width)
    {
        double nm = Geo.Nm(a, b);
        int n = Math.Max(2, (int)(nm / 150));
        var pts = new List<Point>();
        for (int i = 0; i <= n; i++)
        {
            var (p, front) = Project(Geo.Toward(a, b, nm * i / n));
            if (front) pts.Add(p);
        }
        if (pts.Count > 1) Line(dc, colour, width, [.. pts]);
    }

    private void DrawGlobeMarks(DrawingContext dc, Campaign c)
    {
        var g = Engine;
        double blink = 0.5 + 0.5 * Math.Sin(_clock * 6);
        // Nations' names, once near enough.
        if (g.Zoom >= 420)
            for (int i = 0; i < Earth.Nations.Length; i++)
            {
                var (p, front) = Project(Earth.Nations[i].At);
                if (front) Text(dc, Earth.Nations[i].Name, p, 18, c.Nations[i].Withdrawn ? Alpha(Red, 0.7) : Alpha(0xFFFFFFFF, 0.45), center: true);
            }
        // Cities.
        foreach (var city in Earth.Cities)
        {
            var (p, front) = Project(city.At);
            if (!front) continue;
            dc.FillEllipse(p.X, p.Y, 1.8, 0xFFFFE8A0);
            if (g.Zoom >= 600 && !c.Bases.Any(b => b.Name == city.Name)) Text(dc, city.Name, new Point(p.X + 5, p.Y - 8), 15, 0xC0FFE8A0);
        }
        // Radar coverage.
        foreach (var b in c.Bases.Where(b => b.RadarRange > 0)) GlobeCircle(dc, b.At, b.RadarRange, 0x5040C8FF, 1.2);
        // Bases.
        foreach (var b in c.Bases)
        {
            var (p, front) = Project(b.At);
            if (!front) continue;
            dc.FillRectangle(Brush(0xFF000000), new Rect(p.X - 6, p.Y - 6, 12, 12));
            dc.FillRectangle(Brush(0xFF3A8AFF), new Rect(p.X - 5, p.Y - 5, 10, 10));
            dc.DrawRectangle(null, Pen(0xFFFFFFFF, 1.2), new Rect(p.X - 5, p.Y - 5, 10, 10));
            Text(dc, b.Name, new Point(p.X + 9, p.Y + 1), 17, 0xFFB0D8FF);
        }
        // Sites.
        foreach (var s in c.Sites.Where(s => s.Known))
        {
            var (p, front) = Project(s.At);
            if (!front) continue;
            switch (s.Kind)
            {
                case SiteKind.CrashSite:
                    dc.DrawLine(Pen(Amber, 3), new Point(p.X - 6, p.Y - 6), new Point(p.X + 6, p.Y + 6));
                    dc.DrawLine(Pen(Amber, 3), new Point(p.X + 6, p.Y - 6), new Point(p.X - 6, p.Y + 6));
                    Text(dc, "CRASH SITE", new Point(p.X + 9, p.Y + 10), 15, Amber);
                    break;
                case SiteKind.Terror:
                    dc.DrawEllipse(null, Pen(Alpha(Red, blink), 2), p, 8 + blink * 6, 8 + blink * 6);
                    dc.FillEllipse(p.X, p.Y, 5, Red);
                    Text(dc, "TERROR", new Point(p.X + 9, p.Y + 10), 16, Red);
                    break;
                case SiteKind.AlienBase:
                    Poly(dc, Violet, 0xFF000000, 1, new Point(p.X, p.Y - 8), new Point(p.X + 8, p.Y), new Point(p.X, p.Y + 8), new Point(p.X - 8, p.Y));
                    Text(dc, "ALIEN BASE", new Point(p.X + 10, p.Y + 10), 15, Violet);
                    break;
                default:
                    dc.DrawEllipse(Brush(0xFF2A0A3A), Pen(Alpha(Violet, 0.6 + 0.4 * blink), 2), p, 9, 9);
                    Text(dc, $"{Rules.Homeworld} GATE", new Point(p.X + 12, p.Y), 18, Violet);
                    break;
            }
        }
        // UFOs on radar.
        foreach (var u in c.Ufos.Where(u => u.Detected))
        {
            var (p, front) = Project(u.At);
            if (!front) continue;
            if (u.State == UfoState.Flying)
            {
                var (ahead, f2) = Project(Geo.Toward(u.At, u.Dest, 260));
                if (f2) dc.DrawLine(Pen(Alpha(Red, 0.5), 1.2), p, ahead);
            }
            dc.DrawEllipse(null, Pen(Alpha(Red, blink), 1.5), p, 10 + blink * 4, 10 + blink * 4);
            DrawSaucer(dc, p, 9, u.Type, u.State == UfoState.Landed ? Amber : Red);
            Text(dc, u.Name, new Point(p.X + 12, p.Y - 12), 16, u.State == UfoState.Landed ? Amber : 0xFFFF8A7A);
        }
        // Craft in flight, and where they're going.
        foreach (var k in c.Crafts.Where(k => k.Flying))
        {
            var (p, front) = Project(k.At);
            GeoPoint? target = c.UfoById(k.TargetUfo)?.At ?? c.SiteById(k.TargetSite)?.At ?? k.Waypoint;
            if (k.State == CraftState.Returning && c.BaseById(k.BaseId) is { } home) target = home.At;
            if (target is { } tgt) GlobeLine(dc, k.At, tgt, 0x70FFFFFF, 1);
            if (!front) continue;
            uint col = k.Info.Seats > 0 ? 0xFF7AF0FF : Green;
            double hd = target is { } t2 ? Math.Atan2(Project(t2).At.X - p.X, -(Project(t2).At.Y - p.Y)) : 0;
            Point R(double dx, double dy) => new(p.X + dx * Math.Cos(hd) - dy * Math.Sin(hd), p.Y + dx * Math.Sin(hd) + dy * Math.Cos(hd));
            Poly(dc, col, 0xFF000000, 1, R(0, -8), R(6, 6), R(0, 3), R(-6, 6));
            Text(dc, k.Name, new Point(p.X + 9, p.Y + 10), 15, col);
        }
    }

    /// <summary>A UFO's silhouette: a saucer, wider and deeper the bigger the type.</summary>
    private static void DrawSaucer(DrawingContext dc, Point p, double size, UfoType t, uint colour)
    {
        double w = size * (1 + (int)t * 0.12), h = size * 0.38;
        dc.FillEllipse(p.X, p.Y, w + 1.5, h + 1.5, 0xFF000000);
        dc.FillEllipse(p.X, p.Y, w, h, colour);
        dc.FillEllipse(p.X, p.Y - h * 0.6, w * 0.45, h * 0.9, Blend(colour, 0xFFFFFFFF, 0.45));
        dc.FillEllipse(p.X, p.Y + h * 0.15, w * 0.8, h * 0.25, Shade(colour, 0.55));
    }

    private void DrawTicker(DrawingContext dc)
    {
        var g = Engine;
        if (g.Ticker.Count == 0) return;
        double y = GlobeArea.Bottom - 16 - Math.Min(3, g.Ticker.Count) * 20;
        foreach (var line in g.Ticker.TakeLast(3))
        {
            Text(dc, "> " + line, new Point(14, y + 10), 18, 0xD0A8E8FF);
            y += 20;
        }
    }

    // ───────────────────────────── the command panel ─────────────────────────────

    private static readonly Rect CommandRect = new(780, 0, 244, 768);
    private static Rect SpeedRect(int i) => new(792 + i % 3 * 75, 214 + i / 3 * 34, 70, 30);
    private static readonly (string Label, GeoScreen Screen)[] Commands =
    [
        ("INTERCEPT", GeoScreen.Intercept), ("BASES", GeoScreen.Base), ("RESEARCH", GeoScreen.Research), ("MANUFACTURE", GeoScreen.Manufacture),
        ("PURCHASE", GeoScreen.Purchase), ("SELL", GeoScreen.Sell), ("SOLDIERS", GeoScreen.Soldiers), ("FUNDING", GeoScreen.Funding),
        ("MEMORIAL", GeoScreen.Memorial), ("SAVE / LOAD", GeoScreen.Files),
    ];
    private static Rect CommandButton(int i) => new(792, 296 + i * 36, 220, 31);
    private static readonly Rect ZoomIn = new(950, 676, 56, 32), ZoomOut = new(950, 712, 56, 32);
    private static Rect RotateRect(int dir) => dir switch { 0 => new(842, 670, 40, 30), 1 => new(884, 694, 40, 30), 2 => new(842, 718, 40, 30), _ => new(800, 694, 40, 30) };

    private void DrawCommandPanel(DrawingContext dc)
    {
        var g = Engine;
        var c = g.Campaign;
        dc.FillRectangle(Brush(0xFF060C14), CommandRect);
        Panel(dc, CommandRect.Deflate(4), PanelEdge, 0xFF0A1622);
        // The emblem: an open eye inside a ring.
        var e = new Point(830, 44);
        dc.DrawEllipse(Brush(0xFF0E2A3E), Pen(Cyan, 2), e, 24, 24);
        Poly(dc, 0xFFE6F0F4, null, 0, new Point(e.X - 17, e.Y), new Point(e.X - 6, e.Y - 8), new Point(e.X + 6, e.Y - 8), new Point(e.X + 17, e.Y), new Point(e.X + 6, e.Y + 8), new Point(e.X - 6, e.Y + 8));
        dc.FillEllipse(e.X, e.Y, 6.5, 0xFF1A6A9A);
        dc.FillEllipse(e.X, e.Y, 3, 0xFF02060A);
        Text(dc, Rules.Title, new Point(862, 36), 44, Cyan);
        Text(dc, "PLANETARY DEFENCE", new Point(864, 62), 16, Dim);
        if (c is null) return;
        var now = c.Now;
        dc.FillRectangle(Brush(0xFF02080C), new Rect(792, 86, 220, 116), 3);
        dc.DrawRectangle(null, Pen(0xFF1E4A60, 1), new Rect(792, 86, 220, 116), 3, 3);
        Text(dc, now.ToString("HH:mm:ss"), new Point(902, 112), 40, Green, center: true);
        Text(dc, now.DayOfWeek.ToString().ToUpperInvariant(), new Point(902, 140), 20, Dim, center: true);
        Text(dc, $"{now.Day} {now:MMMM} {now.Year}".ToUpperInvariant(), new Point(902, 160), 22, White, center: true);
        Text(dc, $"FUNDS ${c.Money / 1000:N0}K", new Point(902, 186), 22, c.Money < 0 ? Red : Amber, center: true);
        for (int i = 0; i < GameEngine.SpeedNames.Length; i++)
            Button(dc, SpeedRect(i), GameEngine.SpeedNames[i], size: 17, lit: g.SpeedIndex == i && g.TimeRunning, accent: Green);
        for (int i = 0; i < Commands.Length; i++)
            Button(dc, CommandButton(i), Commands[i].Label, c.Bases.Count > 0 || Commands[i].Screen == GeoScreen.Files, 21, g.Screen == Commands[i].Screen);
        string[] arrows = ["^", ">", "v", "<"];
        for (int d = 0; d < 4; d++) Button(dc, RotateRect(d), arrows[d], size: 22);
        Button(dc, ZoomIn, "+", size: 26);
        Button(dc, ZoomOut, "-", size: 26);
        Text(dc, "ROTATE", new Point(862, 662), 15, Dim, center: true);
        Text(dc, "ZOOM", new Point(978, 662), 15, Dim, center: true);
        Text(dc, $"SCORE {g.Score}", new Point(902, 752), 18, Dim, center: true);
    }

    // ───────────────────────────── clicks on the geoscape ─────────────────────────────

    private void GeoClick(Point at, bool shift)
    {
        var g = Engine;
        switch (g.Screen)
        {
            case GeoScreen.Event: EventClick(at); return;
            case GeoScreen.AirCombat: AirClick(at); return;
            case GeoScreen.Intercept: InterceptClick(at); return;
            case GeoScreen.Base: BaseClick(at); return;
            case GeoScreen.Research: ResearchClick(at); return;
            case GeoScreen.Manufacture: ManufactureClick(at, shift); return;
            case GeoScreen.Purchase: PurchaseClick(at, shift); return;
            case GeoScreen.Sell: SellClick(at, shift); return;
            case GeoScreen.Soldiers: SoldiersClick(at); return;
            case GeoScreen.Funding or GeoScreen.Memorial: if (DoneRect.Contains(at)) g.CloseScreen(); return;
            case GeoScreen.Files: FilesClick(at); return;
        }
        if (CommandRect.Contains(at))
        {
            for (int i = 0; i < GameEngine.SpeedNames.Length; i++) if (SpeedRect(i).Contains(at)) g.SetSpeed(i);
            for (int i = 0; i < Commands.Length; i++)
            {
                if (!CommandButton(i).Contains(at)) continue;
                if (Commands[i].Screen == GeoScreen.Intercept) g.OpenIntercept(null, null);
                else if (Commands[i].Screen == GeoScreen.Files) g.OpenFiles(saving: g.Campaign is { Bases.Count: > 0 });
                else g.Open(Commands[i].Screen);
            }
            for (int d = 0; d < 4; d++)
                if (RotateRect(d).Contains(at)) g.Rotate(d == 1 ? 15 : d == 3 ? -15 : 0, d == 0 ? 10 : d == 2 ? -10 : 0);
            if (ZoomIn.Contains(at)) g.ZoomBy(1);
            if (ZoomOut.Contains(at)) g.ZoomBy(-1);
            return;
        }
        if (GlobeArea.Contains(at))
        {
            _dragFrom = at;
            _dragged = false;
        }
    }

    private void GeoRightClick(Point at)
    {
        var g = Engine;
        if (g.Screen is GeoScreen.Research or GeoScreen.Manufacture) { ListRightClick(at); return; }
        if (g.Screen == GeoScreen.Base) { BaseRightClick(at); return; }
        if (g.Screen is not (GeoScreen.Event or GeoScreen.AirCombat)) g.CloseScreen();
    }

    // ───────────────────────────── events ─────────────────────────────

    private static readonly Rect EventRect = new(150, 230, 480, 300);
    private static Rect ChoiceRect(int i, int n) => new(EventRect.Center.X - n * 75 + i * 150 + 4, EventRect.Bottom - 56, 142, 40);

    private void DrawEvent(DrawingContext dc)
    {
        if (Engine.Event is not { } e) return;
        uint accent = e.Kind switch
        {
            EventKind.UfoDetected or EventKind.TerrorSite or EventKind.BaseAttack or EventKind.CraftLost => Red,
            EventKind.ResearchDone or EventKind.ProductionDone => Green,
            EventKind.MonthlyReport => Amber,
            _ => Cyan,
        };
        Panel(dc, EventRect, accent);
        Heading(dc, EventRect, e.Title, accent);
        if (e.Kind == EventKind.UfoDetected && Engine.Campaign?.UfoById(e.Ufo) is { } u)
        {
            DrawSaucer(dc, new Point(EventRect.Right - 70, EventRect.Y + 92), 30, u.Type, 0xFFB8C0C8);
            Paragraph(dc, e.Text, new Rect(EventRect.X + 22, EventRect.Y + 56, EventRect.Width - 150, 160), 22, White);
        }
        else Paragraph(dc, e.Text, new Rect(EventRect.X + 22, EventRect.Y + 56, EventRect.Width - 44, 170), 22, White);
        var choices = GameEngine.Choices(e);
        for (int i = 0; i < choices.Length; i++) Button(dc, ChoiceRect(i, choices.Length), choices[i], size: 20, accent: accent);
    }

    private void EventClick(Point at)
    {
        if (Engine.Event is not { } e) return;
        var choices = GameEngine.Choices(e);
        for (int i = 0; i < choices.Length; i++)
            if (ChoiceRect(i, choices.Length).Contains(at)) { Engine.Answer(i); return; }
    }

    // ───────────────────────────── interception ─────────────────────────────

    private static readonly Rect InterceptRect = new(60, 150, 660, 440);
    private static Rect CraftRow(int i) => new(InterceptRect.X + 16, InterceptRect.Y + 92 + i * 34, InterceptRect.Width - 32, 30);
    private static readonly Rect CloseRect = new(InterceptRect.Right - 130, InterceptRect.Bottom - 50, 112, 36);

    private void DrawIntercept(DrawingContext dc)
    {
        var g = Engine;
        var c = g.Campaign!;
        Panel(dc, InterceptRect);
        string target = g.TargetUfo is { } u ? $"TARGET: {u.Name} ({u.Info.Size}{(u.State == UfoState.Landed ? ", LANDED" : "")})"
            : g.TargetSite is { } s ? $"TARGET: {s.Name}" : "CRAFT";
        Heading(dc, InterceptRect, target, g.TargetUfo is not null || g.TargetSite is not null ? Red : Cyan);
        Text(dc, "CRAFT            BASE           STATUS        FUEL  DAMAGE  CREW", new Point(InterceptRect.X + 24, InterceptRect.Y + 70), 19, Dim);
        var list = g.InterceptChoices();
        for (int i = 0; i < list.Count && i < 9; i++)
        {
            var k = list[i];
            var r = CraftRow(i);
            bool over = r.Contains(_mouse);
            dc.FillRectangle(Brush(over ? 0xFF16364A : i % 2 == 0 ? 0xFF0C1C2A : 0xFF0A1622), r, 2);
            string status = k.State switch
            {
                CraftState.Ready => "READY", CraftState.Out => "OUT", CraftState.Returning => "RETURNING", CraftState.Waiting => "ON STATION",
                CraftState.Refuelling => "REFUELLING", CraftState.Repairing => "REPAIRS", CraftState.Rearming => "REARMING", _ => "LOST",
            };
            string crew = k.Info.Seats > 0 ? $"{c.CrewOf(k).Count()}/{k.Info.Seats}" : "-";
            uint col = k.CanLaunch || k.Flying ? White : Dim;
            Text(dc, k.Name, new Point(r.X + 8, r.Center.Y), 21, col);
            Text(dc, c.BaseById(k.BaseId)?.Name ?? "", new Point(r.X + 150, r.Center.Y), 19, col);
            Text(dc, status, new Point(r.X + 290, r.Center.Y), 19, k.State == CraftState.Ready ? Green : col);
            Text(dc, $"{(int)(100 * k.Fuel / k.Info.FuelHours)}%", new Point(r.X + 420, r.Center.Y), 19, col);
            Text(dc, $"{(int)(100 * k.Damage / k.Info.Hp)}%", new Point(r.X + 480, r.Center.Y), 19, k.Damage > 0 ? Amber : col);
            Text(dc, crew, new Point(r.X + 556, r.Center.Y), 19, col);
        }
        if (list.Count == 0) Text(dc, "NO CRAFT", new Point(InterceptRect.Center.X, InterceptRect.Center.Y), 28, Dim, center: true);
        Text(dc, g.TargetUfo is null && g.TargetSite is null ? "CLICK A UFO OR A SITE ON THE GLOBE TO SEND CRAFT AFTER IT" : "CLICK A CRAFT TO LAUNCH IT",
            new Point(InterceptRect.X + 24, InterceptRect.Bottom - 32), 19, Dim);
        Button(dc, CloseRect, "CANCEL");
    }

    private void InterceptClick(Point at)
    {
        var g = Engine;
        if (CloseRect.Contains(at)) { g.CloseScreen(); return; }
        var list = g.InterceptChoices();
        for (int i = 0; i < list.Count && i < 9; i++)
            if (CraftRow(i).Contains(at)) { g.LaunchAt(list[i]); return; }
    }

    // ───────────────────────────── the air battle ─────────────────────────────

    private static readonly Rect AirRect = new(70, 140, 640, 470);
    private static Rect AirButton(int i) => new(AirRect.X + 14 + i * 124, AirRect.Bottom - 50, 118, 38);
    private static readonly string[] AirModes = ["STANDOFF", "CAUTIOUS", "STANDARD", "AGGRESSIVE", "DISENGAGE"];

    private void DrawAirCombat(DrawingContext dc)
    {
        if (Engine.Air is not { } a) return;
        Panel(dc, AirRect, Red);
        Heading(dc, AirRect, $"{a.Craft.Name}  VS  {a.Ufo.Name} ({a.Ufo.Info.Name})", Red);
        // The view ahead: a dusk sky, clouds sliding past, the UFO bigger the nearer it is.
        var view = new Rect(AirRect.X + 14, AirRect.Y + 50, 420, 300);
        using (dc.PushClip(view))
        {
            for (int i = 0; i < 30; i++)
            {
                double t = i / 30.0;
                dc.FillRectangle(Brush(Blend(0xFF0A1430, 0xFF6A4A6A, t * t)), new Rect(view.X, view.Y + t * view.Height, view.Width, view.Height / 30 + 1));
            }
            for (int i = 0; i < 14; i++)
            {
                double cloudX = ((Hash(i, 1, 3) % 520 - _clock * (40 + i * 6)) % 520 + 520) % 520 + view.X - 60;
                double cloudY = view.Y + 150 + Hash(i, 2, 3) % 140;
                dc.FillEllipse(cloudX, cloudY, 40 + Hash(i, 3, 3) % 50, 8 + Hash(i, 4, 3) % 8, 0x50C8B8D0);
            }
            double scale = Math.Clamp(14 / Math.Max(2, a.Distance), 0.12, 6);
            var up = new Point(view.Center.X + Math.Sin(_clock * 0.9) * 40, view.Center.Y - 30 + Math.Sin(_clock * 1.3) * 16);
            if (a.Result != AirResult.Destroyed || a.Over < 0.4)
            {
                DrawSaucer(dc, up, 26 * scale, a.Ufo.Type, 0xFFA8B0BC);
                double rw = 26 * scale * (1 + (int)a.Ufo.Type * 0.12), rh = 26 * scale * 0.38;
                for (int i = 0; i < 10; i++)
                {
                    double ang = i * Math.PI / 5 + _clock * 2;
                    if (Math.Sin(ang) < -0.2) continue;
                    dc.FillEllipse(up.X + Math.Cos(ang) * rw * 0.92, up.Y + Math.Sin(ang) * rh * 0.5 + rh * 0.2, Math.Max(1.2, 2.2 * Math.Min(scale, 2)), Alpha(i % 2 == 0 ? 0xFFFF6A3A : 0xFF6AFFE0, 0.6 + 0.4 * Math.Sin(_clock * 6 + i)));
                }
            }
            foreach (var s in a.Shots)
            {
                var from = s.FromCraft ? new Point(view.Center.X + (s.Weapon == ItemKind.LancerLauncher ? -60 : 40), view.Bottom) : up;
                var to = s.FromCraft ? (s.Hit ? up : new Point(up.X + 60, up.Y - 50)) : (s.Hit ? new Point(view.Center.X, view.Bottom) : new Point(view.X + 30, view.Bottom));
                var p = new Point(from.X + (to.X - from.X) * s.Progress, from.Y + (to.Y - from.Y) * s.Progress);
                uint col = !s.FromCraft ? 0xFF6AFF9A : s.Weapon switch { ItemKind.LaserCannon => 0xFFFF4A4A, ItemKind.StormBeam => 0xFF6AFF9A, _ => 0xFFFFE08A };
                if (s.Weapon == ItemKind.LancerLauncher && s.FromCraft)
                {
                    dc.FillEllipse(p.X, p.Y, 3, 0xFFFFFFFF);
                    dc.DrawLine(Pen(0x90FFC080, 3), p, new Point(p.X - (to.X - from.X) * 0.08, p.Y - (to.Y - from.Y) * 0.08));
                }
                else dc.DrawLine(Pen(col, s.FromCraft ? 2 : 3), p, new Point(p.X - (to.X - from.X) * 0.1, p.Y - (to.Y - from.Y) * 0.1));
            }
            if (a.Done && a.Result is AirResult.Downed or AirResult.Destroyed && a.Over < 1.6)
            {
                double k = a.Over / 1.6;
                dc.FillEllipse(up.X, up.Y, 20 + k * 60 * scale, Alpha(0xFFFFB040, 1 - k));
                dc.FillEllipse(up.X, up.Y, 10 + k * 30 * scale, Alpha(0xFFFFFFE0, 1 - k));
            }
            // Our interceptor, seen from behind and above, its engines burning.
            var me = new Point(view.Center.X + Math.Sin(_clock * 0.7) * 10, view.Bottom - 34 + Math.Sin(_clock * 1.1) * 3);
            Poly(dc, 0xFF4A525C, 0xFF101418, 1.5, new Point(me.X, me.Y - 22), new Point(me.X + 62, me.Y + 10), new Point(me.X + 14, me.Y + 6), new Point(me.X, me.Y + 14), new Point(me.X - 14, me.Y + 6), new Point(me.X - 62, me.Y + 10));
            Poly(dc, 0xFF6A747E, null, 0, new Point(me.X, me.Y - 22), new Point(me.X + 10, me.Y + 4), new Point(me.X - 10, me.Y + 4));
            dc.FillEllipse(me.X - 6, me.Y + 12, 4, Alpha(0xFFFFB040, 0.7 + 0.3 * Math.Sin(_clock * 30)));
            dc.FillEllipse(me.X + 6, me.Y + 12, 4, Alpha(0xFFFFB040, 0.7 + 0.3 * Math.Sin(_clock * 31)));
            // The cockpit's frame and sight.
            dc.DrawEllipse(null, Pen(0x8040FF80, 1.5), view.Center, 30, 30);
            dc.DrawLine(Pen(0x8040FF80, 1), new Point(view.Center.X - 46, view.Center.Y), new Point(view.Center.X - 20, view.Center.Y));
            dc.DrawLine(Pen(0x8040FF80, 1), new Point(view.Center.X + 20, view.Center.Y), new Point(view.Center.X + 46, view.Center.Y));
        }
        dc.DrawRectangle(null, Pen(0xFF2E6A8A, 2), view);
        // The range strip: the craft at the bottom, the UFO at its distance, each weapon's reach.
        var strip = new Rect(AirRect.X + 450, AirRect.Y + 50, 40, 300);
        dc.FillRectangle(Brush(0xFF02080C), strip);
        double Y(double km) => strip.Bottom - Math.Clamp(km / 100, 0, 1) * strip.Height;
        foreach (var gun in a.Craft.Guns)
        {
            dc.DrawLine(Pen(0x8040FF80, 1), new Point(strip.X, Y(gun.Info.Range)), new Point(strip.Right, Y(gun.Info.Range)));
        }
        if (a.Ufo.Info.Range > 0) dc.DrawLine(Pen(0xA0FF4A3A, 1), new Point(strip.X, Y(a.Ufo.Info.Range)), new Point(strip.Right, Y(a.Ufo.Info.Range)));
        dc.FillEllipse(strip.Center.X, Y(a.Distance), 7, Red);
        Poly(dc, Green, null, 0, new Point(strip.Center.X, strip.Bottom - 14), new Point(strip.Center.X + 7, strip.Bottom - 2), new Point(strip.Center.X - 7, strip.Bottom - 2));
        dc.DrawRectangle(null, Pen(0xFF2E6A8A, 1.5), strip);
        // The numbers.
        double x = AirRect.X + 504;
        Text(dc, $"RANGE {a.Distance:0} KM", new Point(x, AirRect.Y + 66), 22, White);
        Text(dc, $"UFO DAMAGE {(int)(100 * a.Ufo.Damage / a.Ufo.Info.Hp)}%", new Point(x, AirRect.Y + 96), 20, Red);
        Bar(dc, new Rect(x, AirRect.Y + 110, 120, 8), a.Ufo.Damage, a.Ufo.Info.Hp, Red);
        Text(dc, $"OUR DAMAGE {(int)(100 * a.Craft.Damage / a.Craft.Info.Hp)}%", new Point(x, AirRect.Y + 136), 20, Amber);
        Bar(dc, new Rect(x, AirRect.Y + 150, 120, 8), a.Craft.Damage, a.Craft.Info.Hp, Amber);
        double gy = AirRect.Y + 182;
        foreach (var gun in a.Craft.Guns)
        {
            Text(dc, gun.Info.Name, new Point(x, gy), 18, Green);
            Text(dc, gun.Info.Rounds > 0 ? $"{gun.Rounds} ROUNDS, {gun.Info.Range} KM" : $"{gun.Info.Range} KM", new Point(x, gy + 18), 16, Dim);
            gy += 44;
        }
        double ly = AirRect.Y + 364;
        foreach (var line in a.Log.TakeLast(2)) { Text(dc, line, new Point(AirRect.X + 20, ly), 19, Amber); ly += 20; }
        for (int i = 0; i < AirModes.Length; i++) Button(dc, AirButton(i), AirModes[i], !a.Done, 18, (int)a.Mode == i, i == 4 ? Amber : Cyan);
        if (a.Done)
        {
            string word = a.Result switch { AirResult.Downed => "UFO DOWN!", AirResult.Destroyed => "UFO DESTROYED!", AirResult.CraftLost => "CRAFT LOST", AirResult.Escaped => "UFO ESCAPED", _ => "DISENGAGED" };
            Text(dc, word, new Point(view.Center.X, view.Center.Y + 90), 52, a.Result is AirResult.Downed or AirResult.Destroyed ? Green : Red, center: true);
        }
    }

    private void AirClick(Point at)
    {
        for (int i = 0; i < AirModes.Length; i++)
            if (AirButton(i).Contains(at)) Engine.SetAirMode((AirMode)i);
    }
}
