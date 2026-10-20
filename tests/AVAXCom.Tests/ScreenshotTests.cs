using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAXCom.Engine;
using AVAXCom.Views;

namespace AVAXCom.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set XCOM_SHOTS to a folder to save them as
/// PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 5)
    {
        string folder = TestWorld.TempFolder();
        var g = new GameEngine(HighScoreTable.Load(folder), folder, seed: seed) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("XCOM_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAXCom-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(15);
        Shot(w, c, "title");
        g.HighScores.Insert("AVA", 21480, 9);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + 1)));
        Assert.Equal(GameState.Attract, g.State);
        for (int i = 0; i < 60 * 200 && g.State != GameState.HighScores; i++) g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Shot(w, c, "high-scores");
    }

    /// <summary>A war a few days old: a base in Europe, an interceptor on its way to a UFO over the Atlantic.</summary>
    private static Campaign War(GameEngine g)
    {
        var camp = g.QuickStart("MUNICH");
        camp.Seconds = 3600 * 30;
        var b = camp.Bases[0];
        b.Facilities.Add(new Facility { Type = FacilityType.LargeRadar, X = 3, Y = 3 });
        var u = camp.SpawnUfo(UfoMission.Research, UfoType.Raider, Race.Pallid);
        u.At = new GeoPoint(44, -14);
        u.Dest = new GeoPoint(36, -6);
        camp.Detect(u);
        camp.Events.Clear();
        return camp;
    }

    [AvaloniaFact]
    public void The_geoscape_tracking_a_ufo_and_the_interception()
    {
        var (w, c, g) = Open();
        var camp = War(g);
        var u = camp.Ufos[0];
        var falcon = camp.Crafts.First(k => k.Type == CraftType.Falcon);
        Assert.Null(camp.Launch(falcon, u));
        camp.Advance(60 * 25);
        camp.Events.Clear();
        g.View = new GeoPoint(38, -6);
        g.ZoomBy(1);
        g.Say("UFO DETECTED: FALCON-1 INTERCEPTING", 5);
        c.Advance(0.4);
        Shot(w, c, "geoscape");
        // The interception itself.
        for (int i = 0; i < 60 * 60 && g.Screen != GeoScreen.AirCombat; i++) g.Tick();
        Assert.Equal(GeoScreen.AirCombat, g.Screen);
        g.SetAirMode(AirMode.Aggressive);
        for (int i = 0; i < 60 * 30 && !(g.Air!.Shots.Count >= 2 && g.Air.Distance < 8); i++) { g.Tick(); c.Advance(1 / 60.0); }
        Shot(w, c, "interception");
    }

    [AvaloniaFact]
    public void The_base_and_the_laboratories()
    {
        var (w, c, g) = Open();
        var camp = War(g);
        var b = camp.Bases[0];
        camp.Money += 3_000_000;
        Assert.Null(camp.Build(b, FacilityType.Containment, 0, 2));
        Assert.Null(camp.Build(b, FacilityType.Lab, 5, 2));
        b.Facilities.First(f => f.Type == FacilityType.Lab && !f.Built).DaysLeft = 0;
        Assert.Null(camp.Build(b, FacilityType.MissileDefence, 0, 3));
        camp.Bases[0].Facilities.First(f => f.Type == FacilityType.Containment).DaysLeft = 4;
        g.Open(GeoScreen.Base);
        g.SelectedCraft = camp.Crafts.First(k => k.Type == CraftType.Falcon);
        c.PointAt(new Point(400, 300));
        c.Advance(0.5);
        Shot(w, c, "base");
        // The research screen: lasers and an autopsy on the benches, finds and a captive waiting.
        b.Add(ItemKind.PallidCorpse, 2);
        b.Add(ItemKind.SkimmerCorpse, 1);
        b.Add(ItemKind.StormPistol, 1);
        b.Add(ItemKind.Vorium, 40);
        b.Add(ItemKind.Hullmetal, 12);
        b.Add(ItemKind.NavLattice, 1);
        b.Captives.Add(new Captive(Race.Pallid, AlienRank.Navigator));
        b.Scientists = 40;
        camp.Researched.Add(Topic.LaserWeapons);
        Assert.Null(camp.StartResearch(b, Topic.LaserPistol, 14));
        Assert.Null(camp.StartResearch(b, Topic.PallidAutopsy, 8));
        Assert.Null(camp.StartResearch(b, Topic.Hullmetal, 10));
        b.Research[0].Done = 60;
        b.Research[1].Done = 14;
        b.Research[2].Done = 33;
        g.Open(GeoScreen.Research);
        var topics = camp.Available(b).ToList();
        c.PointAt(new Point(200, 410 + topics.IndexOf(Topic.PallidInterrogation) * 30 + 14));
        c.Advance(0.2);
        Shot(w, c, "research");
    }

    /// <summary>A battle on farmland against Pallids, everything seen.</summary>
    private static Battle Field(GameEngine g, int seed = 21, MissionKind kind = MissionKind.LandedUfo)
    {
        var b = BattleGen.Skirmish(seed, Terrain.Farmland, UfoType.Raider, Race.Pallid, soldiers: 8, kind: kind);
        g.StartBattle(b);
        return b;
    }

    [AvaloniaFact]
    public void Deployment_from_the_transport()
    {
        var (w, c, g) = Open();
        var b = Field(g);
        g.Idle(60 * 5);
        var lead = b.Squad.First();
        g.CenterOn(lead.X, lead.Y - 5, 0);
        g.Select(b.Squad.ElementAt(2));
        c.PointAt(new Point(560, 250));
        c.Advance(0.3);
        Shot(w, c, "deployment");
    }

    /// <summary>The squad put out near the UFO, spread over tiles from which an alien outside it is in plain view.</summary>
    private static (Battle B, BUnit Shooter, BUnit Alien) Contact(GameEngine g, int seed = 21)
    {
        for (int k = 0; k < 30; k++)
            if (TryContact(g, seed + k) is { } found) return found;
        throw new InvalidOperationException("no open ground for a firefight");
    }

    private static (Battle B, BUnit Shooter, BUnit Alien)? TryContact(GameEngine g, int seed)
    {
        var b = Field(g, seed);
        g.Idle(60 * 5);
        foreach (var u in b.Squad) u.X = -100;
        BUnit? alien = null;
        var spots = new List<Cell>();
        var probe = b.Squad.First();
        bool Open(int x, int y)
        {
            for (int dy = -1; dy <= 2; dy++)
            for (int dx = -1; dx <= 2; dx++)
            {
                if (!b.In(x + dx, y + dy, 0)) return false;
                if (b.West[x + dx, y + dy, 0] != Piece.None || b.North[x + dx, y + dy, 0] != Piece.None || b.Floor[x + dx, y + dy, 0] == Piece.UfoFloor) return false;
            }
            return true;
        }
        foreach (var a in b.Units.Where(u => u.Side == Side.Alien && !u.Guard && u.Z == 0 && Open(u.X, u.Y)).OrderBy(u => Math.Abs(u.X - b.W / 2)))
        {
            spots.Clear();
            for (int r = 5; r <= 9; r++)
                for (int dx = -r; dx <= r; dx++)
                {
                    var cell = new Cell(a.X + dx, a.Y + r, 0);
                    if (!b.In(cell) || b.UnitAt(cell) is not null || Pieces.Of(b.Obj[cell.X, cell.Y, 0]).BlocksMove || b.Floor[cell.X, cell.Y, 1] != Piece.None) continue;
                    if (spots.Any(s => Math.Abs(s.X - cell.X) + Math.Abs(s.Y - cell.Y) < 2)) continue;
                    probe.X = cell.X; probe.Y = cell.Y; probe.Z = 0;
                    probe.Facing = Tac.DirOf(a.X - cell.X, a.Y - cell.Y);
                    if (Tac.Sees(b, probe, a)) spots.Add(cell);
                }
            if (spots.Count >= 5) { alien = a; break; }
        }
        if (alien is null) return null;
        int i = 0;
        foreach (var u in b.Squad)
        {
            var cell = i < spots.Count ? spots[i] : b.Exit.Select(e => new Cell(e.X, e.Y, 0)).First(e => b.UnitAt(e) is null);
            u.X = cell.X; u.Y = cell.Y; u.Z = 0;
            u.Facing = Tac.DirOf(alien!.X - u.X, alien.Y - u.Y);
            u.Kneeling = i % 3 == 1;
            i++;
        }
        alien!.Facing = Tac.DirOf(b.Squad.First().X - alien.X, b.Squad.First().Y - alien.Y);
        b.Turn = 3;
        Tac.Look(b);
        var shooter = b.Squad.Where(u => Tac.Sees(b, u, alien)).OrderBy(u => Math.Abs(u.X - alien.X) + Math.Abs(u.Y - alien.Y)).First();
        return (b, shooter, alien);
    }

    [AvaloniaFact]
    public void A_firefight_with_the_chance_to_hit()
    {
        var (w, c, g) = Open();
        var (b, shooter, alien) = Contact(g);
        Assert.True(alien.Seen);
        g.Select(shooter);
        g.CenterOn((shooter.X + alien.X) / 2.0, (shooter.Y + alien.Y) / 2.0, 0);
        // The aimed shot armed, and a shot already on its way from a squad mate.
        g.ArmAction(Arm.Aimed, shooter.Right);
        Assert.Equal(Arm.Aimed, g.Armed);
        var mate = b.Squad.Where(u => u != shooter && u.Active && Tac.Sees(b, u, alien) && Tac.Trace(b, Tac.Eye(u), Tac.Centre(alien), shot: true, ignore: u).Unit == alien)
            .OrderByDescending(u => Math.Abs(u.X - alien.X) + Math.Abs(u.Y - alien.Y)).First();
        Assert.Null(Tac.Shoot(b, mate, new Cell(alien.X, alien.Y, alien.Z), FireMode.Snap));
        for (int i = 0; i < 4; i++) Tac.Step(b, 0.03);
        var at = c.TileScreen(new Cell(alien.X, alien.Y, alien.Z));
        c.PointAt(at);
        c.Advance(0.2);
        Shot(w, c, "firefight");
    }

    [AvaloniaFact]
    public void A_grenade_brings_down_a_wall()
    {
        var (w, c, g) = Open();
        var b = BattleGen.Skirmish(33, Terrain.Urban, UfoType.Raider, Race.Coilback, soldiers: 8, kind: MissionKind.LandedUfo);
        g.StartBattle(b);
        g.Idle(60 * 5);
        // A house wall near the squad, and a grenade going off against it.
        var lead = b.Squad.First();
        Cell? wall = null;
        for (int r = 3; r < 20 && wall is null; r++)
            for (int dx = -r; dx <= r && wall is null; dx++)
            {
                int x = lead.X + dx, y = lead.Y - r;
                if (b.In(x, y, 0) && b.West[x, y, 0] is Piece.Brick && b.Floor[x, y, 1] != Piece.None) wall = new Cell(x, y, 0);
            }
        Assert.NotNull(wall);
        foreach (var u in b.Squad) { u.X = Math.Clamp(wall.Value.X - 3 + u.Id % 4, 0, b.W - 1); u.Y = Math.Clamp(wall.Value.Y + 4 + u.Id % 2, 0, b.H - 1); u.Facing = 0; }
        Tac.Look(b);
        g.CenterOn(wall.Value.X, wall.Value.Y + 1, 0);
        Tac.Explode(b, wall.Value, 70, 3, DamageType.Explosive, null);
        for (int i = 0; i < 8; i++) Tac.Step(b, 0.03);
        g.Select(b.Squad.First());
        c.Advance(0.2);
        Shot(w, c, "explosion");
    }

    [AvaloniaFact]
    public void The_inventory()
    {
        var (w, c, g) = Open();
        var b = Field(g);
        g.Idle(60 * 5);
        var u = b.Squad.First();
        g.Select(u);
        u.Backpack.Add(Item.Make(ItemKind.FieldKit));
        u.Left = Item.Make(ItemKind.FragGrenade);
        b.ItemsAt(new Cell(u.X, u.Y, u.Z)).Add(Item.Make(ItemKind.Pistol));
        b.ItemsAt(new Cell(u.X, u.Y, u.Z)).Add(Item.Make(ItemKind.PistolClip));
        g.ToggleInventory();
        Assert.True(g.Inventory);
        c.PointAt(new Point(257, 250));
        c.Advance(0.2);
        Shot(w, c, "inventory");
    }

    [AvaloniaFact]
    public void The_debrief()
    {
        var (w, c, g) = Open();
        var camp = War(g);
        camp.Bases[0].Put(FacilityType.Containment, 1, 4);
        var raven = camp.Crafts.First(k => k.Type == CraftType.Raven);
        var site = new Site { Id = camp.NextId++, Kind = SiteKind.CrashSite, At = new GeoPoint(47, 2), Ufo = UfoType.Raider, Race = Race.Pallid, Hours = 40, Known = true, Damage = 0.6, Name = "CRASH SITE" };
        camp.Sites.Add(site);
        Assert.Null(camp.Launch(raven, site: site));
        raven.At = site.At;
        var m = camp.MissionFor(raven)!;
        var b = BattleGen.Make(m, camp.Bases[0]);
        b.Instant = true;
        // The squad wins: every alien down, two taken alive.
        int n = 0;
        foreach (var a in b.Units.Where(u => u.Side == Side.Alien).ToList())
        {
            if (n++ < 2) { a.Stun = a.Hp; a.Out = true; }
            else Tac.Kill(b, a, b.Squad.First(), DamageType.Laser);
        }
        Tac.Kill(b, b.Squad.Last(), null, DamageType.Plasma);
        b.Squad.First().Hits = 3;
        Tac.CheckEnd(b);
        Assert.True(b.Outcome);
        g.StartBattle(b);
        for (int i = 0; i < 60 * 4 && g.State != GameState.Debrief; i++) g.Tick();
        Assert.Equal(GameState.Debrief, g.State);
        g.Idle(120);
        c.Advance(1);
        Shot(w, c, "debrief");
    }
}
