using Avalonia.Media.Imaging;
using AVAXCom.Engine;

namespace AVAXCom.Views;

/// <summary>
/// The battlefield's scenery in isometric view, drawn pixel by pixel once each: floors (grass, furrowed fields, roads,
/// floorboards, the UFO's glowing deck), walls on a tile's west or north edge (brick, planks, hedges, fences, windows,
/// doors, the UFO's hull), and the things standing in tiles (trees, crops, crates, furniture, stairs, alien consoles,
/// drive cores, cars, the hive mind). Each comes in a few variants, so a field doesn't repeat, and at a few brightnesses,
/// for night and for what's only remembered. A tile's top corner is the anchor every sprite is placed by.
/// </summary>
public static class IsoArt
{
    public const int TW = 64, TH = 32, LH = 40;
    /// <summary>The brightness of each shade a sprite is kept at: lit, dusk, night, remembered.</summary>
    public static readonly double[] Levels = [1.0, 0.78, 0.58, 0.48];

    public sealed record Sprite(WriteableBitmap Bitmap, int AnchorX, int AnchorY, int W, int H);

    private static readonly Dictionary<(Piece, Part, int, int), Sprite> Cache = [];

    /// <summary>A sprite for a piece in a part of a tile, its variant (0..3) and shade (an index into <see cref="Levels"/>).</summary>
    public static Sprite Get(Piece piece, Part part, int variant, int shade)
    {
        var key = (piece, part, variant & 3, shade);
        if (Cache.TryGetValue(key, out var s)) return s;
        var (pix, ax, ay) = Draw(piece, part, variant & 3);
        if (Levels[shade] < 1)
        {
            // Darker, and bluer, the less there is to see by.
            double k = Levels[shade];
            for (int i = 0; i < pix.P.Length; i++)
            {
                uint c = pix.P[i];
                if (c >> 24 == 0) continue;
                uint d = Pix.Mix(Pix.Scale(c, k), 0xFF101A30, (1 - k) * 0.35);
                pix.P[i] = (c & 0xFF000000) | (d & 0x00FFFFFF);
            }
        }
        return Cache[key] = new Sprite(pix.ToBitmap(), ax, ay, pix.W, pix.H);
    }

    // ───────────────────────────── drawing helpers ─────────────────────────────

    /// <summary>Fills a polygon (scanline, even-odd).</summary>
    public static void FillPoly(Pix p, uint c, params (double X, double Y)[] pts)
    {
        double minY = pts.Min(q => q.Y), maxY = pts.Max(q => q.Y);
        var xs = new List<double>();
        for (int y = (int)Math.Floor(minY); y <= (int)Math.Ceiling(maxY); y++)
        {
            double sy = y + 0.5;
            xs.Clear();
            for (int i = 0; i < pts.Length; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Length];
                if (a.Y <= sy && b.Y > sy || b.Y <= sy && a.Y > sy) xs.Add(a.X + (sy - a.Y) / (b.Y - a.Y) * (b.X - a.X));
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
                for (int x = (int)Math.Round(xs[k]); x < (int)Math.Round(xs[k + 1]); x++) p.Set(x, y, c);
        }
    }

    /// <summary>A point in a tile, (u, v) across it (0..1 each, u along x, v along y) and h pixels up, on a sprite
    /// whose tile corner is at (ax, ay).</summary>
    private static (double X, double Y) At(int ax, int ay, double u, double v, double h = 0) => (ax + (u - v) * TW / 2, ay + (u + v) * TH / 2 - h);

    /// <summary>A box standing in a tile: from (u0, v0) to (u1, v1) across, h pixels tall: its top and its two near sides.</summary>
    private static void Box(Pix p, int ax, int ay, double u0, double v0, double u1, double v1, double h, uint top, double z0 = 0)
    {
        uint left = Pix.Scale(top, 0.72), right = Pix.Scale(top, 0.55);
        // The side facing down the y axis (lower left), and the side facing down the x axis (lower right).
        FillPoly(p, left, At(ax, ay, u0, v1, z0), At(ax, ay, u1, v1, z0), At(ax, ay, u1, v1, z0 + h), At(ax, ay, u0, v1, z0 + h));
        FillPoly(p, right, At(ax, ay, u1, v1, z0), At(ax, ay, u1, v0, z0), At(ax, ay, u1, v0, z0 + h), At(ax, ay, u1, v1, z0 + h));
        FillPoly(p, top, At(ax, ay, u0, v0, z0 + h), At(ax, ay, u1, v0, z0 + h), At(ax, ay, u1, v1, z0 + h), At(ax, ay, u0, v1, z0 + h));
        // Highlights on the top's far edges.
        var a = At(ax, ay, u0, v0, z0 + h);
        var b = At(ax, ay, u1, v0, z0 + h);
        var c = At(ax, ay, u0, v1, z0 + h);
        p.Line((int)a.X, (int)a.Y, (int)b.X, (int)b.Y, Pix.Scale(top, 1.2));
        p.Line((int)a.X, (int)a.Y, (int)c.X, (int)c.Y, Pix.Scale(top, 1.12));
    }

    /// <summary>A shaded ball (a tree's crown, a bush), centred at a pixel.</summary>
    private static void Ball(Pix p, double cx, double cy, double r, uint c, int seed)
    {
        for (int y = (int)(cy - r); y <= (int)(cy + r); y++)
        for (int x = (int)(cx - r); x <= (int)(cx + r); x++)
        {
            double dx = (x + 0.5 - cx) / r, dy = (y + 0.5 - cy) / r, d = dx * dx + dy * dy;
            if (d > 1) continue;
            double light = 1.1 - 0.5 * (dx + dy + 1) / 2.4 - 0.2 * d;
            double grain = Pix.Rand(x, y, seed) > 0.8 ? 0.85 : 1;
            p.Set(x, y, Pix.Scale(c, light * grain));
        }
    }

    // ───────────────────────────── the pieces ─────────────────────────────

    private static (Pix, int, int) Draw(Piece piece, Part part, int v)
    {
        if (part == Part.Floor)
        {
            var p = new Pix(TW, TH + 2);
            Floor(p, piece, v);
            return (p, TW / 2, 0);
        }
        if (part is Part.West or Part.North)
        {
            var p = new Pix(TW / 2 + 4, TH / 2 + LH + 4);
            Wall(p, piece, part == Part.West, v);
            return (p, part == Part.West ? TW / 2 + 2 : 2, LH + 2);
        }
        var o = new Pix(TW, 128);
        Thing(o, piece, v);
        return (o, TW / 2, 96);
    }

    private static uint FloorColour(Piece f) => f switch
    {
        Piece.Grass => 0xFF4E7A34, Piece.Dirt => 0xFF7A5E3E, Piece.Furrows => 0xFF6A4E2E, Piece.Road => 0xFF3A3C40, Piece.Pavement => 0xFF8A8A86,
        Piece.Sand => 0xFFC8A870, Piece.Snow => 0xFFE4ECF2, Piece.WoodFloor => 0xFF8A6238, Piece.TileFloor => 0xFFB8B0A0, Piece.Carpet => 0xFF7A2E34,
        Piece.Roof => 0xFF6A3A30, Piece.LaneX or Piece.LaneY => 0xFF3A3C40, Piece.UfoFloor => 0xFF3A4048, Piece.CraftDeck => 0xFF5A6258, Piece.CraftRoof => 0xFF6A7468, Piece.Rubble => 0xFF6A6460,
        Piece.CaveFloor => 0xFF4A4038, Piece.HiveFloor => 0xFF4A2A48, Piece.BaseFloor => 0xFF5A646E, Piece.Scorched => 0xFF2E2620, _ => 0xFF000000,
    };

    private static void Floor(Pix p, Piece f, int v)
    {
        uint baseC = FloorColour(f);
        for (int y = 0; y < TH; y++)
        for (int x = 0; x < TW; x++)
        {
            // Inside the diamond?
            double u = (x + 0.5 - TW / 2.0) / (TW / 2.0), w = (y + 0.5 - TH / 2.0) / (TH / 2.0);
            if (Math.Abs(u) + Math.Abs(w) > 1.0) continue;
            // Tile coordinates across the diamond: a along x, b along y.
            double a = ((x + 0.5 - TW / 2.0) / (TW / 2.0) + (y + 0.5) / (TH / 2.0)) / 2, b = ((y + 0.5) / (TH / 2.0) - (x + 0.5 - TW / 2.0) / (TW / 2.0)) / 2;
            double r = Pix.Rand(x, y, 31 + v * 7 + (int)f);
            uint c = baseC;
            switch (f)
            {
                case Piece.Grass:
                    c = Pix.Mix(0xFF3E6A2A, 0xFF6A9A40, Geo.Noise(a * 3 + v, b * 3, v, 3) * 0.8 + r * 0.2);
                    if (r > 0.93) c = 0xFF8AB850;
                    break;
                case Piece.Dirt or Piece.Sand or Piece.Snow or Piece.CaveFloor or Piece.Scorched:
                    c = Pix.Scale(baseC, 0.88 + Geo.Noise(a * 4 + v, b * 4, 1, 5) * 0.24);
                    if (r > 0.95) c = Pix.Scale(baseC, 0.7);
                    if (f == Piece.Scorched && r > 0.9) c = 0xFF4A3A30;
                    break;
                case Piece.Furrows:
                    c = (int)(b * 8) % 2 == 0 ? 0xFF7A5A34 : 0xFF5A3E24;
                    if (r > 0.9) c = Pix.Scale(c, 1.15);
                    break;
                case Piece.Road or Piece.LaneX or Piece.LaneY:
                    c = Pix.Scale(baseC, 0.9 + r * 0.16 + Geo.Noise(a * 6, b * 6, v, 4) * 0.08);
                    if (f == Piece.LaneY && Math.Abs(a - 0.5) < 0.035 && (int)(b * 3) % 2 == 0) c = 0xFFE8E0A0;
                    if (f == Piece.LaneX && Math.Abs(b - 0.5) < 0.035 && (int)(a * 3) % 2 == 0) c = 0xFFE8E0A0;
                    break;
                case Piece.Pavement or Piece.TileFloor or Piece.BaseFloor or Piece.CraftDeck or Piece.CraftRoof:
                    bool seam = a % 0.5 < 0.04 || b % 0.5 < 0.04;
                    c = seam ? Pix.Scale(baseC, 0.7) : Pix.Scale(baseC, 0.95 + r * 0.1);
                    if (f == Piece.TileFloor && ((int)(a * 2) + (int)(b * 2)) % 2 == 0 && !seam) c = Pix.Scale(baseC, 0.82);
                    if (f == Piece.CraftDeck && v % 2 == 0 && Math.Abs(a - b) < 0.06) c = 0xFFD8B030;
                    break;
                case Piece.WoodFloor:
                    c = (int)(b * 5) % 2 == 0 ? 0xFF8A6238 : 0xFF7A5430;
                    if ((b * 5) % 1 < 0.08) c = 0xFF4A3420;
                    break;
                case Piece.Carpet:
                    c = (int)(a * 6 + b * 6) % 3 == 0 ? 0xFF8A3A3E : 0xFF6A2A30;
                    break;
                case Piece.Roof:
                    c = (int)(a * 6) % 2 == 0 ? 0xFF7A4234 : 0xFF6A3A2E;
                    if ((a * 6) % 1 < 0.1) c = 0xFF4A2620;
                    break;
                case Piece.UfoFloor:
                    c = Pix.Scale(baseC, 0.9 + r * 0.15);
                    if (a % 0.5 < 0.035 || b % 0.5 < 0.035) c = 0xFF2A9A8A;
                    break;
                case Piece.HiveFloor:
                    c = Pix.Mix(0xFF3A1E3A, 0xFF6A3A5A, Geo.Noise(a * 5 + v, b * 5, 2, 9));
                    if (r > 0.96) c = 0xFFA06AC0;
                    break;
                case Piece.Rubble:
                    c = Pix.Scale(baseC, 0.7 + r * 0.6);
                    break;
            }
            p.Set(x, y, c);
        }
        // A darker rim to make the grid readable.
        for (int x = 0; x < TW; x++)
        {
            int y = (int)Math.Round(Math.Abs(x + 0.5 - TW / 2.0) / 2);
            p.Set(x, y, Pix.Scale(p.Get(x, y) | 0xFF000000, 0.85));
        }
    }

    private static uint WallColour(Piece w) => w switch
    {
        Piece.Brick => 0xFF9A4A34, Piece.Plank => 0xFF8A6A42, Piece.Fence => 0xFF9A7A50, Piece.Hedge => 0xFF2E5A26, Piece.Window => 0xFF7A8A9A,
        Piece.Door or Piece.DoorOpen => 0xFF6A4628, Piece.UfoWall or Piece.UfoDoor or Piece.UfoDoorOpen => 0xFF4A5260, Piece.CraftHull => 0xFF5E6A5A,
        Piece.CaveWall => 0xFF5A4E40, Piece.HiveWall => 0xFF5A2A52, Piece.BaseWall or Piece.BaseDoor or Piece.BaseDoorOpen => 0xFF6A7480, Piece.Stub => 0xFF7A6A5A,
        _ => 0xFF808080,
    };

    /// <summary>A wall on a tile's west edge (running up-left from the tile's top corner) or north edge (up-right).</summary>
    private static void Wall(Pix p, Piece w, bool west, int v)
    {
        var info = Pieces.Of(w);
        int ox = west ? TW / 2 + 2 : 2, oy = LH + 2;
        int sign = west ? -1 : 1;
        int height = (int)Math.Round(info.Height * LH);
        uint c = WallColour(w);
        bool open = w is Piece.DoorOpen or Piece.UfoDoorOpen or Piece.BaseDoorOpen;
        for (int i = 0; i <= TW / 2; i++)
        {
            int x = ox + sign * i, baseY = oy + i / 2;
            double t = i / (double)(TW / 2);
            for (int h = 0; h < height; h++)
            {
                int y = baseY - h;
                double r = Pix.Rand(i, h, 17 + v + (int)w);
                uint col = c;
                switch (w)
                {
                    case Piece.Brick:
                        int course = h / 5;
                        bool mortar = h % 5 == 0 || (i + course % 2 * 4) % 8 == 0;
                        col = mortar ? 0xFFB8A890 : Pix.Scale(c, 0.85 + r * 0.25);
                        break;
                    case Piece.Plank:
                        col = i % 6 == 0 ? Pix.Scale(c, 0.6) : Pix.Scale(c, 0.9 + r * 0.15);
                        break;
                    case Piece.Fence:
                        if (i % 8 > 1 && h % 7 > 2) continue;
                        col = Pix.Scale(c, 0.85 + r * 0.2);
                        break;
                    case Piece.Hedge:
                        if (h > height - 4 && r > 0.6) continue;
                        col = Pix.Mix(0xFF1E4A1E, 0xFF4A7A30, Geo.Noise(i / 3.0, h / 3.0, v, 13) * 0.8 + r * 0.2);
                        break;
                    case Piece.Window:
                        bool frame = h < 4 || h >= height - 4 || i < 3 || i > TW / 2 - 3 || i % 10 == 0 || h == height / 2;
                        col = frame ? 0xFFD8D0C0 : Pix.Mix(0x908ABCE0, 0xA0D8F0FF, (h + i) % 12 < 3 ? 1 : 0);
                        break;
                    case Piece.Door or Piece.DoorOpen:
                        bool doorway = i > 6 && i < TW / 2 - 6 && h < height - 6;
                        if (open && doorway) continue;
                        col = doorway ? Pix.Scale(0xFF7A5230, 0.9 + (i % 5 == 0 ? -0.2 : r * 0.1)) : 0xFF9A4A34;
                        if (!open && doorway && i == TW / 2 - 10 && h == height / 2) col = 0xFFE8D060;
                        break;
                    case Piece.UfoWall or Piece.UfoDoor or Piece.UfoDoorOpen:
                        bool hatch = w != Piece.UfoWall && i > 7 && i < TW / 2 - 7 && h < height - 5;
                        if (open && hatch) continue;
                        col = Pix.Scale(c, 0.85 + r * 0.1 + t * 0.1);
                        if (h == height - 8 || h == 6) col = 0xFF3AE0C8;
                        if (hatch) col = (i + h) % 6 == 0 ? 0xFF2A3038 : 0xFF5A6474;
                        break;
                    case Piece.CraftHull:
                        col = h % 13 == 0 || i % 16 == 0 ? Pix.Scale(c, 0.7) : Pix.Scale(c, 0.92 + r * 0.1);
                        if (h > 14 && h < 22 && i % 16 > 4 && i % 16 < 12) col = 0xFF2A3A4A;
                        break;
                    case Piece.CaveWall:
                        col = Pix.Mix(0xFF3A3028, 0xFF6A5A48, Geo.Noise(i / 5.0, h / 5.0, v, 19) * 0.8 + r * 0.2);
                        break;
                    case Piece.HiveWall:
                        col = Pix.Mix(0xFF3A1838, 0xFF7A3A6A, Geo.Noise(i / 4.0, h / 6.0, v, 23));
                        if (Math.Abs(Math.Sin(i * 0.6 + h * 0.1)) < 0.12) col = 0xFFB06AC0;
                        break;
                    case Piece.BaseWall or Piece.BaseDoor or Piece.BaseDoorOpen:
                        bool blast = w != Piece.BaseWall && i > 5 && i < TW / 2 - 5 && h < height - 4;
                        if (open && blast) continue;
                        col = h % 10 == 0 ? Pix.Scale(c, 0.7) : Pix.Scale(c, 0.92 + r * 0.08);
                        if (blast) col = (i / 4 + h / 4) % 2 == 0 ? 0xFFE8C030 : 0xFF202020;
                        break;
                    default:
                        col = Pix.Scale(c, 0.8 + r * 0.3);
                        break;
                }
                // Light from the north-west: west walls face south-west (lit), north walls face south-east (shaded).
                col = Pix.Scale(col | 0xFF000000, west ? 1.0 : 0.78) & 0x00FFFFFF | (col & 0xFF000000);
                p.Set(x, y, col);
            }
            // The wall's top edge.
            if (height > 2 && !(open && i > 6 && i < TW / 2 - 6))
                p.Set(x, baseY - height, Pix.Scale(c, 1.3));
        }
    }

    private static void Thing(Pix p, Piece o, int v)
    {
        const int ax = TW / 2, ay = 96;
        var d = new Dice(100 + (int)o * 7 + v);
        // A soft shadow under most things.
        if (o is not (Piece.Stairs or Piece.Lift or Piece.Debris or Piece.Bedrock))
            FillPoly(p, 0x50000000, At(ax, ay, 0.15, 0.25), At(ax, ay, 0.85, 0.25), At(ax, ay, 0.95, 0.85), At(ax, ay, 0.25, 0.95));
        switch (o)
        {
            case Piece.Tree:
            {
                var c = At(ax, ay, 0.5, 0.5);
                p.Fill((int)c.X - 2, (int)c.Y - 34, 5, 34, 0xFF5A3A22);
                uint leaf = d.Pick(new uint[] { 0xFF3A6A2A, 0xFF4A7A30, 0xFF2E5E26 });
                for (int i = 0; i < 6; i++) Ball(p, c.X + d.Range(-10, 10), c.Y - 48 - d.Range(-10, 10), d.Range(10, 15), leaf, i + v);
                Ball(p, c.X, c.Y - 60, 12, Pix.Scale(leaf, 1.1), 9 + v);
                break;
            }
            case Piece.Pine:
            {
                var c = At(ax, ay, 0.5, 0.5);
                p.Fill((int)c.X - 2, (int)c.Y - 14, 4, 14, 0xFF4A3020);
                for (int k = 0; k < 4; k++)
                {
                    double top = c.Y - 76 + k * 13, bottom = top + 26, w = 9 + k * 5;
                    FillPoly(p, Pix.Scale(0xFF2A4E30, 1 + k * 0.05), (c.X, top), (c.X + w, bottom), (c.X - w, bottom));
                    FillPoly(p, 0xFF1E3A24, (c.X, top), (c.X + w, bottom), (c.X + 2, bottom));
                }
                if (v % 2 == 0) for (int k = 0; k < 6; k++) p.Set((int)c.X + d.Range(-8, 8), (int)c.Y - d.Range(20, 70), 0xFFE8F0F8);
                break;
            }
            case Piece.Bush:
            {
                var c = At(ax, ay, 0.5, 0.5);
                for (int i = 0; i < 4; i++) Ball(p, c.X + d.Range(-9, 9), c.Y - 10 + d.Range(-3, 3), d.Range(7, 10), 0xFF3E6A2C, i + v);
                break;
            }
            case Piece.Corn:
            {
                for (int i = 0; i < 9; i++)
                {
                    var c = At(ax, ay, 0.15 + i % 3 * 0.33, 0.15 + i / 3 * 0.33);
                    int h = 22 + d.Next(8);
                    p.Line((int)c.X, (int)c.Y, (int)c.X + d.Range(-2, 2), (int)c.Y - h, 0xFF8AA040);
                    p.Disc(c.X + 1, c.Y - h + 3, 2, 3.5, 0xFFD8B850);
                    p.Line((int)c.X, (int)c.Y - h / 2, (int)c.X + 5, (int)c.Y - h / 2 - 4, 0xFF6A8A30);
                }
                break;
            }
            case Piece.Haystack:
            {
                var c = At(ax, ay, 0.5, 0.5);
                Ball(p, c.X, c.Y - 12, 15, 0xFFD8B860, v);
                for (int k = -12; k <= 12; k += 6) p.Line((int)c.X + k, (int)c.Y - 22, (int)c.X + k / 2, (int)c.Y - 2, 0xFFB09040);
                break;
            }
            case Piece.Crate: Box(p, ax, ay, 0.2, 0.2, 0.8, 0.8, 18, 0xFFA07A48); break;
            case Piece.Table:
                Box(p, ax, ay, 0.15, 0.2, 0.85, 0.8, 3, 0xFF8A5E34, 12);
                Box(p, ax, ay, 0.2, 0.7, 0.26, 0.76, 12, 0xFF5A3E24);
                Box(p, ax, ay, 0.74, 0.7, 0.8, 0.76, 12, 0xFF5A3E24);
                break;
            case Piece.Bed:
                Box(p, ax, ay, 0.15, 0.1, 0.85, 0.9, 8, 0xFF6A5040);
                Box(p, ax, ay, 0.18, 0.15, 0.82, 0.85, 3, 0xFF5A7AA8, 8);
                Box(p, ax, ay, 0.2, 0.12, 0.8, 0.3, 4, 0xFFE8E8F0, 9);
                break;
            case Piece.Shelves:
                Box(p, ax, ay, 0.1, 0.35, 0.9, 0.65, 34, 0xFF7A5A3A);
                for (int k = 0; k < 4; k++)
                {
                    var a = At(ax, ay, 0.12, 0.66, 6 + k * 8);
                    p.Fill((int)a.X + 2, (int)a.Y - 3, 20, 3, d.Pick(new uint[] { 0xFFC84A3A, 0xFF4A7AC8, 0xFFD8C060, 0xFF5AA058 }));
                }
                break;
            case Piece.Stairs:
                for (int k = 0; k < 5; k++) Box(p, ax, ay, 0.05, 0.05 + k * 0.18, 0.95, 0.23 + k * 0.18, 8 * (5 - k), Pix.Scale(0xFF8A7A68, 0.85 + k * 0.05));
                break;
            case Piece.Lift:
            {
                var c = At(ax, ay, 0.5, 0.5);
                p.Disc(c.X, c.Y, 22, 11, 0xFF2A3A48);
                p.Disc(c.X, c.Y, 18, 9, 0xFF3AE0C8);
                p.Disc(c.X, c.Y, 12, 6, 0xFFB0FFF0);
                for (int k = 0; k < 30; k++) p.Set((int)c.X + d.Range(-14, 14), (int)c.Y - d.Range(2, 40), 0x903AE0C8);
                break;
            }
            case Piece.Console or Piece.NavConsole:
                Box(p, ax, ay, 0.2, 0.25, 0.8, 0.75, 14, o == Piece.Console ? 0xFF4A5868 : 0xFF3A4A6A);
                FillPoly(p, o == Piece.Console ? 0xFF3AE0C8 : 0xFFB07AFF, At(ax, ay, 0.28, 0.3, 15), At(ax, ay, 0.72, 0.3, 15), At(ax, ay, 0.72, 0.7, 15), At(ax, ay, 0.28, 0.7, 15));
                if (o == Piece.NavConsole) Ball(p, At(ax, ay, 0.5, 0.5).X, At(ax, ay, 0.5, 0.5).Y - 26, 7, 0xFFD0A8FF, v);
                break;
            case Piece.DriveCore:
            {
                Box(p, ax, ay, 0.2, 0.2, 0.8, 0.8, 10, 0xFF3A4048);
                var c = At(ax, ay, 0.5, 0.5);
                Ball(p, c.X, c.Y - 28, 13, 0xFF9AFF6A, v);
                p.Disc(c.X - 4, c.Y - 32, 4, 0xC0FFFFFF);
                for (int k = 0; k < 3; k++) p.Ring(c.X, c.Y - 28, 15 + k * 3, 16 + k * 3, 0x606AFF9A);
                break;
            }
            case Piece.Pod:
            {
                var c = At(ax, ay, 0.5, 0.5);
                Ball(p, c.X, c.Y - 12, 11, 0xFF6A5A7A, v);
                p.Disc(c.X, c.Y - 14, 6, 9, 0xC06AFF9A);
                break;
            }
            case Piece.Barrel:
            {
                var c = At(ax, ay, 0.5, 0.5);
                p.Fill((int)c.X - 8, (int)c.Y - 18, 16, 18, 0xFF9A3A2A);
                p.Fill((int)c.X + 3, (int)c.Y - 18, 5, 18, 0xFF7A2A20);
                p.Disc(c.X, c.Y - 18, 8, 3.5, 0xFFB84A3A);
                p.Fill((int)c.X - 8, (int)c.Y - 12, 16, 2, 0xFFE8C030);
                break;
            }
            case Piece.Car or Piece.Wreck:
            {
                uint body = o == Piece.Wreck ? 0xFF3A3430 : d.Pick(new uint[] { 0xFFB83A30, 0xFF3A6AB8, 0xFFD8D0C0, 0xFF3A8A4A });
                Box(p, ax, ay, 0.05, 0.2, 0.95, 0.8, 9, body, 3);
                Box(p, ax, ay, 0.28, 0.26, 0.75, 0.74, 8, o == Piece.Wreck ? 0xFF2A2420 : 0xFF5A7A9A, 12);
                foreach (var (u, w) in new[] { (0.2, 0.8), (0.8, 0.8) }) { var c = At(ax, ay, u, w); p.Disc(c.X, c.Y - 2, 3.5, 0xFF141414); }
                break;
            }
            case Piece.Boulder:
            {
                var c = At(ax, ay, 0.5, 0.5);
                Ball(p, c.X, c.Y - 12, 16, 0xFF8A8278, v);
                Ball(p, c.X + 8, c.Y - 6, 9, 0xFF7A7268, v + 3);
                break;
            }
            case Piece.Cactus:
            {
                var c = At(ax, ay, 0.5, 0.5);
                p.Fill((int)c.X - 3, (int)c.Y - 32, 7, 32, 0xFF4A7A3A);
                p.Fill((int)c.X - 11, (int)c.Y - 22, 4, 10, 0xFF4A7A3A);
                p.Fill((int)c.X - 11, (int)c.Y - 14, 9, 3, 0xFF4A7A3A);
                p.Fill((int)c.X + 6, (int)c.Y - 26, 4, 12, 0xFF3E6A30);
                p.Fill((int)c.X + 3, (int)c.Y - 16, 6, 3, 0xFF3E6A30);
                break;
            }
            case Piece.Brain:
            {
                var c = At(ax, ay, 0.5, 0.5);
                Ball(p, c.X, c.Y - 30, 26, 0xFFC87AA0, v);
                for (int k = 0; k < 40; k++)
                {
                    double a = d.Range(0, Math.PI * 2), r = d.Range(4, 24);
                    p.Line((int)(c.X + Math.Cos(a) * r), (int)(c.Y - 30 + Math.Sin(a) * r * 0.9), (int)(c.X + Math.Cos(a + 0.4) * r), (int)(c.Y - 30 + Math.Sin(a + 0.4) * r * 0.9), 0xFF8A3A68);
                }
                break;
            }
            case Piece.Pillar: Box(p, ax, ay, 0.25, 0.25, 0.75, 0.75, 40, 0xFF6A4A6A); break;
            case Piece.Machine:
                Box(p, ax, ay, 0.15, 0.2, 0.85, 0.8, 22, 0xFF5A6A74);
                p.Disc(At(ax, ay, 0.5, 0.8, 12).X, At(ax, ay, 0.5, 0.8, 12).Y, 3, 0xFFFF4A3A);
                break;
            case Piece.Fungus:
            {
                var c = At(ax, ay, 0.5, 0.5);
                for (int i = 0; i < 4; i++)
                {
                    double x = c.X + d.Range(-12, 12), h = d.Range(10, 24);
                    p.Fill((int)x - 1, (int)(c.Y - h), 3, (int)h, 0xFF8A5A7A);
                    Ball(p, x, c.Y - h, d.Range(4, 7), 0xFFB07AFF, i);
                }
                break;
            }
            case Piece.Debris:
                for (int i = 0; i < 6; i++)
                {
                    var c = At(ax, ay, d.Range(0.2, 0.8), d.Range(0.2, 0.8));
                    FillPoly(p, d.Pick(new uint[] { 0xFF5A5450, 0xFF6A6460, 0xFF4A5260 }), (c.X - 5, c.Y), (c.X + 4, c.Y - 2), (c.X + 2, c.Y - 6), (c.X - 3, c.Y - 5));
                }
                break;
            case Piece.Wing:
                Box(p, ax, ay, -0.05, 0.1, 1.05, 0.9, 4, 0xFF5E6A5A, 14);
                Box(p, ax, ay, 0.3, 0.25, 0.7, 0.75, 12, 0xFF4A5448, 3);
                { var e = At(ax, ay, 0.5, 0.75, 9); p.Disc(e.X, e.Y, 5, 3, 0xFF202428); p.Disc(e.X, e.Y, 3, 2, 0xFFFF9A40); }
                break;
            case Piece.Bedrock:
            {
                Box(p, ax, ay, 0, 0, 1, 1, LH, 0xFF4A4038);
                for (int i = 0; i < 40; i++)
                {
                    var c = At(ax, ay, d.Range(0, 1), d.Range(0, 1), LH);
                    p.Set((int)c.X, (int)c.Y, 0xFF6A5A48);
                }
                break;
            }
        }
    }
}
