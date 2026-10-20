namespace AVAXCom.Engine;

public static partial class Tac
{
    // ───────────────────────────── the chance to hit ─────────────────────────────

    /// <summary>
    /// The chance (percent) a shot hits where it's aimed: the firer's accuracy times the way of firing's, better
    /// kneeling, worse with a two-handed weapon and something in the other hand, worse with wounded arms, and falling
    /// off past eighteen tiles.
    /// </summary>
    public static int HitChance(BUnit u, Item w, FireMode m, Cell target)
    {
        var info = w.Info;
        int mode = m switch { FireMode.Snap => info.Snap, FireMode.Aimed => info.Aimed, _ => info.Auto };
        if (mode <= 0) return 0;
        double acc = u.Accuracy * mode / 100.0;
        if (u.Kneeling) acc *= 1.15;
        if (info.TwoHanded && u.Right is not null && u.Left is not null) acc *= 0.8;
        acc *= Math.Max(0.5, 1 - 0.1 * (u.Wounds[(int)Body.LeftArm] + u.Wounds[(int)Body.RightArm]));
        double d = Math.Sqrt(Math.Pow(target.X - u.X, 2) + Math.Pow(target.Y - u.Y, 2) + Math.Pow((target.Z - u.Z) * 1.5, 2));
        if (d > 18) acc *= Math.Max(0.4, 1 - (d - 18) * 0.025);
        return (int)Math.Clamp(Math.Round(acc), 0, 100);
    }

    public static int MeleeChance(BUnit u) => Math.Clamp(70 + (u.Strength - 30) / 3 + (u.Side == Side.Alien ? 15 : 0), 20, 100);

    // ───────────────────────────── shooting ─────────────────────────────

    /// <summary>Fires at a tile (snap, aimed, or a burst of three): the time units are spent and the shots go one by one.</summary>
    public static string? Shoot(Battle b, BUnit u, Cell target, FireMode m)
    {
        if (!Ready(b, u)) return "NOT NOW";
        var w = u.Weapon;
        if (w is null) return "NO WEAPON IN HAND";
        if (!w.Loaded) return "NO AMMUNITION: RELOAD";
        int cost = ShotCost(u, w, m);
        if (cost < 0) return "IT CANNOT FIRE THAT WAY";
        if (target.X == u.X && target.Y == u.Y && target.Z == u.Z) return "NOT AT YOURSELF";
        int turn = target.X != u.X || target.Y != u.Y ? PathFinder.TurnCost(u.Facing, DirOf(target.X - u.X, target.Y - u.Y)) : 0;
        if (u.Tu < cost + turn) return "NOT ENOUGH TIME UNITS";
        u.Tu -= cost + turn;
        if (turn > 0) u.Facing = DirOf(target.X - u.X, target.Y - u.Y);
        int shots = m == FireMode.Auto ? 3 : 1;
        for (int i = 0; i < shots; i++) b.Burst.Add(new Shot(u, target, i * BurstGap, m, false));
        return null;
    }

    /// <summary>
    /// One shot leaves: hit exactly where aimed with the chance to hit, else somewhere near it (and perhaps something else
    /// on the way); it flies along until it meets a wall, an object, a floor or a body.
    /// </summary>
    private static void FireOne(Battle b, BUnit u, Cell target, FireMode mode)
    {
        var w = u.Weapon;
        if (!u.Active || w is null || !w.Loaded) return;
        if (w.Clip is { } clip) clip.Rounds--;
        int chance = HitChance(u, w, mode, target);
        var aimAt = b.UnitAt(target) is { } tu ? Centre(tu) : (target.X + 0.5, target.Y + 0.5, target.Z + 0.4);
        var from = Eye(u);
        from = (from.X, from.Y, from.Z - 0.1);
        u.Shots++;
        if (!b.Rng.Chance(chance / 100.0))
        {
            double dist = Math.Sqrt(Math.Pow(aimAt.Item1 - from.X, 2) + Math.Pow(aimAt.Item2 - from.Y, 2));
            double spread = 0.6 + dist * 0.09;
            for (int tries = 0; tries < 8; tries++)
            {
                var off = (aimAt.Item1 + b.Rng.Range(-spread, spread), aimAt.Item2 + b.Rng.Range(-spread, spread), aimAt.Item3 + b.Rng.Range(-0.4, 0.3));
                double ox = off.Item1 - (target.X + 0.5), oy = off.Item2 - (target.Y + 0.5);
                if (ox * ox + oy * oy > BodyRadius * BodyRadius * 2.5 || tries == 7) { aimAt = off; break; }
            }
        }
        var hit = Trace(b, from, aimAt, shot: true, ignore: u);
        var (power, type, radius) = w.Shot;
        var p = new Projectile
        {
            X0 = from.X, Y0 = from.Y, Z0 = from.Z, X1 = hit.X, Y1 = hit.Y, Z1 = hit.Z, Type = type, Rocket = radius > 0,
            Speed = ShotSpeed / Math.Max(1, Math.Sqrt(Math.Pow(hit.X - from.X, 2) + Math.Pow(hit.Y - from.Y, 2) + Math.Pow(hit.Z - from.Z, 2))) * (radius > 0 ? 0.5 : 1),
        };
        b.Effects.Add(new Fx { Kind = FxKind.Flash, X = from.X, Y = from.Y, Z = from.Z, Life = 0.12, Colour = Colour(type) });
        b.Heard.Add(type switch { DamageType.Laser => Sfx.Laser, DamageType.Plasma => Sfx.Storm, _ when radius > 0 => Sfx.Cannon, _ when power >= 50 => Sfx.Cannon, _ => Sfx.Rifle });
        p.OnArrive = () => Impact(b, u, hit, power, type, radius);
        b.Flying.Add(p);
        if (b.Instant) { b.Flying.Remove(p); p.OnArrive(); }
    }

    public static uint Colour(DamageType t) => t switch
    {
        DamageType.Laser => 0xFFFF4A4A,
        DamageType.Plasma => 0xFF6AFF9A,
        DamageType.Stun => 0xFF9AC8FF,
        _ => 0xFFFFE08A,
    };

    /// <summary>Where a shot ends: a blast, a wound, or damage to the scenery.</summary>
    private static void Impact(Battle b, BUnit? shooter, Hit hit, int power, DamageType type, int radius)
    {
        if (hit.Kind == HitKind.Out) return;
        if (radius > 0)
        {
            // The blast centres on the tile the shot was in when it stopped.
            var c = new Cell(Math.Clamp((int)Math.Floor(hit.X), 0, b.W - 1), Math.Clamp((int)Math.Floor(hit.Y), 0, b.H - 1), Math.Clamp((int)Math.Floor(hit.Z), 0, Battle.Levels - 1));
            if (hit.Kind is HitKind.West or HitKind.North && b.UnitAt(c) is null) c = hit.Cell;
            Explode(b, c, power, radius, type, shooter);
            return;
        }
        b.Effects.Add(new Fx { Kind = FxKind.Hit, X = hit.X, Y = hit.Y, Z = hit.Z, Life = 0.35, Colour = Colour(type) });
        switch (hit.Kind)
        {
            case HitKind.Unit:
                if (shooter is not null && Hostile(shooter, hit.Unit!)) shooter.Hits++;
                Damage(b, hit.Unit!, power, type, shooter, shooter is null ? -1 : DirOf(shooter.X - hit.Unit!.X, shooter.Y - hit.Unit!.Y));
                break;
            case HitKind.West or HitKind.North or HitKind.Object or HitKind.Floor:
                var part = hit.Kind switch { HitKind.West => Part.West, HitKind.North => Part.North, HitKind.Object => Part.Object, _ => Part.Floor };
                HurtTerrain(b, part, hit.Cell, (int)(power * b.Rng.Range(0.5, 1.5)));
                break;
        }
        Look(b);
        CheckEnd(b);
    }

    // ───────────────────────────── melee ─────────────────────────────

    /// <summary>A blow at a unit next to it: a shock baton (stun), or an alien's claws.</summary>
    public static string? Strike(Battle b, BUnit u, BUnit target)
    {
        if (!Ready(b, u)) return "NOT NOW";
        if (Math.Max(Math.Abs(target.X - u.X), Math.Abs(target.Y - u.Y)) > 1 || target.Z != u.Z) return "TOO FAR AWAY";
        var baton = u.Right is { Info.Class: ItemClass.Melee } r ? r : u.Left is { Info.Class: ItemClass.Melee } l ? l : null;
        int power = baton?.Info.Power ?? u.Melee;
        if (power <= 0) return "NOTHING TO STRIKE WITH";
        var type = baton?.Info.Damage ?? u.MeleeType;
        int cost = baton is not null ? Percent(u, baton.Info.AimedTu) : Percent(u, 30);
        if (u.Tu < cost) return "NOT ENOUGH TIME UNITS";
        u.Tu -= cost;
        u.Facing = DirOf(target.X - u.X, target.Y - u.Y);
        b.Heard.Add(Sfx.Melee);
        b.Wait = b.Instant ? 0 : 0.35;
        if (!b.Rng.Chance(MeleeChance(u) / 100.0))
        {
            b.Effects.Add(new Fx { Kind = FxKind.Words, X = target.X + 0.5, Y = target.Y + 0.5, Z = target.Z + 1, Text = "MISS", Life = 0.8, Colour = 0xFFFFFFFF });
            return null;
        }
        u.MeleeHits++;
        Damage(b, target, power, type, u, DirOf(u.X - target.X, u.Y - target.Y), melee: true);
        Look(b);
        CheckEnd(b);
        return null;
    }

    // ───────────────────────────── throwing ─────────────────────────────

    /// <summary>How far a unit can throw an item: farther the stronger it is and the lighter the item.</summary>
    public static int ThrowRange(BUnit u, Item i) => (int)Math.Clamp(6 + u.Strength / 6.0 - i.Weight / 2.0, 3, 16);

    /// <summary>Throws an item in hand at a tile: it arcs over low things and lands near where it was meant to, the better
    /// the thrower the nearer. A primed grenade waits for its fuse; a flare lights up the night where it falls.</summary>
    public static string? Throw(Battle b, BUnit u, Item item, Cell target)
    {
        if (!Ready(b, u)) return "NOT NOW";
        if (u.Right != item && u.Left != item) return "IT MUST BE IN HAND";
        double dist = Math.Sqrt(Math.Pow(target.X - u.X, 2) + Math.Pow(target.Y - u.Y, 2));
        if (dist > ThrowRange(u, item)) return "OUT OF RANGE";
        int cost = Percent(u, ThrowPercent) + (target.X != u.X || target.Y != u.Y ? PathFinder.TurnCost(u.Facing, DirOf(target.X - u.X, target.Y - u.Y)) : 0);
        if (u.Tu < cost) return "NOT ENOUGH TIME UNITS";
        u.Tu -= cost;
        if (target.X != u.X || target.Y != u.Y) u.Facing = DirOf(target.X - u.X, target.Y - u.Y);
        if (u.Right == item) u.Right = null; else u.Left = null;
        u.Throws++;
        // Where it's meant to land, and where it does.
        var land = target;
        if (!b.Rng.Chance(u.Throwing / 100.0))
        {
            int spread = 1 + (int)(dist * 0.15);
            land = new Cell(Math.Clamp(target.X + b.Rng.Range(-spread, spread), 0, b.W - 1), Math.Clamp(target.Y + b.Rng.Range(-spread, spread), 0, b.H - 1), target.Z);
        }
        var from = (u.X + 0.5, u.Y + 0.5, u.Z + 0.8);
        var to = (land.X + 0.5, land.Y + 0.5, land.Z + 0.15);
        double peak = Math.Min(2.2, 0.4 + dist * 0.12);
        var stop = land;
        // Follow the arc, a short line at a time, until it strikes something.
        var prev = from;
        for (int i = 1; i <= 24; i++)
        {
            double t = i / 24.0;
            var pt = (from.Item1 + (to.Item1 - from.Item1) * t, from.Item2 + (to.Item2 - from.Item2) * t, from.Item3 + (to.Item3 - from.Item3) * t + 4 * peak * t * (1 - t));
            var h = Trace(b, prev, pt, shot: true, ignore: u, step: 0.08, units: false, extend: false);
            if (!h.Clear)
            {
                // It bounces back into the tile it came from.
                double bx = h.X - (pt.Item1 - prev.Item1) * 0.3, by = h.Y - (pt.Item2 - prev.Item2) * 0.3, bz = Math.Max(0, h.Z - 0.05);
                stop = new Cell(Math.Clamp((int)bx, 0, b.W - 1), Math.Clamp((int)by, 0, b.H - 1), Math.Clamp((int)bz, 0, Battle.Levels - 1));
                to = (stop.X + 0.5, stop.Y + 0.5, stop.Z + 0.15);
                break;
            }
            prev = pt;
        }
        stop = b.FloorBelow(stop);
        b.Heard.Add(Sfx.Throw);
        var p = new Projectile { X0 = from.Item1, Y0 = from.Item2, Z0 = from.Item3, X1 = to.Item1, Y1 = to.Item2, Z1 = stop.Z + 0.15, Thrown = true, Peak = peak, Speed = 1 / ThrowSeconds, Type = DamageType.Explosive };
        p.OnArrive = () =>
        {
            b.ItemsAt(stop).Add(item);
            if (item.Kind == ItemKind.Flare) Light(b, stop, item.Info.Radius);
            Look(b);
        };
        b.Flying.Add(p);
        return null;
    }

    /// <summary>A flare's light: tiles within its radius count as lit at night.</summary>
    public static void Light(Battle b, Cell at, int radius)
    {
        for (int y = Math.Max(0, at.Y - radius); y <= Math.Min(b.H - 1, at.Y + radius); y++)
        for (int x = Math.Max(0, at.X - radius); x <= Math.Min(b.W - 1, at.X + radius); x++)
            if ((x - at.X) * (x - at.X) + (y - at.Y) * (y - at.Y) <= radius * radius) b.Flares[x, y]++;
    }

    // ───────────────────────────── explosions ─────────────────────────────

    /// <summary>
    /// A blast: everything within its radius (on its storey, and half as hard on the ones above and below) is hit, the
    /// harder the nearer; walls, floors and objects that can't take it are destroyed (a destroyed floor drops whoever
    /// stood on it), and a strong wall between shields what's behind it.
    /// </summary>
    public static void Explode(Battle b, Cell at, int power, int radius, DamageType type, BUnit? source)
    {
        if (radius <= 0) radius = 1;
        b.Effects.Add(new Fx { Kind = type == DamageType.Stun ? FxKind.Smoke : FxKind.Explosion, X = at.X + 0.5, Y = at.Y + 0.5, Z = at.Z + 0.3, Life = 0.9, Size = radius });
        b.Heard.Add(power >= 90 ? Sfx.BigExplosion : Sfx.Explosion);
        var centre = (at.X + 0.5, at.Y + 0.5, at.Z + 0.5);
        var hurtUnits = new List<(BUnit, int)>();
        for (int z = Math.Max(0, at.Z - 1); z <= Math.Min(Battle.Levels - 1, at.Z + 1); z++)
        for (int y = Math.Max(0, at.Y - radius); y <= Math.Min(b.H - 1, at.Y + radius); y++)
        for (int x = Math.Max(0, at.X - radius); x <= Math.Min(b.W - 1, at.X + radius); x++)
        {
            double d = Math.Sqrt((x - at.X) * (x - at.X) + (y - at.Y) * (y - at.Y) + (z - at.Z) * (z - at.Z) * 2.0);
            if (d > radius + 0.5) continue;
            double dmg = power * (1 - d / (radius + 1)) * (z == at.Z ? 1 : 0.5);
            if (dmg < 1) continue;
            if (d > 1.1)
            {
                var h = Trace(b, centre, (x + 0.5, y + 0.5, z + 0.5), shot: true, goal: new Cell(x, y, z), step: 0.2, units: false, extend: false);
                // A strong wall or floor between shields what's behind it (but not itself).
                if (!h.Clear && !(h.Kind == HitKind.Floor && h.Cell == new Cell(x, y, z))) dmg *= 0.3;
            }
            int hit = (int)dmg;
            if (b.UnitAt(x, y, z) is { } u) hurtUnits.Add((u, hit));
            if (b.BodyAt(x, y, z) is { } body) hurtUnits.Add((body, hit));
            if (type == DamageType.Stun) continue;
            HurtTerrain(b, Part.Object, new Cell(x, y, z), hit);
            HurtTerrain(b, Part.West, new Cell(x, y, z), hit);
            HurtTerrain(b, Part.North, new Cell(x, y, z), hit);
            if (z > 0) HurtTerrain(b, Part.Floor, new Cell(x, y, z), hit);
            else if (hit > 30 && b.Floor[x, y, 0] is Piece.Grass or Piece.Furrows or Piece.Snow or Piece.Sand) b.Floor[x, y, 0] = Piece.Scorched;
        }
        foreach (var (u, hit) in hurtUnits)
            if (!u.Dead) Damage(b, u, hit, type, source, -1, blast: true);
        Fall(b);
        Look(b);
        CheckEnd(b);
    }

    /// <summary>Damage to a piece of scenery: what can't stand it is destroyed (leaving its ruin), and what blows up does.</summary>
    public static void HurtTerrain(Battle b, Part part, Cell c, int damage)
    {
        var piece = b.Get(part, c.X, c.Y, c.Z);
        if (piece == Piece.None || damage <= 0) return;
        var info = Pieces.Of(piece);
        if (info.Armour >= 255) return;
        byte[,,] hurt = part switch { Part.Floor => b.FloorHurt, Part.West => b.WestHurt, Part.North => b.NorthHurt, _ => b.ObjHurt };
        int total = hurt[c.X, c.Y, c.Z] + damage / 3;
        if (damage < info.Armour && total < info.Armour)
        {
            hurt[c.X, c.Y, c.Z] = (byte)Math.Min(254, total);
            return;
        }
        b.Set(part, c.X, c.Y, c.Z, info.Ruin);
        if (piece == Piece.Brain) { b.BrainDestroyed = true; b.News.Add("THE HIVE MIND IS DESTROYED"); }
        if (info.Explodes > 0) Explode(b, c, info.Explodes, info.Explodes >= 100 ? 5 : 3, DamageType.Explosive, null);
    }

    /// <summary>Units (and things) left with no floor under them fall to the next one down, and are hurt by the fall.</summary>
    public static void Fall(Battle b)
    {
        foreach (var u in b.Units.Where(u => !u.Dead && u.Z > 0 && !(u.Flies && u.Active)).ToList())
        {
            int z = u.Z;
            while (z > 0 && b.Floor[u.X, u.Y, z] == Piece.None && b.Obj[u.X, u.Y, z - 1] is not (Piece.Stairs or Piece.Lift)) z--;
            if (z == u.Z) continue;
            int levels = u.Z - z;
            u.Z = z;
            if (b.UnitAt(u.X, u.Y, z) is { } under && under != u) Damage(b, under, 20, DamageType.Kinetic, null, -1, blast: true);
            Damage(b, u, 10 * levels, DamageType.Kinetic, null, -1, blast: true);
        }
        foreach (var (cell, items) in b.Ground.ToList())
        {
            if (items.Count == 0 || cell.Z == 0) continue;
            var to = b.FloorBelow(cell);
            if (to == cell) continue;
            b.ItemsAt(to).AddRange(items);
            items.Clear();
        }
    }

    // ───────────────────────────── wounds and death ─────────────────────────────

    /// <summary>Which armour faces an attack from a direction (-1: from below, as a blast's).</summary>
    public static int ArmourFacing(BUnit u, int fromDir)
    {
        if (fromDir < 0) return u.Under;
        int d = Math.Abs(fromDir - u.Facing) % 8;
        d = Math.Min(d, 8 - d);
        return d <= 1 ? u.Front : d >= 3 ? u.Rear : u.SideArmour;
    }

    /// <summary>
    /// A hit on a unit: between half and one and a half times the weapon's power, less the armour it meets. What gets
    /// through costs health (and morale), and a heavy hit opens fatal wounds that bleed every turn. Stun damage only
    /// knocks out. A unit with no health left dies; one stunned past its health falls unconscious.
    /// </summary>
    public static void Damage(Battle b, BUnit u, int power, DamageType type, BUnit? by, int fromDir, bool blast = false, bool melee = false)
    {
        if (u.Dead) return;
        int dmg = (int)Math.Round(power * b.Rng.Range(0.5, 1.5));
        int armour = ArmourFacing(u, blast ? -1 : fromDir);
        if (type == DamageType.Stun)
        {
            if (u.Race is Race.Warden or Race.Husk && u.Side == Side.Alien) return;
            u.Stun += Math.Max(0, dmg - armour / 2);
            b.Effects.Add(new Fx { Kind = FxKind.Smoke, X = u.X + 0.5, Y = u.Y + 0.5, Z = u.Z + 0.5, Life = 0.6 });
            if (u.Stun >= u.Hp && !u.Out) KnockOut(b, u);
            return;
        }
        int dealt = Math.Max(0, dmg - armour);
        if (dealt == 0)
        {
            b.Effects.Add(new Fx { Kind = FxKind.Words, X = u.X + 0.5, Y = u.Y + 0.5, Z = u.Z + 1, Text = "NO EFFECT", Life = 0.8, Colour = 0xFFB0B0B0 });
            return;
        }
        u.Hp -= dealt;
        u.DamageTaken += dealt;
        b.Effects.Add(new Fx { Kind = u.Race == Race.Warden && u.Side == Side.Alien ? FxKind.Hit : FxKind.Blood, X = u.X + 0.5, Y = u.Y + 0.5, Z = u.Z + 0.5, Life = 0.5, Colour = u.IsHuman ? 0xFFC81E1E : 0xFF5AC83A });
        b.Effects.Add(new Fx { Kind = FxKind.Words, X = u.X + 0.5, Y = u.Y + 0.5, Z = u.Z + 1, Text = $"-{dealt}", Life = 0.9, Colour = 0xFFFF6A5A });
        if (dealt >= 10 && !(u.Side == Side.Alien && u.Race == Race.Warden))
            u.Wounds[b.Rng.Next(6)] += Math.Min(3, 1 + dealt / 25);
        u.Morale = Math.Max(0, u.Morale - dealt * Math.Max(0, 110 - u.Bravery) / 100);
        if (u.Hp <= 0)
        {
            Kill(b, u, by, type, null, melee);
            return;
        }
        if (u.IsHuman) b.Heard.Add(Sfx.SoldierHurt);
    }

    private static void KnockOut(Battle b, BUnit u)
    {
        u.Out = true;
        u.Kneeling = false;
        DropAll(b, u);
        b.Say(u.IsSoldier ? $"{u.Name} IS KNOCKED OUT" : $"{u.Title} IS KNOCKED OUT");
        if (u.Side == Side.Alien) b.News.Add($"{u.Title} STUNNED");
        Look(b);
        CheckEnd(b);
    }

    private static void DropAll(Battle b, BUnit u)
    {
        var at = new Cell(u.X, u.Y, u.Z);
        foreach (var i in u.Carried.ToList()) b.Drop(i, at);
        u.Right = u.Left = null;
        u.Belt.Clear();
        u.Backpack.Clear();
    }

    /// <summary>
    /// A death: everything carried falls, the body is left (aliens' corpses can be recovered), the killer is credited, and
    /// the dead unit's side loses heart. A Husker's victim rises as a Husk; a Husk that falls frees a new Husker; a
    /// Warden bursts.
    /// </summary>
    public static void Kill(Battle b, BUnit u, BUnit? by, DamageType type, string? why = null, bool melee = false)
    {
        if (u.Dead) return;
        u.Dead = true;
        u.Out = false;
        u.Hp = Math.Min(u.Hp, 0);
        var at = new Cell(u.X, u.Y, u.Z);
        DropAll(b, u);
        if (by is not null && Hostile(by, u)) by.Kills++;
        b.Effects.Add(new Fx { Kind = FxKind.Death, X = u.X + 0.5, Y = u.Y + 0.5, Z = u.Z + 0.5, Life = 1.0, Colour = u.IsHuman ? 0xFFC81E1E : 0xFF5AC83A });
        bool husked = melee && by is { Side: Side.Alien, Race: Race.Husker } && u.IsHuman;
        switch (u.Side)
        {
            case Side.Player:
                b.Heard.Add(Sfx.SoldierDeath);
                b.News.Add(why ?? $"{u.Name} HAS BEEN KILLED");
                break;
            case Side.Civilian:
                b.Heard.Add(Sfx.SoldierDeath);
                if (by is null || by.Side == Side.Alien || by.Team == Side.Alien) b.CiviliansKilled++;
                break;
            default:
                b.Heard.Add(Sfx.AlienDeath);
                b.AliensKilled.Add((u.Race, u.AlienRank));
                b.Say($"{u.Title} KILLED");
                if (u.Race != Race.Husk || !b.Rng.Chance(1)) b.Drop(Item.Make(u.RaceInfo.Corpse, false), at);
                break;
        }
        // Morale: the dead unit's side loses heart, less under a high-ranking officer.
        int leader = u.Side == Side.Player ? b.Units.Where(o => o.Active && o.IsSoldier).Select(o => (int)o.Soldier!.Rank).DefaultIfEmpty(0).Max() : 2;
        foreach (var o in b.Units.Where(o => o.Active && o.Side == u.Side && o != u))
            o.Morale = Math.Max(0, o.Morale - Math.Max(2, 10 + (110 - o.Bravery) / 5 - leader * 2));
        if (by is not null)
            foreach (var o in b.Units.Where(o => o.Active && o.Side == by.Side)) o.Morale = Math.Min(100, o.Morale + 4);
        if (husked)
        {
            var husk = MakeAlien(b, Race.Husk, AlienRank.Terror, b.Mission.Month);
            husk.Tu = 0;
            Place(b, husk, at);
            b.News.Add($"{(u.IsSoldier ? u.Name : "A CIVILIAN")} RISES AS A HUSK");
        }
        else if (u.Side == Side.Alien && u.Race == Race.Husk)
        {
            var husker = MakeAlien(b, Race.Husker, AlienRank.Terror, b.Mission.Month);
            husker.Tu = 0;
            Place(b, husker, at);
            b.News.Add("A HUSKER BURSTS FROM THE HUSK");
        }
        else if (u.Side == Side.Alien && u.Race == Race.Warden)
            Explode(b, at, 70, 3, DamageType.Explosive, null);
        Look(b);
        CheckEnd(b);
    }

    // ───────────────────────────── reaction fire ─────────────────────────────

    /// <summary>
    /// The enemy's chance to interrupt: every enemy that sees a unit act, with a loaded weapon and the time units for a
    /// snap shot, fires if its reactions (scaled by its time units left) beat the actor's.
    /// </summary>
    public static void React(Battle b, BUnit actor)
    {
        if (!actor.Active) return;
        double mine = actor.Reactions * (double)actor.Tu / Math.Max(1, actor.MaxTu);
        foreach (var w in b.Units.Where(w => w.Active && w.Panic == Panic.None && Hostile(w, actor) && w.Team != b.Active).ToList())
        {
            var gun = w.Weapon;
            if (gun is null || !gun.Loaded) continue;
            int cost = ShotCost(w, gun, FireMode.Snap);
            if (cost < 0 || w.Tu < cost) continue;
            double theirs = w.Reactions * (double)w.Tu / Math.Max(1, w.MaxTu);
            if (theirs <= mine) continue;
            if (!Sees(b, w, actor)) continue;
            w.Tu -= cost;
            w.ReactionShots++;
            b.Heard.Add(Sfx.ReactionFire);
            b.News.Add(w.Side == Side.Player ? "REACTION FIRE!" : "INCOMING FIRE!");
            b.Burst.Add(new Shot(w, new Cell(actor.X, actor.Y, actor.Z), 0.15, FireMode.Snap, true));
            mine = actor.Reactions * (double)actor.Tu / Math.Max(1, actor.MaxTu);
        }
    }

}
