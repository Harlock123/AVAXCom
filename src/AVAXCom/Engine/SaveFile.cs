using System.Globalization;
using System.Text;

namespace AVAXCom.Engine;

/// <summary>
/// Campaigns saved on the geoscape, in numbered slots (slot 0 is the automatic save made after every mission and at
/// every month's end): a small text file per slot, one line per thing (the war's state, each nation, base, facility,
/// stored item, captive, arrival, research and production job, craft and its guns, soldier, fallen soldier, UFO,
/// site, finished topic and month of history), its fields separated by bars. Kept next to the high-score table.
/// </summary>
public static class SaveFile
{
    private const string Magic = "AVAXCOM 1";
    public const int Slots = 6;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string PathIn(string folder, int slot) => Path.Combine(folder, slot == 0 ? "autosave.sav" : $"slot{slot}.sav");

    public static bool Exists(string folder, int slot) => File.Exists(PathIn(folder, slot));

    /// <summary>The slot saved to most recently, or -1 if there's none.</summary>
    public static int Latest(string folder)
    {
        int best = -1;
        DateTime when = DateTime.MinValue;
        for (int s = 0; s <= Slots; s++)
        {
            if (!Exists(folder, s)) continue;
            var t = File.GetLastWriteTimeUtc(PathIn(folder, s));
            if (t > when) { when = t; best = s; }
        }
        return best;
    }

    public static void Delete(string folder, int slot)
    {
        try { File.Delete(PathIn(folder, slot)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static string F(double v) => v.ToString("R", Inv);
    private static string L(string key, params object[] values) => key + "|" + string.Join("|", values.Select(v => v switch
    {
        double d => F(d),
        bool b => b ? "1" : "0",
        Enum e => Convert.ToInt32(e, Inv).ToString(Inv),
        _ => Convert.ToString(v, Inv)!.Replace('|', '/').Replace('\n', ' '),
    }));

    public static string Write(Campaign c)
    {
        var o = new StringBuilder();
        void W(string key, params object[] v) => o.AppendLine(L(key, v));
        o.AppendLine(Magic);
        W("war", c.Seed, c.Rng.State.ToString(Inv), c.Seconds, c.Money, c.Score, c.MonthOurs, c.MonthTheirs, c.NextId, c.NextUfoHours, c.TerrorsThisMonth,
            c.BasesThisMonth, c.Heat, c.TerribleMonths, c.DebtMonths, c.HourAcc, c.SweepAcc, c.LastMonth);
        for (int i = 0; i < c.Nations.Length; i++)
        {
            var n = c.Nations[i];
            W("nation", i, n.Funding, n.Satisfaction, n.Withdrawn, n.Ours, n.Theirs, n.LastChange);
        }
        foreach (var b in c.Bases)
        {
            W("base", b.Id, b.Name, b.At.Lat, b.At.Lon, b.Scientists, b.Engineers);
            foreach (var f in b.Facilities) W("facility", b.Id, f.Type, f.X, f.Y, f.DaysLeft);
            foreach (var (k, n) in b.Stores) W("store", b.Id, k, n);
            foreach (var cap in b.Captives) W("captive", b.Id, cap.Race, cap.Rank);
            foreach (var a in b.Arriving)
            {
                W("arrival", b.Id, a.Hours, a.What, a.Item, a.Count, a.Scientists, a.Engineers, a.Craft is { } ct ? (int)ct : -1, a.Soldier?.Id ?? -1);
                if (a.Soldier is { } s) WriteSoldier(o, s, "recruit");
            }
            foreach (var r in b.Research) W("research", b.Id, r.Topic, r.Scientists, r.Done);
            foreach (var p in b.Production) W("production", b.Id, p.Project, p.Engineers, p.Total, p.Made, p.Progress);
        }
        foreach (var k in c.Crafts)
        {
            W("craft", k.Id, k.BaseId, k.Type, k.Name, k.At.Lat, k.At.Lon, k.State, k.Damage, k.Fuel, k.TargetUfo, k.TargetSite,
                k.Waypoint?.Lat ?? double.NaN, k.Waypoint?.Lon ?? double.NaN);
            foreach (var g in k.Guns) W("gun", k.Id, g.Kind, g.Rounds);
        }
        foreach (var s in c.Soldiers) WriteSoldier(o, s, "soldier");
        foreach (var f in c.Memorial) W("fallen", f.Name, f.Rank, f.Nation, f.Missions, f.Kills, f.Date.Ticks, f.Where);
        foreach (var u in c.Ufos)
            W("ufo", u.Id, u.Type, u.Race, u.Mission, u.At.Lat, u.At.Lon, u.Dest.Lat, u.Dest.Lon, u.State, u.Hours, u.Legs, u.Damage, u.Detected, u.Known, u.TargetBase, u.Heading);
        foreach (var s in c.Sites)
            W("site", s.Id, s.Kind, s.At.Lat, s.At.Lon, s.Ufo, s.Race, double.IsInfinity(s.Hours) ? -1.0 : s.Hours, s.Known, s.Damage, s.Name);
        foreach (var t in c.Researched) W("done", t);
        foreach (var h in c.History) W("month", h.Year, h.Month, h.Ours, h.Theirs, h.Funding, h.Money, h.Rating);
        if (c.PendingDefence is var (pb, pu, pr, pd)) W("defence", pb, pu, pr, pd);
        return o.ToString();
    }

    private static void WriteSoldier(StringBuilder o, Soldier s, string key) =>
        o.AppendLine(L(key, s.Id, s.Name, s.Nation, s.Female, s.Tu, s.Health, s.Reactions, s.Firing, s.Throwing, s.Strength, s.Bravery, s.PsiStrength, s.PsiSkill,
            s.Rank, s.Missions, s.Kills, s.WoundDays, s.Armour, s.Weapon, s.BaseId, s.CraftId, s.PsiTraining, s.PsiKnown));

    private static Soldier ReadSoldier(string[] v) => new()
    {
        Id = I(v[1]), Name = v[2], Nation = Math.Clamp(I(v[3]), 0, Earth.Nations.Length - 1), Female = v[4] == "1", Tu = I(v[5]), Health = I(v[6]), Reactions = I(v[7]),
        Firing = I(v[8]), Throwing = I(v[9]), Strength = I(v[10]), Bravery = I(v[11]), PsiStrength = I(v[12]), PsiSkill = I(v[13]), Rank = E<Rank>(v[14]),
        Missions = I(v[15]), Kills = I(v[16]), WoundDays = D(v[17]), Armour = E<ArmourType>(v[18]), Weapon = E<ItemKind>(v[19]), BaseId = I(v[20]),
        CraftId = I(v[21]), PsiTraining = v[22] == "1", PsiKnown = v[23] == "1",
    };

    private static int I(string s) => int.Parse(s, Inv);
    private static double D(string s) => double.Parse(s, Inv);
    private static T E<T>(string s) where T : struct, Enum
    {
        int n = I(s);
        if (!Enum.IsDefined(typeof(T), n)) throw new FormatException("bad value");
        return (T)Enum.ToObject(typeof(T), n);
    }

    /// <summary>A campaign read back, or null if the text isn't a save (or is damaged).</summary>
    public static Campaign? Read(string text)
    {
        try
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length < 2 || lines[0] != Magic) return null;
            Campaign? c = null;
            var recruits = new Dictionary<int, Soldier>();
            foreach (var line in lines.Skip(1))
            {
                var v = line.Split('|');
                if (c is null && v[0] != "war") return null;
                switch (v[0])
                {
                    case "war":
                        c = new Campaign(I(v[1]));
                        c.Rng.State = ulong.Parse(v[2], Inv);
                        c.Seconds = D(v[3]); c.Money = I(v[4]); c.Score = I(v[5]); c.MonthOurs = I(v[6]); c.MonthTheirs = I(v[7]); c.NextId = I(v[8]);
                        c.NextUfoHours = D(v[9]); c.TerrorsThisMonth = I(v[10]); c.BasesThisMonth = I(v[11]); c.Heat = D(v[12]); c.TerribleMonths = I(v[13]);
                        c.DebtMonths = I(v[14]); c.HourAcc = D(v[15]); c.SweepAcc = D(v[16]); c.LastMonth = I(v[17]);
                        break;
                    case "nation":
                        var n = c!.Nations[I(v[1])];
                        n.Funding = I(v[2]); n.Satisfaction = I(v[3]); n.Withdrawn = v[4] == "1"; n.Ours = I(v[5]); n.Theirs = I(v[6]); n.LastChange = I(v[7]);
                        break;
                    case "base":
                        c!.Bases.Add(new Base { Id = I(v[1]), Name = v[2], At = new GeoPoint(D(v[3]), D(v[4])), Scientists = I(v[5]), Engineers = I(v[6]) });
                        break;
                    case "facility":
                        c!.BaseById(I(v[1]))!.Facilities.Add(new Facility { Type = E<FacilityType>(v[2]), X = I(v[3]), Y = I(v[4]), DaysLeft = D(v[5]) });
                        break;
                    case "store":
                        c!.BaseById(I(v[1]))!.Add(E<ItemKind>(v[2]), I(v[3]));
                        break;
                    case "captive":
                        c!.BaseById(I(v[1]))!.Captives.Add(new Captive(E<Race>(v[2]), E<AlienRank>(v[3])));
                        break;
                    case "recruit":
                        var r = ReadSoldier(v);
                        recruits[r.Id] = r;
                        break;
                    case "arrival":
                        int ct = I(v[8]);
                        c!.BaseById(I(v[1]))!.Arriving.Add(new Arrival
                        {
                            Hours = D(v[2]), What = v[3], Item = E<ItemKind>(v[4]), Count = I(v[5]), Scientists = I(v[6]), Engineers = I(v[7]),
                            Craft = ct >= 0 ? (CraftType)ct : null, // a recruit's soldier is matched below, from the line after
                        });
                        break;
                    case "research":
                        c!.BaseById(I(v[1]))!.Research.Add(new ResearchJob { Topic = E<Topic>(v[2]), Scientists = I(v[3]), Done = D(v[4]) });
                        break;
                    case "production":
                        Rules.Project(v[2]);
                        c!.BaseById(I(v[1]))!.Production.Add(new ProductionJob { Project = v[2], Engineers = I(v[3]), Total = I(v[4]), Made = I(v[5]), Progress = D(v[6]) });
                        break;
                    case "craft":
                        double wl = D(v[12]), wo = D(v[13]);
                        c!.Crafts.Add(new Craft
                        {
                            Id = I(v[1]), BaseId = I(v[2]), Type = E<CraftType>(v[3]), Name = v[4], At = new GeoPoint(D(v[5]), D(v[6])), State = E<CraftState>(v[7]),
                            Damage = D(v[8]), Fuel = D(v[9]), TargetUfo = I(v[10]), TargetSite = I(v[11]), Waypoint = double.IsNaN(wl) ? null : new GeoPoint(wl, wo),
                        });
                        break;
                    case "gun":
                        c!.CraftById(I(v[1]))!.Guns.Add(new CraftGun { Kind = E<ItemKind>(v[2]), Rounds = I(v[3]) });
                        break;
                    case "soldier":
                        c!.Soldiers.Add(ReadSoldier(v));
                        break;
                    case "fallen":
                        c!.Memorial.Add(new Fallen(v[1], E<Rank>(v[2]), I(v[3]), I(v[4]), I(v[5]), new DateTime(long.Parse(v[6], Inv), DateTimeKind.Utc), v[7]));
                        break;
                    case "ufo":
                        c!.Ufos.Add(new Ufo
                        {
                            Id = I(v[1]), Type = E<UfoType>(v[2]), Race = E<Race>(v[3]), Mission = E<UfoMission>(v[4]), At = new GeoPoint(D(v[5]), D(v[6])),
                            Dest = new GeoPoint(D(v[7]), D(v[8])), State = E<UfoState>(v[9]), Hours = D(v[10]), Legs = I(v[11]), Damage = D(v[12]),
                            Detected = v[13] == "1", Known = v[14] == "1", TargetBase = I(v[15]), Heading = D(v[16]),
                        });
                        break;
                    case "site":
                        double hours = D(v[7]);
                        c!.Sites.Add(new Site
                        {
                            Id = I(v[1]), Kind = E<SiteKind>(v[2]), At = new GeoPoint(D(v[3]), D(v[4])), Ufo = E<UfoType>(v[5]), Race = E<Race>(v[6]),
                            Hours = hours < 0 ? double.PositiveInfinity : hours, Known = v[8] == "1", Damage = D(v[9]), Name = v[10],
                        });
                        break;
                    case "done":
                        c!.Researched.Add(E<Topic>(v[1]));
                        break;
                    case "month":
                        c!.History.Add(new MonthRecord(I(v[1]), I(v[2]), I(v[3]), I(v[4]), I(v[5]), I(v[6]), v[7]));
                        break;
                    case "defence":
                        c!.PendingDefence = (I(v[1]), E<UfoType>(v[2]), E<Race>(v[3]), D(v[4]));
                        break;
                }
            }
            if (c is null) return null;
            // Recruits on their way are matched to their arrivals, in order.
            foreach (var b in c.Bases)
                foreach (var a in b.Arriving.Where(a => a.Scientists == 0 && a.Engineers == 0 && a.Item == ItemKind.None && a.Craft is null && a.Soldier is null))
                {
                    var r = recruits.Values.FirstOrDefault(s => s.BaseId == b.Id && a.What == s.Name);
                    if (r is null) continue;
                    a.Soldier = r;
                    recruits.Remove(r.Id);
                }
            return c;
        }
        catch (Exception e) when (e is FormatException or OverflowException or IndexOutOfRangeException or NullReferenceException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    public static bool Save(Campaign c, string folder, int slot)
    {
        try
        {
            Directory.CreateDirectory(folder);
            string path = PathIn(folder, slot), temp = path + ".tmp";
            File.WriteAllText(temp, Write(c));
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static Campaign? Load(string folder, int slot)
    {
        try
        {
            return Exists(folder, slot) ? Read(File.ReadAllText(PathIn(folder, slot))) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A slot's one-line summary for the save and load screen.</summary>
    public static string Describe(string folder, int slot)
    {
        var c = Load(folder, slot);
        if (c is null) return Exists(folder, slot) ? "(DAMAGED)" : "(EMPTY)";
        var t = c.Now;
        return $"{t.Day} {t:MMMM} {t.Year}".ToUpperInvariant() + $"   ${c.Money / 1000:N0}K   SCORE {c.Score + c.MonthOurs - c.MonthTheirs}   {c.Bases.Count} BASE{(c.Bases.Count == 1 ? "" : "S")}";
    }
}
