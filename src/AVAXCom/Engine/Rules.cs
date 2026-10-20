namespace AVAXCom.Engine;

public enum DamageType { Kinetic, Laser, Plasma, Explosive, Stun, Melee }

/// <summary>Everything that can be carried, kept in the stores, bought, sold, made or found.</summary>
public enum ItemKind
{
    None,
    Pistol, PistolClip, Rifle, RifleClip, Cannon, CannonShells, Launcher, Rocket,
    LaserPistol, LaserRifle, LaserLance,
    StormPistol, StormPistolCell, StormRifle, StormRifleCell, StormCannon, StormCannonCell,
    FragGrenade, Flare, DemoCharge, PulseGrenade, StunBomb, ShockBaton, FieldKit, MindLens,
    CarapaceArmour, StriderSuit,
    Vorium, Hullmetal, DriveCore, NavLattice,
    PallidCorpse, SkimmerCorpse, CoilbackCorpse, BrakkCorpse, VeiledCorpse, HuskerCorpse, HuskCorpse, WardenWreck,
    LancerLauncher, LancerMissile, AutoCannon, AutoCannonShells, LaserCannon, StormBeam,
}

public enum ItemClass { Weapon, Ammo, Grenade, Melee, Medical, Psi, Armour, Artefact, Corpse, CraftWeapon, CraftAmmo }

/// <summary>
/// An item's numbers. A weapon's three ways of firing each have an accuracy (a percentage of the firer's own, 0 if it
/// can't fire that way) and a cost in time units (a percentage of the firer's full allowance, as in the original).
/// Weapons that take clips get their damage from the clip; beam weapons have their own and never run dry.
/// </summary>
public sealed record ItemInfo(
    ItemKind Kind, string Name, ItemClass Class, int Weight, int Size, int Power = 0, DamageType Damage = DamageType.Kinetic,
    int Snap = 0, int Aimed = 0, int Auto = 0, int SnapTu = 0, int AimedTu = 0, int AutoTu = 0,
    ItemKind Ammo = ItemKind.None, int Rounds = 0, bool TwoHanded = false, int Radius = 0,
    int Buy = 0, int Sell = 0, int Score = 0, double Space = 0.1, bool Alien = false)
{
    public bool Fires => Snap > 0 || Aimed > 0 || Auto > 0;
}

public enum Race { Pallid, Skimmer, Coilback, Brakk, Veiled, Husker, Husk, Warden }
public enum AlienRank { Soldier, Navigator, Leader, Commander, Terror }

/// <summary>An alien race's numbers (a soldier of it; ranks above are stronger).</summary>
public sealed record RaceInfo(
    Race Race, string Name, string Plural, int Tu, int Hp, int Front, int Side, int Rear, int Under,
    int Reactions, int Accuracy, int Bravery, int PsiStrength, int PsiSkill, int Strength, bool Flies,
    int Melee, DamageType MeleeType, ItemKind Weapon, ItemKind Corpse, int Score, string Blurb);

public enum UfoType { Seeker, Skiff, Raider, Harvester, Carrier, Dreadnought }

public sealed record UfoInfo(UfoType Type, string Name, string Size, int Hp, int Speed, int Range, int Power, double FireEvery, int Crew, int Score, double Detect);

public enum CraftType { Raven, Falcon, Vantage, Starfall }

public sealed record CraftInfo(CraftType Type, string Name, string Role, int Hp, int Speed, double FuelHours, int Weapons, int Seats, int Buy, int Upkeep);

/// <summary>A craft's gun or launcher: range in kilometres, damage per hit, seconds to reload, rounds it holds (0: never runs
/// out) and the chance each shot hits.</summary>
public sealed record CraftWeaponInfo(ItemKind Kind, string Name, int Range, int Damage, double Reload, int Rounds, double Accuracy, ItemKind AmmoItem, int PerItem);

public enum FacilityType
{
    Lift, Hangar, Quarters, Lab, Workshop, Stores, Containment, SmallRadar, LargeRadar,
    MissileDefence, LaserDefence, StormDefence, GravShield, MindShield, SignalDecoder, PsiLab,
}

/// <summary>A base facility: cost (in thousands), days to build, monthly upkeep (thousands), and what it holds or does.</summary>
public sealed record FacilityInfo(FacilityType Type, string Name, int Size, int Cost, int Days, int Upkeep, int Capacity, Topic? Needs, string Blurb);

public enum Topic
{
    LaserWeapons, LaserPistol, LaserRifle, LaserLance, LaserCannon, LaserDefences,
    PallidAutopsy, SkimmerAutopsy, CoilbackAutopsy, BrakkAutopsy, VeiledAutopsy, HuskerAutopsy, HuskAutopsy, WardenAutopsy,
    PallidInterrogation, SkimmerInterrogation, CoilbackInterrogation, BrakkInterrogation, VeiledInterrogation, HuskerInterrogation,
    Vorium, Hullmetal, DriveCore, NavLattice,
    StormPistol, StormRifle, StormCannon, PulseGrenade, StunBomb, StormBeam, StormDefences,
    CarapaceArmour, StriderSuit, GravDrive, GravShield, SignalDecoder,
    Psionics, MindLens, MindShield,
    AlienOrigins, TheHomeworld, Starfall,
}

/// <summary>
/// A research topic: its cost in scientist-days, the topics it needs first (all of <see cref="Needs"/>, and one of
/// <see cref="NeedsAny"/> if any are given), and the item or live captive it needs in the base (used up when the work
/// begins).
/// </summary>
public sealed record TopicInfo(Topic Topic, string Name, int Cost, Topic[] Needs, ItemKind Item = ItemKind.None, Race? Captive = null, int Score = 20, Topic[]? NeedsAny = null, string Text = "");

/// <summary>Something the workshops make: an item or a craft, its engineer-hours and cost (dollars) each, the topic it needs,
/// and the alien materials used up for each one.</summary>
public sealed record ProjectInfo(string Name, ItemKind Item, CraftType? Craft, int Hours, int Cost, Topic? Needs, (ItemKind Item, int Count)[] Materials);

public enum ArmourType { Vest, Carapace, Strider }

public sealed record ArmourInfo(ArmourType Type, string Name, int Front, int Side, int Rear, int Under, ItemKind Item, uint Colour);

/// <summary>Every number of the game, in one place.</summary>
public static class Rules
{
    public const string Organisation = "VIGIL";
    public const string Title = "VIGIL";
    public const string Subtitle = "THE SILENT INVASION";
    public const string Homeworld = "NYX";
    public const string Crystal = "VORIUM";

    // ───────────────────────────── items ─────────────────────────────

    private static ItemInfo[]? _items;
    public static ItemInfo[] Items => _items ??= Build();

    private static ItemInfo[] Build()
    {
        var list = new List<ItemInfo>
        {
            new(ItemKind.None, "", ItemClass.Artefact, 0, 0),
            new(ItemKind.Pistol, "PISTOL", ItemClass.Weapon, 5, 1, Snap: 60, Aimed: 78, SnapTu: 18, AimedTu: 30, Ammo: ItemKind.PistolClip, Buy: 800, Sell: 600),
            new(ItemKind.PistolClip, "PISTOL CLIP", ItemClass.Ammo, 1, 1, 26, DamageType.Kinetic, Rounds: 12, Buy: 70, Sell: 50),
            new(ItemKind.Rifle, "RIFLE", ItemClass.Weapon, 8, 2, Snap: 60, Aimed: 110, Auto: 35, SnapTu: 25, AimedTu: 60, AutoTu: 35, Ammo: ItemKind.RifleClip, TwoHanded: true, Buy: 3000, Sell: 2200),
            new(ItemKind.RifleClip, "RIFLE CLIP", ItemClass.Ammo, 2, 1, 30, DamageType.Kinetic, Rounds: 20, Buy: 200, Sell: 150),
            new(ItemKind.Cannon, "ASSAULT CANNON", ItemClass.Weapon, 18, 3, Snap: 60, Aimed: 90, SnapTu: 33, AimedTu: 80, Ammo: ItemKind.CannonShells, TwoHanded: true, Buy: 6400, Sell: 4800, Space: 0.2),
            new(ItemKind.CannonShells, "CANNON SHELLS", ItemClass.Ammo, 5, 1, 56, DamageType.Kinetic, Rounds: 6, Buy: 300, Sell: 220),
            new(ItemKind.Launcher, "ROCKET LAUNCHER", ItemClass.Weapon, 10, 3, Snap: 55, Aimed: 115, SnapTu: 45, AimedTu: 75, Ammo: ItemKind.Rocket, TwoHanded: true, Buy: 4000, Sell: 3000, Space: 0.2),
            new(ItemKind.Rocket, "ROCKET", ItemClass.Ammo, 6, 1, 75, DamageType.Explosive, Rounds: 1, Radius: 3, Buy: 900, Sell: 700),
            new(ItemKind.LaserPistol, "LASER PISTOL", ItemClass.Weapon, 6, 1, 46, DamageType.Laser, 40, 68, 28, 20, 55, 25, Sell: 20000, Score: 0),
            new(ItemKind.LaserRifle, "LASER RIFLE", ItemClass.Weapon, 8, 2, 60, DamageType.Laser, 65, 100, 46, 25, 50, 34, TwoHanded: true, Sell: 36000),
            new(ItemKind.LaserLance, "LASER LANCE", ItemClass.Weapon, 18, 3, 85, DamageType.Laser, 50, 84, 0, 33, 75, 0, TwoHanded: true, Sell: 61000, Space: 0.2),
            new(ItemKind.StormPistol, "STORM PISTOL", ItemClass.Weapon, 3, 1, Snap: 65, Aimed: 85, Auto: 50, SnapTu: 30, AimedTu: 60, AutoTu: 30, Ammo: ItemKind.StormPistolCell, Sell: 52000, Score: 4, Alien: true),
            new(ItemKind.StormPistolCell, "STORM PISTOL CELL", ItemClass.Ammo, 1, 1, 46, DamageType.Plasma, Rounds: 26, Sell: 4000, Score: 1, Alien: true),
            new(ItemKind.StormRifle, "STORM RIFLE", ItemClass.Weapon, 5, 2, Snap: 86, Aimed: 100, Auto: 55, SnapTu: 30, AimedTu: 60, AutoTu: 36, Ammo: ItemKind.StormRifleCell, TwoHanded: true, Sell: 84000, Score: 6, Alien: true),
            new(ItemKind.StormRifleCell, "STORM RIFLE CELL", ItemClass.Ammo, 2, 1, 80, DamageType.Plasma, Rounds: 28, Sell: 6000, Score: 1, Alien: true),
            new(ItemKind.StormCannon, "STORM CANNON", ItemClass.Weapon, 8, 3, Snap: 75, Aimed: 110, SnapTu: 33, AimedTu: 60, Ammo: ItemKind.StormCannonCell, TwoHanded: true, Sell: 120000, Score: 8, Space: 0.2, Alien: true),
            new(ItemKind.StormCannonCell, "STORM CANNON CELL", ItemClass.Ammo, 3, 1, 115, DamageType.Plasma, Rounds: 35, Sell: 9000, Score: 1, Alien: true),
            new(ItemKind.FragGrenade, "FRAG GRENADE", ItemClass.Grenade, 3, 1, 50, DamageType.Explosive, Radius: 3, Buy: 300, Sell: 200),
            new(ItemKind.Flare, "FLARE", ItemClass.Grenade, 1, 1, 0, DamageType.Explosive, Radius: 5, Buy: 60, Sell: 40),
            new(ItemKind.DemoCharge, "DEMOLITION CHARGE", ItemClass.Grenade, 6, 1, 110, DamageType.Explosive, Radius: 4, Buy: 500, Sell: 400),
            new(ItemKind.PulseGrenade, "PULSE GRENADE", ItemClass.Grenade, 1, 1, 80, DamageType.Explosive, Radius: 3, Sell: 7000, Score: 2, Alien: true),
            new(ItemKind.StunBomb, "STUN BOMB", ItemClass.Grenade, 1, 1, 90, DamageType.Stun, Radius: 3, Sell: 6000, Score: 2, Alien: true),
            new(ItemKind.ShockBaton, "SHOCK BATON", ItemClass.Melee, 3, 1, 65, DamageType.Stun, Aimed: 100, AimedTu: 30, Buy: 1200, Sell: 900),
            new(ItemKind.FieldKit, "FIELD KIT", ItemClass.Medical, 5, 1, Buy: 2000, Sell: 1500),
            new(ItemKind.MindLens, "MIND LENS", ItemClass.Psi, 3, 1, Aimed: 100, AimedTu: 25, Sell: 46000),
            new(ItemKind.CarapaceArmour, "CARAPACE ARMOUR", ItemClass.Armour, 0, 0, Sell: 22000, Space: 0.4),
            new(ItemKind.StriderSuit, "STRIDER SUIT", ItemClass.Armour, 0, 0, Sell: 54000, Space: 0.6),
            new(ItemKind.Vorium, Crystal, ItemClass.Artefact, 1, 1, Sell: 5000, Score: 3, Space: 0.02, Alien: true),
            new(ItemKind.Hullmetal, "HULLMETAL", ItemClass.Artefact, 3, 1, Sell: 6500, Score: 2, Space: 0.1, Alien: true),
            new(ItemKind.DriveCore, "DRIVE CORE", ItemClass.Artefact, 10, 2, Sell: 250000, Score: 25, Space: 0.3, Alien: true),
            new(ItemKind.NavLattice, "NAV LATTICE", ItemClass.Artefact, 6, 2, Sell: 80000, Score: 15, Space: 0.2, Alien: true),
            new(ItemKind.LancerLauncher, "LANCER LAUNCHER", ItemClass.CraftWeapon, 0, 0, Buy: 120000, Sell: 90000, Space: 0.6),
            new(ItemKind.LancerMissile, "LANCER MISSILE", ItemClass.CraftAmmo, 0, 0, Buy: 2500, Sell: 1800, Space: 0.2),
            new(ItemKind.AutoCannon, "AUTOCANNON", ItemClass.CraftWeapon, 0, 0, Buy: 30000, Sell: 22000, Space: 0.6),
            new(ItemKind.AutoCannonShells, "AUTOCANNON SHELLS", ItemClass.CraftAmmo, 0, 0, Buy: 1200, Sell: 800, Space: 0.1),
            new(ItemKind.LaserCannon, "LASER CANNON", ItemClass.CraftWeapon, 0, 0, Sell: 180000, Space: 0.6),
            new(ItemKind.StormBeam, "STORM BEAM", ItemClass.CraftWeapon, 0, 0, Sell: 310000, Space: 0.6),
        };
        foreach (var r in Races)
            list.Add(new ItemInfo(r.Corpse, r.Race == Race.Warden ? "WARDEN WRECK" : r.Name + " CORPSE", ItemClass.Corpse, 30, 3, Sell: 12000 + r.Score * 400, Score: 5, Space: 1, Alien: true));
        var all = new ItemInfo[Enum.GetValues<ItemKind>().Length];
        foreach (var i in list) all[(int)i.Kind] = i;
        return all;
    }

    public static ItemInfo Of(ItemKind k) => Items[(int)k];

    /// <summary>What can be bought from the start.</summary>
    public static readonly ItemKind[] Market =
    [
        ItemKind.Pistol, ItemKind.PistolClip, ItemKind.Rifle, ItemKind.RifleClip, ItemKind.Cannon, ItemKind.CannonShells,
        ItemKind.Launcher, ItemKind.Rocket, ItemKind.FragGrenade, ItemKind.Flare, ItemKind.DemoCharge, ItemKind.ShockBaton,
        ItemKind.FieldKit, ItemKind.LancerLauncher, ItemKind.LancerMissile, ItemKind.AutoCannon, ItemKind.AutoCannonShells,
    ];

    public const int SoldierCost = 40000, ScientistCost = 60000, EngineerCost = 25000;
    public const int SoldierPay = 20000, ScientistPay = 30000, EngineerPay = 25000;
    /// <summary>Hours for hired staff, bought items and craft to arrive.</summary>
    public const double ArrivalHours = 72;

    // ───────────────────────────── armour ─────────────────────────────

    public static readonly ArmourInfo[] Armours =
    [
        new(ArmourType.Vest, "BALLISTIC VEST", 12, 8, 5, 2, ItemKind.None, 0xFF5A6A48),
        new(ArmourType.Carapace, "CARAPACE ARMOUR", 50, 40, 30, 30, ItemKind.CarapaceArmour, 0xFF6A7890),
        new(ArmourType.Strider, "STRIDER SUIT", 90, 70, 55, 55, ItemKind.StriderSuit, 0xFF8A8AA8),
    ];

    public static ArmourInfo Of(ArmourType a) => Armours[(int)a];

    // ───────────────────────────── the aliens ─────────────────────────────

    public static readonly RaceInfo[] Races =
    [
        new(Race.Pallid, "PALLID", "PALLIDS", 54, 30, 4, 3, 2, 2, 63, 54, 80, 40, 0, 30, false, 0, DamageType.Melee, ItemKind.StormPistol, ItemKind.PallidCorpse, 10,
            "SMALL GREY-WHITE HUMANOIDS WITH SWOLLEN SKULLS AND BLACK ALMOND EYES. FRAIL, BUT THEIR LEADERS CAN REACH INTO A MIND."),
        new(Race.Skimmer, "SKIMMER", "SKIMMERS", 55, 45, 10, 8, 6, 6, 50, 50, 90, 40, 0, 60, true, 0, DamageType.Melee, ItemKind.StormRifle, ItemKind.SkimmerCorpse, 15,
            "LEGLESS TORSOS GRAFTED TO A JET HARNESS. THEY DRIFT OVER WALLS AND ROOFS AND STRIKE FROM ABOVE."),
        new(Race.Coilback, "COILBACK", "COILBACKS", 40, 60, 16, 14, 12, 10, 40, 60, 80, 30, 0, 50, false, 0, DamageType.Melee, ItemKind.StormRifle, ItemKind.CoilbackCorpse, 18,
            "SERPENTINE BODIES ON A SINGLE MUSCULAR COIL, SCALED IN DULL BRONZE. SLOW, TOUGH, FOND OF GRENADES."),
        new(Race.Brakk, "BRAKK", "BRAKK", 60, 125, 34, 30, 26, 22, 65, 70, 100, 20, 0, 90, false, 0, DamageType.Melee, ItemKind.StormCannon, ItemKind.BrakkCorpse, 30,
            "HULKING SHOCK TROOPS IN BOLTED OLIVE PLATE. THEY ADVANCE THROUGH FIRE THAT WOULD STOP ANYTHING ELSE."),
        new(Race.Veiled, "VEILED", "VEILED", 70, 55, 30, 30, 30, 30, 70, 80, 100, 120, 90, 60, false, 0, DamageType.Melee, ItemKind.StormRifle, ItemKind.VeiledCorpse, 40,
            "TALL, ROBED AND HOODED, FACELESS BEHIND A MIRRORED VEIL. THE MINDS BEHIND THE INVASION; THEIR WILL CAN TAKE YOURS."),
        new(Race.Husker, "HUSKER", "HUSKERS", 90, 96, 30, 26, 22, 20, 80, 0, 110, 10, 0, 110, false, 90, DamageType.Melee, ItemKind.None, ItemKind.HuskerCorpse, 30,
            "A SIX-LIMBED HORROR OF CHITIN AND HOOKED MANDIBLES. WHAT IT KILLS DOES NOT STAY DEAD: IT GETS UP AS A HUSK."),
        new(Race.Husk, "HUSK", "HUSKS", 40, 85, 4, 4, 4, 4, 40, 0, 110, 0, 0, 60, false, 40, DamageType.Melee, ItemKind.None, ItemKind.HuskCorpse, 5,
            "A HUSKER'S VICTIM, HOLLOWED OUT AND WALKING. WHEN IT FALLS, A NEW HUSKER BREAKS FREE OF IT."),
        new(Race.Warden, "WARDEN", "WARDENS", 70, 140, 60, 55, 50, 45, 60, 70, 110, 0, 0, 100, true, 0, DamageType.Melee, ItemKind.StormCannon, ItemKind.WardenWreck, 40,
            "A HOVERING LENS OF ARMOURED HULLMETAL WITH A STORM PROJECTOR IN ITS RIM. IT BURSTS WHEN DESTROYED."),
    ];

    public static RaceInfo Of(Race r) => Races[(int)r];

    public static string RankName(AlienRank r) => r switch
    {
        AlienRank.Soldier => "SOLDIER",
        AlienRank.Navigator => "NAVIGATOR",
        AlienRank.Leader => "LEADER",
        AlienRank.Commander => "COMMANDER",
        _ => "TERROR UNIT",
    };

    /// <summary>The terror unit that goes with a race: Husker-led horrors for some, Wardens for others.</summary>
    public static Race TerrorOf(Race r) => r is Race.Pallid or Race.Veiled or Race.Brakk ? Race.Warden : Race.Husker;

    // ───────────────────────────── UFOs and craft ─────────────────────────────

    public static readonly UfoInfo[] Ufos =
    [
        new(UfoType.Seeker, "SEEKER", "VERY SMALL", 50, 2200, 0, 0, 99, 2, 50, 0.45),
        new(UfoType.Skiff, "SKIFF", "SMALL", 160, 2400, 15, 15, 3, 4, 75, 0.6),
        new(UfoType.Raider, "RAIDER", "MEDIUM", 300, 2500, 22, 40, 3, 7, 150, 0.8),
        new(UfoType.Harvester, "HARVESTER", "LARGE", 620, 2700, 30, 60, 3, 9, 250, 1.0),
        new(UfoType.Carrier, "CARRIER", "LARGE", 1000, 3500, 40, 90, 2.5, 11, 400, 1.0),
        new(UfoType.Dreadnought, "DREADNOUGHT", "VERY LARGE", 2400, 4500, 65, 140, 2, 15, 700, 1.2),
    ];

    public static UfoInfo Of(UfoType t) => Ufos[(int)t];

    public static readonly CraftInfo[] Crafts =
    [
        new(CraftType.Raven, "RAVEN", "TROOP TRANSPORT", 150, 760, 10, 0, 14, 600000, 50000),
        new(CraftType.Falcon, "FALCON", "INTERCEPTOR", 100, 2100, 3, 2, 0, 450000, 40000),
        new(CraftType.Vantage, "VANTAGE", "STRIKE CRAFT", 400, 4200, 24, 2, 12, 0, 30000),
        new(CraftType.Starfall, "STARFALL", "DEEP-SPACE ASSAULT SHIP", 1200, 5400, 60, 3, 24, 0, 60000),
    ];

    public static CraftInfo Of(CraftType t) => Crafts[(int)t];

    public static readonly CraftWeaponInfo[] CraftWeapons =
    [
        new(ItemKind.LancerLauncher, "LANCER MISSILES", 60, 70, 10, 6, 0.7, ItemKind.LancerMissile, 1),
        new(ItemKind.AutoCannon, "AUTOCANNON", 12, 12, 0.6, 120, 0.3, ItemKind.AutoCannonShells, 50),
        new(ItemKind.LaserCannon, "LASER CANNON", 25, 70, 3, 0, 0.42, ItemKind.None, 0),
        new(ItemKind.StormBeam, "STORM BEAM", 55, 140, 2.5, 0, 0.5, ItemKind.None, 0),
    ];

    public static CraftWeaponInfo WeaponOf(ItemKind k) => CraftWeapons.First(w => w.Kind == k);

    // ───────────────────────────── base facilities ─────────────────────────────

    public const int BaseSize = 6;
    public const int NewBaseCost = 900000;

    public static readonly FacilityInfo[] Facilities =
    [
        new(FacilityType.Lift, "ACCESS LIFT", 1, 300, 6, 4, 0, null, "THE WAY DOWN INTO THE BASE. EVERY FACILITY MUST CONNECT TO IT."),
        new(FacilityType.Hangar, "HANGAR", 2, 200, 25, 25, 1, null, "HOUSES, REFUELS AND REPAIRS ONE CRAFT."),
        new(FacilityType.Quarters, "LIVING QUARTERS", 1, 400, 16, 10, 50, null, "BUNKS AND MESS FOR FIFTY PERSONNEL."),
        new(FacilityType.Lab, "LABORATORY", 1, 750, 26, 30, 50, null, "BENCH SPACE FOR FIFTY SCIENTISTS."),
        new(FacilityType.Workshop, "WORKSHOP", 1, 800, 32, 35, 50, null, "TOOLS AND FLOOR FOR FIFTY ENGINEERS."),
        new(FacilityType.Stores, "GENERAL STORES", 1, 150, 10, 5, 50, null, "FIFTY UNITS OF STORAGE FOR EQUIPMENT AND FINDS."),
        new(FacilityType.Containment, "ALIEN CONTAINMENT", 1, 400, 18, 15, 10, null, "HOLDS TEN LIVE ALIENS. CAPTIVES ARE LOST WITHOUT IT."),
        new(FacilityType.SmallRadar, "SMALL RADAR", 1, 500, 12, 10, 1500, null, "DETECTS UFOS WITHIN 1,500 NAUTICAL MILES."),
        new(FacilityType.LargeRadar, "LARGE RADAR", 1, 800, 25, 15, 2250, null, "DETECTS UFOS WITHIN 2,250 NAUTICAL MILES, MORE SURELY."),
        new(FacilityType.MissileDefence, "MISSILE DEFENCE", 1, 200, 16, 5, 500, null, "FIRES ON ANY UFO THAT ATTACKS THE BASE."),
        new(FacilityType.LaserDefence, "LASER DEFENCE", 1, 400, 24, 8, 800, Topic.LaserDefences, "A HEAVIER BASE DEFENCE OF FOCUSED LASERS."),
        new(FacilityType.StormDefence, "STORM DEFENCE", 1, 600, 34, 12, 1200, Topic.StormDefences, "THE ALIENS' OWN WEAPON TURNED SKYWARD."),
        new(FacilityType.GravShield, "GRAV SHIELD", 1, 800, 38, 15, 0, Topic.GravShield, "HOLDS AN ATTACKING UFO IN PLACE: EVERY DEFENCE FIRES TWICE."),
        new(FacilityType.MindShield, "MIND SHIELD", 1, 1300, 33, 50, 0, Topic.MindShield, "HIDES THE BASE FROM THE ALIENS' SEARCHES."),
        new(FacilityType.SignalDecoder, "SIGNAL DECODER", 1, 1400, 26, 30, 2400, Topic.SignalDecoder, "READS UFO SIGNALS: TYPE, CREW AND MISSION."),
        new(FacilityType.PsiLab, "PSI LAB", 1, 750, 24, 16, 10, Topic.Psionics, "TRAINS UP TO TEN SOLDIERS' PSIONIC SKILL A MONTH."),
    ];

    public static FacilityInfo Of(FacilityType t) => Facilities[(int)t];

    // ───────────────────────────── research ─────────────────────────────

    private static readonly Topic[] AnyInterrogation =
        [Topic.PallidInterrogation, Topic.SkimmerInterrogation, Topic.CoilbackInterrogation, Topic.BrakkInterrogation, Topic.VeiledInterrogation, Topic.HuskerInterrogation];

    public static readonly TopicInfo[] Topics =
    [
        new(Topic.LaserWeapons, "LASER WEAPONS", 120, [], Score: 20, Text: "FOCUSED LIGHT, HOT ENOUGH TO CUT ALIEN ARMOUR. THE FIRST STEP AWAY FROM BULLETS."),
        new(Topic.LaserPistol, "LASER PISTOL", 90, [Topic.LaserWeapons], Text: "A SIDEARM THAT NEVER NEEDS RELOADING."),
        new(Topic.LaserRifle, "LASER RIFLE", 150, [Topic.LaserPistol], Text: "THE STANDARD WEAPON OF A VIGIL SQUAD FROM NOW ON."),
        new(Topic.LaserLance, "LASER LANCE", 240, [Topic.LaserRifle], Text: "A HEAVY BEAM THAT BURNS THROUGH COVER."),
        new(Topic.LaserCannon, "LASER CANNON", 180, [Topic.LaserLance], Text: "A CRAFT-MOUNTED LASER FOR THE INTERCEPTORS."),
        new(Topic.LaserDefences, "LASER DEFENCES", 150, [Topic.LaserCannon], Text: "GROUND BATTERIES OF LASER CANNON FOR THE BASES."),
        new(Topic.PallidAutopsy, "PALLID AUTOPSY", 30, [], ItemKind.PallidCorpse, Score: 15, Text: "A FRAGILE BODY AND A BRAIN OUT OF ALL PROPORTION TO IT."),
        new(Topic.SkimmerAutopsy, "SKIMMER AUTOPSY", 30, [], ItemKind.SkimmerCorpse, Score: 15, Text: "MORE MACHINE THAN FLESH BELOW THE RIBS. THE HARNESS IS GROWN IN, NOT WORN."),
        new(Topic.CoilbackAutopsy, "COILBACK AUTOPSY", 30, [], ItemKind.CoilbackCorpse, Score: 15, Text: "A SINGLE GREAT MUSCLE, A SLOW HEART AND A BROOD SAC OF EGGS."),
        new(Topic.BrakkAutopsy, "BRAKK AUTOPSY", 40, [], ItemKind.BrakkCorpse, Score: 20, Text: "BRED FOR WAR: DENSE BONE, TWO HEARTS, AND IMPLANTS THAT SUPPRESS FEAR."),
        new(Topic.VeiledAutopsy, "VEILED AUTOPSY", 50, [], ItemKind.VeiledCorpse, Score: 25, Text: "A WITHERED BODY KEPT ALIVE BY THE MIND INSIDE IT. THE ROBES ARE ITS LIFE SUPPORT."),
        new(Topic.HuskerAutopsy, "HUSKER AUTOPSY", 40, [], ItemKind.HuskerCorpse, Score: 20, Text: "AN OVIPOSITOR IN THE JAW SEEDS ITS VICTIMS. KILL IT BEFORE IT REACHES YOU."),
        new(Topic.HuskAutopsy, "HUSK AUTOPSY", 20, [], ItemKind.HuskCorpse, Score: 10, Text: "NOTHING OF THE PERSON IS LEFT. A NEW HUSKER GROWS WHERE THE ORGANS WERE."),
        new(Topic.WardenAutopsy, "WARDEN ANALYSIS", 50, [], ItemKind.WardenWreck, Score: 25, Text: "A DRIVE CORE IN MINIATURE KEEPS IT ALOFT; THAT IS WHY IT EXPLODES."),
        new(Topic.PallidInterrogation, "PALLID INTERROGATION", 80, [], Captive: Race.Pallid, Score: 40, Text: "IT SPEAKS OF A GREAT MIND THAT CALLS THEM, AND OF THE MIND-WEAPON CALLED PSIONICS."),
        new(Topic.SkimmerInterrogation, "SKIMMER INTERROGATION", 80, [], Captive: Race.Skimmer, Score: 40, Text: "SKIMMERS ARE SERVANTS. THEY DO NOT KNOW WHY THEY ARE HERE, ONLY WHO SENT THEM."),
        new(Topic.CoilbackInterrogation, "COILBACK INTERROGATION", 80, [], Captive: Race.Coilback, Score: 40, Text: "THE COILBACKS BREED HUSKERS FOR THEIR MASTERS, AND FEAR THEM."),
        new(Topic.BrakkInterrogation, "BRAKK INTERROGATION", 80, [], Captive: Race.Brakk, Score: 40, Text: "A BRAKK KNOWS ONLY ORDERS. ITS IMPLANTS BURN OUT WHEN IT IS TAKEN."),
        new(Topic.VeiledInterrogation, "VEILED INTERROGATION", 120, [], Captive: Race.Veiled, Score: 80, Text: "THE VEILED SPEAK FOR NYX, A DARK WORLD BEYOND THE MOON'S ORBIT, AND FOR THE HIVE MIND THAT RULES IT."),
        new(Topic.HuskerInterrogation, "HUSKER STUDY", 80, [], Captive: Race.Husker, Score: 40, Text: "THERE IS NO MIND TO QUESTION, ONLY HUNGER."),
        new(Topic.Vorium, Crystal, 50, [], ItemKind.Vorium, Score: 30, Text: "A VIOLET CRYSTAL THAT GIVES OFF ENERGY AS IT DECAYS. IT POWERS EVERYTHING THE ALIENS BUILD."),
        new(Topic.Hullmetal, "HULLMETAL", 120, [], ItemKind.Hullmetal, Score: 30, Text: "AN ALLOY LIGHTER THAN ALUMINIUM AND HARDER THAN TUNGSTEN. WE CAN WORK IT."),
        new(Topic.DriveCore, "DRIVE CORE", 360, [Topic.Vorium], ItemKind.DriveCore, Score: 60, Text: "A VORIUM REACTOR THAT BENDS GRAVITY. THE HEART OF EVERY UFO."),
        new(Topic.NavLattice, "NAV LATTICE", 240, [], ItemKind.NavLattice, Score: 40, Text: "A CRYSTAL COMPUTER THAT FINDS ITS WAY BY THE STARS AND BY SOMETHING ELSE."),
        new(Topic.StormPistol, "STORM PISTOL", 150, [], ItemKind.StormPistol, Score: 30, Text: "A PISTOL THAT THROWS BOLTS OF CONTAINED PLASMA. ITS CELLS NEED VORIUM."),
        new(Topic.StormRifle, "STORM RIFLE", 210, [], ItemKind.StormRifle, Score: 40, Text: "THE ALIENS' STANDARD ARM: ACCURATE, POWERFUL, AND NOW OURS."),
        new(Topic.StormCannon, "STORM CANNON", 270, [], ItemKind.StormCannon, Score: 50, Text: "A HEAVY PLASMA WEAPON THAT CAN BRING DOWN A WALL."),
        new(Topic.PulseGrenade, "PULSE GRENADE", 90, [], ItemKind.PulseGrenade, Text: "A SMALL, VICIOUS ALIEN GRENADE."),
        new(Topic.StunBomb, "STUN BOMB", 90, [], ItemKind.StunBomb, Text: "A GAS GRENADE THAT KNOCKS OUT WITHOUT KILLING: THE WAY TO TAKE PRISONERS."),
        new(Topic.StormBeam, "STORM BEAM", 360, [Topic.StormCannon, Topic.Vorium], Score: 50, Text: "A STORM CANNON SCALED UP FOR OUR CRAFT."),
        new(Topic.StormDefences, "STORM DEFENCES", 300, [Topic.StormBeam], Text: "STORM BEAM BATTERIES FOR THE BASES."),
        new(Topic.CarapaceArmour, "CARAPACE ARMOUR", 210, [Topic.Hullmetal, Topic.PallidAutopsy], Score: 30, Text: "HULLMETAL PLATES OVER A WOVEN BODYSUIT."),
        new(Topic.StriderSuit, "STRIDER SUIT", 450, [Topic.CarapaceArmour, Topic.DriveCore, Topic.BrakkAutopsy], Score: 50, Text: "A POWERED SUIT OF HULLMETAL, BUILT AFTER THE BRAKK'S OWN."),
        new(Topic.GravDrive, "GRAV DRIVE", 600, [Topic.DriveCore, Topic.NavLattice, Topic.Hullmetal], Score: 80, Text: "A DRIVE CORE IN A HULL OF OUR OWN: THE VANTAGE, FASTER THAN ANY JET."),
        new(Topic.GravShield, "GRAV SHIELD", 360, [Topic.GravDrive], Text: "A GRAVITY FIELD OVER A BASE THAT HOLDS ATTACKERS IN ITS GUNS' REACH."),
        new(Topic.SignalDecoder, "SIGNAL DECODER", 300, [Topic.NavLattice], Text: "A RECEIVER FOR THE UFOS' OWN SIGNALS."),
        new(Topic.Psionics, "PSIONICS", 360, [Topic.PallidInterrogation], Score: 60, Text: "THE MIND CAN BE A WEAPON. SOME OF OUR SOLDIERS HAVE THE GIFT; A PSI LAB WILL FIND THEM."),
        new(Topic.MindLens, "MIND LENS", 180, [Topic.Psionics], Text: "A FOCUS FOR A GIFTED SOLDIER'S WILL: PANIC AN ALIEN, OR TAKE IT OVER."),
        new(Topic.MindShield, "MIND SHIELD", 360, [Topic.Psionics], Text: "A FIELD THAT HIDES A BASE FROM THE ALIENS' PSIONIC SEARCH."),
        new(Topic.AlienOrigins, "ALIEN ORIGINS", 300, [], Score: 60, NeedsAny: AnyInterrogation, Text: "THE ALIENS ARE NOT ONE PEOPLE BUT MANY, ENSLAVED AND SENT BY ONE POWER."),
        new(Topic.TheHomeworld, "THE HOMEWORLD", 600, [Topic.AlienOrigins, Topic.VeiledInterrogation], Score: 200, Text: "NYX: A DARK WORLD BEHIND A GATE IN SPACE. THE HIVE MIND LIVES THERE. DESTROY IT AND THE INVASION ENDS."),
        new(Topic.Starfall, "STARFALL", 900, [Topic.GravDrive, Topic.AlienOrigins], Score: 120, Text: "A SHIP TO CROSS THE GATE: THE ONLY CRAFT THAT CAN REACH NYX."),
    ];

    public static TopicInfo Of(Topic t) => Topics[(int)t];

    /// <summary>The autopsy (or study) topic for a corpse, and the interrogation topic for a live alien.</summary>
    public static Topic? AutopsyOf(Race r) => r switch
    {
        Race.Pallid => Topic.PallidAutopsy, Race.Skimmer => Topic.SkimmerAutopsy, Race.Coilback => Topic.CoilbackAutopsy,
        Race.Brakk => Topic.BrakkAutopsy, Race.Veiled => Topic.VeiledAutopsy, Race.Husker => Topic.HuskerAutopsy,
        Race.Husk => Topic.HuskAutopsy, _ => Topic.WardenAutopsy,
    };

    public static Topic? InterrogationOf(Race r) => r switch
    {
        Race.Pallid => Topic.PallidInterrogation, Race.Skimmer => Topic.SkimmerInterrogation, Race.Coilback => Topic.CoilbackInterrogation,
        Race.Brakk => Topic.BrakkInterrogation, Race.Veiled => Topic.VeiledInterrogation, Race.Husker => Topic.HuskerInterrogation,
        _ => null,
    };

    // ───────────────────────────── manufacture ─────────────────────────────

    public static readonly ProjectInfo[] Projects =
    [
        new("FIELD KIT", ItemKind.FieldKit, null, 120, 1200, null, []),
        new("FRAG GRENADE", ItemKind.FragGrenade, null, 30, 150, null, []),
        new("LANCER MISSILE", ItemKind.LancerMissile, null, 40, 1500, null, []),
        new("LASER PISTOL", ItemKind.LaserPistol, null, 300, 8000, Topic.LaserPistol, []),
        new("LASER RIFLE", ItemKind.LaserRifle, null, 400, 20000, Topic.LaserRifle, []),
        new("LASER LANCE", ItemKind.LaserLance, null, 600, 32000, Topic.LaserLance, []),
        new("LASER CANNON", ItemKind.LaserCannon, null, 800, 90000, Topic.LaserCannon, []),
        new("STORM PISTOL", ItemKind.StormPistol, null, 500, 30000, Topic.StormPistol, [(ItemKind.Vorium, 1)]),
        new("STORM PISTOL CELL", ItemKind.StormPistolCell, null, 60, 1500, Topic.StormPistol, [(ItemKind.Vorium, 1)]),
        new("STORM RIFLE", ItemKind.StormRifle, null, 700, 45000, Topic.StormRifle, [(ItemKind.Vorium, 1)]),
        new("STORM RIFLE CELL", ItemKind.StormRifleCell, null, 80, 2500, Topic.StormRifle, [(ItemKind.Vorium, 1)]),
        new("STORM CANNON", ItemKind.StormCannon, null, 900, 60000, Topic.StormCannon, [(ItemKind.Vorium, 2)]),
        new("STORM CANNON CELL", ItemKind.StormCannonCell, null, 100, 3500, Topic.StormCannon, [(ItemKind.Vorium, 1)]),
        new("STUN BOMB", ItemKind.StunBomb, null, 120, 2500, Topic.StunBomb, [(ItemKind.Vorium, 1)]),
        new("STORM BEAM", ItemKind.StormBeam, null, 1200, 120000, Topic.StormBeam, [(ItemKind.Vorium, 4)]),
        new("MIND LENS", ItemKind.MindLens, null, 600, 50000, Topic.MindLens, [(ItemKind.Vorium, 1)]),
        new("CARAPACE ARMOUR", ItemKind.CarapaceArmour, null, 800, 25000, Topic.CarapaceArmour, [(ItemKind.Hullmetal, 4)]),
        new("STRIDER SUIT", ItemKind.StriderSuit, null, 1500, 40000, Topic.StriderSuit, [(ItemKind.Hullmetal, 6), (ItemKind.Vorium, 2)]),
        new("VANTAGE", ItemKind.None, CraftType.Vantage, 12000, 500000, Topic.GravDrive, [(ItemKind.DriveCore, 1), (ItemKind.NavLattice, 1), (ItemKind.Hullmetal, 15)]),
        new("STARFALL", ItemKind.None, CraftType.Starfall, 24000, 1200000, Topic.Starfall, [(ItemKind.DriveCore, 2), (ItemKind.NavLattice, 1), (ItemKind.Hullmetal, 30), (ItemKind.Vorium, 20)]),
    ];

    public static ProjectInfo Project(string name) => Projects.First(p => p.Name == name);

    // ───────────────────────────── soldiers ─────────────────────────────

    public static readonly string[] RankNames = ["ROOKIE", "SQUADDIE", "SERGEANT", "CAPTAIN", "COLONEL", "COMMANDER"];

    // ───────────────────────────── the council's opinion ─────────────────────────────

    public static string Rating(int monthScore) => monthScore switch
    {
        <= -500 => "TERRIBLE",
        < 0 => "POOR",
        < 500 => "OK",
        < 1500 => "GOOD",
        _ => "EXCELLENT",
    };
}
