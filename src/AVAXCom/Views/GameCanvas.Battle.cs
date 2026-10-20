using Avalonia;
using Avalonia.Media;
using AVAXCom.Engine;

namespace AVAXCom.Views;

public sealed partial class GameCanvas
{
    private static readonly Rect MapRect = new(0, 0, Iso.Width, Iso.MapH);
    private static readonly Rect HudRect = new(0, Iso.MapH, Iso.Width, Iso.HudH);
    private static readonly Rect RightHand = new(318, Iso.MapH + 14, 96, 108), LeftHand = new(424, Iso.MapH + 14, 96, 108);
    private static readonly (string Label, string Key)[] HudButtons =
    [
        ("LEVEL UP", "PGUP"), ("LEVEL DOWN", "PGDN"), ("KNEEL", "K"), ("INVENTORY", "I"), ("CENTRE", "C"),
        ("PREVIOUS", "SH+TAB"), ("NEXT", "TAB"), ("RELOAD", "R"), ("END TURN", "E"), ("MENU", "ESC"),
    ];
    private static Rect HudButton(int i) => new(540 + i % 5 * 96, Iso.MapH + 14 + i / 5 * 56, 90, 50);
    private static Rect SquadBox(int i) => new(8 + i * 40, 8, 36, 30);
    private static readonly Rect MenuRect = new(312, 220, 400, 300);
    private static Rect MenuButton(int i) => new(MenuRect.X + 40, MenuRect.Y + 90 + i * 56, MenuRect.Width - 80, 44);

    private (int Unit, Cell From, int Tu, Cell To, List<Cell>? Path, int Cost) _preview = (-1, default, 0, new Cell(-1, -1, -1), null, 0);

    // ───────────────────────────── the map ─────────────────────────────

    private void DrawBattle(DrawingContext dc)
    {
        var g = Engine;
        var b = g.Battle!;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, ScreenWidth, ScreenHeight));
        var (ox, oy) = g.Origin;
        Cell? hover = MapRect.Contains(_mouse) && !g.Inventory ? g.TileAt(_mouse.X, _mouse.Y) : null;
        using (dc.PushClip(MapRect))
        {
            for (int z = 0; z <= g.ViewLevel; z++)
            {
                // Floors first, then everything standing, back to front.
                for (int y = 0; y < b.H; y++)
                for (int x = 0; x < b.W; x++)
                {
                    var f = b.Floor[x, y, z];
                    if (f == Piece.None || !b.Explored[x, y, z] && !(z == 0 && Revealed(b))) continue;
                    var (sx, sy) = Iso.ToScreen(x, y, z);
                    sx += ox;
                    sy += oy;
                    if (sx < -40 || sx > ScreenWidth + 40 || sy < -60 || sy > Iso.MapH + 20) continue;
                    DrawSprite(dc, IsoArt.Get(f, Part.Floor, (int)(Hash(x, y, z) & 3), ShadeOf(b, x, y, z)), sx, sy);
                }
                if (z == g.ViewLevel) DrawFloorMarks(dc, b, z, hover);
                for (int s = 0; s <= b.W + b.H - 2; s++)
                for (int x = Math.Max(0, s - b.H + 1); x <= Math.Min(b.W - 1, s); x++)
                {
                    int y = s - x;
                    var (sx, sy) = Iso.ToScreen(x, y, z);
                    sx += ox;
                    sy += oy;
                    if (sx < -60 || sx > ScreenWidth + 60 || sy < -40 || sy > Iso.MapH + 140) continue;
                    bool known = b.Explored[x, y, z] || Revealed(b);
                    int shade = ShadeOf(b, x, y, z);
                    if (known || x > 0 && b.Explored[x - 1, y, z])
                    {
                        var w = b.West[x, y, z];
                        if (w != Piece.None) DrawSprite(dc, IsoArt.Get(w, Part.West, (int)(Hash(x, y, 3) & 3), Math.Min(shade, x > 0 ? ShadeOf(b, x - 1, y, z) : 3)), sx, sy);
                    }
                    if (known || y > 0 && b.Explored[x, y - 1, z])
                    {
                        var n = b.North[x, y, z];
                        if (n != Piece.None) DrawSprite(dc, IsoArt.Get(n, Part.North, (int)(Hash(x, y, 5) & 3), Math.Min(shade, y > 0 ? ShadeOf(b, x, y - 1, z) : 3)), sx, sy);
                    }
                    if (!known) continue;
                    if (b.Ground.TryGetValue(new Cell(x, y, z), out var items) && items.Count > 0) DrawGroundItems(dc, items, new Point(sx, sy + 16), shade);
                    var o = b.Obj[x, y, z];
                    if (o != Piece.None) DrawSprite(dc, IsoArt.Get(o, Part.Object, (int)(Hash(x, y, 7) & 3), shade), sx, sy);
                    foreach (var u in b.Units.Where(u => !u.Dead && u.X == x && u.Y == y && u.Z == z))
                        if (u.Team == Side.Player || u.Seen || Revealed(b)) DrawUnit(dc, u, new Point(sx, sy + 16), u == g.Selected && b.Active == Side.Player, shade);
                }
            }
            DrawShots(dc, b, ox, oy);
            DrawCursor(dc, b, hover);
        }
        if (b.Active != Side.Player && b.Outcome is null && !(b.Walker is { Seen: true }) && b.Flying.Count == 0 && b.Effects.All(e => e.Kind is FxKind.Words or FxKind.Death or FxKind.Smoke) && g.State == GameState.Battle)
            DrawHiddenMovement(dc);
        DrawSquadStrip(dc, b);
        Text(dc, b.Active == Side.Player ? $"TURN {b.Turn}" : "ALIEN ACTIVITY", new Point(1010, 22), 26, b.Active == Side.Player ? Cyan : Red, right: true);
        Text(dc, $"LEVEL {g.ViewLevel + 1}", new Point(1010, 46), 20, Dim, right: true);
        if (b.Night) Text(dc, "NIGHT", new Point(1010, 66), 18, Violet, right: true);
        DrawHud(dc, b);
        if (_handMenu >= 0) DrawHandMenu(dc, b);
        if (g.Inventory) DrawInventory(dc, b);
        if (g.BattleMenu) DrawBattleMenu(dc, b);
    }

    /// <summary>Everything shown regardless of sight (the demonstration).</summary>
    private bool Revealed(Battle b) => Engine.State == GameState.Attract;

    private static int ShadeOf(Battle b, int x, int y, int z)
    {
        if (!b.Visible[x, y, z]) return 3;
        if (!b.Night) return 0;
        return b.Lit(x, y, z) ? 1 : 2;
    }

    private static void DrawSprite(DrawingContext dc, IsoArt.Sprite s, double sx, double sy) =>
        dc.DrawImage(s.Bitmap, new Rect(Math.Round(sx - s.AnchorX), Math.Round(sy - s.AnchorY), s.W, s.H));

    /// <summary>A point of the battlefield (tiles and storeys) on the screen.</summary>
    private Point WorldToScreen(double x, double y, double z)
    {
        var (ox, oy) = Engine.Origin;
        var (sx, sy) = Iso.ToScreen(x, y, z);
        return new Point(sx + ox, sy + oy);
    }

    private Point TileCentre(Cell c) => WorldToScreen(c.X + 0.5, c.Y + 0.5, c.Z);

    /// <summary>Where a tile's middle is on the screen (for tests: pointing at it).</summary>
    public Point TileScreen(Cell c) => TileCentre(c);

    private static void Diamond(DrawingContext dc, Point centre, uint fill, uint? edge, double width = 2)
    {
        Poly(dc, fill, edge, width, new Point(centre.X, centre.Y - 16), new Point(centre.X + 32, centre.Y), new Point(centre.X, centre.Y + 16), new Point(centre.X - 32, centre.Y));
    }

    /// <summary>Marks on the floor of the storey in view: the transport's deck, the way a walk would go, and its cost.</summary>
    private void DrawFloorMarks(DrawingContext dc, Battle b, int z, Cell? hover)
    {
        var g = Engine;
        if (b.Turn <= 2 && z == 0)
            foreach (var (x, y) in b.Exit) Diamond(dc, TileCentre(new Cell(x, y, 0)), 0x1840FF80, null);
        if (g.Selected is not { } u || b.Active != Side.Player || b.Busy || g.Armed != Arm.None || hover is not { } h || !b.In(h) || b.Outcome is not null) return;
        if (_preview.Unit != u.Id || _preview.From != new Cell(u.X, u.Y, u.Z) || _preview.To != h || _preview.Tu != u.Tu)
        {
            var path = b.UnitAt(h) is null ? PathFinder.Find(b, u, h) : null;
            _preview = (u.Id, new Cell(u.X, u.Y, u.Z), u.Tu, h, path, path is null ? 0 : PathFinder.Cost(b, u, path));
        }
        if (_preview.Path is not { } p) return;
        int spent = u.Kneeling ? Tac.StandCost : 0, facing = u.Facing;
        var at = new Cell(u.X, u.Y, u.Z);
        foreach (var c in p)
        {
            if (c.X != at.X || c.Y != at.Y) { int dir = Tac.DirOf(c.X - at.X, c.Y - at.Y); spent += PathFinder.TurnCost(facing, dir); facing = dir; }
            spent += Math.Max(0, PathFinder.StepCost(b, u, at, c, true));
            at = c;
            var centre = TileCentre(c);
            dc.FillEllipse(centre.X, centre.Y, 4, spent <= u.Tu ? Green : Red);
        }
    }

    private void DrawCursor(DrawingContext dc, Battle b, Cell? hover)
    {
        var g = Engine;
        if (hover is not { } h || !b.In(h)) return;
        var c = TileCentre(h);
        bool armed = g.Armed != Arm.None;
        Diamond(dc, c, armed ? 0x30FF4A3Au : 0x20FFFFFFu, armed ? Red : Amber, 2);
        // A line down to the ground from an upper storey, to judge height by.
        if (h.Z > 0)
        {
            var ground = TileCentre(h with { Z = 0 });
            dc.DrawLine(Pen(0x60FFC040, 1), c, ground);
        }
        if (g.Selected is not { } u || b.Active != Side.Player) return;
        string? label = null;
        uint colour = White;
        switch (g.Armed)
        {
            case Arm.Snap or Arm.Aimed or Arm.Auto when u.Weapon is { } w:
                var mode = g.Armed == Arm.Snap ? FireMode.Snap : g.Armed == Arm.Aimed ? FireMode.Aimed : FireMode.Auto;
                int pct = Tac.HitChance(u, w, mode, h), cost = Tac.ShotCost(u, w, mode);
                label = $"{pct}%  {cost} TU";
                colour = u.Tu >= cost ? (pct >= 50 ? Green : Amber) : Red;
                DrawReticle(dc, c - new Point(0, 18), colour);
                break;
            case Arm.Throw when g.ArmedItem is { } item:
                double d = Math.Sqrt(Math.Pow(h.X - u.X, 2) + Math.Pow(h.Y - u.Y, 2));
                bool inRange = d <= Tac.ThrowRange(u, item);
                label = inRange ? $"THROW  {Tac.Percent(u, Tac.ThrowPercent)} TU" : "OUT OF RANGE";
                colour = inRange ? Green : Red;
                break;
            case Arm.None when _preview.Path is not null && _preview.To == h:
                label = $"{_preview.Cost} TU";
                colour = _preview.Cost <= u.Tu ? Green : Red;
                break;
            case Arm.Strike or Arm.Heal or Arm.Stimulant or Arm.Painkiller or Arm.PsiPanic or Arm.PsiControl:
                label = g.Armed.ToString().ToUpperInvariant();
                if (g.Armed is Arm.PsiPanic or Arm.PsiControl && b.UnitAt(h) is { } t && t.Team != Side.Player)
                    label = $"{Tac.PsiChance(u, t, g.Armed == Arm.PsiControl)}%";
                colour = Violet;
                break;
        }
        if (label is not null)
        {
            var at = new Point(_mouse.X + 18, _mouse.Y - 22);
            double w = TextWidth(label, 22) + 14;
            dc.FillRectangle(Brush(0xD0000000), new Rect(at.X - 4, at.Y - 13, w, 26), 3);
            Text(dc, label, new Point(at.X + 3, at.Y), 22, colour);
        }
    }

    private static void DrawReticle(DrawingContext dc, Point c, uint colour)
    {
        dc.DrawEllipse(null, Pen(colour, 1.5), c, 24, 24);
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            dc.DrawLine(Pen(colour, 2), new Point(c.X + dx * 18, c.Y + dy * 18), new Point(c.X + dx * 30, c.Y + dy * 30));
    }

    /// <summary>Shots in the air, blasts and the rest.</summary>
    private void DrawShots(DrawingContext dc, Battle b, double ox, double oy)
    {
        int level = Engine.ViewLevel;
        foreach (var p in b.Flying)
        {
            double t = Math.Clamp(p.Progress, 0, 1);
            double x = p.X0 + (p.X1 - p.X0) * t, y = p.Y0 + (p.Y1 - p.Y0) * t, z = p.Z0 + (p.Z1 - p.Z0) * t;
            if (p.Thrown) z += 4 * p.Peak * t * (1 - t);
            var at = WorldToScreen(x, y, z);
            if (p.Thrown)
            {
                dc.FillEllipse(at.X, at.Y + 2, 4, 2, 0x60000000);
                dc.FillEllipse(at.X, at.Y - 2, 4, 0xFF2A3A2A);
                dc.FillEllipse(at.X - 1, at.Y - 3, 1.5, 0xFFA0C0A0);
                continue;
            }
            double back = Math.Max(0, t - 0.15);
            var tail = WorldToScreen(p.X0 + (p.X1 - p.X0) * back, p.Y0 + (p.Y1 - p.Y0) * back, p.Z0 + (p.Z1 - p.Z0) * back);
            switch (p.Type)
            {
                case DamageType.Laser:
                    dc.DrawLine(Pen(0x60FF2020, 6), WorldToScreen(p.X0, p.Y0, p.Z0), at);
                    dc.DrawLine(Pen(0xFFFF6A6A, 2), WorldToScreen(p.X0, p.Y0, p.Z0), at);
                    break;
                case DamageType.Plasma:
                    dc.DrawLine(Pen(0x806AFF9A, 5), tail, at);
                    dc.FillEllipse(at.X, at.Y, 5, 0xFFB8FFD0);
                    dc.FillEllipse(at.X, at.Y, 9, 0x506AFF9A);
                    break;
                default:
                    if (p.Rocket)
                    {
                        dc.DrawLine(Pen(0x80B0B0B0, 5), tail, at);
                        dc.FillEllipse(at.X, at.Y, 4, 0xFFFFE8B0);
                    }
                    else dc.DrawLine(Pen(0xFFFFE08A, 2), tail, at);
                    break;
            }
        }
        foreach (var e in b.Effects)
        {
            if (e.Z >= level + 1.2) continue;
            var at = WorldToScreen(e.X, e.Y, e.Z);
            double k = e.Age / e.Life;
            switch (e.Kind)
            {
                case FxKind.Explosion:
                {
                    // A cluster of puffs: white-hot at the heart, orange and red outside, turning to smoke that rises and
                    // spreads; sparks and debris thrown out; a scorch of light on the ground at first.
                    double r = 12 + e.Size * 16;
                    double grow = Math.Sqrt(Math.Min(1, k * 2.2));
                    if (k < 0.3) dc.FillEllipse(at.X, at.Y, r * 2.2, r * 1.1, Alpha(0xFFFFC060, 0.35 * (1 - k / 0.3)));
                    for (int i = 0; i < 16; i++)
                    {
                        double a = i * 2.39996 + e.X * 3, d = r * (0.25 + (i % 5) * 0.17) * grow;
                        double px = at.X + Math.Cos(a) * d * 1.3, py = at.Y - r * 0.5 * grow + Math.Sin(a) * d * 0.7 - k * k * 40 * (i % 3) / 2;
                        double pr = r * (0.55 - (i % 5) * 0.06) * (0.6 + grow * 0.6);
                        double heat = Math.Clamp(1 - d / (r * 1.2) - k * 1.4, 0, 1);
                        uint col = heat > 0.66 ? Blend(0xFFFFB040, 0xFFFFF4D0, (heat - 0.66) * 3) : heat > 0.2 ? Blend(0xFFC83A18, 0xFFFFB040, (heat - 0.2) / 0.46) : Blend(0xFF3A3432, 0xFFC83A18, heat * 5);
                        dc.FillEllipse(px, py, pr, pr * 0.82, Alpha(col, Math.Min(1, (1 - k) * 1.4)));
                    }
                    for (int i = 0; i < 14; i++)
                    {
                        double a = i * Math.PI / 7 + e.Y;
                        double d = r * (0.8 + k * 2.4);
                        var p = new Point(at.X + Math.Cos(a) * d * 1.2, at.Y - r * 0.3 + Math.Sin(a) * d * 0.6 - Math.Sin(Math.Min(1, k * 1.5) * Math.PI) * 26);
                        if (i % 3 == 0) dc.FillRectangle(Brush(Alpha(0xFF6A6058, 1 - k)), new Rect(p.X - 2, p.Y - 2, 4, 3));
                        else dc.FillEllipse(p.X, p.Y, 1.8, Alpha(0xFFFFD070, 1 - k));
                    }
                    break;
                }
                case FxKind.Flash:
                    dc.FillEllipse(at.X, at.Y, 7, Alpha(e.Colour, 1 - k));
                    dc.FillEllipse(at.X, at.Y, 3, Alpha(0xFFFFFFFF, 1 - k));
                    break;
                case FxKind.Hit:
                    for (int i = 0; i < 6; i++)
                    {
                        double a = i * 1.05 + e.Y;
                        dc.DrawLine(Pen(Alpha(e.Colour, 1 - k), 1.5), at, new Point(at.X + Math.Cos(a) * (4 + k * 12), at.Y + Math.Sin(a) * (4 + k * 12)));
                    }
                    break;
                case FxKind.Blood:
                    for (int i = 0; i < 7; i++)
                    {
                        double a = i * 0.9 + e.X;
                        dc.FillEllipse(at.X + Math.Cos(a) * k * 14, at.Y - 10 + Math.Sin(a) * k * 8 + k * k * 14, 2.2, Alpha(e.Colour, 1 - k));
                    }
                    break;
                case FxKind.Psi:
                    for (int i = 0; i < 3; i++)
                    {
                        double rr = ((k + i / 3.0) % 1) * 34;
                        dc.DrawEllipse(null, Pen(Alpha(Violet, 1 - (k + i / 3.0) % 1), 2), new Point(at.X, at.Y - 14), rr, rr * 0.6);
                    }
                    break;
                case FxKind.Smoke:
                    for (int i = 0; i < 5; i++)
                        dc.FillEllipse(at.X + Math.Cos(i * 1.3) * 10 * k, at.Y - 8 - k * 20 + Math.Sin(i * 1.3) * 5, 6 + k * 10, Alpha(0xFFB8C0D0, 0.5 * (1 - k)));
                    break;
                case FxKind.Words:
                    Text(dc, e.Text, new Point(at.X, at.Y - 30 - k * 24), 22, Alpha(e.Colour, 1 - k * k), center: true);
                    break;
            }
        }
    }

    private void DrawHiddenMovement(DrawingContext dc)
    {
        dc.FillRectangle(Brush(0xE0040810), MapRect);
        var c = MapRect.Center;
        // The aliens' sigil: an eye in a triangle.
        Poly(dc, 0, Red, 3, new Point(c.X, c.Y - 120), new Point(c.X + 70, c.Y - 10), new Point(c.X - 70, c.Y - 10));
        dc.DrawEllipse(Brush(0xFF200808), Pen(Red, 2), new Point(c.X, c.Y - 46), 24, 12);
        dc.FillEllipse(c.X, c.Y - 46, 8, Red);
        double pulse = 0.6 + 0.4 * Math.Sin(_clock * 4);
        Text(dc, "HIDDEN MOVEMENT", new Point(c.X, c.Y + 34), 64, Alpha(Red, pulse), center: true);
        Text(dc, "THE ALIENS ARE MOVING", new Point(c.X, c.Y + 84), 24, Dim, center: true);
    }

    private void DrawSquadStrip(DrawingContext dc, Battle b)
    {
        var squad = b.Units.Where(u => u.Side == Side.Player).ToList();
        for (int i = 0; i < squad.Count && i < 18; i++)
        {
            var u = squad[i];
            var r = SquadBox(i);
            uint edge = u == Engine.Selected ? Amber : u.Dead ? 0xFF3A2020 : u.Team != Side.Player ? Violet : 0xFF2E6A8A;
            dc.FillRectangle(Brush(u.Dead ? 0xC0200A0A : 0xC0081420), r, 3);
            dc.DrawRectangle(null, Pen(edge, 1.5), r, 3, 3);
            Text(dc, u.Dead ? "X" : $"{i + 1}", new Point(r.Center.X, r.Y + 11), 18, u.Dead ? Red : u.Out ? Dim : White, center: true);
            if (!u.Dead)
            {
                Bar(dc, new Rect(r.X + 3, r.Bottom - 7, r.Width - 6, 4), u.Hp, u.MaxHp, u.Hp * 2 > u.MaxHp ? Green : Red);
                if (u.Tu < 4) dc.FillRectangle(Brush(0x80000000), r);
            }
        }
    }

    // ───────────────────────────── the soldier's panel ─────────────────────────────

    private void DrawHud(DrawingContext dc, Battle b)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF060C14), HudRect);
        dc.FillRectangle(Brush(0xFF2E6A8A), new Rect(0, HudRect.Y, ScreenWidth, 2));
        var u = g.Selected;
        if (u is not null)
        {
            if (u.Soldier is { } s) DrawSoldierBadge(dc, new Point(40, HudRect.Y + 40), s.Rank);
            Text(dc, u.IsSoldier ? u.Name : u.Title, new Point(78, HudRect.Y + 22), 24, White);
            Text(dc, u.IsSoldier ? u.Soldier!.RankName + (u.Kneeling ? "  -  KNEELING" : "") : (u.Team == Side.Player ? "UNDER YOUR CONTROL" : ""), new Point(78, HudRect.Y + 44), 18, Amber);
            void StatBar(int row, string label, int v, int max, uint col)
            {
                double y = HudRect.Y + 70 + row * 18;
                Text(dc, label, new Point(12, y), 17, Dim);
                Text(dc, $"{v}", new Point(96, y), 17, White, right: true);
                Bar(dc, new Rect(102, y - 5, 200 * Math.Min(1, max / 100.0), 10), v, max, col);
            }
            StatBar(0, "TIME UNITS", u.Tu, u.MaxTu, Green);
            StatBar(1, "HEALTH", u.Hp, u.MaxHp, u.Hp * 2 > u.MaxHp ? 0xFFD84A3A : Red);
            StatBar(2, "MORALE", u.Morale, 100, 0xFF4A9AE0);
            if (u.FatalWounds > 0) Text(dc, $"FATAL WOUNDS {u.FatalWounds}", new Point(200, HudRect.Y + 44), 18, (int)(_clock * 3) % 2 == 0 ? Red : White);
            DrawHand(dc, RightHand, "RIGHT", u.Right, b);
            DrawHand(dc, LeftHand, "LEFT", u.Left, b);
        }
        for (int i = 0; i < HudButtons.Length; i++)
        {
            var r = HudButton(i);
            bool on = b.Active == Side.Player && b.Outcome is null;
            Button(dc, r, "", on, 16, i == 2 && u is { Kneeling: true }, i == 8 ? Amber : i == 9 ? Dim : Cyan);
            Text(dc, HudButtons[i].Label, new Point(r.Center.X, r.Y + 18), 18, on ? White : 0xFF4A5A68, center: true);
            Text(dc, HudButtons[i].Key, new Point(r.Center.X, r.Y + 36), 14, Dim, center: true);
        }
    }

    private void DrawHand(DrawingContext dc, Rect r, string label, Item? item, Battle b)
    {
        bool over = r.Contains(_mouse);
        dc.FillRectangle(Brush(over ? 0xFF16364A : 0xFF0C1822), r, 4);
        dc.DrawRectangle(null, Pen(over ? Cyan : 0xFF2E6A8A, 1.5), r, 4, 4);
        Text(dc, label, new Point(r.X + 6, r.Y + 10), 15, Dim);
        if (item is null) return;
        DrawItemIcon(dc, new Rect(r.X + 8, r.Y + 22, r.Width - 16, 52), item.Kind);
        string sub = item.Info.Ammo != ItemKind.None ? (item.Clip is { } c ? $"{c.Rounds}" : "EMPTY") : item.Fuse >= 0 ? $"PRIMED {item.Fuse}" : item.Kind == ItemKind.FieldKit ? $"{item.Heals}/{item.Stims}/{item.Pains}" : "";
        Text(dc, item.Info.Name.Length > 13 ? item.Info.Name[..13] : item.Info.Name, new Point(r.Center.X, r.Bottom - 26), 14, White, center: true);
        Text(dc, sub, new Point(r.Center.X, r.Bottom - 10), 16, sub == "EMPTY" ? Red : Amber, center: true);
    }

    // ───────────────────────────── the hand's menu ─────────────────────────────

    /// <summary>What can be done with what's in a hand: (label, the armed action or null, an immediate act, still possible).</summary>
    private List<(string Label, Action Do, bool On)> HandActions(Battle b, BUnit u, Item item)
    {
        var g = Engine;
        var list = new List<(string, Action, bool)>();
        var info = item.Info;
        if (info.Fires)
        {
            foreach (var (mode, arm, name) in new[] { (FireMode.Snap, Arm.Snap, "SNAP SHOT"), (FireMode.Aimed, Arm.Aimed, "AIMED SHOT"), (FireMode.Auto, Arm.Auto, "AUTO SHOT") })
            {
                int cost = Tac.ShotCost(u, item, mode);
                if (cost < 0) continue;
                list.Add(($"{name}   ACC {Tac.HitChance(u, item, mode, new Cell(u.X + 6, u.Y, u.Z))}%   {cost} TU", () => g.ArmAction(arm, item), u.Tu >= cost && item.Loaded && u.Weapon == item));
            }
            if (info.Ammo != ItemKind.None) list.Add(($"RELOAD   {Tac.ReloadCost} TU", g.Reload, u.Tu >= Tac.ReloadCost));
        }
        switch (info.Class)
        {
            case ItemClass.Grenade:
                if (item.Kind != ItemKind.Flare && item.Fuse < 0)
                    for (int f = 0; f <= 2; f++)
                    {
                        int fuse = f;
                        list.Add(($"PRIME: {(f == 0 ? "END OF TURN" : $"{f} TURN{(f == 1 ? "" : "S")}")}   {Tac.PrimeCost} TU", () => Prime(b, u, item, fuse), u.Tu >= Tac.PrimeCost));
                    }
                list.Add(($"THROW   {Tac.Percent(u, Tac.ThrowPercent)} TU", () => g.ArmAction(Arm.Throw, item), u.Tu >= Tac.Percent(u, Tac.ThrowPercent)));
                break;
            case ItemClass.Melee:
                list.Add(($"STRIKE   {Tac.Percent(u, info.AimedTu)} TU", () => g.ArmAction(Arm.Strike, item), u.Tu >= Tac.Percent(u, info.AimedTu)));
                break;
            case ItemClass.Medical:
                list.Add(($"TREAT WOUND ({item.Heals})   {Tac.KitCost} TU", () => g.ArmAction(Arm.Heal, item), item.Heals > 0 && u.Tu >= Tac.KitCost));
                list.Add(($"STIMULANT ({item.Stims})   {Tac.KitCost} TU", () => g.ArmAction(Arm.Stimulant, item), item.Stims > 0 && u.Tu >= Tac.KitCost));
                list.Add(($"PAINKILLER ({item.Pains})   {Tac.KitCost} TU", () => g.ArmAction(Arm.Painkiller, item), item.Pains > 0 && u.Tu >= Tac.KitCost));
                break;
            case ItemClass.Psi:
                list.Add(($"PANIC UNIT   {Tac.Percent(u, Tac.PsiPercent)} TU", () => g.ArmAction(Arm.PsiPanic, item), u.PsiSkill > 0));
                list.Add(($"MIND CONTROL   {Tac.Percent(u, Tac.PsiPercent)} TU", () => g.ArmAction(Arm.PsiControl, item), u.PsiSkill > 0));
                break;
        }
        return list;
    }

    private void Prime(Battle b, BUnit u, Item item, int fuse)
    {
        if (Tac.Prime(b, u, item, fuse) is { } why) Engine.Say(why);
        else Engine.Say(fuse == 0 ? "PRIMED: IT GOES OFF AT THE END OF THE TURN" : $"PRIMED: {fuse} TURN{(fuse == 1 ? "" : "S")}");
    }

    private Rect HandMenuRow(int i, int n)
    {
        var hand = _handMenu == 0 ? RightHand : LeftHand;
        return new Rect(hand.X, hand.Y - 8 - (n - i) * 32, 330, 30);
    }

    private void DrawHandMenu(DrawingContext dc, Battle b)
    {
        var g = Engine;
        if (g.Selected is not { } u || (_handMenu == 0 ? u.Right : u.Left) is not { } item) { _handMenu = -1; return; }
        var actions = HandActions(b, u, item);
        for (int i = 0; i < actions.Count; i++)
        {
            var r = HandMenuRow(i, actions.Count);
            bool over = r.Contains(_mouse) && actions[i].On;
            dc.FillRectangle(Brush(over ? 0xF016364A : 0xF00A1622), r, 3);
            dc.DrawRectangle(null, Pen(over ? Cyan : 0xFF2E6A8A, 1.2), r, 3, 3);
            Text(dc, actions[i].Label, new Point(r.X + 10, r.Center.Y), 20, actions[i].On ? White : 0xFF4A5A68);
        }
    }

    // ───────────────────────────── the battle's menu ─────────────────────────────

    private void DrawBattleMenu(DrawingContext dc, Battle b)
    {
        dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, ScreenWidth, ScreenHeight));
        Panel(dc, MenuRect);
        Heading(dc, MenuRect, b.Mission.Name);
        int aboard = b.Units.Count(u => u.Side == Side.Player && u.Active && b.Exit.Contains((u.X, u.Y)) && u.Z == 0);
        int squad = b.Units.Count(u => u.Side == Side.Player && u.Active);
        Text(dc, $"ALIENS REMAINING: {(b.Aliens > 0 ? "UNKNOWN" : "NONE")}", new Point(MenuRect.X + 40, MenuRect.Y + 62), 20, Dim);
        Button(dc, MenuButton(0), "CARRY ON", size: 24);
        Button(dc, MenuButton(1), b.Mission.Kind == MissionKind.BaseDefence ? "ABANDON THE BASE" : $"ABORT MISSION ({aboard}/{squad} ABOARD)", b.Outcome is null, 20, accent: Red);
        Text(dc, "SOLDIERS NOT ABOARD THE TRANSPORT ARE LOST", new Point(MenuRect.Center.X, MenuRect.Y + 222), 17, Dim, center: true);
        Text(dc, "M: SOUND ON/OFF   CTRL+S: SCREENSHOT", new Point(MenuRect.Center.X, MenuRect.Y + 262), 17, Dim, center: true);
    }

    // ───────────────────────────── clicks in battle ─────────────────────────────

    private void BattleClick(Point at, bool shift)
    {
        var g = Engine;
        var b = g.Battle!;
        if (g.BattleMenu)
        {
            if (MenuButton(0).Contains(at)) g.ToggleBattleMenu();
            else if (MenuButton(1).Contains(at)) g.Abort();
            return;
        }
        if (g.Inventory) { InventoryClickAt(at, b); return; }
        if (_handMenu >= 0)
        {
            if (g.Selected is { } su && (_handMenu == 0 ? su.Right : su.Left) is { } item)
            {
                var actions = HandActions(b, su, item);
                for (int i = 0; i < actions.Count; i++)
                    if (HandMenuRow(i, actions.Count).Contains(at) && actions[i].On) { _handMenu = -1; actions[i].Do(); return; }
            }
            _handMenu = -1;
            return;
        }
        if (HudRect.Contains(at))
        {
            if (RightHand.Contains(at) && g.Selected?.Right is not null && b.Active == Side.Player) { _handMenu = 0; return; }
            if (LeftHand.Contains(at) && g.Selected?.Left is not null && b.Active == Side.Player) { _handMenu = 1; return; }
            for (int i = 0; i < HudButtons.Length; i++)
            {
                if (!HudButton(i).Contains(at)) continue;
                switch (i)
                {
                    case 0: g.LevelUp(); break;
                    case 1: g.LevelDown(); break;
                    case 2: g.Kneel(); break;
                    case 3: g.ToggleInventory(); break;
                    case 4: g.CenterOnSelected(); break;
                    case 5: g.NextSoldier(back: true); break;
                    case 6: g.NextSoldier(); break;
                    case 7: g.Reload(); break;
                    case 8: g.EndTurn(); break;
                    case 9: g.ToggleBattleMenu(); break;
                }
            }
            return;
        }
        var squad = b.Units.Where(u => u.Side == Side.Player).ToList();
        for (int i = 0; i < squad.Count && i < 18; i++)
            if (SquadBox(i).Contains(at))
            {
                if (squad[i].Active && squad[i].Team == Side.Player) { g.Select(squad[i]); g.CenterOnSelected(); }
                return;
            }
        if (MapRect.Contains(at)) g.ClickTile(g.TileAt(at.X, at.Y));
    }
}
