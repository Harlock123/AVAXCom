namespace AVAXCom.Engine;

/// <summary>
/// The player's side played by the computer, for the demonstration, the tests and the probe. On the ground it plays a
/// careful squad: bandage the bleeding, reload, shoot what it sees (aimed when there's time), grenade a huddle, and
/// otherwise advance a few tiles at a time toward the aliens it knows of (or the UFO), keeping time back for reaction
/// fire. On the geoscape it runs the war: hires, researches the useful topics first, builds, makes lasers, sends
/// interceptors after UFOs and the transport to whatever can be fought.
/// </summary>
public static class Pilot
{
    // ───────────────────────────── the battlescape ─────────────────────────────

    /// <summary>One action for the next soldier who has one, or the end of the turn. False when the turn has ended.</summary>
    public static bool Act(Battle b)
    {
        if (b.Active != Side.Player || b.Busy || b.Outcome is not null) return false;
        foreach (var u in b.Units.Where(u => u.Active && u.Team == Side.Player && u.Panic == Panic.None && !b.Finished.Contains(u.Id)).ToList())
        {
            b.Actions[u.Id] = b.Actions.GetValueOrDefault(u.Id) + 1;
            if (b.Actions[u.Id] <= 8 && Soldier(b, u)) return true;
            b.Finished.Add(u.Id);
        }
        b.Finished.Clear();
        b.Actions.Clear();
        Tac.EndTurn(b);
        return false;
    }

    /// <summary>Plays the player's turn through (and the aliens' after it), for tests and the probe.</summary>
    public static void PlayRound(Battle b)
    {
        for (int i = 0; i < 400 && b.Active == Side.Player && b.Outcome is null; i++)
        {
            Act(b);
            Tac.Settle(b);
        }
        Tac.RunAliens(b);
    }

    private static int Dist(BUnit a, int x, int y) => Math.Max(Math.Abs(a.X - x), Math.Abs(a.Y - y));

    private static bool Soldier(Battle b, BUnit u)
    {
        // Bleeding: treat it.
        if (u.FatalWounds > 0 && FindKit(u) is { } kit)
        {
            if (u.Right is not null && u.Right != kit && u.Left is not null) { }
            else
            {
                if (u.Right != kit && u.Left != kit) Tac.MoveItem(b, u, kit, u.Backpack.Contains(kit) ? Slot.Backpack : Slot.Belt, u.Left is null ? Slot.LeftHand : Slot.RightHand);
                if (Tac.UseKit(b, u, u, KitUse.Heal) is null) return true;
            }
        }
        var gun = u.Weapon;
        if (gun is { Loaded: false } && Tac.Reload(b, u) is null) return true;
        var seen = Tac.EnemiesSeenBy(b, u).ToList();
        if (seen.Count > 0 && gun is { Loaded: true })
        {
            // A grenade at a huddle.
            var grenade = u.Belt.FirstOrDefault(i => i.Kind is ItemKind.FragGrenade or ItemKind.PulseGrenade);
            if (grenade is not null && u.Left is null)
            {
                var huddle = seen.Select(t => (t, n: seen.Count(o => Math.Abs(o.X - t.X) <= 1 && Math.Abs(o.Y - t.Y) <= 1))).OrderByDescending(x => x.n).First();
                int d = Dist(u, huddle.t.X, huddle.t.Y);
                if (huddle.n >= 2 && d >= 4 && d <= Tac.ThrowRange(u, grenade) && u.Tu >= Tac.PrimeCost + Tac.Percent(u, Tac.ThrowPercent) + 4)
                {
                    Tac.MoveItem(b, u, grenade, Slot.Belt, Slot.LeftHand);
                    if (Tac.Prime(b, u, grenade, 0) is null && Tac.Throw(b, u, grenade, new Cell(huddle.t.X, huddle.t.Y, huddle.t.Z)) is null) return true;
                }
            }
            var target = seen.OrderByDescending(t => Tac.HitChance(u, gun, FireMode.Aimed, new Cell(t.X, t.Y, t.Z)) - t.Hp / 4).First();
            var at = new Cell(target.X, target.Y, target.Z);
            int aimed = Tac.ShotCost(u, gun, FireMode.Aimed), snap = Tac.ShotCost(u, gun, FireMode.Snap), auto = Tac.ShotCost(u, gun, FireMode.Auto);
            int dist = Dist(u, at.X, at.Y);
            if (!u.Kneeling && u.Tu >= Tac.KneelCost + aimed && dist > 6 && b.Turn > 1) Tac.Kneel(b, u);
            FireMode? mode = dist <= 4 && auto > 0 && u.Tu >= auto ? FireMode.Auto
                : aimed > 0 && u.Tu >= aimed ? FireMode.Aimed
                : snap > 0 && u.Tu >= snap ? FireMode.Snap : null;
            if (mode is { } m && Tac.Shoot(b, u, at, m) is null) return true;
            return false;
        }
        // At night, one flare a turn thrown ahead toward the goal.
        if (b.Night && b.Actions.GetValueOrDefault(-2) == 0 && u.Belt.FirstOrDefault(i => i.Kind == ItemKind.Flare) is { } flare && u.Left is null && Goal(b, u) is { } fg)
        {
            double fd = Math.Sqrt(Math.Pow(fg.X - u.X, 2) + Math.Pow(fg.Y - u.Y, 2));
            int range = Math.Min(Tac.ThrowRange(u, flare), 9);
            var at = fd <= range ? fg : new Cell(u.X + (int)Math.Round((fg.X - u.X) * range / fd), u.Y + (int)Math.Round((fg.Y - u.Y) * range / fd), u.Z);
            if (b.In(at) && u.Tu >= Tac.Percent(u, Tac.ThrowPercent) + 10)
            {
                b.Actions[-2] = 1;
                Tac.MoveItem(b, u, flare, Slot.Belt, Slot.LeftHand);
                if (Tac.Throw(b, u, flare, at) is null) return true;
            }
        }
        // Nothing in sight: advance. Keep time back for a snap shot.
        int reserve = gun is null ? 0 : Math.Max(0, Tac.ShotCost(u, gun, FireMode.Snap));
        var goal = Goal(b, u);
        if (goal is { } g && !(u.Kneeling && u.Tu < Tac.StandCost + 8) && Advance(b, u, g, reserve)) return true;
        // Done: kneel, if there's time to and still keep a snap shot.
        if (!u.Kneeling && u.Tu >= Tac.KneelCost + reserve && b.Turn > 1 && !b.Exit.Contains((u.X, u.Y)) && Tac.Kneel(b, u) is null) return true;
        return false;
    }

    private static Item? FindKit(BUnit u) => u.Carried.FirstOrDefault(i => i.Kind == ItemKind.FieldKit && i.Heals > 0);

    /// <summary>Where a soldier heads: the nearest alien anyone has seen, else the mission's heart.</summary>
    private static Cell? Goal(Battle b, BUnit u)
    {
        var spotted = b.Units.Where(t => t.Active && t.Team == Side.Alien && (t.Seen || b.Turn > 6)).OrderBy(t => Dist(u, t.X, t.Y)).FirstOrDefault();
        if (spotted is not null) return new Cell(spotted.X, spotted.Y, spotted.Z);
        return b.Objective;
    }

    private static readonly Dictionary<(Battle, Cell, int), Dictionary<Cell, int>> Fields = [];

    /// <summary>
    /// A bound forward: of the tiles the soldier can reach this turn (keeping time back), the one that gets nearest the
    /// goal by the walking distance, favouring tiles with cover beside them — and, once the aliens have been seen, not
    /// too far at a time.
    /// </summary>
    private static bool Advance(Battle b, BUnit u, Cell goal, int reserve)
    {
        var start = new Cell(u.X, u.Y, u.Z);
        if (!Fields.TryGetValue((b, goal, b.Turn), out var field))
        {
            if (Fields.Count > 64) Fields.Clear();
            Fields[(b, goal, b.Turn)] = field = PathFinder.Field(b, u, goal);
        }
        bool contact = b.Units.Any(t => t.Team == Side.Alien && (t.Seen || t.Dead));
        int spend = Math.Min(u.Tu - reserve - (u.Kneeling ? Tac.StandCost : 0), contact ? u.MaxTu / 2 : u.MaxTu);
        if (spend < 4) return false;
        var reach = PathFinder.Reach(b, u, spend);
        int here = field.GetValueOrDefault(start, 9999);
        Cell best = start;
        double bestScore = 0;
        foreach (var (c, cost) in reach)
        {
            if (c == start || b.UnitAt(c) is not null || !field.TryGetValue(c, out int left)) continue;
            double score = (here - left) - Cover(b, c) * (contact ? -3 : -1) - cost * 0.05;
            if (left == 0 && b.UnitAt(goal) is not null) continue;
            if (score > bestScore) { bestScore = score; best = c; }
        }
        if (best == start || bestScore < 2) return false;
        if (Tac.Walk(b, u, best) is not null) return false;
        return true;
    }

    /// <summary>How much cover a tile has: walls on its edges and solid things beside it.</summary>
    private static int Cover(Battle b, Cell c)
    {
        int n = 0;
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            var (piece, _, _) = Tac.EdgeWall(b, c.X, c.Y, c.Z, dx, dy);
            if (piece != Piece.None && Pieces.Of(piece).Height >= 0.5) n++;
            else if (b.In(c.X + dx, c.Y + dy, c.Z) && Pieces.Of(b.Obj[c.X + dx, c.Y + dy, c.Z]) is { BlocksMove: true, Height: >= 0.4 }) n++;
        }
        return n;
    }

    /// <summary>Where the camera should look during the demonstration: the walker, a shot, a blast, else the squad.</summary>
    public static (double X, double Y, double Z) Watch(Battle b)
    {
        if (b.Walker is { } w && (w.Team == Side.Player || w.Seen)) return (w.X, w.Y, w.Z);
        if (b.Effects.FirstOrDefault(e => e.Kind == FxKind.Explosion) is { } e) return (e.X, e.Y, e.Z);
        if (b.Flying.FirstOrDefault() is { } p) return ((p.X0 + p.X1) / 2, (p.Y0 + p.Y1) / 2, p.Z0);
        var squad = b.Units.Where(u => u.Active && u.Team == Side.Player).ToList();
        if (squad.Count == 0) return (b.W / 2.0, b.H / 2.0, 0);
        return (squad.Average(u => u.X), squad.Average(u => u.Y), 0);
    }

    // ───────────────────────────── the geoscape ─────────────────────────────

    /// <summary>The order the pilot researches in: weapons and armour first, then what leads to the end.</summary>
    private static readonly Topic[] Priorities =
    [
        Topic.LaserWeapons, Topic.LaserPistol, Topic.LaserRifle, Topic.PallidAutopsy, Topic.Hullmetal, Topic.CarapaceArmour,
        Topic.Vorium, Topic.StormRifle, Topic.StormPistol, Topic.PallidInterrogation, Topic.SkimmerInterrogation, Topic.CoilbackInterrogation,
        Topic.BrakkInterrogation, Topic.VeiledInterrogation, Topic.AlienOrigins, Topic.NavLattice, Topic.DriveCore, Topic.GravDrive,
        Topic.TheHomeworld, Topic.Starfall, Topic.LaserLance, Topic.LaserCannon, Topic.StunBomb, Topic.Psionics,
    ];

    /// <summary>A look over the war: staff, research, building, the workshops, and craft sent where they're needed.</summary>
    public static void Manage(Campaign c)
    {
        if (c.Bases.Count == 0) c.FoundFirstBase(Earth.Cities.First(x => x.Name == "MUNICH").At);
        foreach (var b in c.Bases)
        {
            // Staff, while there's money and room.
            // Room for a squad of fourteen comes first; scientists fill what's left.
            if (c.Money > 2_000_000 && b.Scientists + b.Arriving.Sum(a => a.Scientists) < Math.Min(b.LabSpace, b.QuartersSpace - 30)) c.Hire(b, Campaign.Staff.Scientist, 5);
            if (c.Money > 1_500_000 && c.SoldiersAt(b).Count() + b.Arriving.Count(a => a.Soldier is not null) < 14) c.Hire(b, Campaign.Staff.Soldier, 2);
            // The stores kept stocked with weapons and kit for a full squad.
            int Have(ItemKind k) => b.Count(k) + b.Arriving.Where(a => a.Item == k).Sum(a => a.Count);
            foreach (var (k, want) in new[] { (ItemKind.Rifle, 14), (ItemKind.RifleClip, 40), (ItemKind.FragGrenade, 14), (ItemKind.Flare, 12), (ItemKind.FieldKit, 5) })
            {
                int guns = k == ItemKind.Rifle ? b.Count(ItemKind.LaserRifle) : 0;
                if (Have(k) + guns < want && c.Money > 600_000) c.Buy(b, k, want - Have(k) - guns);
            }
            // Craft lost are replaced: a transport first, then interceptors.
            bool arriving = b.Arriving.Any(a => a.Craft is not null);
            if (!arriving && !c.CraftsAt(b).Any(k => k.Info.Seats > 0) && c.Money > 900_000) c.BuyCraft(b, CraftType.Raven);
            else if (!arriving && c.CraftsAt(b).Count(k => k.Guns.Count > 0) < 2 && c.Money > 1_200_000) c.BuyCraft(b, CraftType.Falcon);
            // Laser cannon fitted to the interceptors as they're made.
            foreach (var k in c.CraftsAt(b).Where(k => k.Guns.Count > 1 && !k.Flying))
                if (k.Guns[1].Kind == ItemKind.AutoCannon && b.Count(ItemKind.LaserCannon) > 0) c.Fit(k, 1, ItemKind.LaserCannon);
            // Build what's missing: containment, a second lab, quarters.
            foreach (var want in new[] { FacilityType.Quarters, FacilityType.Containment, FacilityType.Lab, FacilityType.MissileDefence, FacilityType.Workshop })
            {
                bool more = want switch
                {
                    FacilityType.Quarters => c.PeopleAt(b) > b.Facilities.Count(f => f.Type == want) * 50 - 6,
                    FacilityType.Lab => b.Facilities.Count(f => f.Type == want) < 2,
                    _ => !b.Facilities.Any(f => f.Type == want),
                };
                if (!more) continue;
                if (c.Money < Rules.Of(want).Cost * 1000 + 1_500_000) break;
                var spot = Enumerable.Range(0, 36).Select(i => (X: i % 6, Y: i / 6)).FirstOrDefault(p => b.CanBuild(want, p.X, p.Y) is null, (X: -1, Y: -1));
                if (spot.X >= 0) c.Build(b, want, spot.X, spot.Y);
                break;
            }
            // Research.
            foreach (var t in Priorities.Concat(Enum.GetValues<Topic>()))
            {
                if (b.FreeScientists <= 0) break;
                if (c.CanResearch(b, t)) c.StartResearch(b, t, b.FreeScientists);
            }
            foreach (var r in b.Research) if (b.FreeScientists > 0) c.Assign(b, r, b.FreeScientists);
            // The workshops: laser rifles to sell and to carry.
            if (b.Production.Count == 0 && b.FreeEngineers > 0 && c.Money > 1_000_000)
            {
                string? job = c.Researched.Contains(Topic.LaserRifle) ? "LASER RIFLE" : null;
                if (c.Researched.Contains(Topic.LaserCannon) && b.Count(ItemKind.LaserCannon) + c.Crafts.Count(k => k.Guns.Any(g => g.Kind == ItemKind.LaserCannon)) < 2) job = "LASER CANNON";
                if (c.Researched.Contains(Topic.Starfall) && c.Crafts.All(k => k.Type != CraftType.Starfall) && b.Count(ItemKind.DriveCore) >= 2) job = "STARFALL";
                if (job is not null) c.StartProduction(b, job, 5, b.FreeEngineers);
            }
            // Sell alien junk the labs are done with.
            foreach (var k in b.Stores.Keys.ToList())
            {
                var info = Rules.Of(k);
                if (info.Class == ItemClass.Corpse && Rules.Topics.Where(t => t.Item == k).All(t => c.Researched.Contains(t.Topic)) && b.Count(k) > 0) c.Sell(b, k, b.Count(k));
                if (k is ItemKind.LaserRifle && b.Count(k) > 14) c.Sell(b, k, b.Count(k) - 14);
            }
        }
        // Craft: interceptors after UFOs in the air, the transport to whatever can be fought.
        foreach (var u in c.Ufos.Where(u => u.Detected))
        {
            // Only what the interceptors can take on: small UFOs early, anything once they carry lasers.
            bool armed = c.Crafts.Any(k => k.Guns.Any(g => g.Kind is ItemKind.LaserCannon or ItemKind.StormBeam));
            if (u.State == UfoState.Flying && !c.Crafts.Any(k => k.TargetUfo == u.Id) && (u.Type <= UfoType.Raider || armed && u.Type <= UfoType.Carrier))
            {
                var fighter = c.Crafts.Where(k => k.Guns.Count > 0 && k.CanLaunch).OrderBy(k => Geo.Nm(k.At, u.At)).FirstOrDefault();
                if (fighter is not null && Geo.Nm(fighter.At, u.At) < fighter.Info.FuelHours * fighter.Info.Speed * 0.4) c.Launch(fighter, u);
            }
            if (u.State == UfoState.Landed && !c.Crafts.Any(k => k.TargetUfo == u.Id && k.Info.Seats > 0)) SendTroops(c, ufo: u);
        }
        foreach (var s in c.Sites.Where(s => s.Known && s.Kind != SiteKind.AlienBase || s.Kind == SiteKind.AlienBase && s.Known && c.MonthIndex >= 3))
            if (!c.Crafts.Any(k => k.TargetSite == s.Id)) SendTroops(c, site: s);
    }

    private static void SendTroops(Campaign c, Ufo? ufo = null, Site? site = null)
    {
        var where = ufo?.At ?? site!.At;
        // Crash sites and alien bases can wait for daylight; terror sites and landed UFOs can't.
        if (site is { Kind: SiteKind.CrashSite or SiteKind.AlienBase } && Geo.IsNight(where, c.Now)) return;
        bool Manned(Craft k) => c.CrewOf(k).Any(s => s.Fit) || c.Soldiers.Any(s => s.BaseId == k.BaseId && s.Fit && s.CraftId < 0);
        var craft = c.Crafts.Where(k => k.Info.Seats > 0 && k.CanLaunch && Manned(k) && (site?.Kind != SiteKind.Homeworld || k.Type == CraftType.Starfall))
            .OrderBy(k => Geo.Nm(k.At, where)).FirstOrDefault();
        if (craft is null) return;
        if (Geo.Nm(craft.At, where) > craft.Info.FuelHours * craft.Info.Speed * 0.45) return;
        // Fill the seats with fit soldiers first.
        foreach (var s in c.SoldiersAt(c.BaseById(craft.BaseId)!).Where(s => s.Fit && s.CraftId < 0).Take(craft.Info.Seats - c.CrewOf(craft).Count())) s.CraftId = craft.Id;
        c.Launch(craft, ufo, site);
    }
}
