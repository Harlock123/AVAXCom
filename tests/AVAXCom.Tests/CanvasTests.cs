using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AVAXCom.Engine;
using AVAXCom.Views;

namespace AVAXCom.Tests;

/// <summary>The keys and clicks as the window gets them, through the canvas, in a headless window.</summary>
public sealed class CanvasTests
{
    private static (GameCanvas Canvas, GameEngine G) Open()
    {
        string folder = TestWorld.TempFolder();
        var g = new GameEngine(HighScoreTable.Load(folder), folder, seed: 9) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        for (int i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();
        return (canvas, g);
    }

    private static void Click(GameCanvas c, Point p)
    {
        c.LeftDown(p, false);
        c.LeftUp(p);
    }

    [AvaloniaFact]
    public void The_title_starts_a_war_and_a_click_on_the_globe_founds_the_base()
    {
        var (c, g) = Open();
        c.PressKey(Key.L);
        c.PressKey(Key.Escape);
        Assert.Equal(GameState.Title, g.State);
        Click(c, new Point(512, 472));
        Assert.Equal(GameState.Geoscape, g.State);
        Assert.True(g.PlacingBase);
        var berlin = Earth.Cities.First(x => x.Name == "BERLIN").At;
        g.View = berlin;
        Click(c, c.ScreenOf(berlin));
        Assert.False(g.PlacingBase);
        Assert.Equal("BERLIN", g.Campaign!.Bases.Single().Name);
    }

    [AvaloniaFact]
    public void The_geoscape_answers_its_keys_and_its_buttons()
    {
        var (c, g) = Open();
        g.QuickStart();
        c.PressKey(Key.D4);
        Assert.Equal(3, g.SpeedIndex);
        c.PressKey(Key.D1);
        Assert.Equal(0, g.SpeedIndex);
        c.PressKey(Key.B);
        Assert.Equal(GeoScreen.Base, g.Screen);
        c.PressKey(Key.Escape);
        Assert.Equal(GeoScreen.Globe, g.Screen);
        c.PressKey(Key.R);
        Assert.Equal(GeoScreen.Research, g.Screen);
        c.PressKey(Key.Escape);
        c.PressKey(Key.F5);
        Assert.Equal(GeoScreen.Files, g.Screen);
        Assert.True(g.Saving);
        c.PressKey(Key.D2);
        Assert.True(SaveFile.Exists(g.SaveFolder, 2));
        Assert.Equal(GeoScreen.Globe, g.Screen);
        // The command panel's buttons: speeds and screens.
        Click(c, new Point(902, 263));
        Assert.Equal(4, g.SpeedIndex);
        Click(c, new Point(902, 347));
        Assert.Equal(GeoScreen.Base, g.Screen);
        Click(c, new Point(938, 728));
        Assert.Equal(GeoScreen.Globe, g.Screen);
        c.PressKey(Key.M);
        Assert.True(c.Sound.IsMuted);
    }

    [AvaloniaFact]
    public void Building_a_facility_by_clicking_the_list_and_the_grid()
    {
        var (c, g) = Open();
        var camp = g.QuickStart();
        var b = camp.Bases[0];
        g.Open(GeoScreen.Base);
        Click(c, new Point(724, 504));
        // The list's first row is the hangar; the third the laboratory.
        Click(c, new Point(800, 96 + 2 * 30 + 14));
        Assert.Equal(FacilityType.Lab, g.Building);
        Click(c, new Point(28 + 3 * 96 + 48, 96 + 3 * 96 + 48));
        Assert.NotNull(b.At2(3, 3));
        Assert.Equal(FacilityType.Lab, b.At2(3, 3)!.Type);
        Assert.False(b.At2(3, 3)!.Built);
        Assert.Null(g.Building);
    }

    [AvaloniaFact]
    public void An_event_window_takes_a_click_or_a_key_and_the_air_battle_takes_its_modes()
    {
        var (c, g) = Open();
        var camp = g.QuickStart("PARIS");
        var u = camp.SpawnUfo(UfoMission.Scout, UfoType.Raider);
        u.At = new GeoPoint(48, 3);
        u.Dest = u.At;
        u.Legs = 99;
        camp.Detect(u);
        g.Tick();
        Assert.Equal(GeoScreen.Event, g.Screen);
        c.PressKey(Key.D2);
        Assert.Equal(GeoScreen.Intercept, g.Screen);
        var falcon = g.InterceptChoices().First(k => k.Type == CraftType.Falcon);
        int row = g.InterceptChoices().IndexOf(falcon);
        Click(c, new Point(300, 150 + 92 + row * 34 + 15));
        Assert.Equal(CraftState.Out, falcon.State);
        for (int i = 0; i < 60 * 120 && g.Screen != GeoScreen.AirCombat; i++) g.Tick();
        Assert.Equal(GeoScreen.AirCombat, g.Screen);
        c.PressKey(Key.D4);
        Assert.Equal(AirMode.Aggressive, g.Air!.Mode);
        Click(c, new Point(84 + 124 + 59, 610 - 50 + 19));
        Assert.Equal(AirMode.Cautious, g.Air!.Mode);
    }

    [AvaloniaFact]
    public void Battle_keys_pick_out_kneel_change_storey_open_the_kit_and_end_the_turn()
    {
        var (c, g) = Open();
        var b = BattleGen.Skirmish(14, soldiers: 4);
        g.StartBattle(b);
        var first = g.Selected!;
        c.PressKey(Key.Tab);
        Assert.NotEqual(first, g.Selected);
        c.PressKey(Key.Tab, shift: true);
        Assert.Equal(first, g.Selected);
        c.PressKey(Key.K);
        Assert.True(first.Kneeling);
        c.PressKey(Key.PageUp);
        Assert.Equal(1, g.ViewLevel);
        c.PressKey(Key.PageDown);
        Assert.Equal(0, g.ViewLevel);
        c.PressKey(Key.I);
        Assert.True(g.Inventory);
        c.PressKey(Key.Escape);
        Assert.False(g.Inventory);
        c.PressKey(Key.D2);
        Assert.Equal(Arm.Aimed, g.Armed);
        c.PressKey(Key.Escape);
        Assert.Equal(Arm.None, g.Armed);
        c.PressKey(Key.Escape);
        Assert.True(g.BattleMenu);
        c.PressKey(Key.Escape);
        Assert.False(g.BattleMenu);
        c.PressKey(Key.E);
        Assert.Equal(Side.Alien, b.Active);
    }

    [AvaloniaFact]
    public void Battle_clicks_walk_a_soldier_and_the_hand_opens_its_menu()
    {
        var (c, g) = Open();
        var b = BattleGen.Skirmish(15, soldiers: 4);
        g.StartBattle(b);
        var u = g.Selected!;
        g.CenterOn(u.X, u.Y - 2, 0);
        var goal = new Cell(u.X, u.Y - 3, 0);
        Click(c, c.TileScreen(goal));
        Assert.True(b.Busy);
        for (int i = 0; i < 120; i++) g.Tick();
        Assert.Equal((goal.X, goal.Y), (u.X, u.Y));
        // The right hand's box opens the rifle's menu; its first entry arms a snap shot.
        Click(c, new Point(366, 700));
        Click(c, new Point(400, Iso.MapH + 14 - 8 - 4 * 32 + 15));
        Assert.Equal(Arm.Snap, g.Armed);
        // The squad strip picks out a soldier.
        Click(c, new Point(8 + 40 * 2 + 18, 22));
        Assert.Equal(b.Squad.ElementAt(2), g.Selected);
        // The end-turn button.
        Click(c, new Point(540 + 3 * 96 + 45, Iso.MapH + 14 + 56 + 25));
        Assert.Equal(Side.Alien, b.Active);
    }

    [AvaloniaFact]
    public void The_inventory_moves_kit_with_clicks()
    {
        var (c, g) = Open();
        var b = BattleGen.Skirmish(16, soldiers: 2);
        g.StartBattle(b);
        var u = g.Selected!;
        var clip = u.Belt[0];
        c.PressKey(Key.I);
        Click(c, new Point(270 + 26, 410 + 26));
        Assert.Equal(clip, g.Holding!.Value.Item);
        Click(c, new Point(560 + 55, 170 + 85));
        Assert.Equal(clip, u.Left);
        Click(c, new Point(940, 722));
        Assert.False(g.Inventory);
    }
}
