namespace AVAXCom.Engine;

public sealed class Facility
{
    public FacilityType Type;
    public int X, Y;
    /// <summary>Days of building still to go (0: working).</summary>
    public double DaysLeft;
    public bool Built => DaysLeft <= 0;
    public int Size => Rules.Of(Type).Size;
    public bool Covers(int x, int y) => x >= X && y >= Y && x < X + Size && y < Y + Size;
}

public sealed record Captive(Race Race, AlienRank Rank);

public enum CraftState { Ready, Refuelling, Repairing, Rearming, Out, Returning, Waiting, Lost }

public sealed class CraftGun
{
    public ItemKind Kind;
    public int Rounds;
    public CraftWeaponInfo Info => Rules.WeaponOf(Kind);
}

/// <summary>A craft: where it is and what it's doing, its damage and fuel, its guns, and where it's going.</summary>
public sealed class Craft
{
    public int Id, BaseId;
    public CraftType Type;
    public string Name = "";
    public GeoPoint At;
    public CraftState State = CraftState.Ready;
    public double Damage, Fuel;
    public List<CraftGun> Guns = [];
    /// <summary>The UFO it's chasing or the site it's flying to (by id; -1 neither).</summary>
    public int TargetUfo = -1, TargetSite = -1;
    /// <summary>Where a patrol goes, when it has no target.</summary>
    public GeoPoint? Waypoint;
    public CraftInfo Info => Rules.Of(Type);
    public bool Flying => State is CraftState.Out or CraftState.Returning or CraftState.Waiting;
    public bool CanLaunch => State == CraftState.Ready || (State is CraftState.Refuelling or CraftState.Rearming && Fuel > Info.FuelHours * 0.5);
}

/// <summary>Someone or something on the way to a base: staff hired, items bought, a craft delivered.</summary>
public sealed class Arrival
{
    public double Hours;
    public string What = "";
    public ItemKind Item;
    public int Count;
    public int Scientists, Engineers;
    public Soldier? Soldier;
    public CraftType? Craft;
}

public sealed class ResearchJob
{
    public Topic Topic;
    public int Scientists;
    /// <summary>Scientist-days done so far.</summary>
    public double Done;
}

public sealed class ProductionJob
{
    public string Project = "";
    public int Engineers, Total, Made;
    /// <summary>Engineer-hours put into the unit in hand.</summary>
    public double Progress;
    public ProjectInfo Info => Rules.Project(Project);
}

/// <summary>
/// A base: a six-by-six grid of facilities (each must join the others, and through them the access lift), its stores,
/// its scientists and engineers, live aliens in containment, staff on the way, and the work in its laboratories and
/// workshops. The rules of building and pulling down live here.
/// </summary>
public sealed class Base
{
    public int Id;
    public string Name = "";
    public GeoPoint At;
    public List<Facility> Facilities = [];
    public Dictionary<ItemKind, int> Stores = [];
    public int Scientists, Engineers;
    public List<Captive> Captives = [];
    public List<Arrival> Arriving = [];
    public List<ResearchJob> Research = [];
    public List<ProductionJob> Production = [];

    public Facility? At2(int x, int y) => Facilities.FirstOrDefault(f => f.Covers(x, y));

    public int Count(ItemKind k) => Stores.TryGetValue(k, out int n) ? n : 0;

    public void Add(ItemKind k, int n = 1)
    {
        if (k == ItemKind.None || n == 0) return;
        Stores[k] = Count(k) + n;
        if (Stores[k] <= 0) Stores.Remove(k);
    }

    public bool Take(ItemKind k, int n = 1)
    {
        if (Count(k) < n) return false;
        Add(k, -n);
        return true;
    }

    private int Built(FacilityType t) => Facilities.Count(f => f.Type == t && f.Built);
    public bool Has(FacilityType t) => Built(t) > 0;

    public int QuartersSpace => Built(FacilityType.Quarters) * Rules.Of(FacilityType.Quarters).Capacity;
    public int LabSpace => Built(FacilityType.Lab) * Rules.Of(FacilityType.Lab).Capacity;
    public int WorkshopSpace => Built(FacilityType.Workshop) * Rules.Of(FacilityType.Workshop).Capacity;
    public int StoreSpace => Built(FacilityType.Stores) * Rules.Of(FacilityType.Stores).Capacity;
    public int ContainmentSpace => Built(FacilityType.Containment) * Rules.Of(FacilityType.Containment).Capacity;
    public int HangarSpace => Built(FacilityType.Hangar);
    public int PsiSpace => Built(FacilityType.PsiLab) * Rules.Of(FacilityType.PsiLab).Capacity;

    /// <summary>The farthest the base's radars see (0 with none working).</summary>
    public double RadarRange => Facilities.Where(f => f.Built && f.Type is FacilityType.SmallRadar or FacilityType.LargeRadar or FacilityType.SignalDecoder).Select(f => (double)Rules.Of(f.Type).Capacity).DefaultIfEmpty(0).Max();

    /// <summary>The base's defences' strength against an attacking UFO.</summary>
    public int Defence => Facilities.Where(f => f.Built && f.Type is FacilityType.MissileDefence or FacilityType.LaserDefence or FacilityType.StormDefence).Sum(f => Rules.Of(f.Type).Capacity);

    public double StoresUsed => Stores.Sum(kv => Rules.Of(kv.Key).Space * kv.Value);

    public int Upkeep => Facilities.Sum(f => Rules.Of(f.Type).Upkeep) * 1000;

    // ───────────────────────────── building ─────────────────────────────

    /// <summary>Why a facility can't go at (x, y), or null if it can: inside the grid, on free ground, and joined to the
    /// rest (a new base's first facility must be the access lift).</summary>
    public string? CanBuild(FacilityType t, int x, int y)
    {
        int s = Rules.Of(t).Size;
        if (x < 0 || y < 0 || x + s > Rules.BaseSize || y + s > Rules.BaseSize) return "OUTSIDE THE BASE";
        for (int i = x; i < x + s; i++)
        for (int j = y; j < y + s; j++)
            if (At2(i, j) is not null) return "THAT GROUND IS TAKEN";
        if (Facilities.Count == 0) return t == FacilityType.Lift ? null : "THE ACCESS LIFT COMES FIRST";
        if (t == FacilityType.Lift) return "A BASE HAS ONE ACCESS LIFT";
        for (int i = x; i < x + s; i++)
        for (int j = y; j < y + s; j++)
            foreach (var (dx, dy) in Steps)
            {
                int nx = i + dx, ny = j + dy;
                bool inside = nx >= x && ny >= y && nx < x + s && ny < y + s;
                if (!inside && At2(nx, ny) is not null) return null;
            }
        return "IT MUST JOIN THE BASE";
    }

    private static readonly (int, int)[] Steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>Puts a facility down to build (no checks, no cost).</summary>
    public Facility Put(FacilityType t, int x, int y, bool built = true)
    {
        var f = new Facility { Type = t, X = x, Y = y, DaysLeft = built ? 0 : Rules.Of(t).Days };
        Facilities.Add(f);
        return f;
    }

    /// <summary>Whether every facility can still reach the lift with <paramref name="without"/> gone.</summary>
    public bool Connected(Facility? without = null)
    {
        var lift = Facilities.FirstOrDefault(f => f.Type == FacilityType.Lift);
        if (lift is null || lift == without) return Facilities.All(f => f == without);
        var seen = new HashSet<Facility> { lift };
        var open = new Queue<Facility>();
        open.Enqueue(lift);
        while (open.Count > 0)
        {
            var f = open.Dequeue();
            for (int i = f.X; i < f.X + f.Size; i++)
            for (int j = f.Y; j < f.Y + f.Size; j++)
                foreach (var (dx, dy) in Steps)
                    if (At2(i + dx, j + dy) is { } n && n != without && seen.Add(n)) open.Enqueue(n);
        }
        return Facilities.All(f => f == without || seen.Contains(f));
    }

    /// <summary>Why a facility can't be pulled down, or null: not the lift, nothing cut off, and nothing left with no room.</summary>
    public string? CanDismantle(Facility f, int crafts, int people)
    {
        if (f.Type == FacilityType.Lift) return "THE ACCESS LIFT STAYS";
        if (!Connected(f)) return "IT WOULD CUT THE BASE IN TWO";
        if (!f.Built) return null;
        int cap = Rules.Of(f.Type).Capacity;
        return f.Type switch
        {
            FacilityType.Quarters when QuartersSpace - cap < people => "THE PERSONNEL NEED THE ROOM",
            FacilityType.Lab when LabSpace - cap < Research.Sum(r => r.Scientists) => "SCIENTISTS ARE WORKING THERE",
            FacilityType.Workshop when WorkshopSpace - cap < Production.Sum(p => p.Engineers) => "ENGINEERS ARE WORKING THERE",
            FacilityType.Stores when StoreSpace - cap < StoresUsed => "THE STORES ARE TOO FULL",
            FacilityType.Containment when ContainmentSpace - cap < Captives.Count => "ALIENS ARE HELD THERE",
            FacilityType.Hangar when HangarSpace - 1 < crafts => "A CRAFT IS HOUSED THERE",
            _ => null,
        };
    }

    public int FreeScientists => Scientists - Research.Sum(r => r.Scientists);
    public int FreeEngineers => Engineers - Production.Sum(p => p.Engineers);
}
