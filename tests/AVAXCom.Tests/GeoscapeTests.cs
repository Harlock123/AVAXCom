using AVAXCom.Engine;

namespace AVAXCom.Tests;

/// <summary>The geoscape's rules: the globe's geometry and projection, the land, the sun, radar and UFOs, the
/// interception, the air battle, crash and terror sites, the funding council's monthly report, the research tree,
/// manufacturing, the base layout, staff and stores, craft in their hangars, and the end of the war.</summary>
public class GeoscapeTests
{
    /// <summary>Runs the clock on through whatever would stop it for the player.</summary>
    private static void Run(Campaign c, double seconds)
    {
        while (seconds > 1e-6 && !c.Over)
        {
            double ran = c.Advance(seconds);
            if (ran <= 0) break;
            seconds -= ran;
        }
    }

    private static Campaign NewWar(string city = "LONDON", int seed = 9)
    {
        var c = new Campaign(seed);
        c.FoundFirstBase(Earth.Cities.First(x => x.Name == city).At);
        return c;
    }

    // ───────────────────────────── the globe ─────────────────────────────

    [Fact]
    public void The_projection_puts_the_centre_in_the_middle_and_unprojecting_brings_a_place_back()
    {
        var centre = new GeoPoint(40, -30);
        var (x, y, front) = Geo.Project(centre, centre);
        Assert.Equal(0, x, 9);
        Assert.Equal(0, y, 9);
        Assert.True(front);
        // North is up, east is right.
        Assert.True(Geo.Project(new GeoPoint(50, -30), centre).Y > 0);
        Assert.True(Geo.Project(new GeoPoint(40, -20), centre).X > 0);
        // The far side is hidden.
        Assert.False(Geo.Project(new GeoPoint(-40, 150), centre).Front);
        foreach (var p in new[] { new GeoPoint(51.5, -0.1), new GeoPoint(10, -60), new GeoPoint(-5, 20), new GeoPoint(75, -10) })
        {
            var (px, py, f) = Geo.Project(p, centre);
            Assert.True(f);
            var back = Geo.Unproject(px, py, centre)!.Value;
            Assert.Equal(p.Lat, back.Lat, 6);
            Assert.Equal(p.Lon, back.Lon, 6);
        }
        Assert.Null(Geo.Unproject(0.9, 0.9, centre));
    }

    [Fact]
    public void Distances_run_along_great_circles_and_toward_moves_that_far()
    {
        var london = new GeoPoint(51.5, -0.1);
        var ny = new GeoPoint(40.7, -74);
        double nm = Geo.Nm(london, ny);
        Assert.InRange(nm, 2900, 3050);
        var half = Geo.Toward(london, ny, nm / 2);
        Assert.Equal(nm / 2, Geo.Nm(london, half), 0);
        Assert.Equal(nm / 2, Geo.Nm(half, ny), 0);
        Assert.Equal(ny, Geo.Toward(london, ny, nm * 2));
        // A quarter of the way round the world is 5,400 nautical miles.
        Assert.Equal(Geo.EarthNm * Math.PI / 2, Geo.Nm(new GeoPoint(0, 0), new GeoPoint(0, 90)), 3);
    }

    [Fact]
    public void The_land_mask_knows_the_continents_and_their_climates()
    {
        foreach (var city in new[] { "LONDON", "PARIS", "CHICAGO", "MOSCOW", "BEIJING", "SAO PAULO", "CAIRO", "SYDNEY", "DELHI" })
            Assert.True(LandMask.IsLand(Earth.Cities.First(c => c.Name == city).At), city);
        foreach (var sea in new[] { new GeoPoint(30, -40), new GeoPoint(0, -140), new GeoPoint(-30, 80), new GeoPoint(40, 160) })
            Assert.False(LandMask.IsLand(sea), sea.ToString());
        Assert.Equal(Climate.Desert, LandMask.ClimateAt(new GeoPoint(23, 12)));
        Assert.Equal(Climate.Ice, LandMask.ClimateAt(new GeoPoint(-80, 40)));
        Assert.Equal(Terrain.Arctic, Campaign.TerrainAt(new GeoPoint(-80, 40)));
        Assert.Equal(Terrain.Desert, Campaign.TerrainAt(new GeoPoint(23, 12)));
        Assert.Equal(0, Earth.NationAt(new GeoPoint(39, -98)));
        Assert.Equal("UNITED KINGDOM", Earth.Nations[Earth.NationAt(new GeoPoint(52, -1.5))].Name);
        Assert.Equal(-1, Earth.NationAt(new GeoPoint(-50, -120)));
    }

    [Fact]
    public void The_sun_crosses_the_sky_westward_and_the_seasons_tilt_it()
    {
        var noon = new DateTime(2031, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        var sun = Geo.Sun(noon);
        Assert.InRange(sun.Lon, -2, 2);
        Assert.InRange(sun.Lat, 22, 24);
        Assert.InRange(Geo.Sun(new DateTime(2031, 12, 21, 12, 0, 0, DateTimeKind.Utc)).Lat, -24, -22);
        Assert.InRange(Geo.Sun(noon.AddHours(6)).Lon, -92, -88);
        Assert.False(Geo.IsNight(new GeoPoint(51.5, 0), noon));
        Assert.True(Geo.IsNight(new GeoPoint(51.5, 0), noon.AddHours(12)));
    }

    // ───────────────────────────── the first base ─────────────────────────────

    [Fact]
    public void The_first_base_comes_with_staff_soldiers_craft_and_stores()
    {
        var c = NewWar();
        var b = c.Bases.Single();
        Assert.Equal("LONDON", b.Name);
        Assert.Equal(8, c.Soldiers.Count);
        Assert.All(c.Soldiers, s => Assert.Equal(c.Crafts.First(k => k.Type == CraftType.Raven).Id, s.CraftId));
        Assert.Equal(10, b.Scientists);
        Assert.Equal(10, b.Engineers);
        Assert.Equal(2, c.Crafts.Count(k => k.Type == CraftType.Falcon && k.Guns.Count == 2));
        Assert.Equal(3, b.HangarSpace);
        Assert.True(b.RadarRange >= 1500);
        Assert.True(b.Connected());
        Assert.True(b.Count(ItemKind.Rifle) > 0);
    }

    // ───────────────────────────── the base's layout ─────────────────────────────

    [Fact]
    public void Facilities_go_on_free_ground_inside_the_grid_joined_to_the_base_and_the_lift_comes_first()
    {
        var empty = new Base();
        Assert.Equal("THE ACCESS LIFT COMES FIRST", empty.CanBuild(FacilityType.Lab, 2, 2));
        Assert.Null(empty.CanBuild(FacilityType.Lift, 2, 2));
        var c = NewWar();
        var b = c.Bases[0];
        Assert.Equal("THAT GROUND IS TAKEN", b.CanBuild(FacilityType.Lab, 2, 2));
        Assert.Equal("OUTSIDE THE BASE", b.CanBuild(FacilityType.Hangar, 5, 4));
        Assert.Equal("IT MUST JOIN THE BASE", b.CanBuild(FacilityType.Lab, 5, 5));
        Assert.Equal("A BASE HAS ONE ACCESS LIFT", b.CanBuild(FacilityType.Lift, 3, 3));
        Assert.Null(b.CanBuild(FacilityType.Lab, 3, 3));
        Assert.Null(b.CanBuild(FacilityType.Hangar, 2, 4));
        // Building costs money and takes days; locked facilities wait for research.
        int money = c.Money;
        Assert.Null(c.Build(b, FacilityType.Lab, 3, 3));
        Assert.Equal(money - 750_000, c.Money);
        Assert.False(b.At2(3, 3)!.Built);
        Assert.Equal("NOT YET RESEARCHED", c.Build(b, FacilityType.LaserDefence, 4, 3));
        for (int d = 0; d < 26; d++) Run(c, 24 * 3600);
        Assert.True(b.At2(3, 3)!.Built);
        Assert.Equal(100, b.LabSpace);
    }

    [Fact]
    public void Facilities_come_down_only_if_nothing_is_cut_off_and_nothing_is_left_without_room()
    {
        var c = NewWar();
        var b = c.Bases[0];
        Assert.Equal("THE ACCESS LIFT STAYS", c.Dismantle(b, b.At2(2, 2)!));
        Assert.Equal("THE PERSONNEL NEED THE ROOM", c.Dismantle(b, b.At2(1, 2)!));
        Assert.Equal("A CRAFT IS HOUSED THERE", c.Dismantle(b, b.At2(0, 0)!));
        // A chain of laboratories: the middle one can't come down while the last hangs from it.
        b.Put(FacilityType.Lab, 2, 4);
        b.Put(FacilityType.Lab, 2, 5);
        Assert.False(b.Connected(b.At2(2, 4)));
        Assert.Equal("IT WOULD CUT THE BASE IN TWO", c.Dismantle(b, b.At2(2, 4)!));
        Assert.Null(c.Dismantle(b, b.At2(2, 5)!));
        Assert.Null(b.At2(2, 5));
        // An empty laboratory comes down.
        b.Put(FacilityType.Lab, 3, 3);
        Assert.Null(c.Dismantle(b, b.At2(3, 3)!));
    }

    // ───────────────────────────── staff and stores ─────────────────────────────

    [Fact]
    public void Staff_and_stores_arrive_three_days_after_they_are_paid_for_if_there_is_room()
    {
        var c = NewWar();
        var b = c.Bases[0];
        int money = c.Money;
        Assert.Null(c.Hire(b, Campaign.Staff.Scientist, 5));
        Assert.Null(c.Hire(b, Campaign.Staff.Soldier, 2));
        Assert.Null(c.Buy(b, ItemKind.RifleClip, 10));
        Assert.Equal(money - 5 * 60000 - 2 * 40000 - 10 * 200, c.Money);
        Assert.Equal(10, b.Scientists);
        Run(c, 71 * 3600);
        Assert.Equal(10, b.Scientists);
        Run(c, 2 * 3600);
        Assert.Equal(15, b.Scientists);
        Assert.Equal(10, c.Soldiers.Count);
        Assert.Equal(50, b.Count(ItemKind.RifleClip));
        // The quarters hold fifty.
        Assert.Equal("NOT ENOUGH LIVING QUARTERS", c.Hire(b, Campaign.Staff.Engineer, 30));
        Assert.Equal("NOT FOR SALE", c.Buy(b, ItemKind.StormRifle));
        c.Money = 10;
        Assert.Equal("NOT ENOUGH MONEY", c.Buy(b, ItemKind.Rifle));
        // Selling: alien things fetch good prices.
        b.Add(ItemKind.StormRifle, 2);
        Assert.Null(c.Sell(b, ItemKind.StormRifle, 2));
        Assert.Equal(10 + 2 * 84000, c.Money);
        Assert.Equal("NOT ENOUGH IN THE STORES", c.Sell(b, ItemKind.StormRifle));
    }

    // ───────────────────────────── research ─────────────────────────────

    [Fact]
    public void Research_needs_its_prerequisites_and_specimens_uses_them_up_and_unlocks_what_follows()
    {
        var c = NewWar();
        var b = c.Bases[0];
        Assert.True(c.CanResearch(b, Topic.LaserWeapons));
        Assert.False(c.CanResearch(b, Topic.LaserPistol));
        Assert.False(c.CanResearch(b, Topic.PallidAutopsy));
        Assert.False(c.CanResearch(b, Topic.AlienOrigins));
        b.Add(ItemKind.PallidCorpse);
        Assert.True(c.CanResearch(b, Topic.PallidAutopsy));
        Assert.Null(c.StartResearch(b, Topic.PallidAutopsy, 5));
        Assert.Equal(0, b.Count(ItemKind.PallidCorpse));
        Assert.False(c.CanResearch(b, Topic.PallidAutopsy));
        Assert.Null(c.StartResearch(b, Topic.LaserWeapons, 5));
        Assert.Equal(0, b.FreeScientists);
        // Scientists are limited by those free and the laboratories' room.
        c.Assign(b, b.Research[1], 50);
        Assert.Equal(5, b.Research[1].Scientists);
        // Five scientists finish 30 scientist-days in six days (and not before).
        Run(c, 5 * 24 * 3600);
        Assert.DoesNotContain(Topic.PallidAutopsy, c.Researched);
        Run(c, 1 * 24 * 3600 + 3600);
        Assert.Contains(Topic.PallidAutopsy, c.Researched);
        Assert.Contains(c.Events, e => e.Kind == EventKind.ResearchDone);
        b.Research[0].Done = Rules.Of(Topic.LaserWeapons).Cost - 1;
        Run(c, 6 * 3600);
        Assert.Contains(Topic.LaserWeapons, c.Researched);
        Assert.True(c.CanResearch(b, Topic.LaserPistol));
        // An interrogation needs a live alien, and any interrogation opens Alien Origins.
        b.Captives.Add(new Captive(Race.Skimmer, AlienRank.Soldier));
        Assert.True(c.CanResearch(b, Topic.SkimmerInterrogation));
        Assert.Null(c.StartResearch(b, Topic.SkimmerInterrogation, 10));
        Assert.Empty(b.Captives);
        b.Research[0].Done = 1000;
        Run(c, 3600);
        Assert.True(c.CanResearch(b, Topic.AlienOrigins));
        Assert.False(c.CanResearch(b, Topic.TheHomeworld));
    }

    [Fact]
    public void The_road_to_the_homeworld_runs_through_the_veiled_and_ends_at_the_gate_of_nyx()
    {
        var c = NewWar();
        var b = c.Bases[0];
        c.Researched.UnionWith([Topic.AlienOrigins, Topic.VeiledInterrogation]);
        Assert.True(c.CanResearch(b, Topic.TheHomeworld));
        Assert.Null(c.StartResearch(b, Topic.TheHomeworld, 10));
        b.Research[0].Done = 10000;
        Run(c, 3600);
        var gate = c.Sites.Single(s => s.Kind == SiteKind.Homeworld);
        Assert.True(gate.Known);
        // Only the Starfall can go there.
        var raven = c.Crafts.First(k => k.Type == CraftType.Raven);
        Assert.Equal("ONLY THE STARFALL CAN REACH NYX", c.Launch(raven, site: gate));
        Assert.False(c.CanMake(Rules.Project("STARFALL")));
        c.Researched.UnionWith([Topic.GravDrive, Topic.Starfall]);
        Assert.True(c.CanMake(Rules.Project("STARFALL")));
    }

    // ───────────────────────────── manufacture ─────────────────────────────

    [Fact]
    public void The_workshops_make_what_has_been_researched_paying_money_and_materials_for_each()
    {
        var c = NewWar();
        var b = c.Bases[0];
        Assert.Equal("NOT YET RESEARCHED", c.StartProduction(b, "LASER RIFLE", 2, 10));
        c.Researched.Add(Topic.LaserRifle);
        int money = c.Money;
        Assert.Null(c.StartProduction(b, "LASER RIFLE", 2, 10));
        Assert.Equal(money - 20000, c.Money);
        // 400 engineer-hours each, ten engineers: forty hours apiece.
        Run(c, 40 * 3600);
        Assert.Equal(1, b.Count(ItemKind.LaserRifle));
        Assert.Equal(money - 40000, c.Money);
        Run(c, 40 * 3600);
        Assert.Equal(2, b.Count(ItemKind.LaserRifle));
        Assert.Empty(b.Production);
        // Alien materials are used up too.
        c.Researched.Add(Topic.CarapaceArmour);
        Assert.Equal("NOT ENOUGH MONEY OR MATERIALS", c.StartProduction(b, "CARAPACE ARMOUR", 1, 10));
        b.Add(ItemKind.Hullmetal, 4);
        Assert.Null(c.StartProduction(b, "CARAPACE ARMOUR", 1, 10));
        Assert.Equal(0, b.Count(ItemKind.Hullmetal));
        // Craft need a free hangar.
        c.Researched.Add(Topic.GravDrive);
        b.Add(ItemKind.DriveCore);
        b.Add(ItemKind.NavLattice);
        b.Add(ItemKind.Hullmetal, 15);
        Assert.Equal("NO FREE HANGAR", c.StartProduction(b, "VANTAGE", 1, 10));
    }

    // ───────────────────────────── UFOs and radar ─────────────────────────────

    [Fact]
    public void Radar_finds_UFOs_in_range_sooner_or_later_and_never_out_of_range()
    {
        var c = NewWar();
        var near = c.SpawnUfo(UfoMission.Scout, UfoType.Raider);
        near.At = new GeoPoint(52, 8);
        near.Dest = near.At;
        near.Legs = 99;
        var far = c.SpawnUfo(UfoMission.Scout, UfoType.Raider);
        far.At = new GeoPoint(-30, 140);
        far.Dest = far.At;
        far.Legs = 99;
        for (int i = 0; i < 48 && !near.Detected; i++) Run(c, 1800);
        Assert.True(near.Detected);
        Assert.False(far.Detected);
        Assert.Contains(c.Events, e => e.Kind == EventKind.UfoDetected && e.Ufo == near.Id);
        // A UFO that flies out of range is lost.
        near.At = new GeoPoint(0, -150);
        near.Dest = near.At;
        Run(c, 1800);
        Assert.False(near.Detected);
        Assert.Contains(c.Events, e => e.Kind == EventKind.UfoLost);
    }

    [Fact]
    public void UFOs_on_missions_land_for_a_while_and_terror_ships_start_a_terror_site()
    {
        var c = NewWar();
        var u = c.SpawnUfo(UfoMission.Research, UfoType.Skiff);
        u.At = Geo.Toward(u.Dest, new GeoPoint(0, 0), 50);
        for (int i = 0; i < 20 && u.State != UfoState.Landed; i++) Run(c, 600);
        Assert.Equal(UfoState.Landed, u.State);
        for (int i = 0; i < 12 && u.State == UfoState.Landed; i++) Run(c, 3600);
        Assert.Equal(UfoState.Flying, u.State);
        var t = c.SpawnUfo(UfoMission.Terror, UfoType.Carrier, Race.Brakk);
        t.At = Geo.Toward(t.Dest, new GeoPoint(0, 0), 20);
        for (int i = 0; i < 20 && !c.Sites.Any(s => s.Kind == SiteKind.Terror); i++) Run(c, 600);
        var site = c.Sites.Single(s => s.Kind == SiteKind.Terror);
        Assert.True(site.Known);
        Assert.Contains(c.Events, e => e.Kind == EventKind.TerrorSite);
        // Left alone, the aliens leave it after a day and more, and score heavily.
        int theirs = c.MonthTheirs;
        for (int i = 0; i < 40 && c.Sites.Contains(site); i++) Run(c, 3600);
        Assert.DoesNotContain(site, c.Sites);
        Assert.True(c.MonthTheirs >= theirs + 400);
    }

    // ───────────────────────────── interception ─────────────────────────────

    [Fact]
    public void An_interceptor_launched_at_a_UFO_catches_it_and_the_air_battle_brings_it_down_on_land()
    {
        var c = NewWar("PARIS");
        var u = c.SpawnUfo(UfoMission.Scout, UfoType.Skiff);
        u.At = new GeoPoint(47, 6);
        u.Dest = new GeoPoint(46, 7);
        u.Legs = 99;
        c.Detect(u);
        var falcon = c.Crafts.First(k => k.Type == CraftType.Falcon);
        Assert.Equal("IT HAS NO WEAPONS", c.Launch(c.Crafts.First(k => k.Type == CraftType.Raven), u));
        Assert.Null(c.Launch(falcon, u));
        c.Events.Clear();
        for (int i = 0; i < 100 && !c.Events.Any(e => e.Kind == EventKind.Intercept); i++) Run(c, 60);
        var ev = c.Events.First(e => e.Kind == EventKind.Intercept);
        Assert.Equal(falcon.Id, ev.Craft);
        // Missiles from a distance: the Skiff can't reply at sixty kilometres.
        var air = c.Engage(falcon, u);
        air.Mode = AirMode.Cautious;
        var result = air.Resolve();
        Assert.True(result is AirResult.Downed or AirResult.Destroyed, result.ToString());
        Assert.Equal(0, falcon.Damage);
        c.Resolve(air);
        Assert.Equal(UfoState.Gone, u.State);
        if (result == AirResult.Downed) Assert.Contains(c.Sites, s => s.Kind == SiteKind.CrashSite && s.Ufo == UfoType.Skiff);
        Assert.Equal(CraftState.Returning, falcon.State);
        Assert.True(c.MonthOurs > 0);
    }

    [Fact]
    public void The_air_battle_closes_to_the_range_its_mode_asks_for_and_a_big_UFO_can_win_it()
    {
        var c = NewWar();
        var u = c.SpawnUfo(UfoMission.Scout, UfoType.Dreadnought);
        var falcon = c.Crafts.First(k => k.Type == CraftType.Falcon);
        var air = c.Engage(falcon, u);
        air.Mode = AirMode.Standoff;
        for (int i = 0; i < 60 * 10; i++) air.Step(1 / 60.0);
        Assert.True(air.Distance >= Rules.Of(UfoType.Dreadnought).Range);
        Assert.Equal(0, falcon.Damage);
        air.Mode = AirMode.Aggressive;
        var result = air.Resolve();
        Assert.Equal(AirResult.CraftLost, result);
        c.Resolve(air);
        Assert.DoesNotContain(falcon, c.Crafts);
        // Breaking off ends the fight with the craft heading home.
        var other = c.Crafts.First(k => k.Type == CraftType.Falcon);
        other.State = CraftState.Out;
        var air2 = c.Engage(other, c.SpawnUfo(UfoMission.Scout, UfoType.Raider));
        air2.Mode = AirMode.Disengage;
        Assert.Equal(AirResult.Disengaged, air2.Resolve());
        c.Resolve(air2);
        Assert.Equal(CraftState.Returning, other.State);
    }

    [Fact]
    public void Craft_come_home_short_of_fuel_and_are_repaired_rearmed_and_refuelled_in_turn()
    {
        var c = NewWar();
        var falcon = c.Crafts.First(k => k.Type == CraftType.Falcon);
        Assert.Null(c.Launch(falcon, patrol: new GeoPoint(10, -60)));
        for (int i = 0; i < 6 * 60 && falcon.State != CraftState.Returning; i++) Run(c, 60);
        Assert.Equal(CraftState.Returning, falcon.State);
        Assert.Contains(c.Events, e => e.Kind == EventKind.LowFuel);
        for (int i = 0; i < 6 * 60 && falcon.Flying; i++) Run(c, 60);
        Assert.False(falcon.Flying);
        falcon.Damage = 30;
        falcon.Guns[0].Rounds = 2;
        falcon.State = CraftState.Repairing;
        Run(c, 3600 * 3);
        Assert.True(falcon.Damage < 30);
        for (int i = 0; i < 48 && falcon.State != CraftState.Ready; i++) Run(c, 3600);
        Assert.Equal(CraftState.Ready, falcon.State);
        Assert.Equal(0, falcon.Damage);
        Assert.Equal(6, falcon.Guns[0].Rounds);
        Assert.Equal(falcon.Info.FuelHours, falcon.Fuel);
    }

    [Fact]
    public void A_transport_at_a_crash_site_offers_a_mission_with_the_crashed_crew_and_the_terrain_below()
    {
        var c = NewWar("CAIRO");
        var raven = c.Crafts.First(k => k.Type == CraftType.Raven);
        var site = new Site { Id = c.NextId++, Kind = SiteKind.CrashSite, At = new GeoPoint(25, 25), Ufo = UfoType.Raider, Race = Race.Coilback, Hours = 40, Known = true, Damage = 0.6, Name = "CRASH SITE" };
        c.Sites.Add(site);
        Assert.Null(c.Launch(raven, site: site));
        for (int i = 0; i < 200 && !c.Events.Any(e => e.Kind == EventKind.CraftArrived); i++) Run(c, 120);
        Assert.Contains(c.Events, e => e.Kind == EventKind.CraftArrived && e.Site == site.Id);
        var m = c.MissionFor(raven)!;
        Assert.Equal(MissionKind.CrashSite, m.Kind);
        Assert.Equal(Terrain.Desert, m.Terrain);
        Assert.Equal(8, m.Squad.Count);
        Assert.True(m.Crew < Rules.Of(UfoType.Raider).Crew);
        Assert.Equal(Race.Coilback, m.Race);
    }

    // ───────────────────────────── the month's end ─────────────────────────────

    [Fact]
    public void A_good_month_raises_funding_where_the_work_was_done_and_a_bad_one_cuts_it()
    {
        var c = NewWar();
        int uk = Array.FindIndex(Earth.Nations, n => n.Name == "UNITED KINGDOM"), usa = 0;
        int ukFunding = c.Nations[uk].Funding, usaFunding = c.Nations[usa].Funding;
        c.Ours(800, new GeoPoint(52, -1));
        c.Theirs(900, new GeoPoint(39, -98));
        int money = c.Money;
        c.MonthEnd();
        Assert.True(c.Nations[uk].Funding > ukFunding);
        Assert.True(c.Nations[usa].Funding < usaFunding);
        Assert.Single(c.History);
        Assert.Equal(-100, c.History[0].Score);
        Assert.Equal("POOR", c.History[0].Rating);
        Assert.Contains(c.Events, e => e.Kind == EventKind.MonthlyReport);
        // Funding in, wages and upkeep out.
        int bills = c.Bases.Sum(b => b.Upkeep + b.Scientists * Rules.ScientistPay + b.Engineers * Rules.EngineerPay) + c.Soldiers.Count * Rules.SoldierPay + c.Crafts.Sum(k => k.Info.Upkeep);
        Assert.Equal(money + c.History[0].Funding - bills, c.Money);
        // The clock makes the report at the turn of the month by itself.
        var c2 = NewWar();
        Run(c2, 31 * 24 * 3600);
        Assert.Single(c2.History);
    }

    [Fact]
    public void Two_terrible_months_running_or_two_in_debt_and_the_council_ends_the_project()
    {
        var c = NewWar();
        c.Theirs(2000, new GeoPoint(39, -98));
        c.MonthEnd();
        Assert.False(c.Over);
        c.Theirs(2000, new GeoPoint(39, -98));
        c.MonthEnd();
        Assert.True(c.Over);
        Assert.False(c.Won);
        var d = NewWar();
        d.Money = -20_000_000;
        d.MonthEnd();
        d.MonthEnd();
        Assert.True(d.Over);
        Assert.Contains("DEBT", d.Ending);
    }

    [Fact]
    public void A_nation_left_unhappy_withdraws_its_funding()
    {
        var c = NewWar();
        int i = Array.FindIndex(Earth.Nations, n => n.Name == "JAPAN");
        c.Nations[i].Satisfaction = 15;
        c.Theirs(400, Earth.Nations[i].At);
        c.MonthEnd();
        Assert.True(c.Nations[i].Withdrawn);
        int funding = c.Funding;
        c.MonthEnd();
        Assert.Equal(funding, c.Funding);
    }

    // ───────────────────────────── bases attacked and lost ─────────────────────────────

    [Fact]
    public void A_UFO_that_attacks_a_base_meets_its_defences_and_a_lost_base_is_gone_with_all_in_it()
    {
        var c = NewWar();
        var b = c.Bases[0];
        for (int i = 0; i < 4; i++) b.Put(FacilityType.StormDefence, 3 + i % 3, 3 + i / 3);
        var u = c.SpawnUfo(UfoMission.Retaliation, UfoType.Raider);
        u.At = Geo.Toward(b.At, new GeoPoint(0, 0), 30);
        Run(c, 3600);
        Assert.Contains(c.Events, e => e.Kind == EventKind.BaseDefended);
        // Undefended, the aliens land and the base must be fought for.
        var c2 = NewWar();
        var u2 = c2.SpawnUfo(UfoMission.Retaliation, UfoType.Dreadnought);
        u2.At = Geo.Toward(c2.Bases[0].At, new GeoPoint(0, 0), 30);
        Run(c2, 3600);
        Assert.Contains(c2.Events, e => e.Kind == EventKind.BaseAttack);
        var m = c2.DefenceMission()!;
        Assert.Equal(MissionKind.BaseDefence, m.Kind);
        Assert.Equal(Terrain.BaseInterior, m.Terrain);
        c2.LoseBase(c2.Bases[0]);
        Assert.True(c2.Over);
        Assert.Equal(8, c2.Memorial.Count);
    }

    // ───────────────────────────── a mission's results ─────────────────────────────

    [Fact]
    public void A_won_mission_brings_back_finds_and_captives_promotes_and_wounds_and_clears_the_site()
    {
        var c = NewWar();
        var b = c.Bases[0];
        b.Put(FacilityType.Containment, 1, 4);
        var raven = c.Crafts.First(k => k.Type == CraftType.Raven);
        var site = new Site { Id = c.NextId++, Kind = SiteKind.CrashSite, At = new GeoPoint(50, 2), Ufo = UfoType.Raider, Race = Race.Pallid, Hours = 40, Known = true, Damage = 0.6, Name = "CRASH SITE" };
        c.Sites.Add(site);
        Assert.Null(c.Launch(raven, site: site));
        raven.At = site.At;
        var m = c.MissionFor(raven)!;
        var battle = BattleGen.Make(m, b);
        Assert.True(b.Count(ItemKind.Rifle) < 10);
        int n = 0;
        foreach (var a in battle.Units.Where(u => u.Side == Side.Alien).ToList())
        {
            if (n++ == 0) { a.Out = true; continue; }
            Tac.Kill(battle, a, battle.Squad.First(), DamageType.Kinetic);
        }
        var hurt = battle.Squad.ElementAt(1);
        hurt.Hp -= 10;
        var dead = battle.Squad.Last();
        Tac.Kill(battle, dead, null, DamageType.Plasma);
        Tac.CheckEnd(battle);
        Assert.True(battle.Outcome);
        var d = Debrief.Of(battle);
        Assert.True(d.Points > 0);
        d.Apply(c, m);
        Assert.Single(b.Captives);
        Assert.True(b.Count(ItemKind.PallidCorpse) >= 1);
        Assert.True(b.Count(ItemKind.Hullmetal) > 0);
        Assert.Equal(10, b.Count(ItemKind.Rifle));
        Assert.DoesNotContain(site, c.Sites);
        Assert.Single(c.Memorial);
        Assert.DoesNotContain(dead.Soldier!, c.Soldiers);
        Assert.True(hurt.Soldier!.WoundDays > 0);
        Assert.False(hurt.Soldier.Fit);
        Assert.Equal(1, battle.Squad.First().Soldier!.Missions);
        Assert.True(battle.Squad.First().Soldier!.Kills > 0);
        Assert.Equal(CraftState.Returning, raven.State);
        Assert.True(c.MonthOurs > 0);
    }

    [Fact]
    public void Soldiers_grow_with_what_they_did_and_rise_in_rank()
    {
        var s = new Soldier { Firing = 50, Reactions = 40, Throwing = 50, Tu = 55, Health = 30, Strength = 30, Bravery = 30 };
        var u = new BUnit { Hits = 4, ReactionShots = 2, Throws = 1, Kills = 3, DamageTaken = 5 };
        var d = new Dice(3);
        Debrief.Grow(s, u, ref d);
        Assert.True(s.Firing > 50);
        Assert.True(s.Reactions > 40);
        Assert.True(s.Throwing > 50);
        Assert.Equal(Rank.Rookie, Soldier.RankFor(0));
        Assert.Equal(Rank.Squaddie, Soldier.RankFor(3));
        Assert.Equal(Rank.Sergeant, Soldier.RankFor(10));
        Assert.Equal(Rank.Colonel, Soldier.RankFor(40));
    }

    [Fact]
    public void The_hive_mind_destroyed_ends_the_war_in_victory()
    {
        var c = NewWar();
        var m = new Mission { Kind = MissionKind.Homeworld, Terrain = Terrain.Hive, Race = Race.Veiled, Crew = 6, Seed = 5, Name = "THE HIVE OF NYX", BaseId = c.Bases[0].Id };
        m.Squad.AddRange(c.Soldiers.Take(6));
        var b = BattleGen.Make(m);
        Assert.Contains(b.Units, u => u.Side == Side.Alien && u.Race == Race.Veiled);
        var brain = Enumerable.Range(0, b.W * b.H).Select(i => new Cell(i % b.W, i / b.W, 0)).First(cell => b.Obj[cell.X, cell.Y, 0] == Piece.Brain);
        Tac.HurtTerrain(b, Part.Object, brain, 500);
        Tac.CheckEnd(b);
        Assert.True(b.BrainDestroyed);
        Assert.True(b.Outcome);
        var d = Debrief.Of(b);
        d.Apply(c, m);
        Assert.True(c.Over);
        Assert.True(c.Won);
    }

    // ───────────────────────────── the aliens' plans ─────────────────────────────

    [Fact]
    public void The_war_grows_worse_month_by_month()
    {
        var c = NewWar();
        Assert.All(Enumerable.Range(0, 30).Select(_ => c.PickRace(0)), r => Assert.True(r is Race.Pallid or Race.Skimmer));
        var later = Enumerable.Range(0, 200).Select(_ => c.PickRace(6)).ToList();
        Assert.Contains(Race.Brakk, later);
        Assert.Contains(Race.Veiled, later);
        // Left to itself, the clock brings UFOs.
        Run(c, 10 * 24 * 3600);
        Assert.True(c.NextId >= 16);
    }
}
