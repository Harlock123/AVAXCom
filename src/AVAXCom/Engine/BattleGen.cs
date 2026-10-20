namespace AVAXCom.Engine;

/// <summary>
/// Builds the battlefield for a mission: farmland with fields of crops, hedges, a farmhouse and a barn; a city of
/// streets, shops and cars; forest, desert and snow; an alien base carved out of rock; the hive of Nyx; or the
/// player's own base, laid out from its facilities. On it go the transport (its ramp down, the squad aboard), the UFO
/// (crashed or landed, its hull and rooms and drive cores, a second deck and a gravity lift in the big ones) and its
/// crew, guards inside and patrols out.
/// </summary>
public static class BattleGen
{
    public const int Size = 40;
    public const int CraftW = 5, CraftH = 6;

    private sealed class Plan(Battle b)
    {
        public readonly Battle B = b;
        public readonly bool[,] Used = new bool[b.W, b.H];
        public Dice Rng = b.Rng;

        public bool Free(int x0, int y0, int w, int h, int margin = 1)
        {
            for (int y = y0 - margin; y < y0 + h + margin; y++)
            for (int x = x0 - margin; x < x0 + w + margin; x++)
                if (x < 1 || y < 1 || x >= B.W - 1 || y >= B.H - 1 || Used[x, y]) return false;
            return true;
        }

        public void Take(int x0, int y0, int w, int h)
        {
            for (int y = Math.Max(0, y0); y < Math.Min(B.H, y0 + h); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(B.W, x0 + w); x++) Used[x, y] = true;
        }

        /// <summary>A free rectangle somewhere (top-left), or null.</summary>
        public (int X, int Y)? Find(int w, int h, int tries = 80, int margin = 1, int minY = 1, int maxY = -1)
        {
            if (maxY < 0) maxY = B.H - 1;
            for (int i = 0; i < tries; i++)
            {
                int x = Rng.Range(1, B.W - w - 2), y = Rng.Range(minY, Math.Max(minY, maxY - h - 1));
                if (Free(x, y, w, h, margin)) return (x, y);
            }
            return null;
        }
    }

    /// <summary>The battle for a mission, with its squad equipped from <paramref name="stores"/> (taking each item out;
    /// null gives everyone a rifle and grenade for nothing).</summary>
    public static Battle Make(Mission m, Base? stores = null)
    {
        int size = m.Terrain == Terrain.BaseInterior ? Rules.BaseSize * 6 : Size;
        var b = new Battle(size, size, m.Seed) { Mission = m, Night = m.Night };
        var plan = new Plan(b);
        b.Objective = new Cell(size / 2, size / 3, 0);
        switch (m.Terrain)
        {
            case Terrain.BaseInterior: BaseInterior(plan, m.Base!); break;
            case Terrain.AlienBase or Terrain.Hive: Caverns(plan, m.Terrain == Terrain.Hive); break;
            default:
                Ground(plan, m.Terrain);
                break;
        }
        if (m.Terrain != Terrain.BaseInterior) PlaceCraft(plan);
        if (m.Kind is MissionKind.CrashSite or MissionKind.LandedUfo && m.Ufo is { } ut) PlaceUfo(plan, ut, m.Kind == MissionKind.CrashSite ? m.Damage : 0);
        if (m.Terrain is >= Terrain.Farmland and <= Terrain.Arctic) Scenery(plan, m.Terrain);
        Squad(plan, m, stores);
        Aliens(plan, m);
        if (m.Kind == MissionKind.Terror) Civilians(plan);
        // The squad knows its own transport, and the ground round it.
        foreach (var (x, y) in b.Exit)
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                if (b.In(x + dx, y + dy, 0)) { b.Explored[x + dx, y + dy, 0] = true; b.Explored[x + dx, y + dy, 1] = true; }
        Tac.Look(b);
        return b;
    }

    // ───────────────────────────── the ground ─────────────────────────────

    private static Piece GroundOf(Terrain t) => t switch
    {
        Terrain.Desert => Piece.Sand, Terrain.Arctic => Piece.Snow, Terrain.Urban => Piece.Pavement, _ => Piece.Grass,
    };

    private static void Ground(Plan p, Terrain t)
    {
        var b = p.B;
        var g = GroundOf(t);
        for (int y = 0; y < b.H; y++)
        for (int x = 0; x < b.W; x++)
        {
            b.Floor[x, y, 0] = g;
            if (t == Terrain.Forest && p.Rng.Chance(0.25)) b.Floor[x, y, 0] = Piece.Dirt;
            if (t == Terrain.Farmland && Geo.Noise(x / 6.0, y / 6.0, 0.5, b.Mission.Seed) > 0.72) b.Floor[x, y, 0] = Piece.Dirt;
        }
        if (t == Terrain.Urban) Streets(p);
    }

    /// <summary>The city's streets: two each way, with pavements, and the blocks between.</summary>
    private static void Streets(Plan p)
    {
        var b = p.B;
        int[] xs = [p.Rng.Range(9, 12), p.Rng.Range(24, 27)], ys = [p.Rng.Range(8, 11), p.Rng.Range(21, 24)];
        foreach (int rx in xs)
            for (int y = 0; y < b.H; y++)
                for (int x = rx; x < rx + 3; x++) { b.Floor[x, y, 0] = x == rx + 1 ? Piece.LaneY : Piece.Road; p.Used[x, y] = true; }
        foreach (int ry in ys)
            for (int x = 0; x < b.W; x++)
                for (int y = ry; y < ry + 3; y++) { b.Floor[x, y, 0] = y == ry + 1 && !xs.Any(rx => x >= rx && x < rx + 3) ? Piece.LaneX : Piece.Road; p.Used[x, y] = true; }
        // Parked cars along the roads.
        for (int i = 0; i < 7; i++)
        {
            bool vertical = p.Rng.Chance(0.5);
            int x = vertical ? xs[p.Rng.Next(2)] + p.Rng.Next(3) : p.Rng.Range(2, b.W - 3);
            int y = vertical ? p.Rng.Range(2, b.H - 10) : ys[p.Rng.Next(2)] + p.Rng.Next(3);
            if (b.Obj[x, y, 0] == Piece.None) b.Obj[x, y, 0] = p.Rng.Chance(0.15) ? Piece.Wreck : Piece.Car;
        }
    }

    // ───────────────────────────── buildings ─────────────────────────────

    /// <summary>
    /// A building: walls round a rectangle on each storey (some windows, a door or two below), floors inside, stairs up
    /// in a corner, a flat roof on top, and furniture.
    /// </summary>
    private static void Building(Plan p, int x0, int y0, int w, int h, int storeys, Piece wall, Piece inside, Piece[] furniture, int doors = 1, double windows = 0.3)
    {
        var b = p.B;
        p.Take(x0 - 1, y0 - 1, w + 2, h + 2);
        for (int z = 0; z < storeys; z++)
        {
            for (int y = y0; y < y0 + h; y++)
            {
                b.Set(Part.West, x0, y, z, Win(p, wall, windows, z, y == y0 || y == y0 + h - 1));
                b.Set(Part.West, x0 + w, y, z, Win(p, wall, windows, z, y == y0 || y == y0 + h - 1));
                for (int x = x0; x < x0 + w; x++)
                {
                    b.Set(Part.Floor, x, y, z, z == 0 ? inside : inside == Piece.TileFloor ? Piece.WoodFloor : inside);
                    b.Obj[x, y, z] = Piece.None;
                    if (x > x0) b.West[x, y, z] = Piece.None;
                    if (y > y0) b.North[x, y, z] = Piece.None;
                }
            }
            for (int x = x0; x < x0 + w; x++)
            {
                b.Set(Part.North, x, y0, z, Win(p, wall, windows, z, x == x0 || x == x0 + w - 1));
                b.Set(Part.North, x, y0 + h, z, Win(p, wall, windows, z, x == x0 || x == x0 + w - 1));
            }
        }
        for (int y = y0; y < y0 + h; y++)
        for (int x = x0; x < x0 + w; x++) b.Set(Part.Floor, x, y, storeys, Piece.Roof);
        // Doors on the ground floor, on the long sides.
        for (int d = 0; d < doors; d++)
        {
            switch (p.Rng.Next(4))
            {
                case 0: b.Set(Part.North, x0 + p.Rng.Range(1, w - 2), y0, 0, Piece.Door); break;
                case 1: b.Set(Part.North, x0 + p.Rng.Range(1, w - 2), y0 + h, 0, Piece.Door); break;
                case 2: b.Set(Part.West, x0, y0 + p.Rng.Range(1, h - 2), 0, Piece.Door); break;
                default: b.Set(Part.West, x0 + w, y0 + p.Rng.Range(1, h - 2), 0, Piece.Door); break;
            }
        }
        // Stairs in a corner, one flight per storey.
        var (sx, sy) = p.Rng.Chance(0.5) ? (x0 + w - 1, y0) : (x0, y0 + h - 1);
        for (int z = 0; z < storeys - 1; z++) b.Obj[sx, sy, z] = Piece.Stairs;
        // Furniture, kept off the stairs and out of the doorways.
        for (int z = 0; z < storeys; z++)
        {
            int n = w * h / 6;
            for (int i = 0; i < n; i++)
            {
                int x = p.Rng.Range(x0, x0 + w - 1), y = p.Rng.Range(y0, y0 + h - 1);
                if (b.Obj[x, y, z] != Piece.None || (x == sx && y == sy) || IsDoorway(b, x, y, z)) continue;
                if (x != x0 && x != x0 + w - 1 && y != y0 && y != y0 + h - 1 && p.Rng.Chance(0.6)) continue;
                b.Obj[x, y, z] = p.Rng.Pick(furniture);
            }
        }
    }

    private static bool IsDoorway(Battle b, int x, int y, int z) =>
        Pieces.Of(b.West[x, y, z]).Door || Pieces.Of(b.North[x, y, z]).Door || b.In(x + 1, y, z) && Pieces.Of(b.West[x + 1, y, z]).Door || b.In(x, y + 1, z) && Pieces.Of(b.North[x, y + 1, z]).Door;

    private static Piece Win(Plan p, Piece wall, double windows, int z, bool corner) =>
        !corner && wall is Piece.Brick or Piece.Plank && p.Rng.Chance(windows + z * 0.1) ? Piece.Window : wall;

    private static void Scenery(Plan p, Terrain t)
    {
        var b = p.B;
        switch (t)
        {
            case Terrain.Farmland:
                Fields(p);
                if (p.Find(7, 6) is var (fx, fy)) Building(p, fx, fy, 7, 6, 2, Piece.Brick, Piece.TileFloor, [Piece.Table, Piece.Bed, Piece.Shelves, Piece.Crate], 2);
                if (p.Find(8, 6) is var (bx, by)) Building(p, bx, by, 8, 6, 1, Piece.Plank, Piece.Dirt, [Piece.Haystack, Piece.Crate, Piece.Barrel], 2, 0.05);
                Scatter(p, Piece.Tree, 16);
                Scatter(p, Piece.Bush, 10);
                Scatter(p, Piece.Haystack, 4);
                break;
            case Terrain.Urban:
                Blocks(p);
                Scatter(p, Piece.Tree, 6);
                Scatter(p, Piece.Barrel, 5);
                Scatter(p, Piece.Crate, 6);
                break;
            case Terrain.Forest:
                if (p.Find(6, 5) is var (cx, cy)) Building(p, cx, cy, 6, 5, 1, Piece.Plank, Piece.WoodFloor, [Piece.Table, Piece.Bed, Piece.Shelves], 1);
                Scatter(p, Piece.Tree, 70);
                Scatter(p, Piece.Pine, 30);
                Scatter(p, Piece.Bush, 40);
                Scatter(p, Piece.Boulder, 6);
                break;
            case Terrain.Desert:
                if (p.Find(6, 6) is var (ax, ay)) Building(p, ax, ay, 6, 6, 1, Piece.Brick, Piece.Dirt, [Piece.Table, Piece.Crate, Piece.Barrel], 2, 0.2);
                Scatter(p, Piece.Cactus, 22);
                Scatter(p, Piece.Boulder, 18);
                Scatter(p, Piece.Bush, 8);
                break;
            default:
                if (p.Find(7, 5) is var (hx, hy)) Building(p, hx, hy, 7, 5, 1, Piece.Plank, Piece.WoodFloor, [Piece.Table, Piece.Bed, Piece.Crate, Piece.Machine], 1);
                Scatter(p, Piece.Pine, 30);
                Scatter(p, Piece.Boulder, 14);
                break;
        }
    }

    /// <summary>Farmland: fields of tall crops and pasture, bounded by hedges with gaps in them.</summary>
    private static void Fields(Plan p)
    {
        var b = p.B;
        for (int f = 0; f < 6; f++)
        {
            int w = p.Rng.Range(7, 12), h = p.Rng.Range(6, 10);
            if (p.Find(w, h, 40, 0) is not var (x0, y0)) continue;
            bool crops = p.Rng.Chance(0.6);
            for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
            {
                if (crops)
                {
                    b.Floor[x, y, 0] = Piece.Furrows;
                    if ((y - y0) % 2 == 0 && p.Rng.Chance(0.85)) b.Obj[x, y, 0] = Piece.Corn;
                }
                p.Used[x, y] = true;
            }
            // The hedge round it, broken by gaps.
            for (int x = x0; x < x0 + w; x++)
            {
                if ((x - x0) % 5 != 2) b.North[x, y0, 0] = Piece.Hedge;
                if ((x - x0) % 6 != 3) b.North[x, y0 + h, 0] = p.Rng.Chance(0.3) ? Piece.Fence : Piece.Hedge;
            }
            for (int y = y0; y < y0 + h; y++)
            {
                if ((y - y0) % 5 != 2) b.West[x0, y, 0] = Piece.Hedge;
                if ((y - y0) % 4 != 1) b.West[x0 + w, y, 0] = p.Rng.Chance(0.3) ? Piece.Fence : Piece.Hedge;
            }
        }
    }

    /// <summary>The city's blocks: shops and houses two storeys high, with yards between them.</summary>
    private static void Blocks(Plan p)
    {
        for (int i = 0; i < 14; i++)
        {
            int w = p.Rng.Range(5, 8), h = p.Rng.Range(4, 7);
            if (p.Find(w, h, 60) is not var (x, y)) continue;
            Building(p, x, y, w, h, p.Rng.Chance(0.75) ? 2 : 1, Piece.Brick, p.Rng.Chance(0.5) ? Piece.TileFloor : Piece.Carpet,
                [Piece.Table, Piece.Shelves, Piece.Bed, Piece.Crate], p.Rng.Chance(0.5) ? 2 : 1, 0.45);
        }
    }

    private static void Scatter(Plan p, Piece what, int n)
    {
        var b = p.B;
        for (int i = 0; i < n * 3 && n > 0; i++)
        {
            int x = p.Rng.Range(1, b.W - 2), y = p.Rng.Range(1, b.H - 2);
            if (p.Used[x, y] && !(what is Piece.Tree or Piece.Bush && b.Floor[x, y, 0] is Piece.Grass or Piece.Dirt && b.Obj[x, y, 0] == Piece.None && !Near(b, x, y))) continue;
            if (b.Obj[x, y, 0] != Piece.None || b.Floor[x, y, 0] is Piece.Road or Piece.CraftDeck or Piece.UfoFloor) continue;
            if (b.Floor[x, y, 1] != Piece.None) continue;
            b.Obj[x, y, 0] = what;
            n--;
        }
    }

    private static bool Near(Battle b, int x, int y) => b.West[x, y, 0] != Piece.None || b.North[x, y, 0] != Piece.None || b.In(x + 1, y, 0) && b.West[x + 1, y, 0] != Piece.None || b.In(x, y + 1, 0) && b.North[x, y + 1, 0] != Piece.None;

    // ───────────────────────────── the transport ─────────────────────────────

    /// <summary>The transport on the ground at the map's south edge, its ramp down toward the north. The squad starts
    /// aboard, and can leave from its deck.</summary>
    private static void PlaceCraft(Plan p)
    {
        var b = p.B;
        int x0 = p.Rng.Range(4, b.W - CraftW - 5), y0 = b.H - CraftH - 2;
        // Clear the ground under it and in front of the ramp.
        for (int y = y0 - 3; y < y0 + CraftH + 1; y++)
        for (int x = x0 - 2; x < x0 + CraftW + 2; x++)
        {
            if (!b.In(x, y, 0)) continue;
            for (int z = 0; z < 2; z++)
            {
                b.Obj[x, y, z] = Piece.None;
                b.West[x, y, z] = Piece.None;
                b.North[x, y, z] = Piece.None;
                if (z > 0 && b.Mission.Terrain is not (Terrain.AlienBase or Terrain.Hive)) b.Floor[x, y, z] = Piece.None;
            }
            if (b.Floor[x, y, 0] is Piece.Furrows) b.Floor[x, y, 0] = Piece.Dirt;
            if (b.Floor[x, y, 0] == Piece.None) b.Floor[x, y, 0] = Piece.CaveFloor;
            p.Used[x, y] = true;
        }
        for (int y = y0; y < y0 + CraftH; y++)
        {
            b.West[x0, y, 0] = Piece.CraftHull;
            b.West[x0 + CraftW, y, 0] = Piece.CraftHull;
            for (int x = x0; x < x0 + CraftW; x++)
            {
                b.Floor[x, y, 0] = Piece.CraftDeck;
                b.Floor[x, y, 1] = Piece.CraftRoof;
                b.Exit.Add((x, y));
            }
        }
        for (int x = x0; x < x0 + CraftW; x++) b.North[x, y0 + CraftH, 0] = Piece.CraftHull;
        // Its wings, out to either side.
        for (int y = y0 + 2; y < y0 + 4; y++)
        {
            b.Obj[x0 - 1, y, 0] = Piece.Wing;
            b.Obj[x0 + CraftW, y, 0] = Piece.Wing;
        }
    }

    // ───────────────────────────── the UFO ─────────────────────────────

    public static int Footprint(UfoType t) => t switch
    {
        UfoType.Seeker => 5, UfoType.Skiff => 7, UfoType.Raider => 9, UfoType.Harvester => 11, UfoType.Carrier => 13, _ => 15,
    };

    /// <summary>
    /// The UFO: a disc of deck plating walled with hull, a door facing the transport, an inner chamber round the drive
    /// core in the bigger ones (with the nav lattice up front), and in the largest a second deck reached by a gravity
    /// lift. A crashed one has holes torn in its hull, wreckage strewn about, and perhaps a drive core gone up.
    /// </summary>
    private static void PlaceUfo(Plan p, UfoType t, double damage)
    {
        var b = p.B;
        int s = Footprint(t);
        double r = s / 2.0, rx = t == UfoType.Dreadnought ? r : r, ry = t == UfoType.Dreadnought ? r * 0.8 : r;
        int cx = Math.Clamp(b.W / 2 + p.Rng.Range(-6, 6), (int)r + 2, b.W - (int)r - 3);
        int cy = Math.Clamp((int)r + 3 + p.Rng.Range(0, 6), (int)r + 2, b.H - CraftH - (int)r - 6);
        bool Inside(int x, int y) => Math.Pow((x + 0.5 - cx) / rx, 2) + Math.Pow((y + 0.5 - cy) / ry, 2) <= 1;
        int levels = t >= UfoType.Harvester ? 2 : 1;
        // Clear the ground round it.
        for (int y = cy - (int)ry - 2; y <= cy + (int)ry + 2; y++)
        for (int x = cx - (int)rx - 2; x <= cx + (int)rx + 2; x++)
        {
            if (!b.In(x, y, 0)) continue;
            for (int z = 0; z < Battle.Levels; z++)
            {
                b.Obj[x, y, z] = Piece.None;
                b.West[x, y, z] = Piece.None;
                b.North[x, y, z] = Piece.None;
                if (z > 0) b.Floor[x, y, z] = Piece.None;
            }
            if (b.Floor[x, y, 0] is Piece.Furrows or Piece.None) b.Floor[x, y, 0] = Piece.Dirt;
            p.Used[x, y] = true;
        }
        var hull = new List<(Part Part, Cell At)>();
        for (int y = cy - (int)ry - 1; y <= cy + (int)ry + 1; y++)
        for (int x = cx - (int)rx - 1; x <= cx + (int)rx + 1; x++)
        {
            if (!b.In(x, y, 0)) continue;
            bool inside = Inside(x, y);
            if (inside)
                for (int z = 0; z <= levels; z++) b.Floor[x, y, z] = Piece.UfoFloor;
            for (int z = 0; z < levels; z++)
            {
                if (inside != Inside(x - 1, y)) { b.West[x, y, z] = Piece.UfoWall; if (z == 0) hull.Add((Part.West, new Cell(x, y, z))); }
                if (inside != Inside(x, y - 1)) { b.North[x, y, z] = Piece.UfoWall; if (z == 0) hull.Add((Part.North, new Cell(x, y, z))); }
            }
        }
        // The door, on the south side facing the transport.
        int doorY = cy + (int)ry;
        while (doorY > cy && !Inside(cx, doorY)) doorY--;
        b.North[cx, doorY + 1, 0] = Piece.UfoDoor;
        if (t >= UfoType.Carrier) b.West[cx - (int)rx + 0 + FirstInside(cx - (int)rx, cy, Inside), cy, 0] = Piece.UfoDoor;
        // Inside: an inner chamber round the drive core.
        if (s >= 9)
        {
            double ir = r * 0.42;
            bool Inner(int x, int y) => Math.Pow(x + 0.5 - cx, 2) + Math.Pow(y + 0.5 - cy, 2) <= ir * ir;
            for (int y = cy - (int)ir - 1; y <= cy + (int)ir + 1; y++)
            for (int x = cx - (int)ir - 1; x <= cx + (int)ir + 1; x++)
            {
                if (Inner(x, y) != Inner(x - 1, y)) b.West[x, y, 0] = Piece.UfoWall;
                if (Inner(x, y) != Inner(x, y - 1)) b.North[x, y, 0] = Piece.UfoWall;
            }
            int iy = cy;
            while (Inner(cx, iy + 1)) iy++;
            b.North[cx, iy + 1, 0] = Piece.UfoDoor;
        }
        b.Obj[cx, cy, 0] = Piece.DriveCore;
        if (t == UfoType.Dreadnought) b.Obj[cx - 1, cy - 1, 0] = Piece.DriveCore;
        if (s >= 7) b.Obj[cx, cy - (int)(ry * 0.65), 0] = Piece.NavConsole;
        PutIfFree(b, cx - (int)(rx * 0.6), cy - 1, 0, Piece.Console);
        PutIfFree(b, cx + (int)(rx * 0.6), cy - 1, 0, Piece.Console);
        if (s >= 9)
        {
            PutIfFree(b, cx - (int)(rx * 0.6), cy + 2, 0, Piece.Pod);
            PutIfFree(b, cx + (int)(rx * 0.6), cy + 2, 0, Piece.Pod);
        }
        if (levels == 2)
        {
            int lx = cx + (int)(rx * 0.62), ly = cy;
            if (b.Obj[lx, ly, 0] != Piece.None) ly++;
            b.Obj[lx, ly, 0] = Piece.Lift;
            PutIfFree(b, cx, cy, 1, Piece.Console);
            PutIfFree(b, cx - 2, cy + 1, 1, Piece.Pod);
            PutIfFree(b, cx + 2, cy - 2, 1, Piece.Pod);
            PutIfFree(b, cx - 2, cy - 2, 1, Piece.Console);
        }
        // A crash tears the hull open and scatters wreckage.
        if (damage > 0)
        {
            int holes = Math.Max(1, (int)(hull.Count * damage * 0.3));
            for (int i = 0; i < holes; i++)
            {
                var (part, at) = hull[p.Rng.Next(hull.Count)];
                if (b.Get(part, at.X, at.Y, 0) == Piece.UfoWall) b.Set(part, at.X, at.Y, 0, Piece.Stub);
            }
            for (int i = 0; i < 10 + (int)(damage * 20); i++)
            {
                int x = cx + p.Rng.Range(-(int)rx - 4, (int)rx + 4), y = cy + p.Rng.Range(-(int)ry - 4, (int)ry + 4);
                if (!b.In(x, y, 0) || Inside(x, y) || b.Floor[x, y, 0] == Piece.CraftDeck) continue;
                b.Floor[x, y, 0] = Piece.Scorched;
                if (p.Rng.Chance(0.3) && b.Obj[x, y, 0] == Piece.None) b.Obj[x, y, 0] = Piece.Debris;
            }
            if (damage > 0.8) { b.Obj[cx, cy, 0] = Piece.Debris; b.Floor[cx, cy, 0] = Piece.Scorched; }
        }
        // The heart of it for the pilot: just inside the door.
        b.Objective = new Cell(cx, doorY, 0);
    }

    private static int FirstInside(int x, int y, Func<int, int, bool> inside)
    {
        int k = 0;
        while (k < 10 && !inside(x + k, y)) k++;
        return k;
    }

    private static void PutIfFree(Battle b, int x, int y, int z, Piece piece)
    {
        if (b.In(x, y, z) && b.Obj[x, y, z] == Piece.None && b.Floor[x, y, z] != Piece.None) b.Obj[x, y, z] = piece;
    }

    // ───────────────────────────── underground ─────────────────────────────

    /// <summary>
    /// An alien base (or the hive of Nyx): solid rock with rooms carved out and joined by tunnels, under a ceiling of
    /// rock. Pods and consoles fill the rooms; the hive's middle chamber holds the hive mind.
    /// </summary>
    private static void Caverns(Plan p, bool hive)
    {
        var b = p.B;
        for (int y = 0; y < b.H; y++)
        for (int x = 0; x < b.W; x++)
        {
            b.Floor[x, y, 0] = hive ? Piece.HiveFloor : Piece.CaveFloor;
            b.Floor[x, y, 1] = Piece.CaveFloor;
            b.Obj[x, y, 0] = Piece.Bedrock;
        }
        var rooms = new List<(int X, int Y, int W, int H)>();
        // The landing cavern at the south, where the transport sets down.
        rooms.Add((b.W / 2 - 7, b.H - 12, 14, 11));
        for (int i = 0; i < 60 && rooms.Count < 9; i++)
        {
            int w = p.Rng.Range(6, 10), h = p.Rng.Range(6, 9), x = p.Rng.Range(2, b.W - w - 2), y = p.Rng.Range(2, b.H - h - 14);
            if (rooms.Any(r => x < r.X + r.W + 2 && r.X < x + w + 2 && y < r.Y + r.H + 2 && r.Y < y + h + 2)) continue;
            rooms.Add((x, y, w, h));
        }
        if (hive)
        {
            // The great chamber in the middle, for the hive mind.
            var big = (X: b.W / 2 - 6, Y: b.H / 2 - 9, W: 12, H: 10);
            rooms.RemoveAll(r => r != rooms[0] && r.X < big.X + big.W + 1 && big.X < r.X + r.W + 1 && r.Y < big.Y + big.H + 1 && big.Y < r.Y + r.H + 1);
            rooms.Insert(1, big);
        }
        foreach (var r in rooms) Carve(b, r.X, r.Y, r.W, r.H, hive);
        // Tunnels: each room joined to the one before it, two tiles wide.
        for (int i = 1; i < rooms.Count; i++)
        {
            var a = rooms[i - 1];
            var c = rooms[i];
            int ax = a.X + a.W / 2, ay = a.Y + a.H / 2, cx = c.X + c.W / 2, cy = c.Y + c.H / 2;
            for (int x = Math.Min(ax, cx); x <= Math.Max(ax, cx); x++) Carve(b, x, ay, 1, 2, hive, tunnel: true);
            for (int y = Math.Min(ay, cy); y <= Math.Max(ay, cy); y++) Carve(b, cx, y, 2, 1, hive, tunnel: true);
        }
        // Walls round the rooms where they meet the rock, doors where tunnels come in: alien-built rooms.
        for (int k = 1; k < rooms.Count; k++)
        {
            var r = rooms[k];
            for (int y = r.Y + 1; y < r.Y + r.H - 1; y++)
            for (int x = r.X + 1; x < r.X + r.W - 1; x++)
            {
                if (p.Rng.Chance(hive ? 0.12 : 0.1) && b.Obj[x, y, 0] == Piece.None)
                    b.Obj[x, y, 0] = hive ? p.Rng.Pick(new[] { Piece.Fungus, Piece.Fungus, Piece.Pod }) : p.Rng.Pick(new[] { Piece.Pod, Piece.Console, Piece.Crate });
            }
            p.Take(r.X, r.Y, r.W, r.H);
        }
        if (!hive)
        {
            var control = rooms[^1];
            b.Obj[control.X + control.W / 2, control.Y + control.H / 2, 0] = Piece.NavConsole;
            b.Obj[control.X + 1, control.Y + 1, 0] = Piece.DriveCore;
            b.Objective = new Cell(control.X + control.W / 2, control.Y + control.H / 2 + 1, 0);
        }
        else
        {
            var big = rooms[1];
            int mx = big.X + big.W / 2 - 1, my = big.Y + big.H / 2 - 1;
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
            {
                b.Obj[mx + dx, my + dy, 0] = Piece.Brain;
                b.Floor[mx + dx, my + dy, 1] = Piece.None;
            }
            for (int i = -1; i <= 2; i++) { PutIfFree(b, mx - 2, my + i, 0, Piece.Pillar); PutIfFree(b, mx + 3, my + i, 0, Piece.Pillar); }
            b.Objective = new Cell(mx, my + 3, 0);
        }
        // Keep the landing cavern clear for the transport.
        p.Take(rooms[0].X, rooms[0].Y, rooms[0].W, rooms[0].H);
    }

    private static void Carve(Battle b, int x0, int y0, int w, int h, bool hive, bool tunnel = false)
    {
        for (int y = y0; y < y0 + h; y++)
        for (int x = x0; x < x0 + w; x++)
        {
            if (!b.In(x, y, 0) || x == 0 || y == 0 || x == b.W - 1 || y == b.H - 1) continue;
            if (b.Obj[x, y, 0] == Piece.Bedrock) b.Obj[x, y, 0] = Piece.None;
            if (!tunnel) b.Floor[x, y, 0] = hive ? Piece.HiveFloor : Piece.UfoFloor;
        }
    }

    /// <summary>The player's own base under attack: each facility a room of six tiles by six (hangars twelve), walled,
    /// with blast doors where facilities meet, and fitted out for what it is.</summary>
    private static void BaseInterior(Plan p, Base bs)
    {
        var b = p.B;
        for (int y = 0; y < b.H; y++)
        for (int x = 0; x < b.W; x++)
        {
            b.Obj[x, y, 0] = Piece.Bedrock;
            b.Floor[x, y, 0] = Piece.BaseFloor;
            b.Floor[x, y, 1] = Piece.CaveFloor;
        }
        foreach (var f in bs.Facilities)
        {
            int x0 = f.X * 6, y0 = f.Y * 6, n = f.Size * 6;
            for (int y = y0; y < y0 + n; y++)
            for (int x = x0; x < x0 + n; x++) b.Obj[x, y, 0] = Piece.None;
            for (int i = 0; i < n; i++)
            {
                b.West[x0, y0 + i, 0] = Piece.BaseWall;
                b.North[x0 + i, y0, 0] = Piece.BaseWall;
                if (x0 + n < b.W) b.West[x0 + n, y0 + i, 0] = Piece.BaseWall;
                if (y0 + n < b.H) b.North[x0 + i, y0 + n, 0] = Piece.BaseWall;
            }
            Piece[] stuff = f.Type switch
            {
                FacilityType.Lab => [Piece.Table, Piece.Console, Piece.Table],
                FacilityType.Workshop => [Piece.Machine, Piece.Table, Piece.Crate],
                FacilityType.Stores => [Piece.Crate, Piece.Shelves, Piece.Crate],
                FacilityType.Quarters => [Piece.Bed, Piece.Bed, Piece.Table],
                FacilityType.Containment => [Piece.Pod],
                FacilityType.Hangar => [Piece.Crate, Piece.Barrel],
                FacilityType.Lift => [],
                _ => [Piece.Machine, Piece.Console],
            };
            int count = f.Type == FacilityType.Hangar ? 5 : 6;
            for (int i = 0; i < count && stuff.Length > 0; i++)
            {
                int x = x0 + 1 + p.Rng.Next(n - 2), y = y0 + 1 + p.Rng.Next(n - 2);
                if ((x - x0) % 6 is 2 or 3 || (y - y0) % 6 is 2 or 3) continue;
                b.Obj[x, y, 0] = p.Rng.Pick(stuff);
            }
            if (f.Type == FacilityType.Lift) { b.Obj[x0 + 1, y0 + 1, 0] = Piece.Machine; b.Objective = new Cell(x0 + 3, y0 + 3, 0); }
        }
        // Blast doors between neighbouring facilities, in the middle of each shared edge.
        for (int gy = 0; gy < Rules.BaseSize; gy++)
        for (int gx = 0; gx < Rules.BaseSize; gx++)
        {
            var f = bs.At2(gx, gy);
            if (f is null) continue;
            if (bs.At2(gx + 1, gy) is { } e && e != f)
                for (int k = 2; k <= 3; k++) b.West[(gx + 1) * 6, gy * 6 + k, 0] = Piece.BaseDoor;
            if (bs.At2(gx, gy + 1) is { } s && s != f)
                for (int k = 2; k <= 3; k++) b.North[gx * 6 + k, (gy + 1) * 6, 0] = Piece.BaseDoor;
            if (bs.At2(gx + 1, gy) == f) for (int k = 0; k < 6; k++) b.West[(gx + 1) * 6, gy * 6 + k, 0] = Piece.None;
            if (bs.At2(gx, gy + 1) == f) for (int k = 0; k < 6; k++) b.North[gx * 6 + k, (gy + 1) * 6, 0] = Piece.None;
        }
    }

    // ───────────────────────────── the squad ─────────────────────────────

    /// <summary>
    /// The squad aboard the transport (or round the lift at home), each with their chosen weapon, loaded, two spare
    /// clips, a grenade, a flare at night, and every third a field kit — all taken from the base's stores, as far as
    /// they go.
    /// </summary>
    private static void Squad(Plan p, Mission m, Base? stores)
    {
        var b = p.B;
        var spots = b.Exit.OrderBy(e => e.Y).ThenBy(e => Math.Abs(e.X - b.Exit.Average(q => q.X))).Select(e => new Cell(e.X, e.Y, 0)).ToList();
        if (spots.Count == 0)
        {
            var c = b.Objective;
            spots = Enumerable.Range(0, 400).Select(i => new Cell(c.X + i % 20 - 10, c.Y + i / 20 - 10, 0))
                .Where(s => b.In(s) && b.Obj[s.X, s.Y, 0] == Piece.None).OrderBy(s => Math.Abs(s.X - c.X) + Math.Abs(s.Y - c.Y)).ToList();
        }
        bool Take(ItemKind k) => stores is null ? k is ItemKind.Rifle or ItemKind.RifleClip or ItemKind.FragGrenade or ItemKind.Flare or ItemKind.FieldKit : stores.Take(k);
        int i = 0;
        foreach (var s in m.Squad)
        {
            if (i >= spots.Count) break;
            var u = Tac.FromSoldier(b, s);
            u.Facing = 0;
            ItemKind[] choices = [s.Weapon, ItemKind.StormRifle, ItemKind.LaserRifle, ItemKind.Rifle, ItemKind.LaserPistol, ItemKind.Pistol];
            foreach (var k in choices.Distinct())
            {
                // A storm weapon only with cells for it.
                if (k == ItemKind.None || k == ItemKind.StormRifle && s.Weapon != k && (stores?.Count(ItemKind.StormRifleCell) ?? 0) == 0) continue;
                if (!Take(k)) continue;
                var w = Item.Make(k, loaded: false);
                var ammo = w.Info.Ammo;
                if (ammo != ItemKind.None)
                {
                    if (Take(ammo)) w.Clip = Item.Make(ammo);
                    for (int c = 0; c < 2; c++) if (Take(ammo)) u.Belt.Add(Item.Make(ammo));
                }
                u.Right = w;
                break;
            }
            if (Take(ItemKind.FragGrenade)) u.Belt.Add(Item.Make(ItemKind.FragGrenade));
            if (m.Night && Take(ItemKind.Flare)) u.Belt.Add(Item.Make(ItemKind.Flare));
            if (i % 3 == 0 && Take(ItemKind.FieldKit)) u.Backpack.Add(Item.Make(ItemKind.FieldKit));
            if (i % 4 == 1 && stores is not null && stores.Take(ItemKind.ShockBaton)) u.Backpack.Add(Item.Make(ItemKind.ShockBaton));
            if (s.PsiSkill > 0 && stores is not null && stores.Take(ItemKind.MindLens)) u.Backpack.Add(Item.Make(ItemKind.MindLens));
            if (stores is not null && u.Right?.Info.Class == ItemClass.Weapon && u.Right.Info.TwoHanded == false && stores.Count(ItemKind.StunBomb) > 0 && i % 2 == 0) { stores.Take(ItemKind.StunBomb); u.Belt.Add(Item.Make(ItemKind.StunBomb)); }
            Tac.Place(b, u, spots[i++]);
        }
    }

    // ───────────────────────────── the aliens ─────────────────────────────

    private static void Aliens(Plan p, Mission m)
    {
        var b = p.B;
        int n = Math.Max(1, m.Crew);
        var ranks = new List<AlienRank>();
        if (n >= 5 || m.Kind is MissionKind.AlienBase or MissionKind.Homeworld) ranks.Add(m.Kind is MissionKind.AlienBase or MissionKind.Homeworld || m.Ufo == UfoType.Dreadnought ? AlienRank.Commander : AlienRank.Leader);
        if (n >= 3) ranks.Add(AlienRank.Navigator);
        if (n >= 8) ranks.Add(AlienRank.Navigator);
        while (ranks.Count < n) ranks.Add(AlienRank.Soldier);
        int terror = m.Kind switch
        {
            MissionKind.Terror => 3 + Math.Min(3, m.Month / 2),
            MissionKind.AlienBase or MissionKind.Homeworld or MissionKind.BaseDefence => 2 + Math.Min(2, m.Month / 3),
            _ => m.Ufo is UfoType.Carrier or UfoType.Dreadnought ? 2 : m.Ufo == UfoType.Harvester && m.Month >= 2 ? 1 : 0,
        };
        bool inUfo = m.Kind is MissionKind.CrashSite or MissionKind.LandedUfo;
        var c = b.Objective;
        var exit = b.Exit.Count > 0 ? (X: b.Exit.Average(e => e.X), Y: b.Exit.Average(e => e.Y)) : (X: -100.0, Y: -100.0);
        int placed = 0;
        foreach (var rank in ranks.Concat(Enumerable.Repeat(AlienRank.Terror, terror)))
        {
            var race = rank == AlienRank.Terror ? Rules.TerrorOf(m.Race) : m.Kind == MissionKind.Homeworld ? p.Rng.Pick(new[] { Race.Veiled, Race.Brakk, Race.Brakk, Race.Pallid }) : m.Race;
            if (m.Kind == MissionKind.Homeworld && rank == AlienRank.Commander) race = Race.Veiled;
            var u = Tac.MakeAlien(b, race, rank, m.Month);
            bool guard = inUfo && (rank is AlienRank.Leader or AlienRank.Commander or AlienRank.Navigator || placed % 3 == 0) && rank != AlienRank.Terror;
            Cell? spot = null;
            for (int tries = 0; tries < 300 && spot is null; tries++)
            {
                int radius = guard ? Footprint(m.Ufo ?? UfoType.Skiff) / 2 - 1 : m.Kind is MissionKind.Terror or MissionKind.AlienBase or MissionKind.Homeworld or MissionKind.BaseDefence ? 30 : 14;
                int x = c.X + p.Rng.Range(-radius, radius), y = c.Y + p.Rng.Range(-radius, radius);
                int z = guard && m.Ufo >= UfoType.Harvester && p.Rng.Chance(0.4) ? 1 : 0;
                var cell = new Cell(x, y, z);
                if (!b.In(cell) || b.UnitAt(cell) is not null || !b.HasFloor(x, y, z) || Pieces.Of(b.Obj[x, y, z]).BlocksMove || b.Obj[x, y, z] == Piece.Lift) continue;
                if (guard != (b.Floor[x, y, 0] == Piece.UfoFloor) && inUfo) continue;
                if (z == 0 && b.Floor[x, y, 1] == Piece.Roof) { /* inside a house: fine */ }
                if (Math.Abs(x - exit.X) + Math.Abs(y - exit.Y) < (m.Kind == MissionKind.BaseDefence ? 0 : 12)) continue;
                if (m.Kind == MissionKind.BaseDefence && m.Base is { } bs && bs.At2(x / 6, y / 6)?.Type is FacilityType.Lift) continue;
                spot = cell;
            }
            if (spot is null) continue;
            u.Guard = guard;
            Tac.Place(b, u, spot.Value);
            placed++;
        }
    }

    private static void Civilians(Plan p)
    {
        var b = p.B;
        int n = 8 + p.Rng.Next(5);
        for (int tries = 0; tries < 500 && n > 0; tries++)
        {
            int x = p.Rng.Range(1, b.W - 2), y = p.Rng.Range(1, b.H - 10);
            if (b.UnitAt(x, y, 0) is not null || Pieces.Of(b.Obj[x, y, 0]).BlocksMove) continue;
            Tac.Place(b, Tac.MakeCivilian(b), new Cell(x, y, 0));
            n--;
        }
    }

    /// <summary>A quick battle for tests and the demonstration: a squad of fresh recruits against a crashed or landed UFO.</summary>
    public static Battle Skirmish(int seed, Terrain terrain = Terrain.Farmland, UfoType ufo = UfoType.Raider, Race race = Race.Pallid, bool night = false, int soldiers = 8, MissionKind kind = MissionKind.CrashSite, int month = 0)
    {
        var d = new Dice(seed);
        var m = new Mission { Kind = kind, Terrain = terrain, Night = night, Ufo = kind is MissionKind.CrashSite or MissionKind.LandedUfo ? ufo : null, Race = race, Crew = Rules.Of(ufo).Crew, Seed = seed, Month = month, Damage = kind == MissionKind.CrashSite ? 0.6 : 0, Name = "SKIRMISH" };
        for (int i = 0; i < soldiers; i++) m.Squad.Add(Soldier.Recruit(ref d, 1000 + i, 0));
        return Make(m);
    }
}
