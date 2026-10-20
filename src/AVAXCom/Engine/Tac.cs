namespace AVAXCom.Engine;

public enum FireMode { Snap, Aimed, Auto }
public enum KitUse { Heal, Stimulant, Painkiller }

public static partial class Tac
{
    public const int KneelCost = 4, StandCost = 8, ReloadCost = 15, PrimeCost = 12, KitCost = 10;
    public const int ThrowPercent = 25, PsiPercent = 25;
    public const int BeltRoom = 4, BackpackRoom = 6;
    /// <summary>Seconds a walker takes over a tile (seen, and unseen), a shot's speed (tiles a second), and a thrown arc's time.</summary>
    public const double WalkSeconds = 0.14, HiddenWalkSeconds = 0.02, ShotSpeed = 45, ThrowSeconds = 0.7, BurstGap = 0.12;

    // ───────────────────────────── making units ─────────────────────────────

    /// <summary>A soldier as they go into battle: their numbers, their armour, and their kit.</summary>
    public static BUnit FromSoldier(Battle b, Soldier s)
    {
        var a = Rules.Of(s.Armour);
        return new BUnit
        {
            Id = b.NextId++, Side = Side.Player, Team = Side.Player, Name = s.Name, Soldier = s,
            Tu = s.Tu, MaxTu = s.Tu, Hp = s.Health, MaxHp = s.Health, Reactions = s.Reactions, Accuracy = s.Firing, Throwing = s.Throwing,
            Strength = s.Strength, Bravery = s.Bravery, PsiStrength = s.PsiStrength, PsiSkill = s.PsiSkill, Armour = s.Armour, Female = s.Female,
            Front = a.Front, SideArmour = a.Side, Rear = a.Rear, Under = a.Under, Facing = 0, Vision = SightRange,
        };
    }

    /// <summary>An alien of a race and rank: leaders and commanders are tougher, and some of them have the gift.</summary>
    public static BUnit MakeAlien(Battle b, Race race, AlienRank rank, int month)
    {
        var r = Rules.Of(race);
        double up = rank switch { AlienRank.Navigator => 1.1, AlienRank.Leader => 1.2, AlienRank.Commander => 1.35, _ => 1 };
        double late = 1 + Math.Min(0.3, month * 0.04);
        int hp = (int)(r.Hp * up * late);
        var u = new BUnit
        {
            Id = b.NextId++, Side = Side.Alien, Team = Side.Alien, Race = race, AlienRank = rank, Name = r.Name,
            Tu = r.Tu, MaxTu = r.Tu, Hp = hp, MaxHp = hp, Reactions = (int)(r.Reactions * up), Accuracy = (int)(r.Accuracy * up), Throwing = 60,
            Strength = r.Strength, Bravery = r.Bravery, PsiStrength = r.PsiStrength, Front = r.Front, SideArmour = r.Side, Rear = r.Rear, Under = r.Under,
            Flies = r.Flies, Melee = r.Melee, MeleeType = r.MeleeType, Facing = b.Rng.Next(8),
        };
        // The gift: the Veiled always; Pallid leaders and commanders once the war is a few months old.
        if (race == Race.Veiled) u.PsiSkill = (int)(r.PsiSkill * up);
        else if (race == Race.Pallid && rank >= AlienRank.Leader && month >= 2) u.PsiSkill = 40 + 15 * (rank - AlienRank.Leader);
        if (r.Weapon != ItemKind.None)
        {
            var w = Item.Make(rank >= AlienRank.Leader && race != Race.Warden && b.Rng.Chance(0.5) && r.Weapon == ItemKind.StormPistol ? ItemKind.StormRifle : r.Weapon);
            u.Right = w;
            if (race != Race.Warden && w.Info.Ammo != ItemKind.None && b.Rng.Chance(0.6)) u.Belt.Add(Item.Make(w.Info.Ammo));
        }
        if (race is Race.Coilback or Race.Brakk && b.Rng.Chance(race == Race.Coilback ? 0.7 : 0.35)) u.Belt.Add(Item.Make(ItemKind.PulseGrenade));
        if (race == Race.Pallid && rank >= AlienRank.Navigator && b.Rng.Chance(0.3)) u.Belt.Add(Item.Make(ItemKind.StunBomb));
        return u;
    }

    public static BUnit MakeCivilian(Battle b) => new()
    {
        Id = b.NextId++, Side = Side.Civilian, Team = Side.Civilian, Name = "CIVILIAN", Tu = 50, MaxTu = 50, Hp = 20, MaxHp = 20,
        Reactions = 20, Bravery = 20, Facing = b.Rng.Next(8), Female = b.Rng.Chance(0.5),
    };

    public static void Place(Battle b, BUnit u, Cell c)
    {
        u.X = c.X;
        u.Y = c.Y;
        u.Z = c.Z;
        b.Units.Add(u);
    }

    // ───────────────────────────── time units ─────────────────────────────

    /// <summary>A unit's time units at the start of its turn: its full allowance, less what it carries over its strength.</summary>
    public static int TurnTu(BUnit u)
    {
        int over = Math.Max(0, u.Weight - u.Strength);
        int wounded = 2 * (u.Wounds[(int)Body.LeftLeg] + u.Wounds[(int)Body.RightLeg]);
        return Math.Max(0, u.MaxTu - over - wounded);
    }

    /// <summary>The time units a percentage of a unit's full allowance comes to.</summary>
    public static int Percent(BUnit u, int pct) => Math.Max(1, (int)Math.Ceiling(u.MaxTu * pct / 100.0));

    public static int ShotCost(BUnit u, Item w, FireMode m)
    {
        var info = w.Info;
        int pct = m switch { FireMode.Snap => info.SnapTu, FireMode.Aimed => info.AimedTu, _ => info.AutoTu };
        return pct <= 0 ? -1 : Percent(u, pct);
    }

    /// <summary>Whether the unit can act now (it's its side's turn, nothing's moving, and it's not out of it).</summary>
    public static bool Ready(Battle b, BUnit u) => u.Active && u.Team == b.Active && !b.Busy && b.Outcome is null && u.Panic == Panic.None;

    private static bool Spend(BUnit u, int tu)
    {
        if (tu < 0 || u.Tu < tu) return false;
        u.Tu -= tu;
        return true;
    }

    // ───────────────────────────── turning, kneeling, walking ─────────────────────────────

    public static string? TurnTo(Battle b, BUnit u, int dir)
    {
        if (!Ready(b, u)) return "NOT NOW";
        dir = (dir % 8 + 8) % 8;
        if (dir == u.Facing) return null;
        if (!Spend(u, PathFinder.TurnCost(u.Facing, dir))) return "NOT ENOUGH TIME UNITS";
        u.Facing = dir;
        if (u.Team == Side.Player) Look(b);
        return null;
    }

    public static string? Face(Battle b, BUnit u, Cell at) => at.X == u.X && at.Y == u.Y ? null : TurnTo(b, u, DirOf(at.X - u.X, at.Y - u.Y));

    public static string? Kneel(Battle b, BUnit u)
    {
        if (!Ready(b, u)) return "NOT NOW";
        if (u.Flies && !u.IsHuman) return "IT CANNOT KNEEL";
        if (!Spend(u, u.Kneeling ? StandCost : KneelCost)) return "NOT ENOUGH TIME UNITS";
        u.Kneeling = !u.Kneeling;
        if (u.Team == Side.Player) Look(b);
        return null;
    }

    /// <summary>Starts a unit walking to a tile; it goes as far as its time units take it, stopping if it sees a new
    /// enemy on the way.</summary>
    public static string? Walk(Battle b, BUnit u, Cell goal)
    {
        if (!Ready(b, u)) return "NOT NOW";
        var path = PathFinder.Find(b, u, goal);
        if (path is null) return "NO WAY THERE";
        if (u.Kneeling)
        {
            if (!Spend(u, StandCost)) return "NOT ENOUGH TIME UNITS";
            u.Kneeling = false;
        }
        b.Walker = u;
        b.WalkPath = path;
        b.WalkT = 0;
        b.WalkStopped = false;
        return null;
    }

    /// <summary>One tile of a walk: turn, step (opening a door on the way), look, and let the enemy react.</summary>
    private static void WalkStep(Battle b)
    {
        var u = b.Walker!;
        if (b.WalkPath.Count == 0 || !u.Active || b.WalkStopped || u.Panic != Panic.None) { b.Walker = null; return; }
        var next = b.WalkPath[0];
        var at = new Cell(u.X, u.Y, u.Z);
        int turn = next.X != at.X || next.Y != at.Y ? PathFinder.TurnCost(u.Facing, DirOf(next.X - at.X, next.Y - at.Y)) : 0;
        int step = PathFinder.StepCost(b, u, at, next);
        if (step < 0 || u.Tu < turn + step) { b.Walker = null; return; }
        u.Tu -= turn + step;
        if (next.X != at.X || next.Y != at.Y) u.Facing = DirOf(next.X - at.X, next.Y - at.Y);
        OpenDoors(b, at, next);
        b.WalkPath.RemoveAt(0);
        var before = u.Team == Side.Player ? b.Units.Where(t => t.Active && t.Team == Side.Alien && t.Seen).ToHashSet() : null;
        var sawBefore = u.Team == Side.Alien ? EnemiesSeenBy(b, u).ToHashSet() : null;
        u.X = next.X;
        u.Y = next.Y;
        u.Z = next.Z;
        if (u.Seen || u.Team == Side.Player) b.Heard.Add(Sfx.Step);
        Look(b);
        // A new enemy in sight stops the walk.
        if (before is not null && b.Units.Any(t => t.Active && t.Team == Side.Alien && t.Seen && !before.Contains(t)))
        {
            b.WalkStopped = true;
            b.News.Add("HOSTILE SIGHTED");
            b.Heard.Add(Sfx.SayHostile);
        }
        if (sawBefore is not null && EnemiesSeenBy(b, u).Any(t => !sawBefore.Contains(t))) b.WalkStopped = true;
        React(b, u);
        if (b.WalkPath.Count == 0) b.WalkStopped = true;
    }

    private static void OpenDoors(Battle b, Cell from, Cell to)
    {
        int dx = to.X - from.X, dy = to.Y - from.Y;
        if (from.Z != to.Z) return;
        void Open(Cell c, Part p)
        {
            var piece = b.Get(p, c.X, c.Y, c.Z);
            var open = piece switch { Piece.Door => Piece.DoorOpen, Piece.UfoDoor => Piece.UfoDoorOpen, Piece.BaseDoor => Piece.BaseDoorOpen, _ => Piece.None };
            if (open == Piece.None) return;
            b.Set(p, c.X, c.Y, c.Z, open);
            b.Heard.Add(Sfx.Door);
        }
        if (dx != 0) { var w = EdgeWall(b, from.X, from.Y, from.Z, dx, 0); Open(w.At, w.Part); }
        if (dy != 0) { var w = EdgeWall(b, from.X + dx, from.Y, from.Z, 0, dy); Open(w.At, w.Part); var w2 = EdgeWall(b, from.X, from.Y, from.Z, 0, dy); Open(w2.At, w2.Part); }
    }

    // ───────────────────────────── the inventory ─────────────────────────────

    public static int Room(Slot s) => s switch { Slot.Belt => BeltRoom, Slot.Backpack => BackpackRoom, Slot.Ground => 999, _ => 1 };

    public static List<Item> Contents(Battle b, BUnit u, Slot s) => s switch
    {
        Slot.Belt => u.Belt,
        Slot.Backpack => u.Backpack,
        Slot.Ground => b.ItemsAt(new Cell(u.X, u.Y, u.Z)),
        Slot.RightHand => u.Right is null ? [] : [u.Right],
        _ => u.Left is null ? [] : [u.Left],
    };

    /// <summary>The time units to move an item from one place on a soldier to another: taking it out, then putting it in.</summary>
    public static int MoveCost(Slot from, Slot to)
    {
        if (from == to) return 0;
        int take = from switch { Slot.Ground => 8, Slot.Backpack => 8, Slot.Belt => 4, _ => 0 };
        int put = to switch { Slot.Backpack => 6, Slot.Belt => 4, Slot.Ground => 2, _ => 2 };
        return take + put;
    }

    /// <summary>Whether moving things about is free: on the first turn, aboard the transport, as the original's
    /// equipping before the ramp drops.</summary>
    public static bool FreeKit(Battle b, BUnit u) => b.Turn == 1 && b.Exit.Contains((u.X, u.Y)) && u.Z == 0;

    public static string? MoveItem(Battle b, BUnit u, Item item, Slot from, Slot to)
    {
        if (!u.Active || u.Team != b.Active || b.Busy) return "NOT NOW";
        if (from == to) return null;
        var source = Contents(b, u, from);
        if (!source.Contains(item)) return "IT IS NOT THERE";
        switch (to)
        {
            case Slot.RightHand when u.Right is not null:
            case Slot.LeftHand when u.Left is not null:
                return "THAT HAND IS FULL";
            case Slot.Belt or Slot.Backpack when item.Info.Size > Room(to) || Contents(b, u, to).Sum(i => i.Info.Size) + item.Info.Size > Room(to):
                return "NO ROOM";
            case Slot.Belt when item.Info.Size > 1:
                return "TOO BIG FOR THE BELT";
            case Slot.Backpack or Slot.Belt or Slot.RightHand or Slot.LeftHand when item.Info.Class == ItemClass.Corpse:
                return "TOO HEAVY TO CARRY";
        }
        int cost = FreeKit(b, u) ? 0 : MoveCost(from, to);
        if (!Spend(u, cost)) return "NOT ENOUGH TIME UNITS";
        switch (from)
        {
            case Slot.RightHand: u.Right = null; break;
            case Slot.LeftHand: u.Left = null; break;
            case Slot.Ground: b.ItemsAt(new Cell(u.X, u.Y, u.Z)).Remove(item); break;
            default: source.Remove(item); break;
        }
        switch (to)
        {
            case Slot.RightHand: u.Right = item; break;
            case Slot.LeftHand: u.Left = item; break;
            case Slot.Ground: b.Drop(item, new Cell(u.X, u.Y, u.Z)); break;
            default: Contents(b, u, to).Add(item); break;
        }
        return null;
    }

    /// <summary>Loads a fresh clip (from the belt, the backpack, the other hand or the ground) into the weapon in hand.</summary>
    public static string? Reload(Battle b, BUnit u)
    {
        if (!Ready(b, u)) return "NOT NOW";
        var w = u.Weapon;
        if (w is null || w.Info.Ammo == ItemKind.None) return "NOTHING TO RELOAD";
        var ground = b.ItemsAt(new Cell(u.X, u.Y, u.Z));
        Item? clip = u.Belt.FirstOrDefault(i => i.Kind == w.Info.Ammo) ?? u.Backpack.FirstOrDefault(i => i.Kind == w.Info.Ammo)
                     ?? (u.Left?.Kind == w.Info.Ammo ? u.Left : u.Right?.Kind == w.Info.Ammo ? u.Right : null) ?? ground.FirstOrDefault(i => i.Kind == w.Info.Ammo);
        if (clip is null) return "NO AMMUNITION";
        if (!Spend(u, ReloadCost)) return "NOT ENOUGH TIME UNITS";
        u.Belt.Remove(clip);
        u.Backpack.Remove(clip);
        ground.Remove(clip);
        if (u.Left == clip) u.Left = null;
        if (u.Right == clip) u.Right = null;
        w.Clip = clip;
        return null;
    }

    /// <summary>Primes a grenade in hand: it goes off at the end of the turn (0) or that many turns later.</summary>
    public static string? Prime(Battle b, BUnit u, Item g, int fuse)
    {
        if (!Ready(b, u)) return "NOT NOW";
        if (g.Info.Class != ItemClass.Grenade || g.Kind == ItemKind.Flare) return "IT CANNOT BE PRIMED";
        if (u.Right != g && u.Left != g) return "IT MUST BE IN HAND";
        if (g.Fuse >= 0) return "IT IS ALREADY PRIMED";
        if (!Spend(u, PrimeCost)) return "NOT ENOUGH TIME UNITS";
        g.Fuse = Math.Clamp(fuse, 0, 5);
        return null;
    }

    // ───────────────────────────── field kits ─────────────────────────────

    /// <summary>Uses a field kit in hand on itself or a unit next to it: treat a wound (the worst body part's fatal
    /// wounds stop and a little health comes back), a stimulant (wakes the stunned), or a painkiller (restores morale).</summary>
    public static string? UseKit(Battle b, BUnit u, BUnit patient, KitUse use)
    {
        if (!Ready(b, u)) return "NOT NOW";
        var kit = u.Right is { Kind: ItemKind.FieldKit } r ? r : u.Left is { Kind: ItemKind.FieldKit } l ? l : null;
        if (kit is null) return "NO FIELD KIT IN HAND";
        if (patient.Dead) return "TOO LATE";
        if (Math.Max(Math.Abs(patient.X - u.X), Math.Abs(patient.Y - u.Y)) > 1 || patient.Z != u.Z) return "TOO FAR AWAY";
        int left = use switch { KitUse.Heal => kit.Heals, KitUse.Stimulant => kit.Stims, _ => kit.Pains };
        if (left <= 0) return "NONE LEFT";
        if (!Spend(u, KitCost)) return "NOT ENOUGH TIME UNITS";
        switch (use)
        {
            case KitUse.Heal:
                kit.Heals--;
                int worst = Array.IndexOf(patient.Wounds, patient.Wounds.Max());
                int healed = patient.Wounds[worst];
                patient.Wounds[worst] = 0;
                patient.Hp = Math.Min(patient.MaxHp, patient.Hp + Math.Max(1, healed) * 3);
                break;
            case KitUse.Stimulant:
                kit.Stims--;
                patient.Stun = 0;
                if (patient.Out && patient.Hp > 0) patient.Out = false;
                break;
            default:
                kit.Pains--;
                patient.Morale = Math.Min(100, patient.Morale + 40);
                patient.Panic = Panic.None;
                break;
        }
        return null;
    }

    // ───────────────────────────── psionics ─────────────────────────────

    /// <summary>The chance (percent) a psionic attack takes: the attacker's strength and skill against the defender's
    /// strength (and skill, if trained).</summary>
    public static int PsiChance(BUnit attacker, BUnit target, bool control)
    {
        double attack = attacker.PsiStrength * (0.5 + attacker.PsiSkill / 100.0);
        double defence = target.PsiStrength + target.PsiSkill / 2.0;
        double chance = 45 + (attack - defence) * 0.6 - (control ? 20 : 0);
        return (int)Math.Clamp(chance, 2, 90);
    }

    /// <summary>A psionic attack: panic the target, or (harder) take it over until the end of its next turn.</summary>
    public static string? Psi(Battle b, BUnit u, BUnit target, bool control)
    {
        if (!Ready(b, u)) return "NOT NOW";
        if (u.PsiSkill <= 0) return "NO PSIONIC SKILL";
        if (u.IsHuman && u.Right?.Kind != ItemKind.MindLens && u.Left?.Kind != ItemKind.MindLens) return "NO MIND LENS IN HAND";
        if (!target.Active || !Hostile(u, target)) return "NOT A TARGET";
        if (target.Race is Race.Warden or Race.Husk && target.Side == Side.Alien) return "IT HAS NO MIND";
        if (!Spend(u, Percent(u, PsiPercent))) return "NOT ENOUGH TIME UNITS";
        u.PsiUses++;
        b.Heard.Add(Sfx.Psionic);
        b.Effects.Add(new Fx { Kind = FxKind.Psi, X = target.X + 0.5, Y = target.Y + 0.5, Z = target.Z + 0.5, Life = 1.2 });
        if (!b.Rng.Chance(PsiChance(u, target, control) / 100.0))
        {
            b.Say($"{u.Title}'S PSIONIC ATTACK FAILS");
            return null;
        }
        if (control)
        {
            target.Team = u.Team;
            target.Controlled = 1;
            target.Tu = TurnTu(target);
            target.Panic = Panic.None;
            b.News.Add(target.Side == Side.Player ? $"{target.Name} IS UNDER ALIEN CONTROL" : $"{target.Title} IS UNDER YOUR CONTROL");
        }
        else
        {
            target.Morale = 0;
            target.Panic = Panic.Frozen;
            target.Tu = 0;
            b.News.Add($"{(target.IsSoldier ? target.Name : target.Title)} HAS PANICKED");
        }
        Look(b);
        CheckEnd(b);
        return null;
    }

    // ───────────────────────────── turns ─────────────────────────────

    /// <summary>
    /// Ends the side's turn: grenades primed to go off now do (and the rest count down), then the next side's turn
    /// begins (the aliens' after the player's; the civilians run, and a new turn is the player's again).
    /// </summary>
    public static void EndTurn(Battle b)
    {
        if (b.Outcome is not null) return;
        Fuses(b);
        if (b.Outcome is not null) return;
        foreach (var u in b.Units.Where(u => u.Team == b.Active)) { if (u.Panic != Panic.None) u.Panic = Panic.None; }
        if (b.Active == Side.Player)
        {
            b.Active = Side.Alien;
            BeginTurn(b, Side.Alien);
            b.Heard.Add(Sfx.HiddenMovement);
        }
        else
        {
            b.Active = Side.Civilian;
            Civilians(b);
            b.Turn++;
            b.Active = Side.Player;
            BeginTurn(b, Side.Player);
            b.Heard.Add(Sfx.TurnStart);
        }
        Look(b);
        CheckEnd(b);
    }

    /// <summary>Primed grenades: on the ground or in a pocket, those at zero go off; the rest count down.</summary>
    private static void Fuses(Battle b)
    {
        var bangs = new List<(Item Item, Cell At)>();
        foreach (var (cell, items) in b.Ground)
            foreach (var i in items.Where(i => i.Fuse >= 0)) { if (i.Fuse == 0) bangs.Add((i, cell)); else i.Fuse--; }
        foreach (var u in b.Units.Where(u => !u.Dead))
            foreach (var i in u.Carried.Where(i => i.Fuse >= 0).ToList()) { if (i.Fuse == 0) bangs.Add((i, new Cell(u.X, u.Y, u.Z))); else i.Fuse--; }
        foreach (var (item, at) in bangs)
        {
            b.ItemsAt(at).Remove(item);
            foreach (var u in b.Units) { if (u.Right == item) u.Right = null; if (u.Left == item) u.Left = null; u.Belt.Remove(item); u.Backpack.Remove(item); }
            var (power, type, radius) = item.Shot;
            Explode(b, at, power, radius, type, null);
        }
    }

    /// <summary>
    /// The start of a side's turn for each of its units: time units back (less any overload), wounds bleed, the stunned
    /// come round, mind control wears off, morale recovers, and those whose nerve has gone panic, freeze or go berserk.
    /// </summary>
    public static void BeginTurn(Battle b, Side side)
    {
        foreach (var u in b.Units.Where(u => !u.Dead && (u.Team == side || u.Side == side && u.Controlled > 0)).ToList())
        {
            if (u.Controlled > 0 && --u.Controlled == 0)
            {
                u.Team = u.Side;
                b.News.Add(u.IsSoldier ? $"{u.Name} IS FREE OF ALIEN CONTROL" : $"{u.Title} BREAKS FREE");
            }
            if (u.Team != side) continue;
            int bleed = u.FatalWounds;
            if (bleed > 0)
            {
                u.Hp -= bleed;
                if (u.Hp <= 0) { Kill(b, u, null, DamageType.Kinetic, $"{u.Name} HAS BLED TO DEATH"); continue; }
            }
            if (u.Stun > 0) u.Stun = Math.Max(0, u.Stun - 3);
            if (u.Out && u.Stun < u.Hp) u.Out = false;
            if (u.Out) continue;
            u.Tu = TurnTu(u);
            u.Panic = Panic.None;
            if (u.Morale < 50 && u.Race is not (Race.Warden or Race.Husk or Race.Husker) && b.Rng.Chance((50 - u.Morale) * 2 / 100.0))
            {
                bool berserk = b.Rng.Chance(1 / 3.0);
                u.Panic = berserk ? Panic.Berserk : Panic.Frozen;
                string who = u.IsSoldier ? u.Name : u.Title;
                b.News.Add(berserk ? $"{who} HAS GONE BERSERK" : $"{who} HAS PANICKED");
                if (!berserk)
                {
                    if (u.Right is not null) { b.Drop(u.Right, new Cell(u.X, u.Y, u.Z)); u.Right = null; }
                    u.Tu = 0;
                }
                else Berserk(b, u);
                u.Morale = Math.Min(100, u.Morale + 20);
            }
            else u.Morale = Math.Min(100, u.Morale + 10);
        }
    }

    /// <summary>A berserk unit spends its time units firing at whoever is nearest (friend or foe), or into the air.</summary>
    private static void Berserk(Battle b, BUnit u)
    {
        var w = u.Weapon;
        if (w is null || !w.Loaded) { u.Tu = 0; return; }
        var target = b.Units.Where(t => t.Active && t != u && Sees(b, u, t)).OrderBy(t => Math.Abs(t.X - u.X) + Math.Abs(t.Y - u.Y)).FirstOrDefault();
        int cost = ShotCost(u, w, FireMode.Snap);
        double delay = 0.4;
        while (cost > 0 && u.Tu >= cost)
        {
            u.Tu -= cost;
            var at = target is not null ? new Cell(target.X, target.Y, target.Z) : new Cell(Math.Clamp(u.X + b.Rng.Range(-8, 8), 0, b.W - 1), Math.Clamp(u.Y + b.Rng.Range(-8, 8), 0, b.H - 1), u.Z);
            b.Burst.Add(new Shot(u, at, delay, FireMode.Snap, false));
            delay += 0.35;
        }
        u.Tu = 0;
    }

    /// <summary>The civilians' turn: each runs a few steps away from the nearest alien it can see.</summary>
    private static void Civilians(Battle b)
    {
        foreach (var c in b.Units.Where(u => u.Active && u.Side == Side.Civilian).ToList())
        {
            var threat = b.Team(Side.Alien).OrderBy(a => Math.Abs(a.X - c.X) + Math.Abs(a.Y - c.Y)).FirstOrDefault();
            for (int step = 0; step < 4; step++)
            {
                var at = new Cell(c.X, c.Y, c.Z);
                var options = PathFinder.Neighbours(b, at).Where(n => n.Z == c.Z && PathFinder.StepCost(b, c, at, n) > 0).ToList();
                if (options.Count == 0) break;
                var pick = threat is null ? options[b.Rng.Next(options.Count)]
                    : options.OrderByDescending(n => Math.Abs(n.X - threat.X) + Math.Abs(n.Y - threat.Y) + b.Rng.Next() * 0.5).First();
                c.Facing = DirOf(pick.X - c.X, pick.Y - c.Y);
                c.X = pick.X;
                c.Y = pick.Y;
            }
        }
    }

    // ───────────────────────────── the animation clock ─────────────────────────────

    /// <summary>
    /// Runs the battle's animations on by a moment: words and blasts fade, shots of a burst leave one by one and fly to
    /// where they hit, and a walker takes its next step when nothing else is moving. On the aliens' turn, when all is
    /// still, the next alien thinks.
    /// </summary>
    public static void Step(Battle b, double dt)
    {
        foreach (var e in b.Effects) e.Age += dt;
        b.Effects.RemoveAll(e => e.Age >= e.Life);
        if (b.Wait > 0) { b.Wait = Math.Max(0, b.Wait - dt); return; }
        if (b.Burst.Count > 0)
        {
            for (int i = 0; i < b.Burst.Count; i++) b.Burst[i] = b.Burst[i] with { Delay = b.Burst[i].Delay - (b.Instant ? 1 : dt) };
            int due = b.Burst.FindIndex(s => s.Delay <= 0);
            if (due >= 0 && b.Flying.Count == 0)
            {
                var shot = b.Burst[due];
                b.Burst.RemoveAt(due);
                FireOne(b, shot.Shooter, shot.Target, shot.Mode);
                if (b.Burst.Count == 0 && !shot.Reaction && shot.Shooter.Active) React(b, shot.Shooter);
            }
        }
        if (b.Flying.Count > 0)
        {
            foreach (var p in b.Flying.ToList())
            {
                p.Progress += dt * p.Speed;
                if (p.Progress < 1) continue;
                b.Flying.Remove(p);
                p.OnArrive?.Invoke();
            }
            return;
        }
        if (b.Burst.Count > 0) return;
        if (b.Walker is { } w)
        {
            b.WalkT += dt;
            double each = b.Instant ? 0 : w.Team != Side.Player && !w.Seen ? HiddenWalkSeconds : WalkSeconds;
            if (b.WalkT >= each)
            {
                b.WalkT = 0;
                WalkStep(b);
                if (b.WalkStopped && b.Flying.Count == 0 && b.Burst.Count == 0) b.Walker = null;
            }
            return;
        }
        if (b.Outcome is null && b.Active == Side.Alien) AlienBrain.Think(b);
    }

    /// <summary>Plays out everything in motion at once (for tests, the pilot and the hidden aliens).</summary>
    public static void Settle(Battle b)
    {
        for (int i = 0; i < 20000 && b.Busy; i++) Step(b, 0.05);
    }

    /// <summary>Plays the aliens' whole turn out (for tests and the pilot), back to the player's.</summary>
    public static void RunAliens(Battle b)
    {
        for (int i = 0; i < 40000 && b.Active != Side.Player && b.Outcome is null; i++) Step(b, 0.05);
        Settle(b);
    }

    /// <summary>Whether the battle is over: every alien dead or out (or the hive mind destroyed) is a win; every soldier
    /// dead, out or taken over is a loss.</summary>
    public static void CheckEnd(Battle b)
    {
        if (b.Outcome is not null) return;
        if (b.Aliens == 0 || b.BrainDestroyed) b.Outcome = true;
        else if (!b.Units.Any(u => u.Side == Side.Player && u.Active && u.Team == Side.Player)) b.Outcome = false;
    }
}
