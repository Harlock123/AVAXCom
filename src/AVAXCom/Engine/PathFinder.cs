namespace AVAXCom.Engine;

/// <summary>
/// Finding the way across the battlefield, in time units: four a straight step, six a diagonal, more through crops,
/// rubble and doors, eight up or down stairs or a gravity lift (or, for something that flies, through open air).
/// Walls stop walkers on their edges; a diagonal step needs both ways round it clear.
/// </summary>
public static class PathFinder
{
    public const int Straight = 4, Diagonal = 6, Climb = 8;

    /// <summary>The time units to step from one tile to the next (or -1 if it can't be done).</summary>
    public static int StepCost(Battle b, BUnit u, Cell from, Cell to, bool ignoreUnits = false)
    {
        if (!b.In(to) || !b.In(from)) return -1;
        int dx = to.X - from.X, dy = to.Y - from.Y, dz = to.Z - from.Z;
        if (Math.Abs(dx) > 1 || Math.Abs(dy) > 1 || Math.Abs(dz) > 1) return -1;
        if (!ignoreUnits && b.UnitAt(to) is { } other && other != u) return -1;
        var obj = Pieces.Of(b.Obj[to.X, to.Y, to.Z]);
        if (obj.BlocksMove) return -1;
        if (dz != 0)
        {
            if (dx != 0 || dy != 0) return -1;
            if (u.Flies)
            {
                // Up through open air (no floor above), down through it (none here).
                if (dz > 0 && b.Floor[to.X, to.Y, to.Z] != Piece.None && b.Obj[from.X, from.Y, from.Z] is not (Piece.Stairs or Piece.Lift)) return -1;
                if (dz < 0 && b.Floor[from.X, from.Y, from.Z] != Piece.None && b.Obj[to.X, to.Y, to.Z] is not (Piece.Stairs or Piece.Lift)) return -1;
                return Climb;
            }
            var below = dz > 0 ? from : to;
            return Pieces.Of(b.Obj[below.X, below.Y, below.Z]).Up ? Climb : -1;
        }
        if (!u.Flies && !b.HasFloor(to.X, to.Y, to.Z)) return -1;
        int extra = 0;
        if (dx != 0 && dy != 0)
        {
            bool a = Open(b, from.X, from.Y, from.Z, dx, 0, ref extra) && Open(b, from.X + dx, from.Y, from.Z, 0, dy, ref extra) && !Pieces.Of(b.Obj[from.X + dx, from.Y, from.Z]).BlocksMove;
            int e2 = 0;
            bool c = Open(b, from.X, from.Y, from.Z, 0, dy, ref e2) && Open(b, from.X, from.Y + dy, from.Z, dx, 0, ref e2) && !Pieces.Of(b.Obj[from.X, from.Y + dy, from.Z]).BlocksMove;
            if (!a || !c) return -1;
            extra = Math.Max(extra, e2);
        }
        else if (!Open(b, from.X, from.Y, from.Z, dx, dy, ref extra)) return -1;
        int floorCost = u.Flies ? 0 : Pieces.Of(b.Floor[to.X, to.Y, to.Z]).Cost;
        return (dx != 0 && dy != 0 ? Diagonal : Straight) + extra + floorCost + obj.Cost;
    }

    private static bool Open(Battle b, int x, int y, int z, int dx, int dy, ref int extra)
    {
        var (piece, _, _) = Tac.EdgeWall(b, x, y, z, dx, dy);
        if (piece == Piece.None) return true;
        var info = Pieces.Of(piece);
        if (info.BlocksMove) return false;
        extra += info.Cost;
        return true;
    }

    public static IEnumerable<Cell> Neighbours(Battle b, Cell c)
    {
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            if (dx != 0 || dy != 0) yield return new Cell(c.X + dx, c.Y + dy, c.Z);
        yield return c with { Z = c.Z + 1 };
        yield return c with { Z = c.Z - 1 };
    }

    /// <summary>The time units to turn from one facing to another: one for each eighth of a turn.</summary>
    public static int TurnCost(int from, int to)
    {
        int d = Math.Abs(from - to) % 8;
        return Math.Min(d, 8 - d);
    }

    /// <summary>The cheapest way from a unit to a tile (not counting the turns), or null if there's none.</summary>
    public static List<Cell>? Find(Battle b, BUnit u, Cell goal, int limit = 10000)
    {
        var start = new Cell(u.X, u.Y, u.Z);
        if (start == goal || !b.In(goal)) return null;
        var cost = new Dictionary<Cell, int> { [start] = 0 };
        var came = new Dictionary<Cell, Cell>();
        var open = new PriorityQueue<Cell, int>();
        open.Enqueue(start, 0);
        while (open.TryDequeue(out var c, out int pri))
        {
            if (c == goal) break;
            int g = cost[c];
            if (pri - Heuristic(c, goal) > g) continue;
            foreach (var n in Neighbours(b, c))
            {
                int step = StepCost(b, u, c, n);
                if (step < 0) continue;
                int ng = g + step;
                if (ng > limit || cost.TryGetValue(n, out int old) && old <= ng) continue;
                cost[n] = ng;
                came[n] = c;
                open.Enqueue(n, ng + Heuristic(n, goal));
            }
        }
        if (!came.ContainsKey(goal)) return null;
        var path = new List<Cell>();
        for (var c = goal; c != start; c = came[c]) path.Add(c);
        path.Reverse();
        return path;
    }

    private static int Heuristic(Cell a, Cell b)
    {
        int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
        return Straight * Math.Max(dx, dy) + (Diagonal - Straight) * Math.Min(dx, dy) + Climb * Math.Abs(a.Z - b.Z);
    }

    /// <summary>The time units a path costs a unit: its steps and the turns along the way.</summary>
    public static int Cost(Battle b, BUnit u, List<Cell> path)
    {
        int total = u.Kneeling ? Tac.StandCost : 0, facing = u.Facing;
        var at = new Cell(u.X, u.Y, u.Z);
        foreach (var c in path)
        {
            if (c.X != at.X || c.Y != at.Y)
            {
                int dir = Tac.DirOf(c.X - at.X, c.Y - at.Y);
                total += TurnCost(facing, dir);
                facing = dir;
            }
            int s = StepCost(b, u, at, c, ignoreUnits: true);
            total += s < 0 ? 999 : s;
            at = c;
        }
        return total;
    }

    /// <summary>Every tile a unit can reach with the time units it has, and what each costs (a flood out from it).</summary>
    public static Dictionary<Cell, int> Reach(Battle b, BUnit u, int tu)
    {
        var start = new Cell(u.X, u.Y, u.Z);
        var cost = new Dictionary<Cell, int> { [start] = 0 };
        var open = new PriorityQueue<Cell, int>();
        open.Enqueue(start, 0);
        while (open.TryDequeue(out var c, out int g))
        {
            if (g > cost[c]) continue;
            foreach (var n in Neighbours(b, c))
            {
                int step = StepCost(b, u, c, n);
                if (step < 0) continue;
                int ng = g + step;
                if (ng > tu || cost.TryGetValue(n, out int old) && old <= ng) continue;
                cost[n] = ng;
                open.Enqueue(n, ng);
            }
        }
        return cost;
    }

    /// <summary>How far every tile is from a goal, in time units, walking (a flood out from the goal, ignoring units).</summary>
    public static Dictionary<Cell, int> Field(Battle b, BUnit u, Cell goal)
    {
        var cost = new Dictionary<Cell, int> { [goal] = 0 };
        var open = new PriorityQueue<Cell, int>();
        open.Enqueue(goal, 0);
        while (open.TryDequeue(out var c, out int g))
        {
            if (g > cost[c]) continue;
            foreach (var n in Neighbours(b, c))
            {
                if (!b.In(n)) continue;
                int step = StepCost(b, u, n, c, ignoreUnits: true);
                if (step < 0) continue;
                int ng = g + step;
                if (cost.TryGetValue(n, out int old) && old <= ng) continue;
                cost[n] = ng;
                open.Enqueue(n, ng);
            }
        }
        return cost;
    }
}
