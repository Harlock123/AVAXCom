namespace AVAXCom.Engine;

/// <summary>
/// The aliens' minds on their turn, one alien and one action at a time. They share what any of them has seen. The
/// gifted reach into a soldier's mind first; the Husker kind run at the nearest prey and tear at it; the rest shoot at
/// what they see (aimed when there's time, a burst up close, a grenade at a huddle), advance on soldiers they know of, or
/// patrol, keeping time back for a shot on the player's turn. Guards stay in their UFO.
/// </summary>
public static class AlienBrain
{
    public const int MaxActions = 8;

    /// <summary>The next alien acts (or, when all have, the turn ends).</summary>
    public static void Think(Battle b)
    {
        if (b.Active == Side.Player) return;
        var u = b.Units.FirstOrDefault(u => u.Active && u.Team == b.Active && u.Panic == Panic.None && !b.Finished.Contains(u.Id));
        if (u is null)
        {
            b.Finished.Clear();
            b.Actions.Clear();
            Tac.EndTurn(b);
            return;
        }
        b.Actions[u.Id] = b.Actions.GetValueOrDefault(u.Id) + 1;
        if (b.Actions[u.Id] > MaxActions || !Act(b, u)) b.Finished.Add(u.Id);
    }

    private static int Dist(BUnit a, Cell c) => Math.Max(Math.Abs(a.X - c.X), Math.Abs(a.Y - c.Y)) + Math.Abs(a.Z - c.Z) * 2;

    /// <summary>One action for an alien; false when it has nothing more to do this turn.</summary>
    public static bool Act(Battle b, BUnit u)
    {
        foreach (var t in Tac.EnemiesSeenBy(b, u)) b.Known[t.Id] = new Cell(t.X, t.Y, t.Z);
        foreach (var id in b.Known.Keys.ToList())
            if (b.Units.FirstOrDefault(t => t.Id == id) is not { Active: true } t || !Tac.Hostile(u, t)) b.Known.Remove(id);
        var known = b.Known.Select(kv => (Unit: b.Units.First(t => t.Id == kv.Key), At: kv.Value)).Where(k => Tac.Hostile(u, k.Unit)).ToList();

        // Psionics first.
        if (u.PsiSkill > 0 && u.Side == Side.Alien && u.Tu >= Tac.Percent(u, Tac.PsiPercent) && b.Actions[u.Id] == 1)
        {
            var mind = known.Select(k => k.Unit).Where(t => t.IsSoldier && t.Team == Side.Player).OrderByDescending(t => Tac.PsiChance(u, t, false)).FirstOrDefault();
            if (mind is not null)
            {
                bool control = (u.Race == Race.Veiled || u.AlienRank == AlienRank.Commander) && Tac.PsiChance(u, mind, true) >= 25 && b.Rng.Chance(0.6);
                if (Tac.Psi(b, u, mind, control) is null) return true;
            }
        }

        var gun = u.Weapon;
        // The horrors: run in and strike.
        if (gun is null && u.Melee > 0)
        {
            var prey = known.OrderBy(k => Dist(u, k.At)).FirstOrDefault();
            if (prey.Unit is null)
            {
                prey = b.Units.Where(t => t.Active && Tac.Hostile(u, t) && t.Side == Side.Civilian).Select(t => (t, new Cell(t.X, t.Y, t.Z))).OrderBy(k => Dist(u, k.Item2)).FirstOrDefault();
                if (prey.Unit is null) return Wander(b, u, 0);
            }
            var p = prey.Unit;
            if (Math.Max(Math.Abs(p.X - u.X), Math.Abs(p.Y - u.Y)) <= 1 && p.Z == u.Z)
                return Tac.Strike(b, u, p) is null;
            return Approach(b, u, new Cell(p.X, p.Y, p.Z), 0, adjacent: true);
        }
        if (gun is null) return Wander(b, u, 0);
        if (!gun.Loaded)
        {
            if (Tac.Reload(b, u) is null) return true;
            return false;
        }
        int snap = Tac.ShotCost(u, gun, FireMode.Snap);
        var seen = Tac.EnemiesSeenBy(b, u).ToList();
        if (seen.Count > 0)
        {
            // A grenade at a huddle, or at one who's far.
            var grenade = u.Belt.FirstOrDefault(i => i.Info.Class == ItemClass.Grenade && i.Kind != ItemKind.Flare);
            // At most one grenade a turn between them, and not before the third turn: the squad gets a chance to spread out.
            if (grenade is not null && b.Turn >= 3 && b.Actions.GetValueOrDefault(-1) == 0 && u.Tu >= Tac.PrimeCost + Tac.Percent(u, Tac.ThrowPercent))
            {
                var huddle = seen.Select(t => (t, n: seen.Count(o => Math.Abs(o.X - t.X) <= 2 && Math.Abs(o.Y - t.Y) <= 2))).OrderByDescending(x => x.n).First();
                double d = Math.Sqrt(Math.Pow(huddle.t.X - u.X, 2) + Math.Pow(huddle.t.Y - u.Y, 2));
                if ((huddle.n >= 2 || b.Rng.Chance(0.25)) && d >= 4 && d <= Math.Min(12, Tac.ThrowRange(u, grenade)) && u.Left is null)
                {
                    b.Actions[-1] = 1;
                    u.Belt.Remove(grenade);
                    u.Left = grenade;
                    if (Tac.Prime(b, u, grenade, 0) is null && Tac.Throw(b, u, grenade, new Cell(huddle.t.X, huddle.t.Y, huddle.t.Z)) is null) return true;
                    if (u.Left == grenade) { u.Left = null; u.Belt.Add(grenade); }
                }
            }
            var target = seen.OrderByDescending(t => Tac.HitChance(u, gun, FireMode.Aimed, new Cell(t.X, t.Y, t.Z)) + (t.Hp < 15 ? 20 : 0) + (t.IsSoldier ? 10 : 0)).First();
            var at = new Cell(target.X, target.Y, target.Z);
            int dist = Dist(u, at);
            int aimed = Tac.ShotCost(u, gun, FireMode.Aimed), auto = Tac.ShotCost(u, gun, FireMode.Auto);
            FireMode? mode = null;
            if (dist <= 5 && auto > 0 && u.Tu >= auto) mode = FireMode.Auto;
            else if (aimed > 0 && u.Tu >= aimed + (u.Guard ? 0 : 0)) mode = FireMode.Aimed;
            else if (snap > 0 && u.Tu >= snap) mode = FireMode.Snap;
            if (mode is { } m && Tac.Shoot(b, u, at, m) is null) return true;
            return false;
        }
        // Nobody in sight: advance on what's known (the bold ones), or patrol.
        bool bold = u.Race is Race.Brakk or Race.Skimmer or Race.Warden || b.Turn >= 4 || u.Team == Side.Alien && u.Side == Side.Player;
        if (!u.Guard && bold && known.Count > 0)
        {
            var nearest = known.OrderBy(k => Dist(u, k.At)).First();
            if (Approach(b, u, nearest.At, snap, adjacent: false)) return true;
        }
        if (!u.Guard && b.Units.Any(t => t.Active && t.Side == Side.Civilian) && b.Rng.Chance(0.7))
        {
            var civ = b.Units.Where(t => t.Active && t.Side == Side.Civilian).OrderBy(t => Dist(u, new Cell(t.X, t.Y, t.Z))).First();
            if (Approach(b, u, new Cell(civ.X, civ.Y, civ.Z), snap, adjacent: false)) return true;
        }
        if (u.Guard)
        {
            // A guard looks about, turning to where it last heard of trouble.
            if (known.Count > 0 && b.Actions[u.Id] == 1)
            {
                var k = known.OrderBy(k => Dist(u, k.At)).First();
                int dir = Tac.DirOf(k.At.X - u.X, k.At.Y - u.Y);
                if (dir != u.Facing && u.Tu - PathFinder.TurnCost(u.Facing, dir) >= snap) { u.Tu -= PathFinder.TurnCost(u.Facing, dir); u.Facing = dir; return true; }
            }
            return false;
        }
        return Wander(b, u, snap);
    }

    /// <summary>Walks toward a tile (or next to it), as far as the time units allow while keeping some back.</summary>
    private static bool Approach(Battle b, BUnit u, Cell goal, int reserve, bool adjacent)
    {
        var start = new Cell(u.X, u.Y, u.Z);
        List<Cell>? path = null;
        if (adjacent)
        {
            foreach (var n in PathFinder.Neighbours(b, goal).Where(n => n.Z == goal.Z && b.UnitAt(n) is null).OrderBy(n => Math.Abs(n.X - u.X) + Math.Abs(n.Y - u.Y)))
            {
                path = PathFinder.Find(b, u, n, 600);
                if (path is not null) break;
            }
        }
        else
        {
            path = PathFinder.Find(b, u, goal, 600);
            if (path is null)
                foreach (var n in PathFinder.Neighbours(b, goal).Where(n => b.In(n) && b.UnitAt(n) is null))
                    if ((path = PathFinder.Find(b, u, n, 600)) is not null) break;
            if (path is { Count: > 0 } && b.UnitAt(path[^1]) is not null) path.RemoveAt(path.Count - 1);
        }
        if (path is null || path.Count == 0) return false;
        return Go(b, u, path, reserve, start);
    }

    /// <summary>Starts walking a path cut short where the time units (less a reserve) run out.</summary>
    private static bool Go(Battle b, BUnit u, List<Cell> path, int reserve, Cell start)
    {
        int spend = u.Tu - reserve, facing = u.Facing, cost = 0, keep = 0;
        var at = start;
        foreach (var c in path)
        {
            int turn = c.X != at.X || c.Y != at.Y ? PathFinder.TurnCost(facing, Tac.DirOf(c.X - at.X, c.Y - at.Y)) : 0;
            int step = PathFinder.StepCost(b, u, at, c, ignoreUnits: true);
            if (step < 0 || cost + turn + step > spend) break;
            cost += turn + step;
            if (c.X != at.X || c.Y != at.Y) facing = Tac.DirOf(c.X - at.X, c.Y - at.Y);
            at = c;
            keep++;
        }
        if (keep == 0) return false;
        var goal = path[keep - 1];
        if (Tac.Walk(b, u, goal) is not null) return false;
        if (b.WalkPath.Count > keep) b.WalkPath.RemoveRange(keep, b.WalkPath.Count - keep);
        return true;
    }

    /// <summary>A stroll to somewhere nearby, now and then.</summary>
    private static bool Wander(Battle b, BUnit u, int reserve)
    {
        if (b.Actions[u.Id] > 1 || !b.Rng.Chance(0.6)) return false;
        for (int tries = 0; tries < 6; tries++)
        {
            var goal = new Cell(Math.Clamp(u.X + b.Rng.Range(-7, 7), 0, b.W - 1), Math.Clamp(u.Y + b.Rng.Range(-7, 7), 0, b.H - 1), u.Z);
            if (!b.HasFloor(goal.X, goal.Y, goal.Z) || b.UnitAt(goal) is not null) continue;
            var path = PathFinder.Find(b, u, goal, 300);
            if (path is { Count: > 0 }) return Go(b, u, path, reserve, new Cell(u.X, u.Y, u.Z));
        }
        return false;
    }
}
