namespace AVAXCom.Engine;

/// <summary>The four parts of a tile, as in the original: its floor, the wall on its west edge, the wall on its north
/// edge, and what stands in it.</summary>
public enum Part { Floor, West, North, Object }

/// <summary>Every piece of scenery a tile can be built from.</summary>
public enum Piece : byte
{
    None,
    // Floors.
    Grass, Dirt, Furrows, Road, Pavement, Sand, Snow, WoodFloor, TileFloor, Carpet, Roof, UfoFloor, CraftDeck, CraftRoof,
    Rubble, CaveFloor, HiveFloor, BaseFloor, Scorched, LaneX, LaneY,
    // Walls (on a tile's west or north edge).
    Brick, Plank, Fence, Hedge, Window, Door, DoorOpen, UfoWall, UfoDoor, UfoDoorOpen, CraftHull, CaveWall, HiveWall, BaseWall, BaseDoor, BaseDoorOpen, Stub,
    // Things standing in a tile.
    Tree, Pine, Bush, Corn, Haystack, Crate, Table, Bed, Shelves, Stairs, Lift, Console, NavConsole, DriveCore, Pod, Barrel, Car,
    Boulder, Cactus, Brain, Pillar, Debris, Machine, Fungus, Wreck, Bedrock, Wing,
}

/// <summary>
/// A piece's numbers: how hard it is to destroy, whether it stops walkers and eyes, how high it stands (0..1 of a
/// storey: a fence is low, a wall full height), what walking through it costs, whether it's a door or a way up, how
/// much light it gives, whether it blows up, and what it leaves when it's destroyed.
/// </summary>
public sealed record PieceInfo(Piece Piece, string Name, Part Part, int Armour, bool BlocksMove, bool BlocksSight, double Height, int Cost = 0,
    bool Door = false, bool Up = false, int Light = 0, int Explodes = 0, Piece Ruin = Piece.None, bool Alien = false);

public static class Pieces
{
    public static readonly PieceInfo[] All = Build();

    private static PieceInfo[] Build()
    {
        var list = new PieceInfo[]
        {
            new(Piece.None, "", Part.Floor, 0, false, false, 0),
            new(Piece.Grass, "GRASS", Part.Floor, 60, false, true, 0, Ruin: Piece.Scorched),
            new(Piece.Dirt, "EARTH", Part.Floor, 60, false, true, 0),
            new(Piece.Furrows, "FIELD", Part.Floor, 60, false, true, 0, Cost: 2),
            new(Piece.Road, "ROAD", Part.Floor, 80, false, true, 0, Ruin: Piece.Rubble),
            new(Piece.Pavement, "PAVEMENT", Part.Floor, 80, false, true, 0, Ruin: Piece.Rubble),
            new(Piece.Sand, "SAND", Part.Floor, 60, false, true, 0),
            new(Piece.Snow, "SNOW", Part.Floor, 60, false, true, 0, Cost: 1),
            new(Piece.WoodFloor, "FLOORBOARDS", Part.Floor, 25, false, true, 0),
            new(Piece.TileFloor, "TILES", Part.Floor, 35, false, true, 0),
            new(Piece.Carpet, "CARPET", Part.Floor, 25, false, true, 0),
            new(Piece.Roof, "ROOF", Part.Floor, 25, false, true, 0),
            new(Piece.UfoFloor, "UFO DECK", Part.Floor, 255, false, true, 0, Light: 1, Alien: true),
            new(Piece.CraftDeck, "TRANSPORT DECK", Part.Floor, 255, false, true, 0),
            new(Piece.CraftRoof, "TRANSPORT ROOF", Part.Floor, 255, false, true, 0),
            new(Piece.Rubble, "RUBBLE", Part.Floor, 60, false, true, 0, Cost: 2),
            new(Piece.CaveFloor, "ROCK FLOOR", Part.Floor, 255, false, true, 0),
            new(Piece.HiveFloor, "HIVE FLOOR", Part.Floor, 255, false, true, 0, Light: 1),
            new(Piece.BaseFloor, "BASE FLOOR", Part.Floor, 255, false, true, 0, Light: 1),
            new(Piece.Scorched, "SCORCHED EARTH", Part.Floor, 60, false, true, 0),
            new(Piece.LaneX, "ROAD", Part.Floor, 80, false, true, 0, Ruin: Piece.Rubble),
            new(Piece.LaneY, "ROAD", Part.Floor, 80, false, true, 0, Ruin: Piece.Rubble),
            new(Piece.Brick, "BRICK WALL", Part.West, 50, true, true, 1, Ruin: Piece.Stub),
            new(Piece.Plank, "WOODEN WALL", Part.West, 25, true, true, 1, Ruin: Piece.Stub),
            new(Piece.Fence, "FENCE", Part.West, 12, true, false, 0.35),
            new(Piece.Hedge, "HEDGE", Part.West, 18, true, true, 0.7),
            new(Piece.Window, "WINDOW", Part.West, 6, true, false, 1, Ruin: Piece.Stub),
            new(Piece.Door, "DOOR", Part.West, 20, false, true, 1, Cost: 4, Door: true, Ruin: Piece.Stub),
            new(Piece.DoorOpen, "OPEN DOOR", Part.West, 20, false, false, 1, Ruin: Piece.Stub),
            new(Piece.UfoWall, "UFO HULL", Part.West, 120, true, true, 1, Alien: true),
            new(Piece.UfoDoor, "UFO DOOR", Part.West, 120, false, true, 1, Door: true, Alien: true),
            new(Piece.UfoDoorOpen, "UFO DOOR", Part.West, 120, false, false, 1, Alien: true),
            new(Piece.CraftHull, "TRANSPORT HULL", Part.West, 255, true, true, 1),
            new(Piece.CaveWall, "ROCK", Part.West, 255, true, true, 1),
            new(Piece.HiveWall, "HIVE WALL", Part.West, 150, true, true, 1, Alien: true),
            new(Piece.BaseWall, "BASE WALL", Part.West, 255, true, true, 1),
            new(Piece.BaseDoor, "BLAST DOOR", Part.West, 255, false, true, 1, Door: true),
            new(Piece.BaseDoorOpen, "BLAST DOOR", Part.West, 255, false, false, 1),
            new(Piece.Stub, "BROKEN WALL", Part.West, 30, false, false, 0.2),
            new(Piece.Tree, "TREE", Part.Object, 40, true, true, 1, Ruin: Piece.Debris),
            new(Piece.Pine, "PINE", Part.Object, 40, true, true, 1, Ruin: Piece.Debris),
            new(Piece.Bush, "BUSH", Part.Object, 10, false, true, 0.5, Cost: 2),
            new(Piece.Corn, "TALL CROPS", Part.Object, 6, false, true, 0.75, Cost: 2),
            new(Piece.Haystack, "HAYSTACK", Part.Object, 12, true, true, 0.6),
            new(Piece.Crate, "CRATE", Part.Object, 20, true, false, 0.45, Ruin: Piece.Debris),
            new(Piece.Table, "TABLE", Part.Object, 10, true, false, 0.35),
            new(Piece.Bed, "BED", Part.Object, 10, true, false, 0.3),
            new(Piece.Shelves, "SHELVES", Part.Object, 20, true, true, 0.9, Ruin: Piece.Debris),
            new(Piece.Stairs, "STAIRS", Part.Object, 50, false, false, 0.5, Cost: 2, Up: true),
            new(Piece.Lift, "GRAVITY LIFT", Part.Object, 200, false, false, 0, Up: true, Light: 2, Alien: true),
            new(Piece.Console, "ALIEN CONSOLE", Part.Object, 60, true, false, 0.45, Light: 1, Alien: true),
            new(Piece.NavConsole, "NAV LATTICE", Part.Object, 60, true, false, 0.5, Light: 1, Alien: true),
            new(Piece.DriveCore, "DRIVE CORE", Part.Object, 80, true, true, 0.8, Light: 2, Explodes: 110, Alien: true),
            new(Piece.Pod, "ALIEN POD", Part.Object, 40, true, false, 0.5, Light: 1, Alien: true),
            new(Piece.Barrel, "FUEL DRUM", Part.Object, 10, true, false, 0.45, Explodes: 45),
            new(Piece.Car, "CAR", Part.Object, 40, true, false, 0.5, Explodes: 50, Ruin: Piece.Wreck),
            new(Piece.Boulder, "BOULDER", Part.Object, 120, true, true, 0.7),
            new(Piece.Cactus, "CACTUS", Part.Object, 10, true, false, 0.8),
            new(Piece.Brain, "THE HIVE MIND", Part.Object, 140, true, true, 1, Light: 3, Alien: true),
            new(Piece.Pillar, "PILLAR", Part.Object, 200, true, true, 1),
            new(Piece.Debris, "DEBRIS", Part.Object, 20, false, false, 0.25, Cost: 2),
            new(Piece.Machine, "MACHINERY", Part.Object, 60, true, false, 0.6),
            new(Piece.Fungus, "HIVE GROWTH", Part.Object, 20, false, true, 0.6, Cost: 2, Light: 1, Alien: true),
            new(Piece.Wreck, "BURNT-OUT CAR", Part.Object, 30, true, false, 0.35),
            new(Piece.Bedrock, "SOLID ROCK", Part.Object, 255, true, true, 1),
            new(Piece.Wing, "TRANSPORT WING", Part.Object, 255, true, false, 0.45),
        };
        var all = new PieceInfo[Enum.GetValues<Piece>().Length];
        foreach (var p in list) all[(int)p.Piece] = p;
        return all;
    }

    public static PieceInfo Of(Piece p) => All[(int)p];
}

/// <summary>A thing on the battlefield: a weapon (with the clip in it), a clip, a grenade (perhaps primed), a kit.</summary>
public sealed class Item
{
    public ItemKind Kind;
    /// <summary>Rounds left (a clip, or a field kit's uses).</summary>
    public int Rounds;
    /// <summary>The clip in a weapon.</summary>
    public Item? Clip;
    /// <summary>A primed grenade's turns to go (-1: not primed; 0: it goes off at the end of this turn).</summary>
    public int Fuse = -1;
    /// <summary>A field kit's charges: wounds it can still treat, stimulants and painkillers.</summary>
    public int Heals, Stims, Pains;

    public ItemInfo Info => Rules.Of(Kind);

    public static Item Make(ItemKind k, bool loaded = true)
    {
        var i = new Item { Kind = k };
        var info = i.Info;
        if (info.Class == ItemClass.Ammo) i.Rounds = info.Rounds;
        if (info.Class == ItemClass.Medical) { i.Heals = 6; i.Stims = 3; i.Pains = 6; }
        if (loaded && info.Ammo != ItemKind.None) i.Clip = new Item { Kind = info.Ammo, Rounds = Rules.Of(info.Ammo).Rounds };
        return i;
    }

    /// <summary>The damage this weapon does now: its clip's (if it takes clips), else its own.</summary>
    public (int Power, DamageType Type, int Radius) Shot
    {
        get
        {
            var info = Info;
            if (info.Ammo != ItemKind.None)
            {
                var c = Rules.Of(info.Ammo);
                return (c.Power, c.Damage, c.Radius);
            }
            return (info.Power, info.Damage, info.Radius);
        }
    }

    /// <summary>Whether it can fire now (beam weapons always can).</summary>
    public bool Loaded => Info.Ammo == ItemKind.None || Clip is { Rounds: > 0 };

    public int Weight => Info.Weight + (Clip?.Info.Weight ?? 0);
}

public enum Side { Player, Alien, Civilian }
public enum Slot { RightHand, LeftHand, Belt, Backpack, Ground }
public enum Body { Head, Torso, LeftArm, RightArm, LeftLeg, RightLeg }
public enum Panic { None, Frozen, Berserk }

/// <summary>
/// A unit in battle: a soldier, an alien or a civilian. Where it stands and faces, its time units, health, stun and
/// morale, its fatal wounds, its armour, its numbers, what it carries in its hands, belt and backpack, and the deeds of
/// this mission that will count toward its experience.
/// </summary>
public sealed class BUnit
{
    public int Id;
    public Side Side;
    /// <summary>The side it fights for now (a mind-controlled soldier fights for the aliens).</summary>
    public Side Team;
    public string Name = "";
    public Soldier? Soldier;
    public Race Race;
    public AlienRank AlienRank;
    public int X, Y, Z;
    /// <summary>Which way it faces: 0 north (up the map), then clockwise to 7 north-west.</summary>
    public int Facing = 4;
    public bool Kneeling, Flies, Female;
    public int Tu, MaxTu, Hp, MaxHp, Stun, Morale = 100;
    public int Reactions, Accuracy, Throwing, Strength, Bravery, PsiStrength, PsiSkill;
    public int Front, SideArmour, Rear, Under;
    public int[] Wounds = new int[6];
    public int Melee;
    public DamageType MeleeType;
    public bool Dead, Out;
    public Panic Panic;
    public int Vision = 20;
    public Item? Right, Left;
    public List<Item> Belt = [], Backpack = [];
    public ArmourType Armour;
    public bool Seen;
    /// <summary>An alien set to guard where it stands (inside its UFO) rather than roam.</summary>
    public bool Guard;
    /// <summary>Turns a mind-controlled unit has left under the aliens' will.</summary>
    public int Controlled;
    // This mission's deeds.
    public int Kills, Hits, Shots, ReactionShots, Throws, MeleeHits, PsiUses, DamageTaken;

    public bool Alive => !Dead;
    public bool Active => !Dead && !Out;
    public int FatalWounds => Wounds.Sum();
    public bool IsHuman => Side != Side.Alien;
    public bool IsSoldier => Soldier is not null;
    public RaceInfo RaceInfo => Rules.Of(Race);

    public IEnumerable<Item> Carried
    {
        get
        {
            if (Right is not null) yield return Right;
            if (Left is not null) yield return Left;
            foreach (var i in Belt) yield return i;
            foreach (var i in Backpack) yield return i;
        }
    }

    public int Weight => Carried.Sum(i => i.Weight);

    /// <summary>The weapon it would fire: the right hand's, else the left's.</summary>
    public Item? Weapon => Right is { Info.Fires: true } ? Right : Left is { Info.Fires: true } ? Left : null;

    public string Title => IsSoldier ? $"{Soldier!.RankName} {Name}" : Side == Side.Civilian ? "CIVILIAN" : $"{RaceInfo.Name} {Rules.RankName(AlienRank)}";
}

/// <summary>A tile position in the battle.</summary>
public readonly record struct Cell(int X, int Y, int Z);

/// <summary>Something moving across the screen: a bullet, a beam, a bolt, a rocket or a thrown grenade.</summary>
public sealed class Projectile
{
    public double X0, Y0, Z0, X1, Y1, Z1;
    public double Progress, Speed, Peak;
    public DamageType Type;
    public bool Thrown, Rocket;
    public Action? OnArrive;
}

public enum FxKind { Flash, Explosion, Hit, Blood, Psi, Smoke, Words, Death }

/// <summary>A shot waiting to leave its gun (the rest of a burst, or a reaction).</summary>
public readonly record struct Shot(BUnit Shooter, Cell Target, double Delay, FireMode Mode, bool Reaction);

/// <summary>A short-lived picture: a muzzle flash, a blast, a puff, a word floating up.</summary>
public sealed class Fx
{
    public FxKind Kind;
    public double X, Y, Z, Age, Life = 0.6, Size = 1;
    public string Text = "";
    public uint Colour;
}

/// <summary>
/// The battlefield: a map of tiles, several storeys high (each tile with its floor, west wall, north wall and object
/// and how much damage each has taken), the units on it, the items lying about, the light, what the player has seen,
/// whose turn it is, and the animations under way (a walk, shots in the air, blasts) that the game waits on.
/// </summary>
public sealed class Battle
{
    public const int Levels = 4;
    public int W, H;
    public Piece[,,] Floor, West, North, Obj;
    public byte[,,] FloorHurt, WestHurt, NorthHurt, ObjHurt;
    public List<BUnit> Units = [];
    public Dictionary<Cell, List<Item>> Ground = [];
    public bool[,,] Explored, Visible;
    public int[,] Flares;
    public Mission Mission = new();
    public bool Night;
    public int Turn = 1;
    public Side Active = Side.Player;
    public Dice Rng;
    public int NextId = 1;
    /// <summary>The transport's deck: units standing here can leave with it.</summary>
    public HashSet<(int X, int Y)> Exit = [];
    // Animations.
    public bool Instant;
    public BUnit? Walker;
    public List<Cell> WalkPath = [];
    public double WalkT;
    public bool WalkStopped;
    public List<Projectile> Flying = [];
    public List<Shot> Burst = [];
    public List<Fx> Effects = [];
    public double Wait;
    public List<string> Log = [];
    /// <summary>Sounds the battle asks for (the engine plays the ones the player would hear).</summary>
    public List<Sfx> Heard = [];
    /// <summary>A word for the player: REACTION FIRE, PANIC and the like.</summary>
    public List<string> News = [];
    /// <summary>Set when the battle is over: won (true) or lost (false); null while it goes on.</summary>
    public bool? Outcome;
    public bool Aborted;
    // Counts for the debrief.
    public int CiviliansKilled, CiviliansSaved;
    public List<(Race Race, AlienRank Rank)> AliensKilled = [];
    public bool BrainDestroyed;
    /// <summary>The heart of the mission (the UFO's middle, the hive mind): where the pilot heads when it knows of nothing else.</summary>
    public Cell Objective;
    /// <summary>The aliens' turn: who has finished, how many actions each has taken, and where they last saw each enemy.</summary>
    public HashSet<int> Finished = [];
    public Dictionary<int, int> Actions = [];
    public Dictionary<int, Cell> Known = [];

    public Battle(int w, int h, int seed)
    {
        W = w;
        H = h;
        Floor = new Piece[w, h, Levels];
        West = new Piece[w, h, Levels];
        North = new Piece[w, h, Levels];
        Obj = new Piece[w, h, Levels];
        FloorHurt = new byte[w, h, Levels];
        WestHurt = new byte[w, h, Levels];
        NorthHurt = new byte[w, h, Levels];
        ObjHurt = new byte[w, h, Levels];
        Explored = new bool[w, h, Levels];
        Visible = new bool[w, h, Levels];
        Flares = new int[w, h];
        Rng = new Dice(seed);
    }

    public bool In(int x, int y, int z) => (uint)x < W && (uint)y < H && (uint)z < Levels;
    public bool In(Cell c) => In(c.X, c.Y, c.Z);

    public Piece Get(Part p, int x, int y, int z) => !In(x, y, z) ? Piece.None : p switch
    {
        Part.Floor => Floor[x, y, z], Part.West => West[x, y, z], Part.North => North[x, y, z], _ => Obj[x, y, z],
    };

    public void Set(Part p, int x, int y, int z, Piece v)
    {
        if (!In(x, y, z)) return;
        switch (p)
        {
            case Part.Floor: Floor[x, y, z] = v; FloorHurt[x, y, z] = 0; break;
            case Part.West: West[x, y, z] = v; WestHurt[x, y, z] = 0; break;
            case Part.North: North[x, y, z] = v; NorthHurt[x, y, z] = 0; break;
            default: Obj[x, y, z] = v; ObjHurt[x, y, z] = 0; break;
        }
    }

    public BUnit? UnitAt(int x, int y, int z) => Units.FirstOrDefault(u => !u.Dead && !u.Out && u.X == x && u.Y == y && u.Z == z);
    public BUnit? UnitAt(Cell c) => UnitAt(c.X, c.Y, c.Z);
    /// <summary>Anything in a tile, stunned bodies too (they block nobody, but are there).</summary>
    public BUnit? BodyAt(int x, int y, int z) => Units.FirstOrDefault(u => !u.Dead && u.Out && u.X == x && u.Y == y && u.Z == z);

    public IEnumerable<BUnit> Team(Side s) => Units.Where(u => u.Active && u.Team == s);
    public IEnumerable<BUnit> Squad => Units.Where(u => u.Side == Side.Player);

    public List<Item> ItemsAt(Cell c)
    {
        if (!Ground.TryGetValue(c, out var l)) Ground[c] = l = [];
        return l;
    }

    public void Drop(Item i, Cell c) => ItemsAt(FloorBelow(c)).Add(i);

    /// <summary>The first tile at or below a cell with a floor to stand on (or the ground).</summary>
    public Cell FloorBelow(Cell c)
    {
        int z = c.Z;
        while (z > 0 && Floor[c.X, c.Y, z] == Piece.None && Obj[c.X, c.Y, z - 1] is not (Piece.Stairs or Piece.Lift)) z--;
        return new Cell(c.X, c.Y, z);
    }

    public bool HasFloor(int x, int y, int z) => In(x, y, z) && (z == 0 || Floor[x, y, z] != Piece.None);

    public int Aliens => Units.Count(u => u.Active && u.Side == Side.Alien && u.Team == Side.Alien);

    public bool Busy => Walker is not null || Flying.Count > 0 || Burst.Count > 0 || Wait > 0;

    public void Say(string text)
    {
        Log.Add(text);
        if (Log.Count > 40) Log.RemoveAt(0);
    }

    /// <summary>The brightness of a tile (0 dark, 1 lit): daylight everywhere, or at night the flares and lit things.</summary>
    public bool Lit(int x, int y, int z) => !Night || Flares[x, y] > 0 || In(x, y, z) && (Pieces.Of(Floor[x, y, z]).Light > 0 || Pieces.Of(Obj[x, y, z]).Light > 0);
}
