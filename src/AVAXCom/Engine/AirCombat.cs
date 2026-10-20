namespace AVAXCom.Engine;

/// <summary>How the interceptor fights: out of everyone's reach, at its longest weapon's range, at its shortest's, close
/// in (better aim, but in the UFO's reach), or breaking off.</summary>
public enum AirMode { Standoff, Cautious, Standard, Aggressive, Disengage }

public enum AirResult { None, Downed, Destroyed, Escaped, CraftLost, Disengaged }

/// <summary>A shot on its way across the combat window: from the craft (or the UFO), whether it will hit, and how hard.</summary>
public sealed class AirShot
{
    public bool FromCraft, Hit;
    public int Damage;
    public ItemKind Weapon;
    public double Progress;
}

/// <summary>
/// The interception window, played in real time: the craft closes to the distance its mode asks for (in kilometres),
/// each weapon fires when the UFO is in its range and it has reloaded, and the UFO fires back when the craft is in its
/// reach. A UFO hurt past half its strength comes down (a crash site, if it falls on land); past all of it, it's
/// blown apart. A UFO that isn't brought down soon breaks away and escapes.
/// </summary>
public sealed class AirCombat
{
    public const double Closing = 4, StartDistance = 75, BreakAway = 95, EscapeSeconds = 50, ShotSeconds = 0.6, AggressiveRange = 3;

    public readonly Craft Craft;
    public readonly Ufo Ufo;
    public double Distance = StartDistance;
    public AirMode Mode = AirMode.Standard;
    public double Time;
    public AirResult Result;
    public readonly double[] Reload;
    public double UfoReload = 1.5;
    public readonly List<AirShot> Shots = [];
    public readonly List<string> Log = [];
    /// <summary>Shots that have struck home, either way (for the sound of each).</summary>
    public int Impacts;
    private Dice _dice;
    /// <summary>Seconds the result has been showing (the window closes a moment after).</summary>
    public double Over;

    public AirCombat(Craft craft, Ufo ufo, int seed)
    {
        Craft = craft;
        Ufo = ufo;
        Reload = new double[craft.Guns.Count];
        _dice = new Dice(seed);
        Log.Add($"{craft.Name} ENGAGES {ufo.Name}");
    }

    public bool Done => Result != AirResult.None;

    private double LongestRange => Craft.Guns.Where(g => g.Info.Rounds == 0 || g.Rounds > 0).Select(g => (double)g.Info.Range).DefaultIfEmpty(0).Max();
    private double ShortestRange => Craft.Guns.Where(g => g.Info.Rounds == 0 || g.Rounds > 0).Select(g => (double)g.Info.Range).DefaultIfEmpty(0).Min();

    /// <summary>The distance the craft is trying to hold.</summary>
    public double Wanted => Mode switch
    {
        AirMode.Standoff => Math.Max(Rules.Of(Ufo.Type).Range + 8, 70),
        AirMode.Cautious => Math.Max(AggressiveRange, LongestRange - 1),
        AirMode.Standard => Math.Max(AggressiveRange, ShortestRange - 1),
        AirMode.Aggressive => AggressiveRange,
        _ => BreakAway + 10,
    };

    public void Step(double dt)
    {
        if (Done) { Over += dt; Shots.Clear(); return; }
        Time += dt;
        var ui = Rules.Of(Ufo.Type);
        double want = Wanted;
        if (Math.Abs(Distance - want) > 0.05) Distance += Math.Sign(want - Distance) * Math.Min(Math.Abs(want - Distance), Closing * dt);
        if (Mode == AirMode.Disengage && Distance >= BreakAway) { Finish(AirResult.Disengaged, $"{Craft.Name} BREAKS OFF"); return; }
        // The craft's weapons.
        for (int i = 0; i < Craft.Guns.Count; i++)
        {
            var g = Craft.Guns[i];
            Reload[i] -= dt;
            if (Reload[i] > 0 || Distance > g.Info.Range + 0.5 || (g.Info.Rounds > 0 && g.Rounds <= 0)) continue;
            Reload[i] = g.Info.Reload;
            if (g.Info.Rounds > 0) g.Rounds--;
            double aim = g.Info.Accuracy + (Mode == AirMode.Aggressive ? 0.15 : 0);
            bool hit = _dice.Chance(aim);
            Shots.Add(new AirShot { FromCraft = true, Hit = hit, Weapon = g.Kind, Damage = (int)(g.Info.Damage * _dice.Range(0.5, 1.0)) });
        }
        // The UFO's.
        UfoReload -= dt;
        if (ui.Range > 0 && UfoReload <= 0 && Distance <= ui.Range)
        {
            UfoReload = ui.FireEvery;
            Shots.Add(new AirShot { FromCraft = false, Hit = _dice.Chance(Mode == AirMode.Aggressive ? 0.7 : 0.55), Damage = _dice.Range(ui.Power / 3, ui.Power) });
        }
        // Shots arriving.
        foreach (var s in Shots.ToList())
        {
            s.Progress += dt / ShotSeconds;
            if (s.Progress < 1) continue;
            Shots.Remove(s);
            if (!s.Hit) continue;
            Impacts++;
            if (s.FromCraft)
            {
                Ufo.Damage += s.Damage;
                if (Ufo.Damage >= ui.Hp) { Finish(AirResult.Destroyed, $"{Ufo.Name} DESTROYED"); return; }
                if (Ufo.Damage >= ui.Hp / 2.0) { Finish(AirResult.Downed, $"{Ufo.Name} CRASH-LANDS"); return; }
            }
            else
            {
                Craft.Damage += s.Damage;
                if (Craft.Damage >= Craft.Info.Hp) { Finish(AirResult.CraftLost, $"{Craft.Name} DESTROYED"); return; }
                Log.Add($"{Craft.Name} HIT ({(int)(100 * Craft.Damage / Craft.Info.Hp)}% DAMAGE)");
            }
        }
        if (Time > EscapeSeconds && Mode != AirMode.Disengage) Finish(AirResult.Escaped, $"{Ufo.Name} OUTRUNS {Craft.Name}");
    }

    private void Finish(AirResult r, string text)
    {
        Result = r;
        Log.Add(text);
    }

    /// <summary>Runs the fight out (for tests and the pilot), at most a couple of minutes of it.</summary>
    public AirResult Resolve()
    {
        for (int i = 0; i < 60 * 150 && !Done; i++) Step(1 / 60.0);
        if (!Done) Finish(AirResult.Escaped, $"{Ufo.Name} ESCAPES");
        return Result;
    }
}
