using AVAXCom.Engine;
using static AVAXCom.Tests.TestWorld;

namespace AVAXCom.Tests;

/// <summary>The battlescape's rules: time units and what things cost, the chance to hit, sight and the fog across
/// storeys, reaction fire, grenades and explosions, morale and panic, wounds and field kits, the inventory and its
/// weights, finding the way, the aliens' minds, and winning and losing.</summary>
public class TacticsTests
{
    // ───────────────────────────── time units ─────────────────────────────

    [Fact]
    public void Walking_costs_four_a_straight_step_six_a_diagonal_and_one_for_each_eighth_of_a_turn()
    {
        var b = Flat();
        var u = Soldier(b, 5, 5, facing: 2);
        Assert.Null(Tac.Walk(b, u, new Cell(8, 5, 0)));
        Settle(b);
        Assert.Equal((8, 5), (u.X, u.Y));
        Assert.Equal(60 - 3 * 4, u.Tu);
        // East to south-east: an eighth of a turn, then a diagonal step.
        Assert.Null(Tac.Walk(b, u, new Cell(9, 6, 0)));
        Settle(b);
        Assert.Equal(48 - 1 - 6, u.Tu);
        Assert.Equal(3, u.Facing);
        // Turning on the spot: a half turn is four.
        Assert.Null(Tac.TurnTo(b, u, 7));
        Assert.Equal(41 - 4, u.Tu);
    }

    [Fact]
    public void Kneeling_costs_four_standing_eight_and_a_kneeler_stands_to_walk()
    {
        var b = Flat();
        var u = Soldier(b, 5, 5);
        Assert.Null(Tac.Kneel(b, u));
        Assert.True(u.Kneeling);
        Assert.Equal(56, u.Tu);
        Assert.Null(Tac.Walk(b, u, new Cell(6, 5, 0)));
        Settle(b);
        Assert.False(u.Kneeling);
        Assert.Equal(56 - 8 - 4, u.Tu);
    }

    [Fact]
    public void A_walk_stops_where_the_time_units_run_out_and_nobody_walks_with_none()
    {
        var b = Flat();
        var u = Soldier(b, 2, 5, facing: 2, tu: 18);
        Assert.Null(Tac.Walk(b, u, new Cell(20, 5, 0)));
        Settle(b);
        Assert.Equal(6, u.X);
        Assert.Equal(2, u.Tu);
        Assert.Null(Tac.Walk(b, u, new Cell(20, 5, 0)));
        Settle(b);
        Assert.Equal(6, u.X);
    }

    [Fact]
    public void Doors_open_for_a_price_stairs_climb_for_eight_and_walls_must_be_walked_round()
    {
        var b = Flat();
        House(b, 10, 10, 5, 4);
        b.North[12, 14, 0] = Piece.Door;
        var u = Soldier(b, 12, 15, facing: 0);
        // In through the door: a step, and four for the door.
        Assert.Equal(8, PathFinder.StepCost(b, u, new Cell(12, 14, 0), new Cell(12, 13, 0)));
        Assert.Null(Tac.Walk(b, u, new Cell(12, 13, 0)));
        Settle(b);
        Assert.Equal(Piece.DoorOpen, b.North[12, 14, 0]);
        Assert.Equal(60 - 4 - 8, u.Tu);
        // Up the stairs in the corner.
        u.Tu = 60;
        var path = PathFinder.Find(b, u, new Cell(10, 10, 1))!;
        Assert.Equal(new Cell(10, 10, 1), path[^1]);
        Assert.Equal(new Cell(10, 10, 0), path[^2]);
        Assert.Equal(PathFinder.Climb, PathFinder.StepCost(b, u, new Cell(10, 10, 0), new Cell(10, 10, 1)));
        // A wall in the way: the path goes round, never through, and never cuts its corner.
        u.X = 11; u.Y = 11;
        var outside = Soldier(b, 8, 12, facing: 2);
        Assert.Equal(-1, PathFinder.StepCost(b, outside, new Cell(9, 12, 0), new Cell(10, 12, 0)));
        Assert.Equal(-1, PathFinder.StepCost(b, outside, new Cell(9, 9, 0), new Cell(10, 10, 0)));
        var round = PathFinder.Find(b, outside, new Cell(13, 12, 0))!;
        Assert.True(round.Contains(new Cell(12, 14, 0)), string.Join(" ", round));
    }

    [Fact]
    public void Something_that_flies_rises_through_open_air_and_walkers_cannot_step_onto_nothing()
    {
        var b = Flat();
        var skimmer = Alien(b, 5, 5, race: Race.Skimmer);
        Assert.True(skimmer.Flies);
        Assert.Equal(PathFinder.Climb, PathFinder.StepCost(b, skimmer, new Cell(5, 5, 0), new Cell(5, 5, 1)));
        Assert.Equal(PathFinder.Straight, PathFinder.StepCost(b, skimmer, new Cell(5, 5, 1), new Cell(6, 5, 1)));
        var walker = Soldier(b, 8, 8);
        Assert.Equal(-1, PathFinder.StepCost(b, walker, new Cell(8, 8, 0), new Cell(8, 8, 1)));
        Assert.Null(PathFinder.Find(b, walker, new Cell(8, 9, 1)));
    }

    // ───────────────────────────── shooting ─────────────────────────────

    [Fact]
    public void The_chance_to_hit_is_accuracy_times_the_mode_better_kneeling_worse_burdened_and_far()
    {
        var b = Flat(60);
        var u = Soldier(b, 5, 5, firing: 60);
        var rifle = u.Right!;
        var near = new Cell(15, 5, 0);
        Assert.Equal(66, Tac.HitChance(u, rifle, FireMode.Aimed, near));
        Assert.Equal(36, Tac.HitChance(u, rifle, FireMode.Snap, near));
        Assert.Equal(21, Tac.HitChance(u, rifle, FireMode.Auto, near));
        u.Kneeling = true;
        Assert.Equal(76, Tac.HitChance(u, rifle, FireMode.Aimed, near));
        u.Kneeling = false;
        u.Left = Item.Make(ItemKind.FragGrenade);
        Assert.Equal(53, Tac.HitChance(u, rifle, FireMode.Aimed, near));
        u.Left = null;
        Assert.Equal(46, Tac.HitChance(u, rifle, FireMode.Aimed, new Cell(35, 5, 0)));
        // A pistol can't fire a burst; the costs are percentages of the full allowance.
        Assert.Equal(0, Tac.HitChance(u, Item.Make(ItemKind.Pistol), FireMode.Auto, near));
        Assert.Equal(15, Tac.ShotCost(u, rifle, FireMode.Snap));
        Assert.Equal(36, Tac.ShotCost(u, rifle, FireMode.Aimed));
        Assert.Equal(21, Tac.ShotCost(u, rifle, FireMode.Auto));
        Assert.Equal(-1, Tac.ShotCost(u, Item.Make(ItemKind.Pistol), FireMode.Auto));
    }

    [Fact]
    public void Shots_spend_time_units_and_rounds_and_a_dead_shot_hits_what_it_aims_at()
    {
        var b = Flat();
        var u = Soldier(b, 5, 5, firing: 250);
        var a = Alien(b, 10, 5);
        a.Front = a.SideArmour = a.Rear = a.Under = 0;
        int hp = a.Hp;
        Assert.Null(Tac.Shoot(b, u, new Cell(10, 5, 0), FireMode.Auto));
        Settle(b);
        Assert.Equal(60 - 21, u.Tu);
        Assert.Equal(17, u.Right!.Clip!.Rounds);
        Assert.True(a.Dead || a.Hp < hp);
        Assert.True(u.Hits >= 1);
        // Not enough time units, or no ammunition: no shot.
        u.Tu = 10;
        Assert.Equal("NOT ENOUGH TIME UNITS", Tac.Shoot(b, u, new Cell(10, 5, 0), FireMode.Snap));
        u.Tu = 60;
        u.Right!.Clip = null;
        Assert.Equal("NO AMMUNITION: RELOAD", Tac.Shoot(b, u, new Cell(10, 5, 0), FireMode.Snap));
        u.Belt.Add(Item.Make(ItemKind.RifleClip));
        Assert.Null(Tac.Reload(b, u));
        Assert.Equal(20, u.Right.Clip!.Rounds);
        Assert.Equal(60 - Tac.ReloadCost, u.Tu);
        Assert.Empty(u.Belt);
    }

    [Fact]
    public void Armour_is_strongest_in_front_and_blasts_meet_the_armour_underneath()
    {
        var b = Flat();
        var u = Soldier(b, 5, 5, facing: 2);
        u.Front = 30; u.SideArmour = 20; u.Rear = 10; u.Under = 5;
        Assert.Equal(30, Tac.ArmourFacing(u, 2));
        Assert.Equal(30, Tac.ArmourFacing(u, 1));
        Assert.Equal(20, Tac.ArmourFacing(u, 4));
        Assert.Equal(10, Tac.ArmourFacing(u, 6));
        Assert.Equal(5, Tac.ArmourFacing(u, -1));
        // A hit that armour stops does nothing; a stun hit only knocks out.
        int hp = u.Hp;
        Tac.Damage(b, u, 10, DamageType.Kinetic, null, 2);
        Assert.Equal(hp, u.Hp);
        Tac.Damage(b, u, 200, DamageType.Stun, null, 2);
        Assert.True(u.Out);
        Assert.False(u.Dead);
        Assert.Equal(hp, u.Hp);
    }

    // ───────────────────────────── sight and the fog ─────────────────────────────

    [Fact]
    public void Walls_hide_windows_and_fences_do_not_and_hedges_hide_the_kneeling()
    {
        var b = Flat();
        var u = Soldier(b, 5, 5, facing: 2);
        var a = Alien(b, 12, 5);
        Assert.True(Tac.Sees(b, u, a));
        b.West[9, 5, 0] = Piece.Brick;
        Assert.False(Tac.Sees(b, u, a));
        b.West[9, 5, 0] = Piece.Window;
        Assert.True(Tac.Sees(b, u, a));
        // A window stops a shot, though, and breaks.
        var hit = Tac.Trace(b, Tac.Eye(u), Tac.Centre(a), shot: true, ignore: u);
        Assert.Equal(HitKind.West, hit.Kind);
        b.West[9, 5, 0] = Piece.Fence;
        Assert.True(Tac.Sees(b, u, a));
        b.West[9, 5, 0] = Piece.Hedge;
        u.Kneeling = true;
        Assert.False(Tac.Sees(b, u, a));
    }

    [Fact]
    public void Eyes_see_ahead_in_a_cone_and_not_behind()
    {
        var b = Flat();
        var u = Soldier(b, 10, 10, facing: 2);
        var ahead = Alien(b, 16, 12);
        var behind = Alien(b, 4, 10);
        Assert.True(Tac.Sees(b, u, ahead));
        Assert.False(Tac.Sees(b, u, behind));
        Tac.Look(b);
        Assert.True(b.Visible[16, 10, 0]);
        Assert.False(b.Visible[4, 10, 0]);
        Assert.True(ahead.Seen);
        Assert.False(behind.Seen);
        // Turning round shows what was behind; what was seen stays explored.
        Assert.Null(Tac.TurnTo(b, u, 6));
        Assert.True(behind.Seen);
        Assert.False(b.Visible[16, 10, 0]);
        Assert.True(b.Explored[16, 10, 0]);
    }

    [Fact]
    public void Floors_hide_one_storey_from_another_and_a_roof_sees_over_walls()
    {
        var b = Flat();
        House(b, 10, 4, 6, 6);
        var below = Soldier(b, 12, 6, 0, facing: 2);
        var upstairs = Alien(b, 12, 6, 1, race: Race.Pallid);
        Assert.False(Tac.Sees(b, below, upstairs));
        // Something hovering over open ground is seen from beneath.
        var hover = Alien(b, 4, 6, 1, race: Race.Skimmer);
        var watcher = Soldier(b, 2, 6, 0, facing: 2);
        Assert.True(Tac.Sees(b, watcher, hover));
        // On the roof, a soldier sees an alien that the house hides from the ground.
        var far = Alien(b, 26, 7);
        var ground = Soldier(b, 8, 7, 0, facing: 2);
        Assert.False(Tac.Sees(b, ground, far));
        var roof = Soldier(b, 15, 7, 2, facing: 2);
        Assert.True(Tac.Sees(b, roof, far));
    }

    [Fact]
    public void At_night_the_squad_sees_nine_tiles_unless_a_flare_lights_the_way_and_aliens_see_in_the_dark()
    {
        var b = Flat(night: true);
        var u = Soldier(b, 5, 10, facing: 2);
        var a = Alien(b, 18, 10, facing: 6);
        Assert.False(Tac.Sees(b, u, a));
        Assert.True(Tac.Sees(b, a, u));
        var near = Alien(b, 12, 10);
        Assert.True(Tac.Sees(b, u, near));
        Tac.Kill(b, near, null, DamageType.Kinetic);
        // A flare thrown beside the far alien.
        var flare = Item.Make(ItemKind.Flare);
        u.Left = flare;
        u.Throwing = 300;
        u.Strength = 60;
        Assert.Null(Tac.Throw(b, u, flare, new Cell(16, 10, 0)));
        Settle(b);
        Assert.True(b.Flares[18, 10] > 0);
        Assert.True(Tac.Sees(b, u, a));
        Assert.True(a.Seen);
    }

    // ───────────────────────────── reaction fire ─────────────────────────────

    [Fact]
    public void An_alien_with_quicker_reactions_fires_at_a_soldier_walking_through_its_sight()
    {
        var b = Flat();
        var a = Alien(b, 15, 4, facing: 4);
        a.Reactions = 100;
        a.Accuracy = 0;
        var u = Soldier(b, 10, 10, facing: 2, reactions: 5);
        Assert.Null(Tac.Walk(b, u, new Cell(20, 10, 0)));
        Settle(b);
        Assert.True(a.ReactionShots >= 1);
        Assert.True(a.Tu < a.MaxTu);
    }

    [Fact]
    public void A_soldier_with_quicker_reactions_than_the_mover_gets_the_interrupt_instead()
    {
        var b = Flat();
        var watcher = Soldier(b, 15, 4, facing: 4, reactions: 100);
        var a = Alien(b, 10, 10, facing: 2);
        a.Reactions = 5;
        b.Active = Side.Alien;
        Assert.Null(Tac.Walk(b, a, new Cell(20, 10, 0)));
        Settle(b);
        Assert.True(watcher.ReactionShots >= 1);
        // A slow soldier gets no interrupt on a quick alien.
        var b2 = Flat();
        var slow = Soldier(b2, 15, 4, facing: 4, reactions: 10);
        var quick = Alien(b2, 10, 10, facing: 2);
        quick.Reactions = 100;
        b2.Active = Side.Alien;
        Assert.Null(Tac.Walk(b2, quick, new Cell(20, 10, 0)));
        Settle(b2);
        Assert.Equal(0, slow.ReactionShots);
    }

    // ───────────────────────────── grenades and explosions ─────────────────────────────

    [Fact]
    public void A_grenade_primed_for_the_end_of_the_turn_goes_off_then_and_one_primed_for_later_waits()
    {
        var b = Flat();
        var u = Soldier(b, 5, 10, facing: 2);
        u.Throwing = 300;
        var a = Alien(b, 11, 10);
        a.Hp = 3;
        var g = Item.Make(ItemKind.FragGrenade);
        u.Left = g;
        Assert.Equal("NOT NOW", Tac.Prime(b, a, g, 0));
        Assert.Null(Tac.Prime(b, u, g, 0));
        Assert.Equal(60 - Tac.PrimeCost, u.Tu);
        Assert.Null(Tac.Throw(b, u, g, new Cell(11, 10, 0)));
        Settle(b);
        Assert.Contains(g, b.ItemsAt(new Cell(11, 10, 0)));
        Assert.False(a.Dead);
        Tac.EndTurn(b);
        Assert.True(a.Dead);
        // A two-turn fuse: still there after the next end of a turn, gone after the one after.
        var b2 = Flat();
        var g2 = Item.Make(ItemKind.FragGrenade);
        g2.Fuse = 1;
        b2.ItemsAt(new Cell(10, 10, 0)).Add(g2);
        Soldier(b2, 2, 2);
        var a2 = Alien(b2, 10, 11);
        a2.Hp = 3;
        Tac.EndTurn(b2);
        Assert.False(a2.Dead);
        Assert.Equal(0, g2.Fuse);
        Tac.EndTurn(b2);
        Assert.True(a2.Dead);
    }

    [Fact]
    public void Throws_reach_further_for_the_strong_and_light_and_cost_a_quarter_of_the_time_units()
    {
        var b = Flat();
        var u = Soldier(b, 5, 5, facing: 2);
        u.Strength = 40;
        var g = Item.Make(ItemKind.FragGrenade);
        Assert.Equal(11, Tac.ThrowRange(u, g));
        Assert.True(Tac.ThrowRange(u, Item.Make(ItemKind.DemoCharge)) < 11);
        u.Strength = 20;
        Assert.True(Tac.ThrowRange(u, g) < 11);
        u.Strength = 40;
        u.Left = g;
        Assert.Equal("OUT OF RANGE", Tac.Throw(b, u, g, new Cell(19, 5, 0)));
        Assert.Null(Tac.Throw(b, u, g, new Cell(12, 5, 0)));
        Assert.Equal(60 - 15, u.Tu);
        Assert.Null(u.Left);
    }

    [Fact]
    public void Explosions_knock_down_walls_and_floors_and_those_standing_on_them_fall()
    {
        var b = Flat();
        House(b, 10, 10, 5, 5);
        var up = Soldier(b, 12, 12, 1);
        int hp = up.Hp;
        Tac.Explode(b, new Cell(12, 12, 0), 120, 2, DamageType.Explosive, null);
        Assert.Equal(Piece.None, b.Floor[12, 12, 1]);
        Assert.True(up.Z == 0 || up.Dead);
        Assert.True(up.Hp < hp);
        // The walls nearest the blast are down to stubs; the far ones stand.
        var b2 = Flat();
        House(b2, 10, 10, 6, 6);
        Tac.Explode(b2, new Cell(10, 12, 0), 110, 2, DamageType.Explosive, null);
        Assert.Equal(Piece.Stub, b2.West[10, 12, 0]);
        Assert.Equal(Piece.Brick, b2.West[16, 13, 0]);
        // A stub can be walked over.
        var s = Soldier(b2, 9, 12, facing: 2);
        Assert.True(PathFinder.StepCost(b2, s, new Cell(9, 12, 0), new Cell(10, 12, 0)) > 0);
        // Fuel drums and drive cores blow up when they're destroyed.
        var b3 = Flat();
        b3.Obj[10, 10, 0] = Piece.Barrel;
        var a = Alien(b3, 11, 11);
        a.Hp = 5;
        a.Under = 0;
        Tac.HurtTerrain(b3, Part.Object, new Cell(10, 10, 0), 50);
        Assert.Equal(Piece.None, b3.Obj[10, 10, 0]);
        Assert.True(a.Dead);
    }

    // ───────────────────────────── morale ─────────────────────────────

    [Fact]
    public void A_death_shakes_the_squad_the_timid_most_and_broken_nerve_means_panic()
    {
        var b = Flat();
        var brave = Soldier(b, 2, 2);
        brave.Bravery = 100;
        var timid = Soldier(b, 3, 3);
        timid.Bravery = 10;
        var victim = Soldier(b, 4, 4);
        Tac.Kill(b, victim, null, DamageType.Plasma);
        Assert.True(timid.Morale < brave.Morale);
        Assert.True(brave.Morale < 100);
        // With no nerve left, the next turn starts in a panic.
        timid.Morale = 0;
        Tac.BeginTurn(b, Side.Player);
        Assert.NotEqual(Panic.None, timid.Panic);
        Assert.Contains(b.News, n => n.Contains("PANICKED") || n.Contains("BERSERK"));
        Assert.Equal(Panic.None, brave.Panic);
        // A painkiller steadies a frightened soldier.
        Settle(b);
        var medic = Soldier(b, 3, 4);
        medic.Left = Item.Make(ItemKind.FieldKit);
        brave.Morale = 30;
        brave.X = 3; brave.Y = 5;
        Assert.Null(Tac.UseKit(b, medic, brave, KitUse.Painkiller));
        Assert.Equal(70, brave.Morale);
    }

    [Fact]
    public void Killing_an_enemy_heartens_the_killers_side()
    {
        var b = Flat();
        var u = Soldier(b, 2, 2);
        var mate = Soldier(b, 3, 2);
        mate.Morale = 60;
        var a = Alien(b, 8, 8);
        Tac.Kill(b, a, u, DamageType.Kinetic);
        Assert.Equal(1, u.Kills);
        Assert.Equal(64, mate.Morale);
        Assert.Single(b.AliensKilled);
        Assert.Contains(b.ItemsAt(new Cell(8, 8, 0)), i => i.Kind == ItemKind.PallidCorpse);
    }

    // ───────────────────────────── wounds and field kits ─────────────────────────────

    [Fact]
    public void Heavy_hits_open_fatal_wounds_that_bleed_each_turn_until_a_field_kit_treats_them()
    {
        var b = Flat();
        var u = Soldier(b, 5, 5);
        u.Front = u.SideArmour = u.Rear = u.Under = 0;
        u.Hp = u.MaxHp = 100;
        for (int i = 0; i < 20 && u.FatalWounds == 0; i++) Tac.Damage(b, u, 20, DamageType.Kinetic, null, 2);
        Assert.True(u.FatalWounds > 0);
        int wounds = u.FatalWounds, hp = u.Hp;
        Tac.BeginTurn(b, Side.Player);
        Assert.Equal(hp - wounds, u.Hp);
        var medic = Soldier(b, 6, 5);
        medic.Right = Item.Make(ItemKind.FieldKit);
        var distant = Soldier(b, 15, 15);
        distant.Right = Item.Make(ItemKind.FieldKit);
        Assert.Equal("TOO FAR AWAY", Tac.UseKit(b, distant, u, KitUse.Heal));
        while (u.FatalWounds > 0) Assert.Null(Tac.UseKit(b, medic, u, KitUse.Heal));
        Assert.True(u.Hp > hp - wounds);
        hp = u.Hp;
        Tac.BeginTurn(b, Side.Player);
        Assert.Equal(hp, u.Hp);
    }

    [Fact]
    public void A_stunned_unit_drops_what_it_carries_and_a_stimulant_brings_it_round()
    {
        var b = Flat();
        var u = Soldier(b, 5, 5);
        var medic = Soldier(b, 6, 5);
        medic.Right = Item.Make(ItemKind.FieldKit);
        var rifle = u.Right!;
        Tac.Damage(b, u, 300, DamageType.Stun, null, 2);
        Assert.True(u.Out);
        Assert.Null(u.Right);
        Assert.Contains(rifle, b.ItemsAt(new Cell(5, 5, 0)));
        Assert.Null(Tac.UseKit(b, medic, u, KitUse.Stimulant));
        Assert.False(u.Out);
        Assert.Equal(0, u.Stun);
    }

    [Fact]
    public void A_shock_baton_takes_an_alien_alive()
    {
        var b = Flat(keep: false);
        var u = Soldier(b, 5, 5, facing: 2, weapon: ItemKind.ShockBaton);
        u.Strength = 70;
        var a = Alien(b, 6, 5);
        for (int i = 0; i < 20 && !a.Out; i++)
        {
            u.Tu = 60;
            Assert.Null(Tac.Strike(b, u, a));
        }
        Assert.True(a.Out);
        Assert.False(a.Dead);
        Assert.True(b.Outcome);
        var d = Debrief.Of(b);
        Assert.Contains(d.Captured, c => c.Race == Race.Pallid);
    }

    // ───────────────────────────── the inventory ─────────────────────────────

    [Fact]
    public void Moving_kit_about_costs_time_units_by_where_it_comes_from_and_goes_and_space_is_limited()
    {
        var b = Flat();
        var u = Soldier(b, 5, 5);
        var clip = Item.Make(ItemKind.RifleClip);
        u.Belt.Add(clip);
        Assert.Equal(6, Tac.MoveCost(Slot.Belt, Slot.LeftHand));
        Assert.Equal(14, Tac.MoveCost(Slot.Ground, Slot.Backpack));
        Assert.Null(Tac.MoveItem(b, u, clip, Slot.Belt, Slot.LeftHand));
        Assert.Equal(54, u.Tu);
        Assert.Equal(clip, u.Left);
        Assert.Equal("THAT HAND IS FULL", Tac.MoveItem(b, u, clip, Slot.LeftHand, Slot.RightHand));
        // The belt takes small things only; the backpack holds six spaces.
        var rifle = Item.Make(ItemKind.Rifle);
        b.ItemsAt(new Cell(5, 5, 0)).Add(rifle);
        Assert.Equal("TOO BIG FOR THE BELT", Tac.MoveItem(b, u, rifle, Slot.Ground, Slot.Belt));
        for (int i = 0; i < 3; i++)
        {
            var r = Item.Make(ItemKind.Rifle);
            b.ItemsAt(new Cell(5, 5, 0)).Add(r);
            u.Tu = 60;
            Assert.Null(Tac.MoveItem(b, u, r, Slot.Ground, Slot.Backpack));
        }
        Assert.Equal("NO ROOM", Tac.MoveItem(b, u, rifle, Slot.Ground, Slot.Backpack));
        // Aboard the transport on the first turn, it's free.
        b.Exit.Add((5, 5));
        u.Tu = 60;
        Assert.Equal("THAT HAND IS FULL", Tac.MoveItem(b, u, rifle, Slot.Ground, Slot.LeftHand));
        Assert.Null(Tac.MoveItem(b, u, clip, Slot.LeftHand, Slot.Ground));
        Assert.Null(Tac.MoveItem(b, u, rifle, Slot.Ground, Slot.LeftHand));
        Assert.Equal(60, u.Tu);
    }

    [Fact]
    public void Carrying_more_than_a_soldier_can_bear_costs_time_units_each_turn()
    {
        var b = Flat();
        var u = Soldier(b, 5, 5);
        u.Strength = 30;
        Assert.Equal(10, u.Weight);
        Assert.Equal(60, Tac.TurnTu(u));
        u.Backpack.Add(Item.Make(ItemKind.Cannon));
        u.Backpack.Add(Item.Make(ItemKind.FieldKit));
        Assert.Equal(10 + 23 + 5, u.Weight);
        Assert.Equal(60 - (38 - 30), Tac.TurnTu(u));
        Tac.BeginTurn(b, Side.Player);
        Assert.Equal(52, u.Tu);
    }

    // ───────────────────────────── the aliens ─────────────────────────────

    [Fact]
    public void An_alien_that_sees_a_soldier_shoots_at_it_on_its_turn()
    {
        var b = Flat();
        var u = Soldier(b, 5, 10, facing: 2);
        u.Hp = u.MaxHp = 1000;
        var a = Alien(b, 12, 10, facing: 6);
        a.Accuracy = 200;
        Tac.EndTurn(b);
        Tac.RunAliens(b);
        Assert.True(a.Shots > 0);
        Assert.True(u.DamageTaken > 0 || u.Dead);
        Assert.Equal(Side.Player, b.Active);
        Assert.Equal(2, b.Turn);
    }

    [Fact]
    public void A_husker_runs_in_and_its_victim_rises_as_a_husk_whose_death_frees_a_new_husker()
    {
        var b = Flat();
        var u = Soldier(b, 5, 10, facing: 2);
        u.Hp = 1;
        u.Front = u.SideArmour = u.Rear = u.Under = 0;
        var h = Alien(b, 10, 10, race: Race.Husker);
        h.Strength = 200;
        Tac.EndTurn(b);
        Tac.RunAliens(b);
        Assert.True(u.Dead);
        var husk = b.Units.Single(x => x.Race == Race.Husk && x.Side == Side.Alien);
        Assert.Equal((5, 10), (husk.X, husk.Y));
        Tac.Kill(b, husk, null, DamageType.Kinetic);
        Assert.Equal(2, b.Units.Count(x => x.Race == Race.Husker && x.Alive));
    }

    [Fact]
    public void A_warden_bursts_when_destroyed()
    {
        var b = Flat();
        var w = Alien(b, 10, 10, race: Race.Warden);
        var near = Soldier(b, 11, 10);
        near.Hp = 4;
        near.Under = 0;
        Tac.Kill(b, w, null, DamageType.Laser);
        Assert.True(near.Dead);
        Assert.Contains(b.Effects, e => e.Kind == FxKind.Explosion);
    }

    [Fact]
    public void Guards_keep_to_their_post_while_patrols_close_in_on_what_they_know_of()
    {
        var b = Flat(40);
        var u = Soldier(b, 3, 20, facing: 2);
        var guard = Alien(b, 30, 20, facing: 0);
        guard.Guard = true;
        var patrol = Alien(b, 30, 25, facing: 0, race: Race.Brakk);
        patrol.Right = Item.Make(ItemKind.StormRifle);
        b.Known[u.Id] = new Cell(u.X, u.Y, 0);
        Tac.EndTurn(b);
        Tac.RunAliens(b);
        Assert.Equal((30, 20), (guard.X, guard.Y));
        Assert.True(patrol.X < 30);
    }

    [Fact]
    public void The_veiled_reach_into_minds_and_a_mind_lens_turns_the_tables()
    {
        var b = Flat();
        var u = Soldier(b, 5, 10, facing: 2);
        u.PsiStrength = 0;
        var v = Alien(b, 25, 10, race: Race.Veiled);
        Assert.True(v.PsiSkill > 0);
        b.Known[u.Id] = new Cell(u.X, u.Y, 0);
        Tac.EndTurn(b);
        Tac.RunAliens(b);
        Assert.True(v.PsiUses > 0);
        // Our own psionic soldier takes over an alien until the aliens' next turn.
        var b2 = Flat();
        var psi = Soldier(b2, 5, 10, facing: 2);
        psi.PsiStrength = 120;
        psi.PsiSkill = 100;
        psi.Left = Item.Make(ItemKind.MindLens);
        var a = Alien(b2, 9, 10);
        a.PsiStrength = 0;
        Assert.Equal(90, Tac.PsiChance(psi, a, true));
        for (int i = 0; i < 20 && a.Team != Side.Player; i++)
        {
            psi.Tu = 60;
            Assert.Null(Tac.Psi(b2, psi, a, control: true));
        }
        Assert.Equal(Side.Player, a.Team);
        Assert.True(a.Tu > 0);
        Tac.BeginTurn(b2, Side.Alien);
        Assert.Equal(Side.Alien, a.Team);
    }

    // ───────────────────────────── the end of a battle ─────────────────────────────

    [Fact]
    public void A_battle_is_won_when_every_alien_is_down_and_lost_when_the_squad_is()
    {
        var b = Flat(keep: false);
        var u = Soldier(b, 5, 5);
        var a = Alien(b, 10, 10);
        Tac.CheckEnd(b);
        Assert.Null(b.Outcome);
        Tac.Kill(b, a, u, DamageType.Kinetic);
        Assert.True(b.Outcome);
        var b2 = Flat(keep: false);
        var s = Soldier(b2, 5, 5);
        Alien(b2, 10, 10);
        Tac.Kill(b2, s, null, DamageType.Plasma);
        Assert.False(b2.Outcome);
    }

    [Fact]
    public void Leaving_early_saves_only_those_aboard_and_what_they_carry()
    {
        var b = BattleGen.Skirmish(4, soldiers: 4);
        var aboard = b.Squad.First();
        var outside = b.Squad.Last();
        outside.X = 20; outside.Y = 15;
        b.Aborted = true;
        b.Outcome = false;
        var d = Debrief.Of(b);
        Assert.Contains(aboard, d.Survivors);
        Assert.Contains(outside, d.Dead);
        Assert.False(d.Won);
        Assert.Equal(-20, d.Lines.Single(l => l.Label == "SOLDIERS LOST").Points);
    }
}
