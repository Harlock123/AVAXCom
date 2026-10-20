namespace AVAXCom.Engine;

public enum HitKind { None, Unit, Floor, West, North, Object, Out }

/// <summary>Where a line through the battlefield stopped, and on what.</summary>
public readonly record struct Hit(HitKind Kind, double X, double Y, double Z, Cell Cell, BUnit? Unit = null)
{
    public bool Clear => Kind == HitKind.None;
}

/// <summary>The rules of the battlefield: seeing, moving, shooting, throwing, blowing things up, morale and the turns.</summary>
public static partial class Tac
{
    /// <summary>How far eyes see by day (and by aliens at any time), and by night unless what's looked at is lit.</summary>
    public const int SightRange = 20, NightRange = 9;
    public const double BodyRadius = 0.32;

    public static readonly (int Dx, int Dy)[] Dirs = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    /// <summary>The facing (0 north, clockwise to 7) that looks along a vector.</summary>
    public static int DirOf(double dx, double dy)
    {
        if (dx == 0 && dy == 0) return 4;
        double a = Math.Atan2(dx, -dy);
        return ((int)Math.Round(a / (Math.PI / 4)) % 8 + 8) % 8;
    }

    /// <summary>How tall a unit stands (of a storey): its body's top, and its eyes.</summary>
    public static double BodyTop(BUnit u) => u.Kneeling ? 0.6 : u.Race == Race.Warden && !u.IsHuman ? 0.7 : 0.85;
    public static double EyeHeight(BUnit u) => u.Kneeling ? 0.5 : 0.75;

    public static (double X, double Y, double Z) Eye(BUnit u) => (u.X + 0.5, u.Y + 0.5, u.Z + EyeHeight(u));
    public static (double X, double Y, double Z) Centre(BUnit u) => (u.X + 0.5, u.Y + 0.5, u.Z + BodyTop(u) * 0.55);

    // ───────────────────────────── walls between tiles ─────────────────────────────

    /// <summary>The wall on the edge crossed stepping one tile in a straight line (dx or dy, not both).</summary>
    public static (Piece Piece, Part Part, Cell At) EdgeWall(Battle b, int x, int y, int z, int dx, int dy)
    {
        if (dx == 1) return (b.Get(Part.West, x + 1, y, z), Part.West, new Cell(x + 1, y, z));
        if (dx == -1) return (b.Get(Part.West, x, y, z), Part.West, new Cell(x, y, z));
        if (dy == 1) return (b.Get(Part.North, x, y + 1, z), Part.North, new Cell(x, y + 1, z));
        return (b.Get(Part.North, x, y, z), Part.North, new Cell(x, y, z));
    }

    private static bool Stops(Piece p, double fz, bool shot)
    {
        if (p == Piece.None) return false;
        var info = Pieces.Of(p);
        if (fz >= info.Height) return false;
        return shot ? p is not (Piece.DoorOpen or Piece.UfoDoorOpen or Piece.BaseDoorOpen) : info.BlocksSight;
    }

    /// <summary>What stops a line passing from one tile into a neighbour (diagonals are stopped only if both ways round are).</summary>
    private static (Piece Piece, Part Part, Cell At)? Between(Battle b, int px, int py, int cx, int cy, int z, double fz, bool shot)
    {
        int dx = cx - px, dy = cy - py;
        if (dx != 0 && dy != 0)
        {
            var a1 = EdgeWall(b, px, py, z, dx, 0);
            var a2 = EdgeWall(b, cx, py, z, 0, dy);
            var b1 = EdgeWall(b, px, py, z, 0, dy);
            var b2 = EdgeWall(b, px, cy, z, dx, 0);
            bool aBlocked = Stops(a1.Piece, fz, shot) || Stops(a2.Piece, fz, shot) || Stops(b.Get(Part.Object, cx, py, z), fz, shot) && shot;
            bool bBlocked = Stops(b1.Piece, fz, shot) || Stops(b2.Piece, fz, shot) || Stops(b.Get(Part.Object, px, cy, z), fz, shot) && shot;
            if (!aBlocked || !bBlocked) return null;
            return Stops(a1.Piece, fz, shot) ? a1 : Stops(a2.Piece, fz, shot) ? a2 : Stops(b1.Piece, fz, shot) ? b1 : b2;
        }
        var w = EdgeWall(b, px, py, z, dx, dy);
        return Stops(w.Piece, fz, shot) ? w : null;
    }

    // ───────────────────────────── tracing a line ─────────────────────────────

    /// <summary>
    /// Follows a line from one point to another (in tiles and storeys), stopping at the first floor, wall or object in
    /// the way, or (for a shot) the first unit it passes through. For a shot the line runs on past its aim until it
    /// hits something; for sight it stops clear at the tile asked about.
    /// </summary>
    public static Hit Trace(Battle b, (double X, double Y, double Z) from, (double X, double Y, double Z) to, bool shot, BUnit? ignore = null, Cell? goal = null, double step = 0.1, bool units = true, bool extend = true)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y, dz = to.Z - from.Z, len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 1e-6) return new Hit(HitKind.None, to.X, to.Y, to.Z, new Cell((int)to.X, (int)to.Y, (int)to.Z));
        double reach = shot && extend ? Math.Max(len, 70) : len;
        int n = (int)Math.Ceiling(reach / step);
        int px = (int)Math.Floor(from.X), py = (int)Math.Floor(from.Y), pz = (int)Math.Floor(from.Z);
        var origin = new Cell(px, py, pz);
        for (int i = 1; i <= n; i++)
        {
            double t = Math.Min(reach, i * step);
            double x = from.X + dx * t / len, y = from.Y + dy * t / len, z = from.Z + dz * t / len;
            int cx = (int)Math.Floor(x), cy = (int)Math.Floor(y), cz = (int)Math.Floor(z);
            var cell = new Cell(cx, cy, cz);
            if (!b.In(cx, cy, cz) || z < 0) return new Hit(HitKind.Out, x, y, z, new Cell(Math.Clamp(cx, 0, b.W - 1), Math.Clamp(cy, 0, b.H - 1), Math.Clamp(cz, 0, Battle.Levels - 1)));
            double fz = z - cz;
            if (cx != px || cy != py)
            {
                if (Math.Abs(cx - px) > 1 || Math.Abs(cy - py) > 1) { px = cx; py = cy; }
                else if (Between(b, px, py, cx, cy, pz, z - pz, shot) is { } w)
                    return new Hit(w.Part == Part.West ? HitKind.West : HitKind.North, x, y, z, w.At);
            }
            if (cz != pz)
            {
                int level = Math.Max(cz, pz);
                if (level > 0 && b.Floor[cx, cy, level] != Piece.None && (shot || Pieces.Of(b.Floor[cx, cy, level]).BlocksSight) && b.Obj[cx, cy, level - 1] != Piece.Lift)
                    return new Hit(HitKind.Floor, x, y, z, new Cell(cx, cy, level));
            }
            px = cx; py = cy; pz = cz;
            if (goal == cell) return new Hit(HitKind.None, x, y, z, cell);
            if (cell != origin && b.Obj[cx, cy, cz] is var o && o != Piece.None && Stops(o, fz, shot))
                return new Hit(HitKind.Object, x, y, z, cell);
            if (shot && units)
            {
                var u = b.UnitAt(cx, cy, cz);
                if (u is not null && u != ignore)
                {
                    double ox = x - (cx + 0.5), oy = y - (cy + 0.5);
                    if (ox * ox + oy * oy <= BodyRadius * BodyRadius && fz <= BodyTop(u)) return new Hit(HitKind.Unit, x, y, z, cell, u);
                }
            }
            if ((!shot || !extend) && t >= len) return new Hit(HitKind.None, x, y, z, cell);
        }
        return new Hit(HitKind.None, to.X, to.Y, to.Z, new Cell((int)to.X, (int)to.Y, (int)to.Z));
    }

    // ───────────────────────────── who sees what ─────────────────────────────

    /// <summary>Whether a direction (dx, dy) is inside a unit's field of view: the original's ninety-degree cone, centred
    /// on where it faces (the tiles next to it are always in view).</summary>
    public static bool InCone(BUnit u, double dx, double dy)
    {
        if (Math.Abs(dx) <= 1.01 && Math.Abs(dy) <= 1.01) return true;
        var (fx, fy) = Dirs[u.Facing];
        double fl = Math.Sqrt(fx * fx + fy * fy), l = Math.Sqrt(dx * dx + dy * dy);
        return (fx * dx + fy * dy) / (fl * l) >= Math.Cos(Math.PI / 4) - 1e-9;
    }

    public static int RangeFor(Battle b, BUnit viewer, int x, int y, int z) =>
        viewer.Side == Side.Alien || b.Lit(x, y, z) ? SightRange : NightRange;

    /// <summary>Whether a unit sees a tile: in range (shorter at night unless it's lit), in its field of view, and with
    /// nothing in the way.</summary>
    public static bool SeesCell(Battle b, BUnit u, int x, int y, int z)
    {
        double dx = x - u.X, dy = y - u.Y;
        double d2 = dx * dx + dy * dy;
        int range = RangeFor(b, u, x, y, z);
        if (d2 > range * range) return false;
        if (!InCone(u, dx, dy)) return false;
        if (x == u.X && y == u.Y && z == u.Z) return true;
        var to = (x + 0.5, y + 0.5, z + 0.35);
        var h = Trace(b, Eye(u), to, shot: false, goal: new Cell(x, y, z), step: 0.2);
        return h.Clear;
    }

    /// <summary>Whether a unit sees another.</summary>
    public static bool Sees(Battle b, BUnit u, BUnit t)
    {
        if (!u.Active || t.Dead) return false;
        double dx = t.X - u.X, dy = t.Y - u.Y;
        int range = RangeFor(b, u, t.X, t.Y, t.Z);
        if (dx * dx + dy * dy > range * range) return false;
        if (!InCone(u, dx, dy)) return false;
        var c = Centre(t);
        var h = Trace(b, Eye(u), c, shot: false, goal: new Cell(t.X, t.Y, t.Z), step: 0.2);
        return h.Clear;
    }

    /// <summary>Works out again what the player's side sees now (and so has explored), and which aliens are in sight.</summary>
    public static void Look(Battle b)
    {
        Array.Clear(b.Visible);
        foreach (var u in b.Units.Where(u => u.Active && u.Team == Side.Player)) LookFrom(b, u);
        foreach (var u in b.Units) u.Seen = u.Team == Side.Player && u.Active || !u.Dead && b.Visible[u.X, u.Y, u.Z];
    }

    private static void LookFrom(Battle b, BUnit u)
    {
        int r = SightRange;
        for (int z = 0; z < Battle.Levels; z++)
        for (int y = Math.Max(0, u.Y - r); y <= Math.Min(b.H - 1, u.Y + r); y++)
        for (int x = Math.Max(0, u.X - r); x <= Math.Min(b.W - 1, u.X + r); x++)
        {
            if (b.Visible[x, y, z]) continue;
            if (z > 0 && b.Floor[x, y, z] == Piece.None && b.Obj[x, y, z] == Piece.None && b.West[x, y, z] == Piece.None && b.North[x, y, z] == Piece.None && b.UnitAt(x, y, z) is null) continue;
            if (!SeesCell(b, u, x, y, z)) continue;
            b.Visible[x, y, z] = true;
            b.Explored[x, y, z] = true;
        }
    }

    /// <summary>The enemies a unit can see now.</summary>
    public static IEnumerable<BUnit> EnemiesSeenBy(Battle b, BUnit u) =>
        b.Units.Where(t => t.Active && Hostile(u, t) && Sees(b, u, t));

    /// <summary>Whether two units are on opposite sides (civilians are the aliens' prey and nobody's threat).</summary>
    public static bool Hostile(BUnit a, BUnit t) => a.Team == Side.Alien ? t.Team != Side.Alien : t.Team == Side.Alien;
}
