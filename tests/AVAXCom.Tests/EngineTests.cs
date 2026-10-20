using Avalonia.Input;
using AVAXCom.Engine;
using static AVAXCom.Tests.TestWorld;

namespace AVAXCom.Tests;

/// <summary>The game around the rules: the title, a new war and its first base, the geoscape's clock and its events, the
/// interception window, a mission from the briefing through the battle to the debrief, saving and loading, the end of
/// the war and the high scores, the demonstration, the battle's camera and orders, and the keys.</summary>
public class EngineTests
{
    private static void Ticks(GameEngine g, double seconds)
    {
        for (int i = 0; i < (int)(seconds * 60); i++) g.Tick();
    }

    // ───────────────────────────── a new war ─────────────────────────────

    [Fact]
    public void A_new_war_starts_on_the_globe_with_the_first_base_to_place_on_land()
    {
        var g = NewGame();
        Assert.Equal(GameState.Title, g.State);
        g.Tick(default, start: true);
        Assert.Equal(GameState.Geoscape, g.State);
        Assert.True(g.PlacingBase);
        Assert.False(g.TimeRunning);
        var before = g.Campaign!.Now;
        Ticks(g, 1);
        Assert.Equal(before, g.Campaign.Now);
        g.GlobeClick(new GeoPoint(30, -40));
        Assert.True(g.PlacingBase);
        Assert.Equal("BASES MUST BE BUILT ON LAND", g.Message);
        g.GlobeClick(Earth.Cities.First(c => c.Name == "MOSCOW").At);
        Assert.False(g.PlacingBase);
        Assert.Equal("MOSCOW", g.Campaign.Bases.Single().Name);
        Assert.True(g.TimeRunning);
        Assert.True(SaveFile.Exists(g.SaveFolder, 0));
    }

    [Fact]
    public void Time_runs_at_the_chosen_speed_and_stops_for_the_screens()
    {
        var g = NewGame();
        var c = g.QuickStart();
        var t0 = c.Now;
        Ticks(g, 1);
        Assert.InRange((c.Now - t0).TotalSeconds, 55, 65);
        g.SetSpeed(3);
        t0 = c.Now;
        Ticks(g, 1);
        Assert.True((c.Now - t0).TotalHours >= 4 || g.Event is not null);
        g.SetSpeed(0);
        g.Open(GeoScreen.Research);
        Assert.False(g.TimeRunning);
        t0 = c.Now;
        Ticks(g, 1);
        Assert.Equal(t0, c.Now);
        g.CloseScreen();
        Assert.True(g.TimeRunning);
    }

    [Fact]
    public void A_UFO_found_stops_the_clock_and_the_player_can_send_an_interceptor_after_it()
    {
        var g = NewGame();
        var c = g.QuickStart("PARIS");
        g.SetSpeed(4);
        var u = c.SpawnUfo(UfoMission.Scout, UfoType.Skiff);
        u.At = new GeoPoint(47, 6);
        u.Dest = new GeoPoint(45, 8);
        u.Legs = 99;
        c.Detect(u);
        g.Tick();
        Assert.Equal(GeoScreen.Event, g.Screen);
        Assert.Equal(EventKind.UfoDetected, g.Event!.Kind);
        Assert.Equal(0, g.SpeedIndex);
        Assert.False(g.TimeRunning);
        g.Answer(1);
        Assert.Equal(GeoScreen.Intercept, g.Screen);
        Assert.Equal(u, g.TargetUfo);
        var falcon = g.InterceptChoices().First(k => k.Type == CraftType.Falcon);
        Assert.Null(g.LaunchAt(falcon));
        Assert.Equal(GeoScreen.Globe, g.Screen);
        Assert.Equal(CraftState.Out, falcon.State);
        // The craft catches up: the air battle opens.
        for (int i = 0; i < 60 * 120 && g.Screen != GeoScreen.AirCombat; i++) g.Tick();
        Assert.Equal(GeoScreen.AirCombat, g.Screen);
        Assert.False(g.TimeRunning);
        g.SetAirMode(AirMode.Cautious);
        Assert.Equal(AirMode.Cautious, g.Air!.Mode);
        for (int i = 0; i < 60 * 120 && g.Air is not null; i++) g.Tick();
        Assert.Null(g.Air);
        Assert.NotEqual(GeoScreen.AirCombat, g.Screen);
    }

    // ───────────────────────────── a mission ─────────────────────────────

    /// <summary>A war with the transport arrived at a crash site, its event showing.</summary>
    private static (GameEngine G, Campaign C, Craft Raven, Site Site) AtCrashSite()
    {
        var g = NewGame();
        var c = g.QuickStart("BERLIN");
        var raven = c.Crafts.First(k => k.Type == CraftType.Raven);
        var site = new Site { Id = c.NextId++, Kind = SiteKind.CrashSite, At = new GeoPoint(52, 14), Ufo = UfoType.Skiff, Race = Race.Pallid, Hours = 40, Known = true, Damage = 0.5, Name = "CRASH SITE" };
        c.Sites.Add(site);
        Assert.Null(c.Launch(raven, site: site));
        for (int i = 0; i < 60 * 60 && g.Event?.Kind != EventKind.CraftArrived; i++)
        {
            g.Tick();
            if (g.Event is { Kind: not EventKind.CraftArrived }) g.Answer();
        }
        Assert.Equal(EventKind.CraftArrived, g.Event!.Kind);
        return (g, c, raven, site);
    }

    [Fact]
    public void From_the_briefing_through_a_won_battle_and_the_debrief_back_to_the_globe()
    {
        var (g, c, raven, site) = AtCrashSite();
        g.Answer(0);
        Assert.Equal(GameState.Briefing, g.State);
        Assert.Equal(MissionKind.CrashSite, g.Mission!.Kind);
        g.Tick(default, start: true);
        Assert.Equal(GameState.Battle, g.State);
        var b = g.Battle!;
        Assert.Equal(raven.Id, b.Mission.CraftId);
        Assert.Equal(c.CrewOf(raven).Count(), b.Squad.Count());
        Assert.Equal(b.Squad.First(), g.Selected);
        // Every alien falls: the battle is won, and after a moment the debrief shows.
        foreach (var a in b.Units.Where(u => u.Side == Side.Alien).ToList()) Tac.Kill(b, a, b.Squad.First(), DamageType.Kinetic);
        Ticks(g, GameEngine.OutcomeSeconds + 0.5);
        Assert.Equal(GameState.Debrief, g.State);
        Assert.True(g.Debrief!.Won);
        Assert.DoesNotContain(site, c.Sites);
        Ticks(g, 1);
        g.Tick(default, start: true);
        Assert.Equal(GameState.Geoscape, g.State);
        Assert.Equal(CraftState.Returning, raven.State);
        Assert.Null(g.Battle);
    }

    [Fact]
    public void Aborting_leaves_with_those_aboard_and_the_site_stays_to_be_tried_again()
    {
        var (g, c, raven, site) = AtCrashSite();
        g.Answer(0);
        g.BeginBattle();
        var b = g.Battle!;
        var outside = b.Squad.Last();
        outside.X = 2; outside.Y = 2;
        g.ToggleBattleMenu();
        Assert.True(g.BattleMenu);
        g.Abort();
        Assert.False(b.Outcome);
        Ticks(g, GameEngine.OutcomeSeconds + 0.5);
        Assert.Equal(GameState.Debrief, g.State);
        Assert.True(g.Debrief!.Aborted);
        Assert.Contains(site, c.Sites);
        Assert.Single(c.Memorial);
        Assert.Equal(7, c.Soldiers.Count);
    }

    [Fact]
    public void Turning_back_sends_the_transport_home_without_a_fight()
    {
        var (g, _, raven, _) = AtCrashSite();
        g.Answer(1);
        Assert.Equal(GameState.Geoscape, g.State);
        Assert.Equal(CraftState.Returning, raven.State);
    }

    // ───────────────────────────── the battle's orders and camera ─────────────────────────────

    private static (GameEngine G, Battle B) InBattle()
    {
        var g = NewGame();
        var b = BattleGen.Skirmish(12, soldiers: 4);
        g.StartBattle(b);
        return (g, b);
    }

    [Fact]
    public void A_click_picks_out_a_soldier_or_walks_the_one_picked_out_and_armed_clicks_fire()
    {
        var (g, b) = InBattle();
        var first = b.Squad.First();
        var second = b.Squad.ElementAt(1);
        g.ClickTile(new Cell(second.X, second.Y, 0));
        Assert.Equal(second, g.Selected);
        var goal = new Cell(second.X, second.Y - 3, 0);
        g.ClickTile(goal);
        Assert.True(b.Busy);
        Ticks(g, 2);
        Assert.Equal((goal.X, goal.Y), (second.X, second.Y));
        // Armed: the next click fires instead.
        g.ArmAction(Arm.Snap, second.Right);
        Assert.Equal(Arm.Snap, g.Armed);
        int rounds = second.Right!.Clip!.Rounds;
        g.ClickTile(new Cell(second.X, second.Y - 6, 0));
        Ticks(g, 1);
        Assert.Equal(rounds - 1, second.Right.Clip!.Rounds);
        g.RightClickTile(default);
        Assert.Equal(Arm.None, g.Armed);
        // Next picks out the next soldier with time units, and centres on them.
        g.Select(first);
        g.NextSoldier();
        Assert.Equal(second, g.Selected);
    }

    [Fact]
    public void Ending_the_turn_hands_over_to_the_aliens_and_back()
    {
        var (g, b) = InBattle();
        g.Kneel();
        Assert.True(g.Selected!.Kneeling);
        g.EndTurn();
        Assert.Equal(Side.Alien, b.Active);
        for (int i = 0; i < 60 * 120 && b.Active != Side.Player && b.Outcome is null; i++) g.Tick();
        Assert.Equal(Side.Player, b.Active);
        Assert.Equal(2, b.Turn);
        Assert.All(b.Squad.Where(u => u.Active && u.Team == Side.Player), u => Assert.Equal(Tac.TurnTu(u), u.Tu));
    }

    [Fact]
    public void The_camera_scrolls_the_way_the_keys_point_and_maps_the_screen_to_the_tiles()
    {
        var (g, b) = InBattle();
        using var input = new InputHandler(gamepad: false);
        g.CenterOn(20, 20, 0);
        double y = g.CamY, x = g.CamX;
        input.OnKeyDown(Key.Up, PhysicalKey.ArrowUp);
        for (int i = 0; i < 20; i++) g.Tick(input.Player);
        Assert.True(g.CamY < y);
        Assert.Equal(x, g.CamX);
        input.OnKeyUp(Key.Up, PhysicalKey.ArrowUp);
        input.PointAt((Iso.Width - 1, 300));
        for (int i = 0; i < 20; i++) g.Tick(input.Player);
        Assert.True(g.CamX > x);
        // The middle of the view is the tile the camera is on.
        g.CenterOn(12, 9, 0);
        Assert.Equal(new Cell(12, 9, 0), g.TileAt(Iso.Width / 2, Iso.MapH / 2));
        g.LevelUp();
        Assert.Equal(1, g.ViewLevel);
        g.LevelDown();
        g.LevelDown();
        Assert.Equal(0, g.ViewLevel);
        var (sx, sy) = Iso.ToScreen(7, 3, 1);
        Assert.Equal((7, 3), Iso.ToTile(sx + 1, sy + 1, 1));
    }

    [Fact]
    public void The_inventory_moves_kit_by_picking_it_up_and_putting_it_down()
    {
        var (g, b) = InBattle();
        var u = g.Selected!;
        var clip = u.Belt.First(i => i.Info.Class == ItemClass.Ammo);
        g.ToggleInventory();
        Assert.True(g.Inventory);
        g.InventoryClick(Slot.Belt, clip);
        Assert.Equal(clip, g.Holding!.Value.Item);
        g.InventoryClick(Slot.LeftHand, null);
        Assert.Equal(clip, u.Left);
        Assert.Null(g.Holding);
        // Aboard on the first turn, it was free.
        Assert.Equal(u.MaxTu, u.Tu);
        g.ToggleInventory();
        Assert.False(g.Inventory);
    }

    // ───────────────────────────── saving and loading ─────────────────────────────

    [Fact]
    public void A_war_saved_in_a_slot_is_the_same_war_loaded_back()
    {
        var g = NewGame();
        var c = g.QuickStart("SYDNEY");
        var b = c.Bases[0];
        c.Money += 1_234_567;
        b.Add(ItemKind.Vorium, 37);
        b.Captives.Add(new Captive(Race.Veiled, AlienRank.Commander));
        Assert.Null(c.StartResearch(b, Topic.LaserWeapons, 7));
        b.Research[0].Done = 12.5;
        c.Researched.Add(Topic.LaserRifle);
        Assert.Null(c.StartProduction(b, "LASER RIFLE", 3, 6));
        Assert.Null(c.Hire(b, Campaign.Staff.Soldier, 1));
        Assert.Null(c.Build(b, FacilityType.Lab, 3, 3));
        var u = c.SpawnUfo(UfoMission.Harvest, UfoType.Harvester, Race.Brakk);
        c.Detect(u);
        c.Sites.Add(new Site { Id = c.NextId++, Kind = SiteKind.AlienBase, At = new GeoPoint(-20, 30), Race = Race.Coilback, Ufo = UfoType.Harvester, Hours = double.PositiveInfinity, Name = "ALIEN BASE" });
        c.Memorial.Add(new Fallen("ANNA SILVA", Rank.Sergeant, 2, 5, 7, c.Now, "CRASHED RAIDER"));
        c.Soldiers[0].Rank = Rank.Captain;
        c.Soldiers[0].Armour = ArmourType.Carapace;
        c.MonthEnd();
        c.Events.Clear();
        var falcon = c.Crafts.First(k => k.Type == CraftType.Falcon);
        falcon.Damage = 12;
        falcon.Guns[0].Rounds = 3;
        Assert.True(g.SaveSlot(3));
        var text = SaveFile.Write(c);
        var back = SaveFile.Read(text)!;
        Assert.Equal(text, SaveFile.Write(back));
        // And through a fresh game.
        var g2 = new GameEngine(HighScoreTable.Load(g.SaveFolder), g.SaveFolder) { Silent = true };
        Assert.True(g2.LoadSlot(3));
        var c2 = g2.Campaign!;
        Assert.Equal(GameState.Geoscape, g2.State);
        Assert.Equal(c.Money, c2.Money);
        Assert.Equal(c.Now, c2.Now);
        Assert.Equal(37, c2.Bases[0].Count(ItemKind.Vorium));
        Assert.Equal(Race.Veiled, c2.Bases[0].Captives.Single().Race);
        Assert.Equal(12.5, c2.Bases[0].Research.Single().Done);
        Assert.Equal("LASER RIFLE", c2.Bases[0].Production.Single().Project);
        Assert.Single(c2.Bases[0].Arriving, a => a.Soldier is not null);
        Assert.False(c2.Bases[0].At2(3, 3)!.Built);
        Assert.True(c2.Ufos.Single().Detected);
        Assert.Equal(Race.Brakk, c2.Ufos.Single().Race);
        Assert.Contains(c2.Sites, s => s.Kind == SiteKind.AlienBase && double.IsPositiveInfinity(s.Hours));
        Assert.Equal("ANNA SILVA", c2.Memorial.Single().Name);
        Assert.Equal(Rank.Captain, c2.Soldiers[0].Rank);
        Assert.Equal(ArmourType.Carapace, c2.Soldiers[0].Armour);
        Assert.Single(c2.History);
        Assert.Equal(12, c2.Crafts.First(k => k.Type == CraftType.Falcon).Damage);
        Assert.Equal(3, c2.Crafts.First(k => k.Type == CraftType.Falcon).Guns[0].Rounds);
        Assert.Contains(Topic.LaserRifle, c2.Researched);
        // The two run on alike.
        c.Advance(3600 * 5);
        c2.Advance(3600 * 5);
        Assert.Equal(SaveFile.Write(c), SaveFile.Write(c2));
    }

    [Fact]
    public void Damaged_or_strange_saves_are_refused_and_the_newest_slot_is_continued()
    {
        Assert.Null(SaveFile.Read(""));
        Assert.Null(SaveFile.Read("NOT A SAVE\nwar|1"));
        Assert.Null(SaveFile.Read("AVAXCOM 1\nwar|x|y"));
        var g = NewGame();
        Assert.False(g.HasSave);
        Assert.False(g.Continue());
        var c = g.QuickStart();
        Assert.True(g.SaveSlot(2));
        c.Money = 77;
        Assert.True(g.SaveSlot(5));
        var g2 = new GameEngine(HighScoreTable.Load(g.SaveFolder), g.SaveFolder) { Silent = true };
        Assert.True(g2.Continue());
        Assert.Equal(77, g2.Campaign!.Money);
        File.WriteAllText(SaveFile.PathIn(g.SaveFolder, 2), "AVAXCOM 1\ngarbage");
        Assert.Equal("(DAMAGED)", SaveFile.Describe(g.SaveFolder, 2));
        Assert.Equal("(EMPTY)", SaveFile.Describe(g.SaveFolder, 4));
        Assert.False(g2.LoadSlot(2));
    }

    // ───────────────────────────── the end of the war ─────────────────────────────

    [Fact]
    public void A_lost_war_goes_to_the_initials_and_the_roll_of_honour()
    {
        var g = NewGame();
        var c = g.QuickStart();
        c.Score = 50000;
        c.End(false, "THE COUNCIL HAS TERMINATED THE PROJECT.");
        g.Tick();
        Assert.Equal(GameState.GameOver, g.State);
        Ticks(g, GameEngine.OverSeconds + 0.2);
        Assert.Equal(GameState.EnterInitials, g.State);
        g.Initials!.Type('Z');
        g.Initials.Type('O');
        g.Initials.Type('E');
        g.Tick();
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(0, g.NewEntryIndex);
        Assert.Equal("ZOE", g.HighScores.Entries[0].Initials);
        Assert.Equal(50000 + c.MonthOurs - c.MonthTheirs, g.HighScores.Entries[0].Score);
        Assert.False(g.HasSave);
        Ticks(g, GameEngine.HighScoreSeconds + 0.2);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void Left_at_the_title_the_game_plays_itself_on_the_globe_then_on_the_ground()
    {
        var g = NewGame();
        Ticks(g, GameEngine.TitleIdleSeconds + 0.5);
        Assert.Equal(GameState.Attract, g.State);
        Assert.True(g.Autopilot);
        Assert.Null(g.Battle);
        Assert.Single(g.Campaign!.Bases);
        for (int i = 0; i < 60 * 90 && g.Battle is null; i++) g.Tick();
        var b = g.Battle!;
        Assert.NotEmpty(b.Squad);
        // The pilot takes the squad out of the transport.
        Ticks(g, 20);
        Assert.True(b.Squad.Any(u => !b.Exit.Contains((u.X, u.Y))) || g.State == GameState.HighScores);
        for (int i = 0; i < 60 * (int)GameEngine.AttractSeconds && g.State == GameState.Attract; i++) g.Tick();
        Assert.Equal(GameState.HighScores, g.State);
    }

    // ───────────────────────────── the keys ─────────────────────────────

    [Fact]
    public void The_keys()
    {
        using var input = new InputHandler(gamepad: false);
        input.OnKeyDown(Key.Up, PhysicalKey.ArrowUp);
        Assert.Equal(-1, input.Player.Dy);                  // up is -1, as on the screen and in the engine
        Assert.Equal(0, input.Player.Dx);
        input.OnKeyDown(Key.Down, PhysicalKey.ArrowDown);
        Assert.Equal(1, input.Player.Dy);                   // the newer of two opposite keys wins
        input.OnKeyUp(Key.Down, PhysicalKey.ArrowDown);
        Assert.Equal(-1, input.Player.Dy);
        input.OnKeyUp(Key.Up, PhysicalKey.ArrowUp);
        Assert.Equal(0, input.Player.Dy);
        input.OnKeyDown(Key.NumPad4);
        Assert.Equal(-1, input.Player.Dx);
        input.OnKeyUp(Key.NumPad4);
        // A Shift let go arrives as CapsLock: the physical key lets go of the Shift all the same.
        input.OnKeyDown(Key.LeftShift, PhysicalKey.ShiftLeft);
        Assert.True(input.Shift);
        input.OnKeyUp(Key.CapsLock, PhysicalKey.ShiftLeft);
        Assert.False(input.Shift);
        input.OnKeyDown(Key.LeftCtrl, PhysicalKey.ControlLeft);
        Assert.True(input.Ctrl);
        input.Clear();
        Assert.False(input.Ctrl);
        input.PointAt((300, 200));
        Assert.Equal((300.0, 200.0), input.Player.Point);
        input.OnKeyDown(Key.Enter);
        Assert.True(input.Start);
    }

    [Fact]
    public void Every_sound_the_engine_asks_for_has_its_file()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Sounds");
        foreach (var s in Enum.GetValues<Sfx>().Where(s => s != Sfx.MusicStop))
            Assert.True(File.Exists(Path.Combine(folder, s + ".wav")), s.ToString());
    }

    [Fact]
    public void The_arrows_turn_the_globe_the_way_they_point()
    {
        var g = NewGame();
        g.QuickStart();
        g.View = new GeoPoint(10, 10);
        g.Rotate(0, 5);
        Assert.Equal(15, g.View.Lat, 6);
        g.Rotate(-20, 0);
        Assert.Equal(-10, g.View.Lon, 6);
        g.ZoomBy(1);
        Assert.Equal(GameEngine.Zooms[2], g.Zoom);
        g.ZoomBy(99);
        Assert.Equal(GameEngine.Zooms[^1], g.Zoom);
    }
}
