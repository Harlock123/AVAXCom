using Avalonia;
using Avalonia.Media;
using AVAXCom.Engine;

namespace AVAXCom.Views;

public sealed partial class GameCanvas
{
    private static readonly Rect FullRect = new(6, 6, 1012, 756);
    private static readonly Rect DoneRect = new(876, 708, 124, 40);
    private const double GridX = 28, GridY = 96;
    private static Rect CellRect(int x, int y, int size = 1) => new(GridX + x * BaseArt.Cell, GridY + y * BaseArt.Cell, size * BaseArt.Cell, size * BaseArt.Cell);
    private static readonly Rect BasePrev = new(28, 52, 40, 32), BaseNext = new(560, 52, 40, 32);
    private static readonly Rect BuildButton = new(636, 486, 176, 36), NewBaseButton = new(822, 486, 178, 36);
    private static Rect BuildRow(int i) => new(636, 96 + i * 30, 364, 28);
    private static Rect CraftRowRect(int i) => new(636, 560 + i * 28, 364, 26);
    private static Rect GunRect(int i) => new(636 + i * 122, 660, 116, 34);

    private void CycleBase(int d)
    {
        var g = Engine;
        if (g.Campaign is not { Bases.Count: > 0 } c) return;
        int i = c.Bases.IndexOf(g.SelectedBase!);
        g.SelectedBase = c.Bases[((i + d) % c.Bases.Count + c.Bases.Count) % c.Bases.Count];
        g.SelectedCraft = null;
    }

    // ───────────────────────────── the base ─────────────────────────────

    private void DrawBase(DrawingContext dc)
    {
        var g = Engine;
        var c = g.Campaign!;
        var b = g.SelectedBase ?? c.Bases[0];
        Panel(dc, FullRect, PanelEdge, 0xFF060C14);
        Text(dc, $"BASE: {b.Name}", new Point(314, 68), 36, Cyan, center: true);
        Button(dc, BasePrev, "<", c.Bases.Count > 1, 26);
        Button(dc, BaseNext, ">", c.Bases.Count > 1, 26);
        // The grid: rock, then the facilities.
        for (int y = 0; y < Rules.BaseSize; y++)
        for (int x = 0; x < Rules.BaseSize; x++)
            dc.DrawImage(BaseArt.Rock(), CellRect(x, y));
        Facility? over = null;
        foreach (var f in b.Facilities)
        {
            var r = CellRect(f.X, f.Y, f.Size);
            dc.DrawImage(BaseArt.Of(f.Type), r);
            if (r.Contains(_mouse)) over = f;
            if (f.Built && f.Type is FacilityType.SmallRadar or FacilityType.LargeRadar)
            {
                double a = _clock * 1.5;
                dc.DrawLine(Pen(0xC05AE07A, 2), new Point(r.X + 48, r.Y + 44), new Point(r.X + 48 + Math.Cos(a) * 34, r.Y + 44 + Math.Sin(a) * 34));
            }
            if (!f.Built)
            {
                dc.FillRectangle(Brush(0xA0000000), r);
                using (dc.PushClip(r))
                    for (double k = 0; k < r.Width + r.Height; k += 18)
                        dc.DrawLine(Pen(0x70E8C030, 5), new Point(r.X + k, r.Y), new Point(r.X + k - r.Height, r.Bottom));
                Text(dc, $"{Math.Ceiling(f.DaysLeft)} DAYS", r.Center, 26, Amber, center: true);
            }
        }
        // Craft in their hangars.
        var hangars = b.Facilities.Where(f => f.Type == FacilityType.Hangar && f.Built).ToList();
        var crafts = c.CraftsAt(b).ToList();
        for (int i = 0; i < crafts.Count && i < hangars.Count; i++)
            if (!crafts[i].Flying) DrawCraftTopDown(dc, CellRect(hangars[i].X, hangars[i].Y, 2).Center, crafts[i].Type, 1);
        // Placing a facility: show where it would go.
        if (g.Building is { } bt)
        {
            int gx = (int)Math.Floor((_mouse.X - GridX) / BaseArt.Cell), gy = (int)Math.Floor((_mouse.Y - GridY) / BaseArt.Cell);
            if (gx >= 0 && gy >= 0 && gx < Rules.BaseSize && gy < Rules.BaseSize)
            {
                bool ok = b.CanBuild(bt, gx, gy) is null;
                var r = CellRect(gx, gy, Rules.Of(bt).Size);
                dc.FillRectangle(Brush(ok ? 0x405AE07Au : 0x40FF4A3Au), r);
                dc.DrawRectangle(null, Pen(ok ? Green : Red, 3), r);
            }
        }
        dc.DrawRectangle(null, Pen(0xFF2E6A8A, 2), new Rect(GridX, GridY, BaseArt.Cell * 6, BaseArt.Cell * 6));
        if (over is not null)
        {
            var info = Rules.Of(over.Type);
            Text(dc, info.Name, new Point(GridX, 690), 26, White);
            Paragraph(dc, info.Blurb, new Rect(GridX, 702, 580, 50), 19, Dim);
        }
        else Text(dc, g.Building is null ? "RIGHT-CLICK A FACILITY TO PULL IT DOWN" : "CLICK WHERE IT SHOULD GO (RIGHT-CLICK: CANCEL)", new Point(GridX, 700), 20, Dim);
        // The right: the facilities to build, or the base's figures.
        if (g.Building is not null || _buildList)
        {
            Text(dc, "BUILD FACILITY", new Point(640, 76), 26, Amber);
            int i = 0;
            foreach (var info in Rules.Facilities.Where(f => f.Type != FacilityType.Lift))
            {
                bool can = info.Needs is not { } t || c.Researched.Contains(t);
                if (!can) continue;
                var r = BuildRow(i++);
                bool lit = g.Building == info.Type;
                dc.FillRectangle(Brush(lit ? 0xFF1A4A2A : r.Contains(_mouse) ? 0xFF16364A : 0xFF0C1C2A), r, 2);
                Text(dc, info.Name, new Point(r.X + 8, r.Center.Y), 20, c.Money >= info.Cost * 1000 ? White : Dim);
                Text(dc, $"${info.Cost}K  {info.Days}D", new Point(r.Right - 8, r.Center.Y), 19, Amber, right: true);
            }
        }
        else
        {
            double y = 84;
            void Row(string label, string value, uint colour = White)
            {
                Text(dc, label, new Point(640, y), 21, Dim);
                Text(dc, value, new Point(1000, y), 21, colour, right: true);
                y += 26;
            }
            Row("FUNDS", $"${c.Money / 1000:N0}K", Amber);
            Row("SOLDIERS", $"{c.SoldiersAt(b).Count()}");
            Row("SCIENTISTS", $"{b.Scientists}  ({b.FreeScientists} FREE)");
            Row("ENGINEERS", $"{b.Engineers}  ({b.FreeEngineers} FREE)");
            Row("LIVING SPACE", $"{c.PeopleAt(b)} / {b.QuartersSpace}", c.PeopleAt(b) > b.QuartersSpace ? Red : White);
            Row("LABORATORY SPACE", $"{b.Research.Sum(r => r.Scientists)} / {b.LabSpace}");
            Row("WORKSHOP SPACE", $"{b.Production.Sum(p => p.Engineers)} / {b.WorkshopSpace}");
            Row("STORES", $"{b.StoresUsed:0} / {b.StoreSpace}", b.StoresUsed > b.StoreSpace ? Red : White);
            Row("ALIEN CONTAINMENT", $"{b.Captives.Count} / {b.ContainmentSpace}");
            Row("HANGARS", $"{crafts.Count} / {b.HangarSpace}");
            Row("RADAR RANGE", b.RadarRange > 0 ? $"{b.RadarRange:N0} NM" : "NONE");
            Row("DEFENCE STRENGTH", $"{b.Defence}");
            Row("MONTHLY UPKEEP", $"${b.Upkeep / 1000:N0}K");
            if (b.Arriving.Count > 0) Row("ARRIVING", $"{b.Arriving.Count} DELIVER{(b.Arriving.Count == 1 ? "Y" : "IES")}", Green);
        }
        Button(dc, BuildButton, g.Building is not null || _buildList ? "BASE FIGURES" : "BUILD FACILITY", size: 20, lit: _buildList);
        Button(dc, NewBaseButton, "NEW BASE $900K", c.Money >= Rules.NewBaseCost, 20);
        Text(dc, "CRAFT (CLICK FOR WEAPONS)", new Point(640, 544), 19, Dim);
        for (int i = 0; i < crafts.Count && i < 3; i++)
        {
            var k = crafts[i];
            var r = CraftRowRect(i);
            dc.FillRectangle(Brush(g.SelectedCraft == k ? 0xFF1A4A2A : r.Contains(_mouse) ? 0xFF16364A : 0xFF0C1C2A), r, 2);
            Text(dc, k.Name, new Point(r.X + 8, r.Center.Y), 20, White);
            Text(dc, k.Info.Seats > 0 ? $"{c.CrewOf(k).Count()} SOLDIERS" : string.Join(" + ", k.Guns.Select(x => x.Info.Name.Split(' ')[0])), new Point(r.X + 130, r.Center.Y), 18, Dim);
            Text(dc, k.State.ToString().ToUpperInvariant(), new Point(r.Right - 8, r.Center.Y), 18, k.State == CraftState.Ready ? Green : Amber, right: true);
        }
        if (g.SelectedCraft is { } sk && sk.Info.Weapons > 0)
            for (int i = 0; i < sk.Info.Weapons; i++)
                Button(dc, GunRect(i), i < sk.Guns.Count ? sk.Guns[i].Info.Name : "EMPTY", size: 16);
        Button(dc, DoneRect, "DONE", size: 22);
    }

    private bool _buildList;

    /// <summary>A craft seen from above, for its hangar.</summary>
    private static void DrawCraftTopDown(DrawingContext dc, Point c, CraftType t, double k)
    {
        uint body = t switch { CraftType.Raven => 0xFF6A7A6A, CraftType.Falcon => 0xFF8A949E, CraftType.Vantage => 0xFF4A4A6A, _ => 0xFF3A3A5A };
        double s = (t switch { CraftType.Raven => 1.15, CraftType.Falcon => 0.9, CraftType.Vantage => 1.0, _ => 1.4 }) * k;
        Point P(double x, double y) => new(c.X + x * s, c.Y + y * s);
        if (t == CraftType.Falcon)
        {
            Poly(dc, body, 0xFF101010, 1.5, P(0, -60), P(8, -20), P(56, 30), P(56, 40), P(10, 30), P(12, 52), P(-12, 52), P(-10, 30), P(-56, 40), P(-56, 30), P(-8, -20));
            Poly(dc, 0xFF2A4A6A, null, 0, P(0, -42), P(5, -22), P(-5, -22));
        }
        else if (t == CraftType.Raven)
        {
            Poly(dc, body, 0xFF101010, 1.5, P(-20, -60), P(20, -60), P(26, -20), P(66, 0), P(66, 14), P(26, 14), P(24, 60), P(-24, 60), P(-26, 14), P(-66, 14), P(-66, 0), P(-26, -20));
            Poly(dc, 0xFF2A4A6A, null, 0, P(-12, -54), P(12, -54), P(14, -40), P(-14, -40));
            dc.FillRectangle(Brush(Shade(body, 0.7)), new Rect(c.X - 16 * s, c.Y + 20 * s, 32 * s, 34 * s));
        }
        else
        {
            Poly(dc, body, 0xFF101010, 1.5, P(0, -66), P(30, -10), P(62, 40), P(20, 34), P(0, 56), P(-20, 34), P(-62, 40), P(-30, -10));
            dc.FillEllipse(c.X, c.Y + 10 * s, 16 * s, 0xFF6AFF9A);
            dc.FillEllipse(c.X, c.Y + 10 * s, 8 * s, 0xFFE0FFE8);
        }
    }

    private void BaseClick(Point at)
    {
        var g = Engine;
        var c = g.Campaign!;
        var b = g.SelectedBase ?? c.Bases[0];
        if (DoneRect.Contains(at)) { _buildList = false; g.Building = null; g.CloseScreen(); return; }
        if (BasePrev.Contains(at)) { CycleBase(-1); return; }
        if (BaseNext.Contains(at)) { CycleBase(1); return; }
        if (BuildButton.Contains(at)) { _buildList = !_buildList; g.Building = null; return; }
        if (NewBaseButton.Contains(at)) { _buildList = false; g.StartNewBase(); return; }
        if (_buildList || g.Building is not null)
        {
            int i = 0;
            foreach (var info in Rules.Facilities.Where(f => f.Type != FacilityType.Lift))
            {
                if (info.Needs is { } t && !c.Researched.Contains(t)) continue;
                if (BuildRow(i++).Contains(at)) { g.Building = info.Type; return; }
            }
        }
        if (g.Building is { } bt)
        {
            int gx = (int)Math.Floor((at.X - GridX) / BaseArt.Cell), gy = (int)Math.Floor((at.Y - GridY) / BaseArt.Cell);
            if (gx >= 0 && gy >= 0 && gx < Rules.BaseSize && gy < Rules.BaseSize)
            {
                if (c.Build(b, bt, gx, gy) is { } why) { g.Say(why); return; }
                g.Building = null;
                g.Say($"{Rules.Of(bt).Name} UNDER CONSTRUCTION");
            }
            return;
        }
        var crafts = c.CraftsAt(b).ToList();
        for (int i = 0; i < crafts.Count && i < 3; i++)
            if (CraftRowRect(i).Contains(at)) { g.SelectedCraft = g.SelectedCraft == crafts[i] ? null : crafts[i]; return; }
        if (g.SelectedCraft is { } k)
            for (int i = 0; i < k.Info.Weapons; i++)
                if (GunRect(i).Contains(at)) CycleGun(c, k, i);
    }

    /// <summary>Fits the next craft weapon the stores hold to a mount.</summary>
    private void CycleGun(Campaign c, Craft k, int slot)
    {
        var b = c.BaseById(k.BaseId)!;
        var kinds = Rules.CraftWeapons.Select(w => w.Kind).ToList();
        int current = slot < k.Guns.Count ? kinds.IndexOf(k.Guns[slot].Kind) : -1;
        for (int n = 1; n <= kinds.Count; n++)
        {
            var next = kinds[(current + n) % kinds.Count];
            if (b.Count(next) > 0) { c.Fit(k, slot, next); Engine.Say($"{Rules.WeaponOf(next).Name} FITTED"); return; }
        }
        Engine.Say("NO OTHER CRAFT WEAPONS IN THE STORES");
    }

    /// <summary>A right-click on the base: pull down the facility under it.</summary>
    private void BaseRightClick(Point at)
    {
        var g = Engine;
        var c = g.Campaign!;
        var b = g.SelectedBase ?? c.Bases[0];
        if (g.Building is not null || _buildList) { g.Building = null; _buildList = false; return; }
        int gx = (int)Math.Floor((at.X - GridX) / BaseArt.Cell), gy = (int)Math.Floor((at.Y - GridY) / BaseArt.Cell);
        if (b.At2(gx, gy) is { } f) g.Say(c.Dismantle(b, f) ?? $"{Rules.Of(f.Type).Name} DISMANTLED");
        else g.CloseScreen();
    }

    // ───────────────────────────── research ─────────────────────────────

    private static Rect JobRow(int i) => new(30, 128 + i * 36, 964, 32);
    private static Rect JobMinus(int i) => new(744, 130 + i * 36, 40, 28);
    private static Rect JobPlus(int i) => new(790, 130 + i * 36, 40, 28);
    private static Rect TopicRow(int i) => new(30, 410 + i * 30, 480, 28);

    private void DrawResearch(DrawingContext dc)
    {
        var g = Engine;
        var c = g.Campaign!;
        var b = g.SelectedBase ?? c.Bases[0];
        Panel(dc, FullRect, Green, 0xFF060C14);
        Heading(dc, FullRect, $"RESEARCH  -  {b.Name}", Green);
        Text(dc, $"SCIENTISTS {b.Scientists}   FREE {b.FreeScientists}   LABORATORY SPACE {b.LabSpace}", new Point(36, 76), 22, White);
        Text(dc, "PROJECT                                SCIENTISTS          PROGRESS", new Point(36, 106), 19, Dim);
        for (int i = 0; i < b.Research.Count && i < 7; i++)
        {
            var r = b.Research[i];
            var info = Rules.Of(r.Topic);
            var row = JobRow(i);
            dc.FillRectangle(Brush(i % 2 == 0 ? 0xFF0C1C2A : 0xFF0A1622), row, 2);
            Text(dc, info.Name, new Point(row.X + 8, row.Center.Y), 22, White);
            Text(dc, $"{r.Scientists}", new Point(row.X + 400, row.Center.Y), 22, Green);
            Bar(dc, new Rect(row.X + 470, row.Y + 9, 220, 14), r.Done, info.Cost, Green);
            Button(dc, JobMinus(i), "-", size: 24);
            Button(dc, JobPlus(i), "+", b.FreeScientists > 0, 24);
            double days = r.Scientists > 0 ? (info.Cost - r.Done) / r.Scientists : double.PositiveInfinity;
            Text(dc, double.IsInfinity(days) ? "HALTED" : $"{Math.Ceiling(days)} DAYS", new Point(row.Right - 8, row.Center.Y), 20, double.IsInfinity(days) ? Red : Dim, right: true);
        }
        Text(dc, "AVAILABLE TO RESEARCH (CLICK TO BEGIN)", new Point(36, 392), 20, Amber);
        var topics = c.Available(b).ToList();
        int start = Math.Min(g.ListScroll, Math.Max(0, topics.Count - 9));
        Topic? hover = null;
        for (int i = 0; i < 9 && start + i < topics.Count; i++)
        {
            var t = topics[start + i];
            var r = TopicRow(i);
            bool over = r.Contains(_mouse);
            if (over) hover = t;
            dc.FillRectangle(Brush(over ? 0xFF16364A : 0xFF0C1C2A), r, 2);
            var info = Rules.Of(t);
            Text(dc, info.Name, new Point(r.X + 8, r.Center.Y), 21, White);
            Text(dc, info.Item != ItemKind.None ? "SPECIMEN" : info.Captive is not null ? "CAPTIVE" : "THEORY", new Point(r.Right - 8, r.Center.Y), 17, Dim, right: true);
        }
        if (topics.Count == 0) Text(dc, "NOTHING NEW TO RESEARCH: BRING BACK ALIEN ARTEFACTS, CORPSES AND CAPTIVES", new Point(36, 430), 20, Dim);
        var shown = hover ?? (b.Research.Count > 0 ? b.Research[0].Topic : topics.Count > 0 ? topics[0] : null);
        var box = new Rect(530, 410, 464, 280);
        Panel(dc, box, 0xFF1E4A30);
        if (shown is { } st)
        {
            var info = Rules.Of(st);
            Text(dc, info.Name, new Point(box.X + 16, box.Y + 24), 26, Green);
            Text(dc, $"{info.Cost} SCIENTIST-DAYS", new Point(box.X + 16, box.Y + 50), 19, Dim);
            Paragraph(dc, info.Text, new Rect(box.X + 16, box.Y + 70, box.Width - 180, 200), 21, White);
            DrawTopicPicture(dc, new Rect(box.Right - 160, box.Y + 60, 144, 200), st);
        }
        Text(dc, $"COMPLETED: {c.Researched.Count} OF {Rules.Topics.Length}", new Point(36, 728), 20, Dim);
        Button(dc, DoneRect, "DONE", size: 22);
    }

    /// <summary>A picture for a research topic: the alien for an autopsy or interrogation, the item for a specimen, and
    /// for a theory, a figure of orbits round a glowing core.</summary>
    private void DrawTopicPicture(DrawingContext dc, Rect r, Topic t)
    {
        var info = Rules.Of(t);
        dc.FillRectangle(Brush(0xFF04080C), r, 4);
        dc.DrawRectangle(null, Pen(0xFF1E4A30, 1), r, 4, 4);
        for (double y = r.Y + 6; y < r.Bottom; y += 12) dc.DrawLine(Pen(0x1840FF80, 1), new Point(r.X + 4, y), new Point(r.Right - 4, y));
        var race = info.Captive ?? Rules.Races.FirstOrDefault(x => x.Corpse == info.Item)?.Race;
        if (race is { } rc)
        {
            var u = new BUnit { Side = Side.Alien, Team = Side.Alien, Race = rc, Facing = 4, Id = 1, Right = Rules.Of(rc).Weapon == ItemKind.None ? null : Item.Make(Rules.Of(rc).Weapon) };
            var f = new Point(r.Center.X, r.Bottom - 30);
            using (dc.PushTransform(Matrix.CreateTranslation(-f.X, -f.Y) * Matrix.CreateScale(3.2, 3.2) * Matrix.CreateTranslation(f.X, f.Y)))
                DrawUnit(dc, u, f, false, 0);
            return;
        }
        if (info.Item != ItemKind.None)
        {
            DrawItemIcon(dc, r.Deflate(18), info.Item);
            return;
        }
        var c = r.Center;
        for (int i = 0; i < 3; i++)
        {
            double a = _clock * (0.6 + i * 0.3) + i * 2.1;
            using (dc.PushTransform(Matrix.CreateTranslation(-c.X, -c.Y) * Matrix.CreateRotation(i * Math.PI / 3) * Matrix.CreateTranslation(c.X, c.Y)))
            {
                dc.DrawEllipse(null, Pen(0xA05AE07A, 1.5), c, 56, 20);
                dc.FillEllipse(c.X + Math.Cos(a) * 56, c.Y + Math.Sin(a) * 20, 4, 0xFFB8FFD0);
            }
        }
        dc.FillEllipse(c.X, c.Y, 14, 0x606AFF9A);
        dc.FillEllipse(c.X, c.Y, 8, 0xFFE0FFE8);
    }

    private void ResearchClick(Point at)
    {
        var g = Engine;
        var c = g.Campaign!;
        var b = g.SelectedBase ?? c.Bases[0];
        if (DoneRect.Contains(at)) { g.CloseScreen(); return; }
        for (int i = 0; i < b.Research.Count && i < 7; i++)
        {
            if (JobMinus(i).Contains(at)) { c.Assign(b, b.Research[i], -5); return; }
            if (JobPlus(i).Contains(at)) { c.Assign(b, b.Research[i], 5); return; }
        }
        var topics = c.Available(b).ToList();
        int start = Math.Min(g.ListScroll, Math.Max(0, topics.Count - 9));
        for (int i = 0; i < 9 && start + i < topics.Count; i++)
            if (TopicRow(i).Contains(at))
            {
                if (c.StartResearch(b, topics[start + i], Math.Min(b.FreeScientists, 20)) is { } why) g.Say(why);
                return;
            }
    }

    private void ListRightClick(Point at)
    {
        var g = Engine;
        var c = g.Campaign!;
        var b = g.SelectedBase ?? c.Bases[0];
        if (g.Screen == GeoScreen.Research)
        {
            for (int i = 0; i < b.Research.Count && i < 7; i++)
                if (JobRow(i).Contains(at)) { c.Assign(b, b.Research[i], -1000); return; }
        }
        else
        {
            for (int i = 0; i < b.Production.Count && i < 7; i++)
                if (JobRow(i).Contains(at)) { b.Production.RemoveAt(i); g.Say("PRODUCTION CANCELLED"); return; }
        }
        g.CloseScreen();
    }

    // ───────────────────────────── manufacture ─────────────────────────────

    private void DrawManufacture(DrawingContext dc)
    {
        var g = Engine;
        var c = g.Campaign!;
        var b = g.SelectedBase ?? c.Bases[0];
        Panel(dc, FullRect, Amber, 0xFF060C14);
        Heading(dc, FullRect, $"MANUFACTURE  -  {b.Name}", Amber);
        Text(dc, $"ENGINEERS {b.Engineers}   FREE {b.FreeEngineers}   WORKSHOP SPACE {b.WorkshopSpace}   FUNDS ${c.Money / 1000:N0}K", new Point(36, 76), 22, White);
        Text(dc, "ITEM                                   ENGINEERS           MADE", new Point(36, 106), 19, Dim);
        for (int i = 0; i < b.Production.Count && i < 7; i++)
        {
            var p = b.Production[i];
            var row = JobRow(i);
            dc.FillRectangle(Brush(i % 2 == 0 ? 0xFF0C1C2A : 0xFF0A1622), row, 2);
            Text(dc, p.Info.Name, new Point(row.X + 8, row.Center.Y), 22, White);
            Text(dc, $"{p.Engineers}", new Point(row.X + 400, row.Center.Y), 22, Amber);
            Text(dc, $"{p.Made}/{p.Total}", new Point(row.X + 470, row.Center.Y), 22, White);
            Bar(dc, new Rect(row.X + 560, row.Y + 9, 130, 14), p.Progress, p.Info.Hours, Amber);
            Button(dc, JobMinus(i), "-", size: 24);
            Button(dc, JobPlus(i), "+", b.FreeEngineers > 0, 24);
            double hours = p.Engineers > 0 ? (p.Info.Hours - p.Progress) / p.Engineers : double.PositiveInfinity;
            Text(dc, double.IsInfinity(hours) ? "HALTED" : $"NEXT {Math.Ceiling(hours)}H", new Point(row.Right - 8, row.Center.Y), 20, double.IsInfinity(hours) ? Red : Dim, right: true);
        }
        Text(dc, "PROJECTS (CLICK: MAKE ONE, SHIFT+CLICK: MAKE FIVE; RIGHT-CLICK A JOB TO CANCEL IT)", new Point(36, 392), 20, Amber);
        var projects = Rules.Projects.Where(c.CanMake).ToList();
        int start = Math.Min(g.ListScroll, Math.Max(0, projects.Count - 9));
        for (int i = 0; i < 9 && start + i < projects.Count; i++)
        {
            var p = projects[start + i];
            var r = new Rect(30, 410 + i * 30, 964, 28);
            dc.FillRectangle(Brush(r.Contains(_mouse) ? 0xFF16364A : 0xFF0C1C2A), r, 2);
            Text(dc, p.Name, new Point(r.X + 8, r.Center.Y), 21, White);
            Text(dc, $"{p.Hours} ENGINEER-HOURS", new Point(r.X + 300, r.Center.Y), 19, Dim);
            Text(dc, $"${p.Cost:N0}", new Point(r.X + 560, r.Center.Y), 19, Amber);
            string mats = string.Join(", ", p.Materials.Select(m => $"{m.Count} {Rules.Of(m.Item).Name}"));
            Text(dc, mats, new Point(r.X + 680, r.Center.Y), 17, Violet);
        }
        Button(dc, DoneRect, "DONE", size: 22);
    }

    private void ManufactureClick(Point at, bool shift)
    {
        var g = Engine;
        var c = g.Campaign!;
        var b = g.SelectedBase ?? c.Bases[0];
        if (DoneRect.Contains(at)) { g.CloseScreen(); return; }
        for (int i = 0; i < b.Production.Count && i < 7; i++)
        {
            if (JobMinus(i).Contains(at)) { c.AssignEngineers(b, b.Production[i], -5); return; }
            if (JobPlus(i).Contains(at)) { c.AssignEngineers(b, b.Production[i], 5); return; }
        }
        var projects = Rules.Projects.Where(c.CanMake).ToList();
        int start = Math.Min(g.ListScroll, Math.Max(0, projects.Count - 9));
        for (int i = 0; i < 9 && start + i < projects.Count; i++)
            if (new Rect(30, 410 + i * 30, 964, 28).Contains(at))
            {
                g.Say(c.StartProduction(b, projects[start + i].Name, shift ? 5 : 1, Math.Min(b.FreeEngineers, 20)) ?? $"{projects[start + i].Name}: PRODUCTION STARTED");
                return;
            }
    }

    // ───────────────────────────── buying and selling ─────────────────────────────

    private static Rect ShopRow(int i) => new(30, 112 + i * 30, 964, 28);

    private static readonly (string Name, int Price)[] Hires = [("SOLDIER", Rules.SoldierCost), ("SCIENTIST", Rules.ScientistCost), ("ENGINEER", Rules.EngineerCost)];

    private void DrawPurchase(DrawingContext dc)
    {
        var g = Engine;
        var c = g.Campaign!;
        var b = g.SelectedBase ?? c.Bases[0];
        Panel(dc, FullRect, Cyan, 0xFF060C14);
        Heading(dc, FullRect, $"PURCHASE / HIRE  -  {b.Name}", Cyan);
        Text(dc, $"FUNDS ${c.Money / 1000:N0}K    STORES {b.StoresUsed:0}/{b.StoreSpace}    LIVING SPACE {c.PeopleAt(b)}/{b.QuartersSpace}    (CLICK: ONE, SHIFT+CLICK: TEN)", new Point(36, 82), 20, White);
        int row = 0;
        foreach (var (name, price) in Hires) ShopLine(dc, row++, name, price, $"{(name == "SOLDIER" ? c.SoldiersAt(b).Count() : name == "SCIENTIST" ? b.Scientists : b.Engineers)} AT BASE");
        foreach (var k in Rules.Crafts.Where(k => k.Buy > 0)) ShopLine(dc, row++, $"{k.Name} ({k.Role})", k.Buy, $"{c.CraftsAt(b).Count(x => x.Type == k.Type)} AT BASE");
        foreach (var k in Rules.Market) ShopLine(dc, row++, Rules.Of(k).Name, Rules.Of(k).Buy, $"{b.Count(k)} IN STORES");
        Button(dc, DoneRect, "DONE", size: 22);
    }

    private void ShopLine(DrawingContext dc, int i, string name, int price, string have)
    {
        var r = ShopRow(i);
        dc.FillRectangle(Brush(r.Contains(_mouse) ? 0xFF16364A : i % 2 == 0 ? 0xFF0C1C2A : 0xFF0A1622), r, 2);
        Text(dc, name, new Point(r.X + 10, r.Center.Y), 21, White);
        Text(dc, $"${price:N0}", new Point(r.X + 560, r.Center.Y), 21, Amber, right: true);
        Text(dc, have, new Point(r.Right - 10, r.Center.Y), 19, Dim, right: true);
    }

    private void PurchaseClick(Point at, bool shift)
    {
        var g = Engine;
        var c = g.Campaign!;
        var b = g.SelectedBase ?? c.Bases[0];
        if (DoneRect.Contains(at)) { g.CloseScreen(); return; }
        int n = shift ? 10 : 1, row = 0;
        string? why = null;
        bool hit = false;
        for (int h = 0; h < Hires.Length; h++, row++)
            if (ShopRow(row).Contains(at)) { why = c.Hire(b, (Campaign.Staff)h, n); hit = true; }
        foreach (var k in Rules.Crafts.Where(k => k.Buy > 0))
        {
            if (ShopRow(row++).Contains(at)) { why = c.BuyCraft(b, k.Type); hit = true; }
        }
        foreach (var k in Rules.Market)
            if (ShopRow(row++).Contains(at)) { why = c.Buy(b, k, n); hit = true; }
        if (!hit) return;
        if (why is not null) g.Say(why);
        else { g.Say("ORDERED: ARRIVES IN 3 DAYS"); Sound.Play(Sfx.Cash); }
    }

    private void DrawSell(DrawingContext dc)
    {
        var g = Engine;
        var c = g.Campaign!;
        var b = g.SelectedBase ?? c.Bases[0];
        Panel(dc, FullRect, Amber, 0xFF060C14);
        Heading(dc, FullRect, $"SELL  -  {b.Name}", Amber);
        Text(dc, $"FUNDS ${c.Money / 1000:N0}K    (CLICK: SELL ONE, SHIFT+CLICK: SELL ALL)    LIVE ALIENS HELD: {b.Captives.Count}", new Point(36, 82), 20, White);
        var items = b.Stores.Keys.OrderBy(k => (int)k).ToList();
        int start = Math.Min(g.ListScroll, Math.Max(0, items.Count - 19));
        for (int i = 0; i < 19 && start + i < items.Count; i++)
        {
            var k = items[start + i];
            ShopLine(dc, i, Rules.Of(k).Name, Rules.Of(k).Sell, $"{b.Count(k)} IN STORES");
        }
        if (items.Count == 0) Text(dc, "THE STORES ARE EMPTY", new Point(512, 300), 28, Dim, center: true);
        Button(dc, DoneRect, "DONE", size: 22);
    }

    private void SellClick(Point at, bool shift)
    {
        var g = Engine;
        var c = g.Campaign!;
        var b = g.SelectedBase ?? c.Bases[0];
        if (DoneRect.Contains(at)) { g.CloseScreen(); return; }
        var items = b.Stores.Keys.OrderBy(k => (int)k).ToList();
        int start = Math.Min(g.ListScroll, Math.Max(0, items.Count - 19));
        for (int i = 0; i < 19 && start + i < items.Count; i++)
        {
            if (!ShopRow(i).Contains(at)) continue;
            var k = items[start + i];
            var why = c.Sell(b, k, shift ? b.Count(k) : 1);
            if (why is not null) g.Say(why);
            else Sound.Play(Sfx.Cash);
            return;
        }
    }

    // ───────────────────────────── soldiers ─────────────────────────────

    private static Rect SoldierRow(int i) => new(24, 112 + i * 28, 520, 26);
    private static readonly Rect ArmourButton = new(570, 470, 210, 34), WeaponButton = new(790, 470, 210, 34), PsiButton = new(570, 514, 210, 34), SackButton = new(790, 514, 210, 34);
    private static Rect SeatButton(int i) => new(570 + i % 2 * 220, 590 + i / 2 * 40, 210, 34);

    private void DrawSoldiers(DrawingContext dc)
    {
        var g = Engine;
        var c = g.Campaign!;
        var b = g.SelectedBase ?? c.Bases[0];
        Panel(dc, FullRect, Cyan, 0xFF060C14);
        Heading(dc, FullRect, $"SOLDIERS  -  {b.Name}", Cyan);
        Text(dc, "NAME                     RANK       MISSIONS KILLS  CRAFT", new Point(30, 92), 19, Dim);
        var list = c.SoldiersAt(b).ToList();
        int start = Math.Min(g.ListScroll, Math.Max(0, list.Count - 21));
        for (int i = 0; i < 21 && start + i < list.Count; i++)
        {
            var s = list[start + i];
            var r = SoldierRow(i);
            dc.FillRectangle(Brush(g.SelectedSoldier == s ? 0xFF1A4A2A : r.Contains(_mouse) ? 0xFF16364A : i % 2 == 0 ? 0xFF0C1C2A : 0xFF0A1622), r, 2);
            uint col = s.Fit ? White : Red;
            Text(dc, s.Name, new Point(r.X + 6, r.Center.Y), 20, col);
            Text(dc, s.RankName, new Point(r.X + 212, r.Center.Y), 18, Amber);
            Text(dc, $"{s.Missions}", new Point(r.X + 330, r.Center.Y), 19, col);
            Text(dc, $"{s.Kills}", new Point(r.X + 390, r.Center.Y), 19, col);
            Text(dc, s.Fit ? c.CraftById(s.CraftId)?.Name ?? "-" : $"WOUNDED {Math.Ceiling(s.WoundDays)}D", new Point(r.X + 430, r.Center.Y), 17, s.Fit ? Dim : Red);
        }
        if (g.SelectedSoldier is { } sel)
        {
            var box = new Rect(560, 60, 450, 640);
            Panel(dc, box, 0xFF1E4A60);
            DrawSoldierBadge(dc, new Point(box.X + 46, box.Y + 52), sel.Rank);
            Text(dc, sel.Name, new Point(box.X + 92, box.Y + 40), 30, White);
            Text(dc, $"{sel.RankName}  -  {Earth.Nations[sel.Nation].Name}", new Point(box.X + 92, box.Y + 66), 20, Amber);
            (string, int, int, uint)[] stats =
            [
                ("TIME UNITS", sel.Tu, 100, Green), ("HEALTH", sel.Health, 80, Red), ("REACTIONS", sel.Reactions, 100, Cyan),
                ("FIRING ACCURACY", sel.Firing, 120, Amber), ("THROWING ACCURACY", sel.Throwing, 120, Amber), ("STRENGTH", sel.Strength, 80, White),
                ("BRAVERY", sel.Bravery, 100, 0xFFFF9AC8), ("PSIONIC STRENGTH", sel.PsiKnown ? sel.PsiStrength : 0, 120, Violet), ("PSIONIC SKILL", sel.PsiSkill, 100, Violet),
            ];
            for (int i = 0; i < stats.Length; i++)
            {
                var (label, value, max, colour) = stats[i];
                double y = box.Y + 108 + i * 32;
                Text(dc, label, new Point(box.X + 18, y), 20, Dim);
                Text(dc, label.StartsWith("PSIONIC STRENGTH") && !sel.PsiKnown ? "?" : $"{value}", new Point(box.X + 240, y), 20, White, right: true);
                Bar(dc, new Rect(box.X + 252, y - 6, 180, 12), value, max, colour);
            }
            Text(dc, $"MISSIONS {sel.Missions}   KILLS {sel.Kills}", new Point(box.X + 18, box.Y + 400), 20, White);
            Button(dc, ArmourButton, Rules.Of(sel.Armour).Name, size: 18);
            Button(dc, WeaponButton, sel.Weapon == ItemKind.None ? "BEST WEAPON" : Rules.Of(sel.Weapon).Name, size: 18);
            Button(dc, PsiButton, sel.PsiTraining ? "PSI TRAINING: ON" : "PSI TRAINING: OFF", b.PsiSpace > 0, 18, sel.PsiTraining);
            Button(dc, SackButton, "DISMISS", size: 18, accent: Red);
            Text(dc, "ASSIGN TO CRAFT", new Point(box.X + 18, 572), 19, Dim);
            var transports = c.CraftsAt(b).Where(k => k.Info.Seats > 0).ToList();
            for (int i = 0; i < transports.Count && i < 4; i++)
                Button(dc, SeatButton(i), $"{transports[i].Name} {c.CrewOf(transports[i]).Count()}/{transports[i].Info.Seats}", size: 18, lit: sel.CraftId == transports[i].Id);
        }
        else Text(dc, "CLICK A SOLDIER FOR DETAILS", new Point(780, 300), 24, Dim, center: true);
        Button(dc, DoneRect, "DONE", size: 22);
    }

    /// <summary>A rank's badge: chevrons for the ranks, bars and a star above.</summary>
    private static void DrawSoldierBadge(DrawingContext dc, Point c, Rank rank)
    {
        dc.DrawEllipse(Brush(0xFF0E2A3E), Pen(Amber, 2), c, 28, 28);
        int n = (int)rank;
        if (n == 0) { Text(dc, "R", c, 30, Dim, center: true); return; }
        for (int i = 0; i < Math.Min(n, 3); i++)
            Poly(dc, Amber, null, 0, new Point(c.X - 16, c.Y - 6 + i * 9), new Point(c.X, c.Y - 14 + i * 9), new Point(c.X + 16, c.Y - 6 + i * 9), new Point(c.X + 16, c.Y - 1 + i * 9), new Point(c.X, c.Y - 9 + i * 9), new Point(c.X - 16, c.Y - 1 + i * 9));
        if (n >= 4)
        {
            double r = 7;
            var pts = Enumerable.Range(0, 10).Select(k => new Point(c.X + Math.Sin(k * Math.PI / 5) * (k % 2 == 0 ? r : r * 0.45), c.Y - 18 + -Math.Cos(k * Math.PI / 5) * (k % 2 == 0 ? r : r * 0.45))).ToArray();
            Poly(dc, n >= 5 ? 0xFFFFFFFF : Amber, null, 0, pts);
        }
    }

    private void SoldiersClick(Point at)
    {
        var g = Engine;
        var c = g.Campaign!;
        var b = g.SelectedBase ?? c.Bases[0];
        if (DoneRect.Contains(at)) { g.SelectedSoldier = null; g.CloseScreen(); return; }
        var list = c.SoldiersAt(b).ToList();
        int start = Math.Min(g.ListScroll, Math.Max(0, list.Count - 21));
        for (int i = 0; i < 21 && start + i < list.Count; i++)
            if (SoldierRow(i).Contains(at)) { g.SelectedSoldier = list[start + i]; return; }
        if (g.SelectedSoldier is not { } s) return;
        if (ArmourButton.Contains(at))
        {
            foreach (var a in Enum.GetValues<ArmourType>().Select(a => (ArmourType)(((int)s.Armour + 1 + (int)a) % 3)))
                if (c.Equip(s, a) is null) { g.Say($"{Rules.Of(a).Name} ISSUED"); return; }
        }
        if (WeaponButton.Contains(at))
        {
            var weapons = new List<ItemKind> { ItemKind.None };
            weapons.AddRange(Rules.Items.Where(i => i.Class == ItemClass.Weapon && (b.Count(i.Kind) > 0 || i.Kind == s.Weapon)).Select(i => i.Kind));
            int k = weapons.IndexOf(s.Weapon);
            s.Weapon = weapons[(k + 1) % weapons.Count];
            return;
        }
        if (PsiButton.Contains(at) && b.PsiSpace > 0)
        {
            if (!s.PsiTraining && c.SoldiersAt(b).Count(x => x.PsiTraining) >= b.PsiSpace) g.Say("THE PSI LAB IS FULL");
            else s.PsiTraining = !s.PsiTraining;
            return;
        }
        if (SackButton.Contains(at)) { c.Sack(s); g.SelectedSoldier = null; g.Say($"{s.Name} DISMISSED"); return; }
        var transports = c.CraftsAt(b).Where(k => k.Info.Seats > 0).ToList();
        for (int i = 0; i < transports.Count && i < 4; i++)
            if (SeatButton(i).Contains(at)) { if (c.ToggleCrew(s, transports[i]) is { } why) g.Say(why); return; }
    }

    // ───────────────────────────── funding and the memorial ─────────────────────────────

    private void DrawFunding(DrawingContext dc)
    {
        var g = Engine;
        var c = g.Campaign!;
        Panel(dc, FullRect, Amber, 0xFF060C14);
        Heading(dc, FullRect, "FUNDING COUNCIL", Amber);
        Text(dc, "NATION              FUNDING   CHANGE  OPINION", new Point(36, 82), 19, Dim);
        for (int i = 0; i < Earth.Nations.Length; i++)
        {
            var n = c.Nations[i];
            double y = 110 + i * 30;
            uint col = n.Withdrawn ? Red : White;
            Text(dc, Earth.Nations[i].Name, new Point(36, y), 21, col);
            Text(dc, n.Withdrawn ? "WITHDRAWN" : $"${n.Funding:N0}K", new Point(310, y), 21, n.Withdrawn ? Red : Amber, right: true);
            Text(dc, n.LastChange == 0 ? "-" : $"{n.LastChange:+0;-0}%", new Point(390, y), 20, n.LastChange > 0 ? Green : n.LastChange < 0 ? Red : Dim, right: true);
            Bar(dc, new Rect(410, y - 7, 120, 14), n.Satisfaction, 100, n.Satisfaction > 60 ? Green : n.Satisfaction > 35 ? Amber : Red);
        }
        Text(dc, $"TOTAL ${c.Funding / 1000:N0}K A MONTH", new Point(36, 600), 24, Amber);
        // The months' scores.
        var chart = new Rect(570, 110, 420, 300);
        Panel(dc, chart.Inflate(10), 0xFF1E4A60);
        Text(dc, "MONTHLY SCORE", new Point(chart.X, chart.Y - 4), 20, Dim);
        var months = c.History.TakeLast(12).ToList();
        months.Add(new MonthRecord(c.Now.Year, c.Now.Month, c.MonthOurs, c.MonthTheirs, c.Funding, c.Money, "NOW"));
        int max = Math.Max(500, months.Max(m => Math.Abs(m.Score)));
        double zero = chart.Y + chart.Height / 2 + 10;
        dc.DrawLine(Pen(0x80FFFFFF, 1), new Point(chart.X, zero), new Point(chart.Right, zero));
        double w = chart.Width / 13;
        for (int i = 0; i < months.Count; i++)
        {
            var m = months[i];
            double h = m.Score / (double)max * (chart.Height / 2 - 20);
            var r = new Rect(chart.X + i * w + 4, Math.Min(zero, zero - h), w - 8, Math.Abs(h));
            dc.FillRectangle(Brush(m.Score >= 0 ? Green : Red), r);
            Text(dc, m.Rating == "NOW" ? "NOW" : new DateTime(m.Year, m.Month, 1).ToString("MMM").ToUpperInvariant(), new Point(r.Center.X, chart.Bottom + 4), 15, Dim, center: true);
        }
        Text(dc, $"THIS MONTH: VIGIL {c.MonthOurs}   ALIENS {c.MonthTheirs}", new Point(570, 450), 21, White);
        Text(dc, $"RATING SO FAR: {Rules.Rating(c.MonthOurs - c.MonthTheirs)}", new Point(570, 478), 21, Amber);
        Text(dc, $"CAMPAIGN SCORE {g.Score}", new Point(570, 506), 21, White);
        Button(dc, CloseRect2, "DONE", size: 22);
    }

    private static readonly Rect CloseRect2 = DoneRect;

    private void DrawMemorial(DrawingContext dc)
    {
        var g = Engine;
        var c = g.Campaign!;
        Panel(dc, FullRect, Dim, 0xFF060C14);
        Heading(dc, FullRect, "MEMORIAL", White);
        if (c.Memorial.Count == 0) Text(dc, "NO SOLDIER HAS FALLEN. YET.", new Point(512, 300), 30, Dim, center: true);
        Text(dc, "NAME                       RANK       MISSIONS  KILLS  DIED", new Point(36, 82), 19, Dim);
        var list = c.Memorial.AsEnumerable().Reverse().Skip(g.ListScroll).Take(20).ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var f = list[i];
            double y = 112 + i * 29;
            Text(dc, f.Name, new Point(36, y), 21, White);
            Text(dc, Rules.RankNames[(int)f.Rank], new Point(290, y), 19, Amber);
            Text(dc, $"{f.Missions}", new Point(420, y), 20, White);
            Text(dc, $"{f.Kills}", new Point(500, y), 20, White);
            Text(dc, $"{f.Date:d MMM yyyy}".ToUpperInvariant() + "  " + f.Where, new Point(560, y), 18, Dim);
        }
        Button(dc, DoneRect, "DONE", size: 22);
    }

    // ───────────────────────────── saving and loading ─────────────────────────────

    private static readonly Rect FilesRect = new(130, 150, 760, 440);
    private static Rect SlotRow(int i) => new(FilesRect.X + 20, FilesRect.Y + 70 + i * 40, FilesRect.Width - 40, 36);
    private static readonly Rect SaveTab = new(FilesRect.X + 20, FilesRect.Bottom - 54, 140, 38), LoadTab = new(FilesRect.X + 170, FilesRect.Bottom - 54, 140, 38),
        FilesClose = new(FilesRect.Right - 160, FilesRect.Bottom - 54, 140, 38);

    private void DrawFiles(DrawingContext dc) => DrawSlots(dc, Engine.Saving, title: true);

    private void DrawSlots(DrawingContext dc, bool saving, bool title)
    {
        var g = Engine;
        Panel(dc, FilesRect);
        Heading(dc, FilesRect, saving ? "SAVE THE CAMPAIGN" : "LOAD A CAMPAIGN", saving ? Green : Cyan);
        for (int s = 0; s <= SaveFile.Slots; s++)
        {
            var r = SlotRow(s);
            dc.FillRectangle(Brush(r.Contains(_mouse) ? 0xFF16364A : 0xFF0C1C2A), r, 2);
            Text(dc, s == 0 ? "AUTO" : $"SLOT {s}", new Point(r.X + 10, r.Center.Y), 22, s == 0 ? Amber : White);
            Text(dc, SlotText(s), new Point(r.X + 120, r.Center.Y), 21, Dim);
        }
        if (title && g.State == GameState.Geoscape)
        {
            Button(dc, SaveTab, "SAVE", g.Campaign is { Bases.Count: > 0 }, 20, saving);
            Button(dc, LoadTab, "LOAD", true, 20, !saving);
        }
        Button(dc, FilesClose, "CANCEL", size: 20);
    }

    private readonly Dictionary<int, (DateTime When, string Text)> _slotText = [];

    /// <summary>A slot's summary, read again only when its file changes.</summary>
    private string SlotText(int slot)
    {
        string path = SaveFile.PathIn(Engine.SaveFolder, slot);
        var when = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (_slotText.TryGetValue(slot, out var cached) && cached.When == when) return cached.Text;
        string text = SaveFile.Describe(Engine.SaveFolder, slot);
        _slotText[slot] = (when, text);
        return text;
    }

    private int SlotAt(Point at)
    {
        for (int s = 0; s <= SaveFile.Slots; s++) if (SlotRow(s).Contains(at)) return s;
        return -1;
    }

    private void FilesClick(Point at)
    {
        var g = Engine;
        if (FilesClose.Contains(at)) { g.CloseScreen(); return; }
        if (SaveTab.Contains(at)) { g.OpenFiles(true); return; }
        if (LoadTab.Contains(at)) { g.OpenFiles(false); return; }
        int slot = SlotAt(at);
        if (slot >= 0) FileSlot(slot);
    }

    private void FileSlot(int slot)
    {
        var g = Engine;
        if (g.Saving)
        {
            if (slot == 0) { g.Say("CHOOSE A NUMBERED SLOT"); return; }
            if (g.SaveSlot(slot)) g.CloseScreen();
        }
        else g.LoadSlot(slot);
    }
}
