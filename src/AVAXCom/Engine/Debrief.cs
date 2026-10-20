namespace AVAXCom.Engine;

/// <summary>
/// The score after a ground mission: the aliens killed and taken alive, the soldiers and civilians lost, the UFO and
/// artefacts recovered, each line with its points; what goes back to the stores; and, once applied to the campaign,
/// each soldier's experience, wounds, promotions and the fallen.
/// </summary>
public sealed class Debrief
{
    public string Name = "";
    public MissionKind Kind;
    public bool Won, Aborted;
    public List<(string Label, int Count, int Points)> Lines = [];
    public Dictionary<ItemKind, int> Recovered = [];
    public List<(Race Race, AlienRank Rank)> Captured = [];
    public List<BUnit> Survivors = [], Dead = [];
    public List<string> Notes = [];
    public int Turns;

    public int Points => Lines.Sum(l => l.Points);

    public string Rating => Points switch { < 0 => "TERRIBLE", < 100 => "POOR", < 300 => "OK", < 600 => "GOOD", _ => "EXCELLENT" };

    private void Line(string label, int count, int each)
    {
        if (count != 0) Lines.Add((label, count, count * each));
    }

    private void Add(ItemKind k, int n = 1)
    {
        if (k == ItemKind.None || n <= 0) return;
        Recovered[k] = Recovered.GetValueOrDefault(k) + n;
    }

    /// <summary>The result of a battle (won, lost, or abandoned by flying off).</summary>
    public static Debrief Of(Battle b)
    {
        var m = b.Mission;
        var d = new Debrief { Name = m.Name, Kind = m.Kind, Won = b.Outcome == true, Aborted = b.Aborted, Turns = b.Turn };
        bool Aboard(BUnit u) => b.Exit.Contains((u.X, u.Y)) && u.Z == 0;
        foreach (var u in b.Squad)
        {
            // Stunned soldiers are carried out if the field is held; left behind otherwise, unless aboard.
            bool safe = !u.Dead && (d.Won || Aboard(u));
            if (safe) d.Survivors.Add(u); else d.Dead.Add(u);
        }
        foreach (var g in b.AliensKilled.GroupBy(a => a.Race))
            d.Line($"{Rules.Of(g.Key).Plural} KILLED", g.Count(), Rules.Of(g.Key).Score);
        if (d.Won)
        {
            foreach (var u in b.Units.Where(u => u.Side == Side.Alien && !u.Dead && u.Out)) d.Captured.Add((u.Race, u.AlienRank));
            foreach (var g in d.Captured.GroupBy(a => a.Race))
                d.Line($"LIVE {Rules.Of(g.Key).Plural} CAPTURED", g.Count(), Rules.Of(g.Key).Score * 2);
            // Everything on the field, and everything the squad carries.
            foreach (var items in b.Ground.Values)
                foreach (var i in items) Gather(d, i);
            foreach (var u in b.Squad)
                foreach (var i in u.Carried) Gather(d, i);
            // The UFO's parts: its hull, its drive cores (with their Vorium) and its nav lattice.
            int hull = 0;
            for (int z = 0; z < Battle.Levels; z++)
            for (int y = 0; y < b.H; y++)
            for (int x = 0; x < b.W; x++)
            {
                if (b.West[x, y, z] is Piece.UfoWall or Piece.HiveWall) hull++;
                if (b.North[x, y, z] is Piece.UfoWall or Piece.HiveWall) hull++;
                switch (b.Obj[x, y, z])
                {
                    case Piece.DriveCore: d.Add(ItemKind.DriveCore); d.Add(ItemKind.Vorium, 30 + b.Rng.Next(30)); break;
                    case Piece.NavConsole: d.Add(ItemKind.NavLattice); break;
                    case Piece.Console: if (b.Rng.Chance(0.4)) d.Add(ItemKind.Vorium, 3); break;
                }
            }
            d.Add(ItemKind.Hullmetal, hull / 5);
            if (m.Ufo is { } ut && m.Kind is MissionKind.CrashSite or MissionKind.LandedUfo)
                d.Lines.Add(($"{Rules.Of(ut).Name} RECOVERED", 1, Rules.Of(ut).Score / (m.Kind == MissionKind.CrashSite ? 3 : 2)));
            int civilians = b.Units.Count(u => u.Side == Side.Civilian && u.Alive);
            d.Line("CIVILIANS SAVED", civilians, 10);
            if (m.Kind == MissionKind.Terror) d.Lines.Add(("TERROR SITE CLEARED", 1, 200));
            if (m.Kind == MissionKind.AlienBase) d.Lines.Add(("ALIEN BASE DESTROYED", 1, 500));
            if (m.Kind == MissionKind.BaseDefence) d.Lines.Add(("BASE DEFENDED", 1, 300));
            if (m.Kind == MissionKind.Homeworld) d.Lines.Add(("THE HIVE MIND DESTROYED", 1, 2000));
        }
        else
        {
            // Only what's aboard comes home.
            foreach (var u in d.Survivors)
                foreach (var i in u.Carried) Gather(d, i);
            if (m.Kind == MissionKind.Terror) d.Lines.Add(("TERROR SITE ABANDONED", 1, -300));
        }
        foreach (var (k, n) in d.Recovered.ToList())
        {
            var info = Rules.Of(k);
            if (info.Score > 0 && info.Class is ItemClass.Artefact or ItemClass.Corpse or ItemClass.Weapon or ItemClass.Grenade && info.Alien)
                d.Line(info.Name + (n > 1 && info.Class is ItemClass.Weapon or ItemClass.Corpse or ItemClass.Grenade && !info.Name.EndsWith('S') ? "S" : ""), n, info.Score);
        }
        d.Line("CIVILIANS KILLED", b.CiviliansKilled, -15);
        d.Line("SOLDIERS LOST", d.Dead.Count, -20);
        return d;
    }

    private static void Gather(Debrief d, Item i)
    {
        d.Add(i.Kind);
        if (i.Clip is { Rounds: > 0 } c) d.Add(c.Kind);
    }

    /// <summary>
    /// The debrief applied to the campaign: points to the month (the organisation's, or the aliens' if negative), the
    /// finds to the base's stores, captives to containment while there's room, the dead to the memorial, the living
    /// their experience, wounds and promotions; the site cleared, a base lost, or the war won.
    /// </summary>
    public void Apply(Campaign c, Mission m)
    {
        var at = m.At;
        if (Points >= 0) c.Ours(Points, at); else c.Theirs(-Points, at);
        var home = c.BaseById(m.BaseId);
        if (home is not null)
        {
            foreach (var (k, n) in Recovered) home.Add(k, n);
            foreach (var cap in Captured)
            {
                if (home.Captives.Count < home.ContainmentSpace) home.Captives.Add(new Captive(cap.Race, cap.Rank));
                else
                {
                    Notes.Add($"A LIVE {Rules.Of(cap.Race).Name} DIED: NO ROOM IN ALIEN CONTAINMENT");
                    home.Add(Rules.Of(cap.Race).Corpse);
                }
            }
        }
        foreach (var u in Dead)
        {
            if (u.Soldier is not { } s) continue;
            c.Soldiers.Remove(s);
            c.Memorial.Add(new Fallen(s.Name, s.Rank, s.Nation, s.Missions + 1, s.Kills + u.Kills, c.Now, m.Name));
        }
        var rng = new Dice(m.Seed + 77);
        foreach (var u in Survivors)
        {
            if (u.Soldier is not { } s) continue;
            Grow(s, u, ref rng);
            s.Missions++;
            s.Kills += u.Kills;
            int lost = s.Health - Math.Max(0, u.Hp);
            if (lost > 0) s.WoundDays = Math.Max(s.WoundDays, lost * 1.5 + u.FatalWounds * 3);
            var rank = Soldier.RankFor(s.Merit);
            if (rank > s.Rank)
            {
                s.Rank = rank;
                Notes.Add($"{s.Name} PROMOTED TO {s.RankName}");
            }
        }
        // One commander at most: the best colonel is made up once there are enough soldiers.
        if (c.Soldiers.Count >= 20 && c.Soldiers.All(s => s.Rank != Rank.Commander) && c.Soldiers.Where(s => s.Rank == Rank.Colonel).MaxBy(s => s.Merit) is { } best && best.Merit >= 40)
        {
            best.Rank = Rank.Commander;
            Notes.Add($"{best.Name} PROMOTED TO COMMANDER");
        }
        if (Won)
        {
            if (c.SiteById(m.SiteId) is { } site) c.Sites.Remove(site);
            if (c.UfoById(m.UfoId) is { } ufo) ufo.State = UfoState.Gone;
            c.Ufos.RemoveAll(u => u.State == UfoState.Gone);
            if (m.Kind == MissionKind.Homeworld) c.End(true, "THE HIVE MIND OF NYX IS DEAD. ITS SLAVE RACES SCATTER, AND THE INVASION IS OVER.");
        }
        else
        {
            if (m.Kind == MissionKind.Terror && c.SiteById(m.SiteId) is { } site) { c.Sites.Remove(site); c.Theirs(600, at); }
            if (m.Kind == MissionKind.BaseDefence && home is not null) { c.LoseBase(home); return; }
        }
        if (c.CraftById(m.CraftId) is { } craft)
        {
            if (Survivors.Count == 0 && !Won && m.Kind != MissionKind.BaseDefence)
            {
                c.Crafts.Remove(craft);
                Notes.Add($"{craft.Name} WAS LOST WITH ITS CREW");
            }
            else c.SendHome(craft);
        }
    }

    /// <summary>A soldier's numbers grow with what they did: firing accuracy with hits, reactions with reaction shots,
    /// throwing with throws, bravery for coming through, psionic skill with use, and a little health, strength and
    /// time units for every mission.</summary>
    public static void Grow(Soldier s, BUnit u, ref Dice d)
    {
        if (u.Hits > 0) s.Firing = Math.Min(120, s.Firing + d.Range(1, 3) + Math.Min(u.Hits, 6) / 2);
        if (u.ReactionShots > 0) s.Reactions = Math.Min(100, s.Reactions + d.Range(1, 3) + Math.Min(u.ReactionShots, 4));
        if (u.Throws > 0) s.Throwing = Math.Min(120, s.Throwing + d.Range(1, 3));
        if (u.MeleeHits > 0) s.Strength = Math.Min(70, s.Strength + 1);
        if (u.PsiUses > 0) s.PsiSkill = Math.Min(100, s.PsiSkill + d.Range(1, 4));
        if (u.DamageTaken > 0 || u.Kills > 0) s.Bravery = Math.Min(100, s.Bravery + (d.Chance(0.4) ? 10 : 0));
        s.Tu = Math.Min(80, s.Tu + d.Range(0, 2));
        s.Health = Math.Min(60, s.Health + d.Range(0, 1));
        s.Strength = Math.Min(70, s.Strength + d.Range(0, 1));
    }
}
