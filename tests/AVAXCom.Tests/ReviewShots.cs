using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAXCom.Engine;
using AVAXCom.Views;

namespace AVAXCom.Tests;

/// <summary>
/// More of the game drawn for a look over it while it's being made (set XCOM_REVIEW to a folder): the underground maps,
/// a base's defence, night, terror, and the screens the README doesn't show.
/// </summary>
public sealed class ReviewShots
{
    private static readonly string? Dir = Environment.GetEnvironmentVariable("XCOM_REVIEW");

    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        string folder = TestWorld.TempFolder();
        var g = new GameEngine(HighScoreTable.Load(folder), folder, seed: 5) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
        return (w, canvas, g);
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
        var frame = w.CaptureRenderedFrame()!;
        Directory.CreateDirectory(Dir!);
        frame.Save(Path.Combine(Dir!, $"review-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static void Battle(string name, Mission m, Base? stores = null, bool reveal = true)
    {
        var (w, c, g) = Open();
        var b = BattleGen.Make(m, stores);
        g.StartBattle(b);
        g.Idle(300);
        if (reveal)
        {
            for (int z = 0; z < Engine.Battle.Levels; z++)
            for (int y = 0; y < b.H; y++)
            for (int x = 0; x < b.W; x++) b.Explored[x, y, z] = b.Visible[x, y, z] = true;
            foreach (var u in b.Units) u.Seen = true;
        }
        g.CenterOn(b.Objective.X, b.Objective.Y + 2, 0);
        c.Advance(0.3);
        Shot(w, c, name);
    }

    private static Mission M(MissionKind kind, Terrain t, Race race, UfoType? ufo, bool night = false, int crew = 8)
    {
        var d = new Dice(7);
        var m = new Mission { Kind = kind, Terrain = t, Race = race, Ufo = ufo, Night = night, Crew = crew, Seed = 77, Month = 4, Name = kind.ToString().ToUpperInvariant(), Damage = kind == MissionKind.CrashSite ? 0.7 : 0 };
        for (int i = 0; i < 8; i++) m.Squad.Add(Soldier.Recruit(ref d, 100 + i, 0));
        return m;
    }

    [AvaloniaFact]
    public void Review_the_maps()
    {
        if (Dir is null) return;
        Battle("alien-base", M(MissionKind.AlienBase, Terrain.AlienBase, Race.Coilback, null, crew: 12));
        Battle("hive", M(MissionKind.Homeworld, Terrain.Hive, Race.Veiled, null, crew: 14));
        Battle("terror", M(MissionKind.Terror, Terrain.Urban, Race.Brakk, null, crew: 9));
        Battle("crash-dreadnought", M(MissionKind.CrashSite, Terrain.Desert, Race.Veiled, UfoType.Dreadnought, crew: 10));
        Battle("night", M(MissionKind.LandedUfo, Terrain.Forest, Race.Skimmer, UfoType.Raider, night: true), reveal: false);
        Battle("arctic", M(MissionKind.LandedUfo, Terrain.Arctic, Race.Pallid, UfoType.Harvester, crew: 9));
        var c = new Campaign(3);
        var b = c.FoundFirstBase(new GeoPoint(50, 10));
        b.Put(FacilityType.Lab, 3, 3);
        b.Put(FacilityType.Containment, 0, 2);
        var dm = M(MissionKind.BaseDefence, Terrain.BaseInterior, Race.Brakk, UfoType.Carrier, crew: 8);
        dm.Base = b;
        Battle("base-defence", dm);
    }

    [AvaloniaFact]
    public void Review_the_demonstration()
    {
        if (Dir is null) return;
        var (w, c, g) = Open();
        for (int i = 0; i < 60 * 30 && g.State != GameState.Attract; i++) g.Tick();
        for (int i = 0; i < 60 * 8; i++) g.Tick();
        c.Advance(0.2);
        Shot(w, c, "attract-geo");
        for (int i = 0; i < 60 * 60 && g.Battle is null; i++) g.Tick();
        for (int i = 0; i < 60 * 25 && g.State == GameState.Attract; i++) g.Tick();
        c.Advance(0.2);
        Shot(w, c, "attract-battle");
    }

    [AvaloniaFact]
    public void Review_the_screens()
    {
        if (Dir is null) return;
        var (w, c, g) = Open();
        var camp = g.QuickStart("CAIRO");
        camp.Seconds = 3600 * 24 * 40;
        g.Open(GeoScreen.Soldiers);
        g.SelectedSoldier = camp.Soldiers[2];
        Shot(w, c, "soldiers");
        g.CloseScreen();
        g.CloseScreen();
        camp.History.Add(new MonthRecord(2031, 1, 900, 400, 3870000, 5000000, "GOOD"));
        camp.Nations[3].LastChange = 12;
        camp.Nations[5].LastChange = -8;
        camp.Nations[7].Withdrawn = true;
        g.Open(GeoScreen.Funding);
        Shot(w, c, "funding");
        g.CloseScreen();
        camp.Researched.UnionWith([Topic.LaserRifle, Topic.CarapaceArmour, Topic.StormRifle]);
        camp.Bases[0].Add(ItemKind.Hullmetal, 20);
        Assert.Null(camp.StartProduction(camp.Bases[0], "LASER RIFLE", 5, 6));
        g.Open(GeoScreen.Manufacture);
        Shot(w, c, "manufacture");
        g.CloseScreen();
        g.Open(GeoScreen.Purchase);
        Shot(w, c, "purchase");
        g.CloseScreen();
        g.OpenFiles(true);
        Shot(w, c, "files");
        g.CloseScreen();
        camp.Bases[0].Add(ItemKind.StormRifle, 3);
        camp.Bases[0].Add(ItemKind.PallidCorpse, 2);
        g.Open(GeoScreen.Sell);
        Shot(w, c, "sell");
        g.CloseScreen();
        camp.Memorial.Add(new Fallen("ANNA SILVA", Rank.Sergeant, 2, 5, 7, camp.Now, "CRASHED RAIDER"));
        camp.Memorial.Add(new Fallen("JOHN CARTER", Rank.Rookie, 0, 1, 0, camp.Now, "TERROR IN CAIRO"));
        g.Open(GeoScreen.Memorial);
        Shot(w, c, "memorial");
        g.CloseScreen();
        var u = camp.SpawnUfo(UfoMission.Research, UfoType.Harvester, Race.Brakk);
        u.At = new GeoPoint(28, 30);
        camp.Detect(u);
        g.Tick();
        Shot(w, c, "event");
        g.Answer(1);
        Shot(w, c, "intercept");
        g.CloseScreen();
        g.Zoom = 850;
        g.View = new GeoPoint(30, 25);
        Shot(w, c, "zoomed");
        var raven = camp.Crafts.First(k => k.Type == CraftType.Raven);
        var site = new Site { Id = camp.NextId++, Kind = SiteKind.Terror, At = new GeoPoint(30, 31), Race = Race.Brakk, Hours = 20, Known = true, Name = "CAIRO" };
        camp.Sites.Add(site);
        camp.Launch(raven, site: site);
        raven.At = site.At;
        g.Answer();
        var m = camp.MissionFor(raven)!;
        typeof(GameEngine).GetMethod("Brief", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(g, [m]);
        c.Advance(1);
        Shot(w, c, "briefing");
        // The aliens' turn, unseen.
        g.BeginBattle();
        g.EndTurn();
        for (int i = 0; i < 10; i++) g.Tick();
        Shot(w, c, "hidden-movement");
        camp.End(false, "THE COUNCIL HAS LOST ALL CONFIDENCE IN THE PROJECT AND TERMINATED IT.");
        g.Abort();
        for (int i = 0; i < 60 * 3; i++) g.Tick();
        g.FinishDebrief();
        g.Tick();
        c.Advance(0.5);
        Shot(w, c, "game-over");
    }
}
