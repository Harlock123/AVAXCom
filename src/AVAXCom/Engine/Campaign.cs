namespace AVAXCom.Engine;

public enum UfoMission { Scout, Research, Abduction, Harvest, Terror, BaseBuild, Supply, Retaliation }
public enum UfoState { Flying, Landed, Gone }

public sealed class Ufo
{
    public int Id;
    public UfoType Type;
    public Race Race;
    public UfoMission Mission;
    public GeoPoint At, Dest;
    public UfoState State;
    /// <summary>Hours left on the ground (landed), and the legs of its route still to fly.</summary>
    public double Hours;
    public int Legs;
    public double Damage;
    /// <summary>On a radar now; ever seen (so it keeps its name).</summary>
    public bool Detected, Known;
    public int TargetBase = -1;
    public double Heading;
    public UfoInfo Info => Rules.Of(Type);
    public string Name => $"UFO-{Id}";
    public double CruiseSpeed => Info.Speed * (Mission == UfoMission.Retaliation ? 0.6 : 0.42);
}

public enum SiteKind { CrashSite, Terror, AlienBase, Homeworld }

public sealed class Site
{
    public int Id;
    public SiteKind Kind;
    public GeoPoint At;
    public UfoType Ufo;
    public Race Race;
    /// <summary>Hours before it's gone (a terror site's aliens leave; a crash site is cleared by the locals).</summary>
    public double Hours;
    public bool Known;
    public double Damage;
    public string Name = "";
}

public sealed class NationState
{
    public int Funding, Satisfaction = 60;
    public bool Withdrawn;
    /// <summary>This month's points: the organisation's in the nation, and the aliens'.</summary>
    public int Ours, Theirs;
    public int LastChange;
}

public sealed record MonthRecord(int Year, int Month, int Ours, int Theirs, int Funding, int Money, string Rating)
{
    public int Score => Ours - Theirs;
}

public enum EventKind
{
    UfoDetected, UfoLost, Intercept, UfoDowned, UfoDestroyed, CraftArrived, CraftReturned, CraftLost, TargetLost, LowFuel,
    TerrorSite, AlienBaseFound, ResearchDone, ProductionDone, FacilityDone, Arrived, MonthlyReport, BaseAttack, BaseDefended, Note,
}

/// <summary>Something for the player's attention on the geoscape (and why time stopped).</summary>
public sealed record GeoEvent(EventKind Kind, string Title, string Text, int Ufo = -1, int Site = -1, int Craft = -1, int Base = -1, bool Interrupt = true);

public enum MissionKind { CrashSite, LandedUfo, Terror, AlienBase, BaseDefence, Homeworld }
public enum Terrain { Farmland, Urban, Forest, Desert, Arctic, AlienBase, Hive, BaseInterior }

/// <summary>A ground mission about to be fought: where, against what, and with whom.</summary>
public sealed class Mission
{
    public MissionKind Kind;
    public Terrain Terrain;
    public bool Night;
    public UfoType? Ufo;
    public Race Race;
    public int Crew, Month, Seed;
    public double Damage;
    public int CraftId = -1, BaseId = -1, SiteId = -1, UfoId = -1;
    public GeoPoint At;
    public string Name = "";
    public List<Soldier> Squad = [];
    /// <summary>The base's layout, for a base's defence.</summary>
    public Base? Base;
}

/// <summary>
/// The war on the geoscape: the date and the money, the funding nations and their opinion, the bases with their staff,
/// stores, laboratories and workshops, the craft, the soldiers and the fallen, and the aliens' UFOs, crash sites, terror
/// sites and bases. <see cref="Advance"/> runs the clock: UFOs fly their missions and land, radars find them, craft
/// chase them, research and manufacture go on, staff arrive, and at each month's end the council reports.
/// </summary>
public sealed class Campaign
{
    public const int StartMoney = 5_000_000;
    /// <summary>The longest step the geoscape takes at once (seconds), and how often radars sweep.</summary>
    public const double MaxStep = 120, SweepSeconds = 600;
    public const double CraftRadarNm = 600, InterceptNm = 20, ArriveNm = 12;

    public int Seed;
    public Dice Rng;
    public double Seconds;
    public int Money = StartMoney;
    public int Score;
    public int MonthOurs, MonthTheirs;
    public NationState[] Nations;
    public List<Base> Bases = [];
    public List<Craft> Crafts = [];
    public List<Soldier> Soldiers = [];
    public List<Fallen> Memorial = [];
    public List<Ufo> Ufos = [];
    public List<Site> Sites = [];
    public HashSet<Topic> Researched = [];
    public List<MonthRecord> History = [];
    public List<GeoEvent> Events = [];
    public int NextId = 1;
    public double NextUfoHours = 20;
    public int TerrorsThisMonth, BasesThisMonth;
    /// <summary>How much the aliens want revenge (shoot-downs and raids raise it).</summary>
    public double Heat;
    public int TerribleMonths, DebtMonths;
    public bool Over, Won;
    public string Ending = "";
    /// <summary>The clock's part-hours and part-sweeps, and the month last reported (kept in saves).</summary>
    public double HourAcc, SweepAcc;
    public int LastMonth = -1;
    private bool _interrupt;

    public Campaign(int seed)
    {
        Seed = seed;
        Rng = new Dice(seed);
        Nations = Earth.Nations.Select(n => new NationState { Funding = n.Funding }).ToArray();
    }

    public DateTime Now => Geo.Start.AddSeconds(Seconds);
    /// <summary>Months since the war began (0 in the first).</summary>
    public int MonthIndex => (Now.Year - Geo.Start.Year) * 12 + Now.Month - Geo.Start.Month;
    public int Funding => Nations.Where(n => !n.Withdrawn).Sum(n => n.Funding) * 1000;
    public bool HasBase => Bases.Count > 0;

    public Base? BaseById(int id) => Bases.FirstOrDefault(b => b.Id == id);
    public Craft? CraftById(int id) => Crafts.FirstOrDefault(c => c.Id == id);
    public Ufo? UfoById(int id) => Ufos.FirstOrDefault(u => u.Id == id && u.State != UfoState.Gone);
    public Site? SiteById(int id) => Sites.FirstOrDefault(s => s.Id == id);
    public IEnumerable<Soldier> SoldiersAt(Base b) => Soldiers.Where(s => s.BaseId == b.Id);
    public IEnumerable<Craft> CraftsAt(Base b) => Crafts.Where(c => c.BaseId == b.Id && c.State != CraftState.Lost);
    public IEnumerable<Soldier> CrewOf(Craft c) => Soldiers.Where(s => s.CraftId == c.Id);

    private void Event(GeoEvent e)
    {
        Events.Add(e);
        if (e.Interrupt) _interrupt = true;
    }

    public void Ours(int points, GeoPoint at)
    {
        MonthOurs += points;
        int n = Earth.NationAt(at);
        if (n >= 0) Nations[n].Ours += points;
    }

    public void Theirs(int points, GeoPoint at)
    {
        MonthTheirs += points;
        int n = Earth.NationAt(at);
        if (n >= 0) Nations[n].Theirs += points;
    }

    // ───────────────────────────── the first base ─────────────────────────────

    /// <summary>
    /// The first base, at a place the player chose, laid out as the original's was: three hangars along the top, the
    /// lift in the middle, quarters, a laboratory, a workshop, stores and a small radar round it. With it come eight
    /// soldiers, ten scientists, ten engineers, two interceptors, a transport and the stores to equip them.
    /// </summary>
    public Base FoundFirstBase(GeoPoint at, string? name = null)
    {
        var b = new Base { Id = NextId++, Name = name ?? BaseName(at), At = at };
        b.Put(FacilityType.Hangar, 0, 0);
        b.Put(FacilityType.Hangar, 2, 0);
        b.Put(FacilityType.Hangar, 4, 0);
        b.Put(FacilityType.Lift, 2, 2);
        b.Put(FacilityType.Quarters, 1, 2);
        b.Put(FacilityType.Lab, 3, 2);
        b.Put(FacilityType.Workshop, 4, 2);
        b.Put(FacilityType.Stores, 2, 3);
        b.Put(FacilityType.SmallRadar, 1, 3);
        b.Scientists = 10;
        b.Engineers = 10;
        foreach (var (k, n) in new[]
                 {
                     (ItemKind.Rifle, 10), (ItemKind.RifleClip, 40), (ItemKind.Pistol, 6), (ItemKind.PistolClip, 20), (ItemKind.Cannon, 2),
                     (ItemKind.CannonShells, 10), (ItemKind.Launcher, 1), (ItemKind.Rocket, 4), (ItemKind.FragGrenade, 20), (ItemKind.Flare, 12),
                     (ItemKind.FieldKit, 3), (ItemKind.ShockBaton, 3), (ItemKind.LancerMissile, 12), (ItemKind.AutoCannonShells, 4),
                 })
            b.Add(k, n);
        Bases.Add(b);
        AddCraft(b, CraftType.Raven);
        for (int i = 0; i < 2; i++)
        {
            var f = AddCraft(b, CraftType.Falcon);
            f.Guns.Add(new CraftGun { Kind = ItemKind.LancerLauncher, Rounds = 6 });
            f.Guns.Add(new CraftGun { Kind = ItemKind.AutoCannon, Rounds = 120 });
        }
        var raven = Crafts.First(c => c.Type == CraftType.Raven && c.BaseId == b.Id);
        for (int i = 0; i < 8; i++)
        {
            var s = Soldier.Recruit(ref Rng, NextId++, b.Id);
            s.CraftId = raven.Id;
            Soldiers.Add(s);
        }
        LastMonth = MonthIndex;
        return b;
    }

    public static string BaseName(GeoPoint at) => Earth.NearestCity(at).Name;

    public Craft AddCraft(Base b, CraftType t)
    {
        int n = Crafts.Count(c => c.Type == t) + 1;
        var c = new Craft { Id = NextId++, BaseId = b.Id, Type = t, Name = $"{Rules.Of(t).Name}-{n}", At = b.At, Fuel = Rules.Of(t).FuelHours };
        Crafts.Add(c);
        return c;
    }

    /// <summary>A new base: on land, for the price of one, with its access lift.</summary>
    public string? FoundBase(GeoPoint at)
    {
        if (!LandMask.IsLand(at)) return "BASES MUST BE BUILT ON LAND";
        if (Bases.Count >= 6) return "NO MORE BASES CAN BE RUN";
        if (Money < Rules.NewBaseCost) return "NOT ENOUGH MONEY";
        if (Bases.Any(b => Geo.Nm(b.At, at) < 400)) return "TOO NEAR ANOTHER BASE";
        Money -= Rules.NewBaseCost;
        var b = new Base { Id = NextId++, Name = BaseName(at), At = at };
        if (Bases.Any(o => o.Name == b.Name)) b.Name += " II";
        b.Put(FacilityType.Lift, 2, 2);
        Bases.Add(b);
        return null;
    }

    // ───────────────────────────── the clock ─────────────────────────────

    /// <summary>
    /// Runs the geoscape on by some seconds, in small steps, stopping early if something needs the player (a UFO
    /// found, an interception, a craft at a site, the monthly report). Returns the seconds actually run.
    /// </summary>
    public double Advance(double seconds)
    {
        _interrupt = false;
        double ran = 0;
        while (ran < seconds - 1e-9 && !_interrupt && !Over)
        {
            double dt = Math.Min(MaxStep, seconds - ran);
            Seconds += dt;
            ran += dt;
            MoveUfos(dt);
            MoveCrafts(dt);
            SweepAcc += dt;
            if (SweepAcc >= SweepSeconds) { SweepAcc -= SweepSeconds; Sweep(); }
            HourAcc += dt;
            while (HourAcc >= 3600) { HourAcc -= 3600; Hour(); }
            if (LastMonth < 0) LastMonth = MonthIndex;
            if (MonthIndex != LastMonth) { LastMonth = MonthIndex; MonthEnd(); }
        }
        return ran;
    }

    // ───────────────────────────── the aliens ─────────────────────────────

    /// <summary>The races that fly this month: more, and worse, as the war goes on.</summary>
    public Race PickRace(int month)
    {
        var pool = new List<Race> { Race.Pallid, Race.Pallid, Race.Skimmer };
        if (month >= 1) pool.AddRange([Race.Coilback, Race.Skimmer]);
        if (month >= 2) pool.AddRange([Race.Brakk, Race.Coilback]);
        if (month >= 3) pool.AddRange([Race.Brakk, Race.Veiled]);
        if (month >= 5) pool.AddRange([Race.Veiled, Race.Brakk]);
        return Rng.Pick(pool);
    }

    private UfoType PickUfo(UfoMission m, int month)
    {
        int up = Math.Min(3, month / 2);
        return m switch
        {
            UfoMission.Scout => Rng.Chance(0.5) ? UfoType.Seeker : UfoType.Skiff,
            UfoMission.Research => month < 1 || Rng.Chance(0.5) ? UfoType.Skiff : UfoType.Raider,
            UfoMission.Abduction or UfoMission.Harvest => (UfoType)Math.Min((int)UfoType.Harvester, (int)UfoType.Skiff + Rng.Next(2 + up)),
            UfoMission.Terror or UfoMission.Supply => UfoType.Carrier,
            UfoMission.BaseBuild => month >= 4 && Rng.Chance(0.5) ? UfoType.Dreadnought : UfoType.Harvester,
            _ => month >= 5 ? UfoType.Dreadnought : month >= 3 ? UfoType.Harvester : UfoType.Raider,
        };
    }

    /// <summary>A random spot of land in a nation (or anywhere on land).</summary>
    public GeoPoint LandIn(int nation)
    {
        for (int i = 0; i < 60; i++)
        {
            GeoPoint p;
            if (nation >= 0)
            {
                var n = Earth.Nations[nation];
                p = new GeoPoint(Math.Clamp(n.Lat + Rng.Range(-n.Reach, n.Reach) * 0.6, -60, 75), Geo.Wrap(n.Lon + Rng.Range(-n.Reach, n.Reach) * 0.8));
            }
            else p = new GeoPoint(Rng.Range(-50, 70), Rng.Range(-180, 180));
            if (LandMask.IsLand(p) && (nation < 0 || Earth.NationAt(p) == nation)) return p;
        }
        return nation >= 0 ? Earth.Nations[nation].At : Earth.Cities[0].At;
    }

    private GeoPoint Anywhere() => new(Rng.Range(-60, 70), Rng.Range(-180, 180));

    /// <summary>Sends a UFO on a mission of the kind given (or one chosen for the month).</summary>
    public Ufo SpawnUfo(UfoMission? mission = null, UfoType? type = null, Race? race = null)
    {
        int month = MonthIndex;
        var m = mission ?? PickMission(month);
        var u = new Ufo
        {
            Id = NextId++, Mission = m, Type = type ?? PickUfo(m, month), Race = race ?? PickRace(month),
            At = Anywhere(), State = UfoState.Flying,
        };
        int nation = Rng.Next(Earth.Nations.Length);
        switch (m)
        {
            case UfoMission.Scout:
                u.Legs = 3;
                u.Dest = LandIn(nation);
                break;
            case UfoMission.Terror:
                u.Dest = Rng.Pick(Earth.Cities).At;
                u.Legs = 1;
                break;
            case UfoMission.Supply when Sites.FirstOrDefault(s => s.Kind == SiteKind.AlienBase) is { } ab:
                u.Dest = ab.At;
                u.Legs = 1;
                break;
            case UfoMission.Retaliation when Bases.Count > 0:
                var target = Rng.Pick(Bases);
                u.TargetBase = target.Id;
                u.Dest = target.At;
                u.Legs = 1;
                break;
            case UfoMission.BaseBuild:
                u.Dest = LandIn(Rng.Chance(0.6) ? nation : -1);
                u.Legs = 1;
                break;
            default:
                if (m is UfoMission.Supply or UfoMission.Retaliation) u.Mission = UfoMission.Research;
                u.Dest = LandIn(nation);
                u.Legs = 2;
                break;
        }
        Ufos.Add(u);
        return u;
    }

    private UfoMission PickMission(int month)
    {
        var pool = new List<UfoMission> { UfoMission.Scout, UfoMission.Scout, UfoMission.Research, UfoMission.Research, UfoMission.Abduction };
        if (month >= 1) pool.AddRange([UfoMission.Harvest, UfoMission.Abduction]);
        if (month >= 1 && TerrorsThisMonth < 1 + month / 3) pool.Add(UfoMission.Terror);
        if (month >= 2 && BasesThisMonth < 1 && Sites.Count(s => s.Kind == SiteKind.AlienBase) < 3) pool.Add(UfoMission.BaseBuild);
        if (Sites.Any(s => s.Kind == SiteKind.AlienBase)) pool.Add(UfoMission.Supply);
        if (month >= 3 && Heat >= 150 && Bases.Any(b => !b.Has(FacilityType.MindShield) || Rng.Chance(0.3))) pool.AddRange([UfoMission.Retaliation, UfoMission.Retaliation]);
        var m = Rng.Pick(pool);
        if (m == UfoMission.Terror) TerrorsThisMonth++;
        if (m == UfoMission.BaseBuild) BasesThisMonth++;
        if (m == UfoMission.Retaliation) Heat = 0;
        return m;
    }

    private void MoveUfos(double dt)
    {
        foreach (var u in Ufos.ToList())
        {
            if (u.State != UfoState.Flying) continue;
            double nm = u.CruiseSpeed * dt / 3600;
            var before = u.At;
            u.At = Geo.Toward(u.At, u.Dest, nm);
            var (x, y, _) = Geo.Project(u.At, before);
            if (Math.Abs(x) + Math.Abs(y) > 1e-9) u.Heading = Math.Atan2(x, y);
            if (Geo.Nm(u.At, u.Dest) > 0.5) continue;
            Arrive(u);
        }
        Ufos.RemoveAll(u => u.State == UfoState.Gone);
    }

    /// <summary>A UFO reaches the end of a leg: it lands, starts a terror site or a base, attacks a base, or flies on.</summary>
    private void Arrive(Ufo u)
    {
        u.Legs--;
        if (u.Legs < 0) { Leave(u); return; }
        switch (u.Mission)
        {
            case UfoMission.Scout:
                if (u.Legs > 0) u.Dest = LandIn(-1);
                else u.Dest = Anywhere();
                return;
            case UfoMission.Terror:
                var city = Earth.NearestCity(u.At);
                var site = new Site { Id = NextId++, Kind = SiteKind.Terror, At = city.At, Race = u.Race, Ufo = u.Type, Hours = 30, Known = true, Name = city.Name };
                Sites.Add(site);
                u.State = UfoState.Gone;
                Event(new GeoEvent(EventKind.TerrorSite, "TERROR SITE", $"ALIENS ARE TERRORISING {city.Name}. SEND A SQUAD TO STOP THEM.", Site: site.Id));
                return;
            case UfoMission.Retaliation:
                if (BaseById(u.TargetBase) is { } b) AttackBase(u, b);
                else Leave(u);
                return;
            case UfoMission.BaseBuild when u.Legs == 0:
                u.State = UfoState.Landed;
                u.Hours = 6;
                return;
            default:
                if (u.Legs > 0 || u.Mission == UfoMission.Supply)
                {
                    u.State = UfoState.Landed;
                    u.Hours = u.Mission switch { UfoMission.Research => Rng.Range(3, 6), UfoMission.Harvest => Rng.Range(4, 8), UfoMission.Supply => 4, _ => Rng.Range(2, 5) };
                }
                else u.Dest = Anywhere();
                return;
        }
    }

    private void Leave(Ufo u)
    {
        u.State = UfoState.Gone;
        if (u.Detected) Event(new GeoEvent(EventKind.UfoLost, "UFO GONE", $"{u.Name} HAS LEFT THE ATMOSPHERE.", Ufo: u.Id, Interrupt: false));
    }

    /// <summary>A landed UFO's time is up: it takes off (a base builder leaves its base behind).</summary>
    private void TakeOff(Ufo u)
    {
        if (u.Mission == UfoMission.BaseBuild)
        {
            var site = new Site { Id = NextId++, Kind = SiteKind.AlienBase, At = u.At, Race = u.Race, Ufo = u.Type, Hours = double.PositiveInfinity, Name = "ALIEN BASE" };
            Sites.Add(site);
        }
        u.State = UfoState.Flying;
        u.Legs = 0;
        u.Dest = Anywhere();
    }

    /// <summary>A retaliation UFO over a base: the defences fire (twice each under a grav shield); if it survives, it lands
    /// its troops and the base must be defended.</summary>
    private void AttackBase(Ufo u, Base b)
    {
        var info = u.Info;
        int shots = b.Has(FacilityType.GravShield) ? 2 : 1;
        foreach (var f in b.Facilities.Where(f => f.Built && f.Type is FacilityType.MissileDefence or FacilityType.LaserDefence or FacilityType.StormDefence))
            for (int i = 0; i < shots; i++)
                if (Rng.Chance(0.7)) u.Damage += Rules.Of(f.Type).Capacity / 2.0;
        if (u.Damage >= info.Hp)
        {
            u.State = UfoState.Gone;
            Ours(info.Score, b.At);
            Event(new GeoEvent(EventKind.BaseDefended, "BASE DEFENCES", $"{b.Name}'S DEFENCES HAVE DESTROYED AN ATTACKING {info.Name}.", Base: b.Id));
            return;
        }
        u.State = UfoState.Gone;
        Event(new GeoEvent(EventKind.BaseAttack, "BASE UNDER ATTACK", $"A {info.Name} HAS LANDED {Rules.Of(u.Race).Plural} AT {b.Name}. DEFEND THE BASE!", Base: b.Id, Ufo: u.Id) { });
        PendingDefence = (b.Id, u.Type, u.Race, u.Damage / info.Hp);
    }

    /// <summary>A base defence waiting to be fought (base, UFO type, race, damage done by the defences).</summary>
    public (int Base, UfoType Ufo, Race Race, double Damage)? PendingDefence;

    // ───────────────────────────── radar ─────────────────────────────

    /// <summary>A radar sweep: each base's radar and each flying craft's may find a UFO in range; tracked UFOs out of every
    /// range are lost.</summary>
    private void Sweep()
    {
        foreach (var u in Ufos)
        {
            if (u.State == UfoState.Gone) continue;
            bool inRange = false;
            double chance = 0;
            foreach (var b in Bases)
            {
                double r = b.RadarRange;
                if (r <= 0 || Geo.Nm(b.At, u.At) > r) continue;
                inRange = true;
                chance = Math.Max(chance, (b.Has(FacilityType.SignalDecoder) ? 0.4 : b.Has(FacilityType.LargeRadar) ? 0.25 : 0.16) * u.Info.Detect);
            }
            foreach (var c in Crafts.Where(c => c.Flying))
            {
                double d = Geo.Nm(c.At, u.At);
                if (d > CraftRadarNm) continue;
                inRange = true;
                chance = Math.Max(chance, c.TargetUfo == u.Id && u.Detected ? 1 : 0.5 * u.Info.Detect);
            }
            if (u.State == UfoState.Landed) chance *= 0.6;
            if (u.Detected)
            {
                if (!inRange)
                {
                    u.Detected = false;
                    Event(new GeoEvent(EventKind.UfoLost, "CONTACT LOST", $"RADAR CONTACT WITH {u.Name} HAS BEEN LOST.", Ufo: u.Id, Interrupt: false));
                }
                continue;
            }
            if (inRange && Rng.Chance(chance)) Detect(u);
        }
        // Alien bases are found by craft flying near them, or now and then by a radar.
        foreach (var s in Sites.Where(s => s.Kind == SiteKind.AlienBase && !s.Known))
        {
            bool near = Crafts.Any(c => c.Flying && Geo.Nm(c.At, s.At) < 450) || Bases.Any(b => b.RadarRange > 0 && Geo.Nm(b.At, s.At) < b.RadarRange * 0.5);
            if (near && Rng.Chance(0.08))
            {
                s.Known = true;
                Event(new GeoEvent(EventKind.AlienBaseFound, "ALIEN BASE", $"AN ALIEN BASE HAS BEEN FOUND NEAR {Earth.NearestCity(s.At).Name}.", Site: s.Id));
            }
        }
    }

    public void Detect(Ufo u)
    {
        u.Detected = true;
        u.Known = true;
        string what = Bases.Any(b => b.Has(FacilityType.SignalDecoder))
            ? $" A {u.Info.Name} CREWED BY {Rules.Of(u.Race).Plural}, ON A {u.Mission.ToString().ToUpperInvariant()} MISSION."
            : $" SIZE: {u.Info.Size}.";
        Event(new GeoEvent(EventKind.UfoDetected, "UFO DETECTED", $"{u.Name} {(u.State == UfoState.Landed ? "HAS LANDED" : "IS FLYING")} NEAR {Earth.NearestCity(u.At).Name}." + what, Ufo: u.Id));
    }

    // ───────────────────────────── craft ─────────────────────────────

    /// <summary>Why a craft can't go after a UFO or to a site, or null if it's on its way.</summary>
    public string? Launch(Craft c, Ufo? ufo = null, Site? site = null, GeoPoint? patrol = null)
    {
        if (!c.CanLaunch && !c.Flying) return c.State switch { CraftState.Repairing => "IT IS UNDER REPAIR", CraftState.Lost => "IT IS LOST", _ => "IT IS NOT READY" };
        if (site is not null && c.Info.Seats == 0) return "AN INTERCEPTOR CANNOT CARRY TROOPS";
        if (site is not null && site.Kind == SiteKind.Homeworld && c.Type != CraftType.Starfall) return "ONLY THE STARFALL CAN REACH NYX";
        if ((site is not null || ufo is { State: UfoState.Landed }) && c.Info.Seats > 0 && !CrewOf(c).Any(s => s.Fit)) return "THE CRAFT HAS NO SQUAD";
        if (ufo is { State: UfoState.Flying } && c.Guns.Count == 0) return "IT HAS NO WEAPONS";
        if (ufo is not null && !ufo.Detected) return "THE UFO IS NOT ON RADAR";
        c.TargetUfo = ufo?.Id ?? -1;
        c.TargetSite = site?.Id ?? -1;
        c.Waypoint = patrol;
        c.State = CraftState.Out;
        return null;
    }

    public void SendHome(Craft c)
    {
        if (!c.Flying) return;
        c.State = CraftState.Returning;
        c.TargetUfo = c.TargetSite = -1;
        c.Waypoint = null;
    }

    private void MoveCrafts(double dt)
    {
        foreach (var c in Crafts)
        {
            if (!c.Flying) continue;
            var home = BaseById(c.BaseId);
            if (home is null) { c.State = CraftState.Lost; continue; }
            c.Fuel -= dt / 3600 * (c.State == CraftState.Waiting ? 0.5 : 1);
            if (c.State != CraftState.Returning && c.Fuel < Geo.Nm(c.At, home.At) / c.Info.Speed + 0.4)
            {
                SendHome(c);
                Event(new GeoEvent(EventKind.LowFuel, "LOW FUEL", $"{c.Name} IS LOW ON FUEL AND RETURNING TO BASE.", Craft: c.Id, Interrupt: false));
            }
            double nm = c.Info.Speed * dt / 3600;
            if (c.State == CraftState.Returning)
            {
                c.At = Geo.Toward(c.At, home.At, nm);
                if (Geo.Nm(c.At, home.At) < 1) Land(c);
                continue;
            }
            if (c.TargetUfo >= 0)
            {
                var u = UfoById(c.TargetUfo);
                if (u is null || !u.Detected)
                {
                    SendHome(c);
                    Event(new GeoEvent(EventKind.TargetLost, "TARGET LOST", $"{c.Name} HAS LOST ITS TARGET AND IS RETURNING.", Craft: c.Id, Interrupt: false));
                    continue;
                }
                if (c.State == CraftState.Out) c.At = Geo.Toward(c.At, u.At, nm);
                double d = Geo.Nm(c.At, u.At);
                if (u.State == UfoState.Flying && c.Guns.Count > 0 && d < Math.Max(InterceptNm, nm))
                {
                    c.State = CraftState.Waiting;
                    Event(new GeoEvent(EventKind.Intercept, "INTERCEPTION", $"{c.Name} HAS CAUGHT {u.Name}.", Ufo: u.Id, Craft: c.Id));
                    return;
                }
                if (u.State == UfoState.Landed && d < ArriveNm)
                {
                    c.At = u.At;
                    c.State = CraftState.Waiting;
                    if (c.Info.Seats > 0 && CrewOf(c).Any(s => s.Fit))
                        Event(new GeoEvent(EventKind.CraftArrived, "LANDED UFO", $"{c.Name} HAS REACHED THE LANDED {u.Info.Name}.", Ufo: u.Id, Craft: c.Id));
                }
                else if (u.State == UfoState.Flying && c.State == CraftState.Waiting) c.State = CraftState.Out;
                continue;
            }
            if (c.TargetSite >= 0)
            {
                var s = SiteById(c.TargetSite);
                if (s is null) { SendHome(c); continue; }
                if (c.State == CraftState.Out) c.At = Geo.Toward(c.At, s.At, nm);
                if (c.State == CraftState.Out && Geo.Nm(c.At, s.At) < ArriveNm)
                {
                    c.At = s.At;
                    c.State = CraftState.Waiting;
                    Event(new GeoEvent(EventKind.CraftArrived, s.Kind switch { SiteKind.Terror => "TERROR SITE", SiteKind.AlienBase => "ALIEN BASE", SiteKind.Homeworld => "NYX", _ => "CRASH SITE" },
                        $"{c.Name} HAS REACHED {s.Name}.", Site: s.Id, Craft: c.Id));
                }
                continue;
            }
            if (c.Waypoint is { } wp && c.State == CraftState.Out)
            {
                c.At = Geo.Toward(c.At, wp, nm);
                if (Geo.Nm(c.At, wp) < 1) c.State = CraftState.Waiting;
            }
        }
    }

    /// <summary>A craft home: it's repaired, rearmed and refuelled before it's ready again.</summary>
    private void Land(Craft c)
    {
        c.At = BaseById(c.BaseId)!.At;
        c.State = c.Damage > 0 ? CraftState.Repairing : NeedsRearming(c) ? CraftState.Rearming : CraftState.Refuelling;
        c.TargetUfo = c.TargetSite = -1;
        c.Waypoint = null;
        Event(new GeoEvent(EventKind.CraftReturned, "CRAFT RETURNED", $"{c.Name} HAS RETURNED TO BASE.", Craft: c.Id, Interrupt: false));
    }

    private static bool NeedsRearming(Craft c) => c.Guns.Any(g => g.Info.Rounds > 0 && g.Rounds < g.Info.Rounds);

    /// <summary>An hour in the hangar: repair, then rearm from the stores, then refuel.</summary>
    private void Service(Craft c)
    {
        var b = BaseById(c.BaseId)!;
        switch (c.State)
        {
            case CraftState.Repairing:
                c.Damage = Math.Max(0, c.Damage - c.Info.Hp * 0.05);
                if (c.Damage <= 0) c.State = NeedsRearming(c) ? CraftState.Rearming : CraftState.Refuelling;
                break;
            case CraftState.Rearming:
                foreach (var g in c.Guns.Where(g => g.Info.Rounds > 0 && g.Rounds < g.Info.Rounds))
                    if (b.Take(g.Info.AmmoItem)) g.Rounds = Math.Min(g.Info.Rounds, g.Rounds + g.Info.PerItem);
                    else g.Rounds = Math.Max(g.Rounds, 0);
                if (!NeedsRearming(c) || c.Guns.Where(g => g.Info.Rounds > 0 && g.Rounds < g.Info.Rounds).All(g => b.Count(g.Info.AmmoItem) == 0)) c.State = CraftState.Refuelling;
                break;
            case CraftState.Refuelling:
                c.Fuel = Math.Min(c.Info.FuelHours, c.Fuel + c.Info.FuelHours * 0.25);
                if (c.Fuel >= c.Info.FuelHours) c.State = CraftState.Ready;
                break;
        }
    }

    /// <summary>Puts a craft weapon from the stores into a gun slot (the old one goes back to the stores).</summary>
    public string? Fit(Craft c, int slot, ItemKind weapon)
    {
        var b = BaseById(c.BaseId)!;
        if (slot >= c.Info.Weapons) return "NO SUCH WEAPON MOUNT";
        if (!b.Take(weapon)) return "NONE IN THE STORES";
        var info = Rules.WeaponOf(weapon);
        var gun = new CraftGun { Kind = weapon, Rounds = 0 };
        if (info.Rounds > 0)
            while (gun.Rounds < info.Rounds && b.Take(info.AmmoItem)) gun.Rounds = Math.Min(info.Rounds, gun.Rounds + info.PerItem);
        if (slot < c.Guns.Count)
        {
            b.Add(c.Guns[slot].Kind);
            c.Guns[slot] = gun;
        }
        else c.Guns.Add(gun);
        return null;
    }

    // ───────────────────────────── the interception ─────────────────────────────

    public AirCombat Engage(Craft c, Ufo u) => new(c, u, Seed + NextId++ * 31);

    /// <summary>How an air battle ended, for the geoscape: a crash site, points, a craft lost or sent home.</summary>
    public void Resolve(AirCombat ac)
    {
        var c = ac.Craft;
        var u = ac.Ufo;
        var info = u.Info;
        switch (ac.Result)
        {
            case AirResult.Destroyed:
                u.State = UfoState.Gone;
                Ours(info.Score, u.At);
                Heat += 20;
                Event(new GeoEvent(EventKind.UfoDestroyed, "UFO DESTROYED", $"{u.Name} HAS BEEN BLOWN APART. NOTHING CAN BE RECOVERED.", Interrupt: false));
                break;
            case AirResult.Downed:
                u.State = UfoState.Gone;
                Ours(info.Score / 2, u.At);
                Heat += 20;
                if (LandMask.IsLand(u.At))
                {
                    var site = new Site { Id = NextId++, Kind = SiteKind.CrashSite, At = u.At, Ufo = u.Type, Race = u.Race, Hours = 48 + Rng.Range(0, 24), Known = true, Damage = u.Damage / info.Hp, Name = "CRASH SITE" };
                    Sites.Add(site);
                    Event(new GeoEvent(EventKind.UfoDowned, "UFO DOWN", $"{u.Name} HAS CRASH-LANDED NEAR {Earth.NearestCity(u.At).Name}. SEND A SQUAD TO RECOVER IT.", Site: site.Id));
                }
                else Event(new GeoEvent(EventKind.UfoDowned, "UFO DOWN", $"{u.Name} HAS COME DOWN IN THE SEA. NOTHING CAN BE RECOVERED.", Interrupt: false));
                break;
            case AirResult.CraftLost:
                c.State = CraftState.Lost;
                Crafts.Remove(c);
                Theirs(100, c.At);
                Event(new GeoEvent(EventKind.CraftLost, "CRAFT LOST", $"{c.Name} HAS BEEN SHOT DOWN.", Interrupt: false));
                return;
        }
        SendHome(c);
    }

    // ───────────────────────────── the hours ─────────────────────────────

    private void Hour()
    {
        foreach (var b in Bases.ToList()) BaseHour(b);
        foreach (var c in Crafts.Where(c => !c.Flying && c.State is not CraftState.Ready and not CraftState.Lost)) Service(c);
        foreach (var s in Soldiers) s.WoundDays = Math.Max(0, s.WoundDays - 1 / 24.0);
        foreach (var u in Ufos.Where(u => u.State != UfoState.Gone).ToList())
        {
            // The aliens' activity: a point most hours a UFO is landed, now and then while it flies.
            if (u.State == UfoState.Landed) Theirs(2, u.At);
            else if (Rng.Chance(u.Mission == UfoMission.Scout ? 0.15 : 0.3)) Theirs(1, u.At);
            if (u.State == UfoState.Landed && --u.Hours <= 0) TakeOff(u);
        }
        foreach (var s in Sites.ToList())
        {
            if (s.Kind == SiteKind.AlienBase) { if (Rng.Chance(0.4)) Theirs(1, s.At); continue; }
            if (s.Kind == SiteKind.Homeworld) continue;
            if (--s.Hours > 0) continue;
            Sites.Remove(s);
            foreach (var c in Crafts.Where(c => c.TargetSite == s.Id)) SendHome(c);
            if (s.Kind == SiteKind.Terror)
            {
                Theirs(400, s.At);
                Event(new GeoEvent(EventKind.Note, "TERROR SITE", $"THE ALIENS HAVE LEFT {s.Name}, UNOPPOSED. HUNDREDS ARE DEAD.", Interrupt: false));
            }
        }
        NextUfoHours -= 1;
        if (NextUfoHours <= 0)
        {
            SpawnUfo();
            int m = Math.Min(6, MonthIndex);
            NextUfoHours = Rng.Range(26 - m * 2.5, 52 - m * 4.5);
        }
    }

    private void BaseHour(Base b)
    {
        foreach (var f in b.Facilities.Where(f => !f.Built))
        {
            f.DaysLeft -= 1 / 24.0;
            if (f.DaysLeft <= 0)
            {
                f.DaysLeft = 0;
                Event(new GeoEvent(EventKind.FacilityDone, "CONSTRUCTION COMPLETE", $"{Rules.Of(f.Type).Name} AT {b.Name} IS READY.", Base: b.Id, Interrupt: false));
            }
        }
        foreach (var a in b.Arriving.ToList())
        {
            if (--a.Hours > 0) continue;
            b.Arriving.Remove(a);
            b.Add(a.Item, a.Count);
            b.Scientists += a.Scientists;
            b.Engineers += a.Engineers;
            if (a.Soldier is { } s) Soldiers.Add(s);
            if (a.Craft is { } ct)
            {
                // A bought interceptor comes armed: missiles and a cannon.
                var k = AddCraft(b, ct);
                if (ct == CraftType.Falcon)
                {
                    k.Guns.Add(new CraftGun { Kind = ItemKind.LancerLauncher, Rounds = 6 });
                    k.Guns.Add(new CraftGun { Kind = ItemKind.AutoCannon, Rounds = 120 });
                }
            }
            Event(new GeoEvent(EventKind.Arrived, "ARRIVALS", $"{a.What} HAS ARRIVED AT {b.Name}.", Base: b.Id, Interrupt: false));
        }
        foreach (var r in b.Research.ToList())
        {
            r.Done += r.Scientists / 24.0;
            var t = Rules.Of(r.Topic);
            if (r.Done < t.Cost) continue;
            b.Research.Remove(r);
            Complete(r.Topic, b);
        }
        foreach (var p in b.Production.ToList())
        {
            if (p.Engineers == 0) continue;
            p.Progress += p.Engineers;
            var info = p.Info;
            if (p.Progress < info.Hours) continue;
            p.Progress -= info.Hours;
            p.Made++;
            if (info.Craft is { } ct) AddCraft(b, ct);
            else b.Add(info.Item);
            if (p.Made >= p.Total)
            {
                b.Production.Remove(p);
                Event(new GeoEvent(EventKind.ProductionDone, "PRODUCTION COMPLETE", $"{p.Total} x {info.Name} MADE AT {b.Name}.", Base: b.Id));
            }
            else if (!Pay(b, info))
            {
                b.Production.Remove(p);
                Event(new GeoEvent(EventKind.ProductionDone, "PRODUCTION HALTED", $"NOT ENOUGH MONEY OR MATERIALS TO GO ON MAKING {info.Name} AT {b.Name}.", Base: b.Id));
            }
        }
    }

    private void Complete(Topic t, Base b)
    {
        Researched.Add(t);
        var info = Rules.Of(t);
        Ours(info.Score, b.At);
        Event(new GeoEvent(EventKind.ResearchDone, "RESEARCH COMPLETE", $"{info.Name}: {info.Text}", Base: b.Id));
        if (t == Topic.TheHomeworld && !Sites.Any(s => s.Kind == SiteKind.Homeworld))
            Sites.Add(new Site { Id = NextId++, Kind = SiteKind.Homeworld, At = new GeoPoint(-82, 140), Race = Race.Veiled, Ufo = UfoType.Dreadnought, Hours = double.PositiveInfinity, Known = true, Name = Rules.Homeworld });
    }

    // ───────────────────────────── the month's end ─────────────────────────────

    /// <summary>
    /// The council's report: the month's score (the organisation's points less the aliens'), each nation's funding up or
    /// down by how it was served (and some may withdraw, above all where an alien base is), the funding paid in and the
    /// bills paid out. Two terrible months running, or two months in debt, and the council ends the project.
    /// </summary>
    public void MonthEnd()
    {
        int score = MonthOurs - MonthTheirs;
        string rating = Rules.Rating(score);
        var lines = new List<string>();
        for (int i = 0; i < Nations.Length; i++)
        {
            var n = Nations[i];
            if (n.Withdrawn) continue;
            int local = n.Ours - n.Theirs;
            n.Satisfaction = Math.Clamp(n.Satisfaction + Math.Clamp(local / 8, -30, 25) + Math.Clamp(score / 150, -8, 8), 0, 100);
            bool alienBase = Sites.Any(s => s.Kind == SiteKind.AlienBase && Earth.NationAt(s.At) == i);
            int change = 0;
            if (n.Satisfaction >= 65) change = Rng.Range(5, 15);
            else if (n.Satisfaction <= 35) change = -Rng.Range(5, 15);
            n.Funding = Math.Clamp(n.Funding + n.Funding * change / 100, 10, Earth.Nations[i].Funding * 3);
            n.LastChange = change;
            if (n.Satisfaction < 12 || alienBase && n.Satisfaction < 40 && Rng.Chance(0.4))
            {
                n.Withdrawn = true;
                lines.Add($"{Earth.Nations[i].Name} HAS {(alienBase ? "SIGNED A PACT WITH THE ALIENS" : "WITHDRAWN FROM THE PROJECT")}.");
            }
            n.Ours = n.Theirs = 0;
        }
        int income = Funding;
        int bills = Bases.Sum(b => b.Upkeep + b.Scientists * Rules.ScientistPay + b.Engineers * Rules.EngineerPay)
                    + Soldiers.Count * Rules.SoldierPay + Crafts.Sum(c => c.Info.Upkeep);
        Money += income - bills;
        Score += score;
        var last = Geo.Start.AddSeconds(Seconds - 3600 * 12);
        History.Add(new MonthRecord(last.Year, last.Month, MonthOurs, MonthTheirs, income, Money, rating));
        // Psionic training.
        foreach (var s in Soldiers.Where(s => s.PsiTraining))
        {
            if (!s.PsiKnown) { s.PsiKnown = true; s.PsiSkill = Math.Max(s.PsiSkill, Rng.Range(10, 24)); }
            else s.PsiSkill = Math.Min(100, s.PsiSkill + Rng.Range(4, 12));
        }
        TerribleMonths = rating == "TERRIBLE" ? TerribleMonths + 1 : 0;
        DebtMonths = Money < 0 ? DebtMonths + 1 : 0;
        string verdict = rating switch
        {
            "TERRIBLE" => "THE COUNCIL IS GRAVELY CONCERNED. ANOTHER MONTH LIKE THIS AND THE PROJECT WILL BE ENDED.",
            "POOR" => "THE COUNCIL EXPECTS BETTER RESULTS.",
            "OK" => "THE COUNCIL IS SATISFIED, FOR NOW.",
            "GOOD" => "THE COUNCIL IS PLEASED WITH YOUR PROGRESS.",
            _ => "THE COUNCIL IS DELIGHTED. KEEP IT UP, COMMANDER.",
        };
        if (TerribleMonths >= 2) End(false, "THE COUNCIL HAS LOST ALL CONFIDENCE IN THE PROJECT AND TERMINATED IT.");
        else if (DebtMonths >= 2) End(false, "THE PROJECT HAS BEEN IN DEBT FOR TWO MONTHS. THE COUNCIL HAS CLOSED IT DOWN.");
        else if (Nations.Count(n => n.Withdrawn) * 2 >= Nations.Length) End(false, "HALF THE COUNCIL HAS WITHDRAWN. THE PROJECT IS OVER.");
        Event(new GeoEvent(EventKind.MonthlyReport, "MONTHLY REPORT",
            $"RATING: {rating}. SCORE {score}. FUNDING ${income / 1000:N0}K. {verdict}" + (lines.Count > 0 ? " " + string.Join(" ", lines) : "")));
        MonthOurs = MonthTheirs = 0;
        TerrorsThisMonth = BasesThisMonth = 0;
    }

    public void End(bool won, string why)
    {
        Over = true;
        Won = won;
        Ending = why;
    }

    /// <summary>A base lost to the aliens: everything in it goes, and with the last one, the war.</summary>
    public void LoseBase(Base b)
    {
        foreach (var s in Soldiers.Where(s => s.BaseId == b.Id).ToList())
        {
            Soldiers.Remove(s);
            Memorial.Add(new Fallen(s.Name, s.Rank, s.Nation, s.Missions, s.Kills, Now, b.Name));
        }
        Crafts.RemoveAll(c => c.BaseId == b.Id);
        Bases.Remove(b);
        Theirs(1000, b.At);
        if (Bases.Count == 0) End(false, $"{b.Name}, THE LAST BASE, HAS FALLEN. THE ALIENS HAVE WON.");
    }

    // ───────────────────────────── the player's hand: staff, stores, research, workshops ─────────────────────────────

    public int PeopleAt(Base b) => SoldiersAt(b).Count() + b.Scientists + b.Engineers + b.Arriving.Sum(a => a.Scientists + a.Engineers + (a.Soldier is null ? 0 : 1));

    public enum Staff { Soldier, Scientist, Engineer }

    public string? Hire(Base b, Staff who, int count = 1)
    {
        int each = who switch { Staff.Soldier => Rules.SoldierCost, Staff.Scientist => Rules.ScientistCost, _ => Rules.EngineerCost };
        if (Money < each * count) return "NOT ENOUGH MONEY";
        if (PeopleAt(b) + count > b.QuartersSpace) return "NOT ENOUGH LIVING QUARTERS";
        Money -= each * count;
        for (int i = 0; i < count; i++)
        {
            var a = new Arrival { Hours = Rules.ArrivalHours };
            switch (who)
            {
                case Staff.Soldier: a.Soldier = Soldier.Recruit(ref Rng, NextId++, b.Id); a.What = a.Soldier.Name; break;
                case Staff.Scientist: a.Scientists = 1; a.What = "A SCIENTIST"; break;
                default: a.Engineers = 1; a.What = "AN ENGINEER"; break;
            }
            b.Arriving.Add(a);
        }
        return null;
    }

    /// <summary>Buys items (or a craft) for a base; they arrive in three days.</summary>
    public string? Buy(Base b, ItemKind k, int count = 1)
    {
        var info = Rules.Of(k);
        if (info.Buy <= 0 || !Rules.Market.Contains(k)) return "NOT FOR SALE";
        if (Money < info.Buy * count) return "NOT ENOUGH MONEY";
        if (b.StoresUsed + b.Arriving.Sum(a => Rules.Of(a.Item).Space * a.Count) + info.Space * count > b.StoreSpace) return "NOT ENOUGH STORE SPACE";
        Money -= info.Buy * count;
        b.Arriving.Add(new Arrival { Hours = Rules.ArrivalHours, Item = k, Count = count, What = $"{count} x {info.Name}" });
        return null;
    }

    public string? BuyCraft(Base b, CraftType t)
    {
        var info = Rules.Of(t);
        if (info.Buy <= 0) return "IT MUST BE MANUFACTURED";
        if (Money < info.Buy) return "NOT ENOUGH MONEY";
        if (CraftsAt(b).Count() + b.Arriving.Count(a => a.Craft is not null) >= b.HangarSpace) return "NO FREE HANGAR";
        Money -= info.Buy;
        b.Arriving.Add(new Arrival { Hours = Rules.ArrivalHours, Craft = t, What = "A NEW " + info.Name });
        return null;
    }

    public string? Sell(Base b, ItemKind k, int count = 1)
    {
        var info = Rules.Of(k);
        if (info.Sell <= 0) return "NOBODY WILL BUY IT";
        if (!b.Take(k, count)) return "NOT ENOUGH IN THE STORES";
        Money += info.Sell * count;
        return null;
    }

    public string? Build(Base b, FacilityType t, int x, int y)
    {
        var info = Rules.Of(t);
        if (info.Needs is { } need && !Researched.Contains(need)) return "NOT YET RESEARCHED";
        if (b.CanBuild(t, x, y) is { } why) return why;
        if (Money < info.Cost * 1000) return "NOT ENOUGH MONEY";
        Money -= info.Cost * 1000;
        b.Put(t, x, y, built: false);
        return null;
    }

    public string? Dismantle(Base b, Facility f)
    {
        if (b.CanDismantle(f, CraftsAt(b).Count(), PeopleAt(b)) is { } why) return why;
        b.Facilities.Remove(f);
        return null;
    }

    /// <summary>Whether a topic can be taken up at a base now: not done or under way, its prerequisites done, and its
    /// specimen (or captive) in this base.</summary>
    public bool CanResearch(Base b, Topic t)
    {
        if (Researched.Contains(t) || Bases.Any(o => o.Research.Any(r => r.Topic == t))) return false;
        var info = Rules.Of(t);
        if (info.Needs.Any(n => !Researched.Contains(n))) return false;
        if (info.NeedsAny is { } any && !any.Any(Researched.Contains)) return false;
        if (info.Item != ItemKind.None && b.Count(info.Item) == 0) return false;
        if (info.Captive is { } race && b.Captives.All(c => c.Race != race)) return false;
        return true;
    }

    public IEnumerable<Topic> Available(Base b) => Enum.GetValues<Topic>().Where(t => CanResearch(b, t));

    /// <summary>Starts a topic (using up its specimen or captive) with scientists on it.</summary>
    public string? StartResearch(Base b, Topic t, int scientists = 0)
    {
        if (!CanResearch(b, t)) return "IT CANNOT BE RESEARCHED HERE";
        var info = Rules.Of(t);
        if (info.Item != ItemKind.None) b.Take(info.Item);
        if (info.Captive is { } race) b.Captives.Remove(b.Captives.First(c => c.Race == race));
        var job = new ResearchJob { Topic = t };
        b.Research.Add(job);
        Assign(b, job, scientists);
        return null;
    }

    /// <summary>Moves scientists on or off a topic, within those free and the laboratories' room.</summary>
    public void Assign(Base b, ResearchJob job, int change)
    {
        int busy = b.Research.Sum(r => r.Scientists);
        int room = Math.Min(b.Scientists, b.LabSpace) - busy;
        job.Scientists = Math.Max(0, job.Scientists + Math.Min(change, room));
    }

    public bool CanMake(ProjectInfo p) => p.Needs is not { } t || Researched.Contains(t);

    private bool Pay(Base b, ProjectInfo p)
    {
        if (Money < p.Cost || p.Materials.Any(m => b.Count(m.Item) < m.Count)) return false;
        Money -= p.Cost;
        foreach (var (item, n) in p.Materials) b.Take(item, n);
        return true;
    }

    public string? StartProduction(Base b, string project, int total, int engineers)
    {
        var p = Rules.Project(project);
        if (!CanMake(p)) return "NOT YET RESEARCHED";
        if (b.WorkshopSpace == 0) return "THE BASE HAS NO WORKSHOP";
        if (p.Craft is not null && CraftsAt(b).Count() + b.Production.Count(j => j.Info.Craft is not null) >= b.HangarSpace) return "NO FREE HANGAR";
        if (!Pay(b, p)) return "NOT ENOUGH MONEY OR MATERIALS";
        var job = new ProductionJob { Project = project, Total = Math.Max(1, total) };
        b.Production.Add(job);
        AssignEngineers(b, job, engineers);
        return null;
    }

    public void AssignEngineers(Base b, ProductionJob job, int change)
    {
        int busy = b.Production.Sum(p => p.Engineers);
        int room = Math.Min(b.Engineers, b.WorkshopSpace) - busy;
        job.Engineers = Math.Max(0, job.Engineers + Math.Min(change, room));
    }

    /// <summary>Puts a soldier on (or takes them off) a craft at their base.</summary>
    public string? ToggleCrew(Soldier s, Craft c)
    {
        if (s.CraftId == c.Id) { s.CraftId = -1; return null; }
        if (c.BaseId != s.BaseId) return "NOT AT THE SAME BASE";
        if (c.Info.Seats == 0) return "IT CARRIES NO TROOPS";
        if (CrewOf(c).Count() >= c.Info.Seats) return "THE CRAFT IS FULL";
        if (c.Flying) return "THE CRAFT IS OUT";
        s.CraftId = c.Id;
        return null;
    }

    /// <summary>Gives a soldier armour from the stores (the old suit goes back).</summary>
    public string? Equip(Soldier s, ArmourType a)
    {
        var b = BaseById(s.BaseId)!;
        var item = Rules.Of(a).Item;
        if (item != ItemKind.None && !b.Take(item)) return "NONE IN THE STORES";
        b.Add(Rules.Of(s.Armour).Item);
        s.Armour = a;
        return null;
    }

    public void Sack(Soldier s)
    {
        Soldiers.Remove(s);
        if (BaseById(s.BaseId) is { } b) b.Add(Rules.Of(s.Armour).Item);
    }

    // ───────────────────────────── missions ─────────────────────────────

    /// <summary>The terrain at a place: the snow and the deserts, the forests and jungles, and farmland everywhere else.</summary>
    public static Terrain TerrainAt(GeoPoint p) => LandMask.ClimateAt(p) switch
    {
        Climate.Ice or Climate.Tundra => Terrain.Arctic,
        Climate.Desert => Terrain.Desert,
        Climate.Forest or Climate.Jungle => Terrain.Forest,
        _ => Terrain.Farmland,
    };

    /// <summary>The mission a craft has arrived at, ready to be fought.</summary>
    public Mission? MissionFor(Craft c)
    {
        var m = new Mission { CraftId = c.Id, BaseId = c.BaseId, Month = MonthIndex, Seed = Rng.Next(1_000_000) + 1, At = c.At };
        m.Squad.AddRange(CrewOf(c).Where(s => s.Fit));
        if (m.Squad.Count == 0) return null;
        if (UfoById(c.TargetUfo) is { State: UfoState.Landed } u)
        {
            m.Kind = MissionKind.LandedUfo;
            m.Ufo = u.Type;
            m.Race = u.Race;
            m.UfoId = u.Id;
            m.Crew = u.Info.Crew;
            m.Terrain = TerrainAt(u.At);
            m.Name = $"LANDED {u.Info.Name}";
        }
        else if (SiteById(c.TargetSite) is { } s)
        {
            m.SiteId = s.Id;
            m.Race = s.Race;
            m.Ufo = s.Ufo;
            switch (s.Kind)
            {
                case SiteKind.CrashSite:
                    m.Kind = MissionKind.CrashSite;
                    m.Crew = Math.Max(1, (int)Math.Round(Rules.Of(s.Ufo).Crew * (1 - s.Damage * 0.6)));
                    m.Damage = s.Damage;
                    m.Terrain = TerrainAt(s.At);
                    m.Name = $"CRASHED {Rules.Of(s.Ufo).Name}";
                    break;
                case SiteKind.Terror:
                    m.Kind = MissionKind.Terror;
                    m.Ufo = null;
                    m.Crew = 8 + Math.Min(6, MonthIndex);
                    m.Terrain = Terrain.Urban;
                    m.Name = $"TERROR IN {s.Name}";
                    break;
                case SiteKind.AlienBase:
                    m.Kind = MissionKind.AlienBase;
                    m.Ufo = null;
                    m.Crew = 12 + Math.Min(6, MonthIndex);
                    m.Terrain = Terrain.AlienBase;
                    m.Name = "ALIEN BASE ASSAULT";
                    break;
                default:
                    m.Kind = MissionKind.Homeworld;
                    m.Ufo = null;
                    m.Race = Race.Veiled;
                    m.Crew = 18;
                    m.Terrain = Terrain.Hive;
                    m.Name = "THE HIVE OF NYX";
                    break;
            }
        }
        else return null;
        m.Night = m.Kind is not (MissionKind.AlienBase or MissionKind.Homeworld) && Geo.IsNight(m.At, Now);
        return m;
    }

    public Mission? DefenceMission()
    {
        if (PendingDefence is not var (bid, ufo, race, damage) || BaseById(bid) is not { } b) return null;
        PendingDefence = null;
        var m = new Mission
        {
            Kind = MissionKind.BaseDefence, BaseId = b.Id, Base = b, Race = race, Ufo = ufo, Month = MonthIndex, Seed = Rng.Next(1_000_000) + 1,
            // Part of the crew comes down into the base (less of it, the more the defences hurt the UFO).
            Crew = Math.Max(4, (int)(Rules.Of(ufo).Crew * 0.7 * (1 - damage * 0.5))), Terrain = Terrain.BaseInterior, At = b.At, Name = $"DEFENCE OF {b.Name}",
        };
        m.Squad.AddRange(SoldiersAt(b).Where(s => s.Fit).Take(18));
        return m;
    }
}
