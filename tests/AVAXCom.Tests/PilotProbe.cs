using AVAXCom.Engine;

namespace AVAXCom.Tests;

/// <summary>
/// How the pilot gets on (set XCOM_PROBE to run it): ground missions of each kind, round by round, and (XCOM_PROBE=war)
/// a whole war on the geoscape with the pilot running it. XCOM_MISSIONS sets how many battles.
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_pilot_does()
    {
        if (Environment.GetEnvironmentVariable("XCOM_PROBE") is not { } mode) return;
        if (mode == "war") { War(); return; }
        if (mode == "mission") { CampaignMission(); return; }
        int n = int.TryParse(Environment.GetEnvironmentVariable("XCOM_MISSIONS"), out int k) ? k : 6;
        var kinds = new[] { (MissionKind.CrashSite, Terrain.Farmland, UfoType.Raider, Race.Pallid), (MissionKind.LandedUfo, Terrain.Forest, UfoType.Skiff, Race.Skimmer),
            (MissionKind.CrashSite, Terrain.Urban, UfoType.Harvester, Race.Coilback), (MissionKind.Terror, Terrain.Urban, UfoType.Carrier, Race.Brakk),
            (MissionKind.LandedUfo, Terrain.Desert, UfoType.Raider, Race.Pallid), (MissionKind.CrashSite, Terrain.Arctic, UfoType.Dreadnought, Race.Veiled) };
        for (int i = 0; i < n; i++)
        {
            var (kind, terrain, ufo, race) = kinds[i % kinds.Length];
            if (int.TryParse(Environment.GetEnvironmentVariable("XCOM_ONLY"), out int only) && only != i) continue;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var b = BattleGen.Skirmish(100 + i, terrain, ufo, race, night: i % 3 == 2, kind: kind, month: i);
            b.Instant = true;
            int start = b.Aliens;
            for (int r = 0; r < 40 && b.Outcome is null; r++)
            {
                Pilot.PlayRound(b);
                if (Environment.GetEnvironmentVariable("XCOM_TRACE") is not null)
                {
                    output.WriteLine($"  turn {b.Turn}: " + string.Join("; ", b.News.Concat(b.Log)));
                    output.WriteLine("    squad: " + string.Join(" ", b.Squad.Select(u => $"{u.X},{u.Y},{u.Z}:{u.Hp}{(u.Dead ? "X" : "")}")) + " | aliens: " + string.Join(" ", b.Units.Where(u => u.Side == Side.Alien).Select(u => $"{u.Race}@{u.X},{u.Y},{u.Z}:{u.Hp}{(u.Dead ? "X" : u.Out ? "o" : "")}")));
                    b.News.Clear(); b.Log.Clear();
                }
            }
            output.WriteLine($"{kind} {terrain} {ufo} {race}{(b.Night ? " night" : "")}: {(b.Outcome == true ? "WON" : b.Outcome == false ? "LOST" : "UNDECIDED")} turn {b.Turn}; aliens {start}->{b.Aliens}; " +
                $"squad {b.Squad.Count(u => u.Alive)}/{b.Squad.Count()} alive; civilians killed {b.CiviliansKilled}; {sw.ElapsedMilliseconds} ms");
        }
    }

    /// <summary>A landed Skiff fought from a real campaign, traced turn by turn.</summary>
    private void CampaignMission()
    {
        for (int seed = 1; seed <= 6; seed++)
        {
            var c = new Campaign(seed);
            c.FoundFirstBase(Earth.Cities.First(x => x.Name == "MUNICH").At);
            if (Environment.GetEnvironmentVariable("XCOM_NIGHT") is not null) c.Seconds = 12 * 3600;
            var raven = c.Crafts.First(k => k.Type == CraftType.Raven);
            var type = Enum.TryParse<UfoType>(Environment.GetEnvironmentVariable("XCOM_UFO"), out var t) ? t : UfoType.Skiff;
            Mission m;
            if (Environment.GetEnvironmentVariable("XCOM_CRASH") is not null)
            {
                var site = new Site { Id = c.NextId++, Kind = SiteKind.CrashSite, At = new GeoPoint(48 + seed, 10), Ufo = type, Race = Race.Pallid, Hours = 40, Known = true, Damage = 0.6, Name = "CRASH SITE" };
                c.Sites.Add(site);
                c.Launch(raven, site: site);
                raven.At = site.At;
                m = c.MissionFor(raven)!;
            }
            else
            {
                var u = c.SpawnUfo(UfoMission.Research, type, Race.Pallid);
                u.At = new GeoPoint(48 + seed, 10);
                u.State = UfoState.Landed;
                u.Hours = 10;
                c.Detect(u);
                c.Launch(raven, u);
                raven.At = u.At;
                raven.TargetUfo = u.Id;
                m = c.MissionFor(raven)!;
            }
            var b = BattleGen.Make(m, c.BaseById(m.BaseId));
            b.Instant = true;
            output.WriteLine($"seed {seed}: {m.Terrain} night={m.Night} crew {m.Crew} squad {m.Squad.Count} weapons " + string.Join(",", b.Squad.Select(x => x.Right?.Kind.ToString() ?? "-")));
            for (int r = 0; r < 40 && b.Outcome is null; r++)
            {
                Pilot.PlayRound(b);
                output.WriteLine($"  turn {b.Turn}: " + string.Join("; ", b.News.Concat(b.Log)));
                output.WriteLine("    squad: " + string.Join(" ", b.Squad.Select(x => $"{x.X},{x.Y},{x.Z}:{x.Hp}{(x.Dead ? "X" : x.Out ? "o" : "")}")) + " | aliens: " + string.Join(" ", b.Units.Where(x => x.Side == Side.Alien).Select(x => $"{x.Race}@{x.X},{x.Y},{x.Z}:{x.Hp}{(x.Dead ? "X" : x.Out ? "o" : "")}")));
                b.News.Clear(); b.Log.Clear();
            }
            output.WriteLine($"  => {(b.Outcome == true ? "WON" : b.Outcome == false ? "LOST" : "UNDECIDED")}");
        }
    }

    private void War()
    {
        var c = new Campaign(5);
        c.FoundFirstBase(Earth.Cities.First(x => x.Name == "MUNICH").At);
        int battles = 0, won = 0;
        for (int hour = 0; hour < 24 * 400 && !c.Over; hour++)
        {
            Pilot.Manage(c);
            c.Advance(3600);
            foreach (var e in c.Events.ToList())
            {
                c.Events.Remove(e);
                switch (e.Kind)
                {
                    case EventKind.Intercept when c.CraftById(e.Craft) is { } k && c.UfoById(e.Ufo) is { } u:
                        var air = c.Engage(k, u);
                        air.Mode = AirMode.Standard;
                        air.Resolve();
                        output.WriteLine($"{c.Now:yyyy-MM-dd} air {k.Name} vs {u.Info.Name}: {air.Result}");
                        c.Resolve(air);
                        break;
                    case EventKind.CraftArrived when c.CraftById(e.Craft) is { } k2 && c.MissionFor(k2) is { } m:
                        Fight(c, m, ref battles, ref won);
                        break;
                    case EventKind.BaseAttack when c.DefenceMission() is { } dm:
                        Fight(c, dm, ref battles, ref won);
                        break;
                    case EventKind.MonthlyReport or EventKind.TerrorSite or EventKind.UfoDowned or EventKind.AlienBaseFound:
                        output.WriteLine($"{c.Now:yyyy-MM-dd} {e.Title}: {e.Text}");
                        break;
                }
            }
        }
        output.WriteLine($"END {c.Now:yyyy-MM-dd} over={c.Over} won={c.Won} {c.Ending} battles {won}/{battles} score {c.Score} money {c.Money} soldiers {c.Soldiers.Count} fallen {c.Memorial.Count} researched {c.Researched.Count}");
    }

    private void Fight(Campaign c, Mission m, ref int battles, ref int won)
    {
        var b = BattleGen.Make(m, c.BaseById(m.BaseId));
        b.Instant = true;
        for (int r = 0; r < 40 && b.Outcome is null; r++) Pilot.PlayRound(b);
        if (b.Outcome is null) { b.Aborted = true; b.Outcome = false; }
        var d = Debrief.Of(b);
        d.Apply(c, m);
        battles++;
        if (d.Won) won++;
        output.WriteLine($"{c.Now:yyyy-MM-dd} {m.Name} ({m.Race}, {m.Squad.Count} soldiers): {(d.Won ? "WON" : "LOST")} {d.Points} pts, dead {d.Dead.Count}, captured {d.Captured.Count}");
    }
}
