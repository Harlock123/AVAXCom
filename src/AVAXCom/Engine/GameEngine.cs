namespace AVAXCom.Engine;

public enum GameState
{
    /// <summary>The title, with its tune.</summary>
    Title,
    /// <summary>The demonstration: the computer plays a little of the geoscape, then a ground mission.</summary>
    Attract,
    Geoscape,
    /// <summary>The briefing before a ground mission.</summary>
    Briefing,
    Battle,
    /// <summary>The score after a ground mission.</summary>
    Debrief,
    /// <summary>The war is over: a moment, then the initials or the table.</summary>
    GameOver,
    EnterInitials,
    HighScores,
}

/// <summary>What the geoscape shows: the globe, or one of its screens over it.</summary>
public enum GeoScreen { Globe, Event, AirCombat, Intercept, Base, Research, Manufacture, Purchase, Sell, Soldiers, Funding, Memorial, Files }

/// <summary>What a click on the battlefield does next: walk (nothing armed), or fire, throw, strike, use a kit or the mind.</summary>
public enum Arm { None, Snap, Aimed, Auto, Throw, Strike, Heal, Stimulant, Painkiller, PsiPanic, PsiControl }

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Click, Refused, Cash, Build, ResearchDone, Alarm,
    UfoDetected, Launch, AirCannon, AirMissile, AirLaser, AirStorm, AirHit, UfoCrash, TransportLand,
    Door, Step, Rifle, Cannon, Laser, Storm, Throw, Explosion, BigExplosion, AlienDeath, SoldierDeath, SoldierHurt,
    ReactionFire, Psionic, Melee, HiddenMovement, TurnStart,
    SayUfoDetected, SayHostile, SayMissionComplete,
    Ambient, GeoMusic, MusicStop, TitleTune, Victory, GameOver,
}

/// <summary>The controls held this step: scroll (up is -1, as on the screen and in the engine) and where the mouse points.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, (double X, double Y)? Point = null);

/// <summary>The isometric battlefield's measures: tiles 64 by 32 on screen, storeys 40 high, and the bar below.</summary>
public static class Iso
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double TileW = 64, TileH = 32, LevelH = 40, HudH = 132;
    public const double MapH = Height - HudH;

    /// <summary>Where a tile's top corner is drawn, relative to the map's origin (tile 0,0 at storey 0).</summary>
    public static (double X, double Y) ToScreen(double x, double y, double z) => ((x - y) * TileW / 2, (x + y) * TileH / 2 - z * LevelH);

    /// <summary>The tile under a point relative to the map's origin, on a storey.</summary>
    public static (int X, int Y) ToTile(double sx, double sy, int z)
    {
        sy += z * LevelH;
        double fx = (sx / (TileW / 2) + sy / (TileH / 2)) / 2, fy = (sy / (TileH / 2) - sx / (TileW / 2)) / 2;
        return ((int)Math.Floor(fx), (int)Math.Floor(fy));
    }
}

/// <summary>
/// The whole game with no UI: the title, the geoscape (its clock and speeds, the globe's view, its screens, the events
/// that stop time, the interception window), the briefing, the battle (the camera and storeys, picking soldiers out,
/// walking, firing, throwing, the inventory, ending the turn), the debrief, saving and loading, the end of the war and
/// the high scores. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed 60 Hz steps.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 20, AttractGeoSeconds = 22, AttractSeconds = 110, HighScoreSeconds = 10, OverSeconds = 6;
    public const double OutcomeSeconds = 2.5, ScrollSpeed = 720, EdgeScroll = 6;
    public static readonly string[] SpeedNames = ["5 SECS", "1 MIN", "5 MINS", "30 MINS", "1 HOUR", "1 DAY"];
    /// <summary>Game seconds a real second at each speed.</summary>
    public static readonly double[] Speeds = [60, 600, 3000, 18000, 36000, 345600];

    private double _accumulator, _idle, _outcomeAt;
    private bool _startPrev, _tunePending = true;
    private string _music = "";
    private readonly Dictionary<Sfx, double> _lastSound = [];
    private int _seed;

    public GameEngine(HighScoreTable? table = null, string? saveFolder = null, int seed = 0)
    {
        HighScores = table ?? HighScoreTable.Load();
        SaveFolder = saveFolder ?? HighScores.Folder;
        _seed = seed != 0 ? seed : (int)(DateTime.Now.Ticks % 1_000_000) + 1;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public double StateTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public string SaveFolder { get; }
    public Campaign? Campaign { get; private set; }
    public Battle? Battle { get; private set; }
    public Mission? Mission { get; private set; }
    public Debrief? Debrief { get; private set; }
    public AirCombat? Air { get; private set; }
    /// <summary>The player's side played by the computer (the demonstration, or a test).</summary>
    public bool Autopilot { get; set; }
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }
    public int Score => Campaign is { } c ? c.Score + c.MonthOurs - c.MonthTheirs : 0;

    // ───────────────────────────── the geoscape's view ─────────────────────────────

    public GeoScreen Screen { get; private set; } = GeoScreen.Globe;
    public GeoEvent? Event { get; private set; }
    public int SpeedIndex { get; set; }
    public bool TimeRunning => State is GameState.Geoscape or GameState.Attract && Screen == GeoScreen.Globe && !PlacingBase && Campaign is { Over: false };
    /// <summary>The globe's centre, and its zoom (the globe's radius in pixels).</summary>
    public GeoPoint View { get; set; } = new(30, 0);
    public double Zoom { get; set; } = 300;
    public static readonly double[] Zooms = [220, 300, 420, 600, 850];
    public int ZoomIndex { get; private set; } = 1;
    private GeoPoint? _spinTo;
    /// <summary>Choosing where to build the first base (or a new one).</summary>
    public bool PlacingBase { get; private set; }
    public bool PlacingFirst { get; private set; }
    /// <summary>The base, facility, craft and soldier the screens are showing; the UFO or site being sent against.</summary>
    public Base? SelectedBase { get; set; }
    public FacilityType? Building { get; set; }
    public Craft? SelectedCraft { get; set; }
    public Soldier? SelectedSoldier { get; set; }
    public Ufo? TargetUfo { get; private set; }
    public Site? TargetSite { get; private set; }
    public int ListScroll { get; set; }
    public List<string> Ticker { get; } = [];
    public bool Saving { get; private set; }

    // ───────────────────────────── the battle's view ─────────────────────────────

    /// <summary>The battle camera: the screen point (relative to the map's origin) at the middle of the view, and the
    /// storey shown (and those under it).</summary>
    public double CamX { get; set; }
    public double CamY { get; set; }
    public int ViewLevel { get; set; }
    public BUnit? Selected { get; private set; }
    public Arm Armed { get; private set; }
    public Item? ArmedItem { get; private set; }
    public bool Inventory { get; private set; }
    public bool BattleMenu { get; private set; }
    /// <summary>An item picked up in the inventory, and where from.</summary>
    public (Item Item, Slot From)? Holding { get; private set; }
    public int FuseChoice { get; set; }

    private void Play(Sfx s)
    {
        if (Silent) return;
        double gap = s switch { Sfx.Step => 0.08, Sfx.SayHostile or Sfx.SayUfoDetected => 2, _ => 0.04 };
        if (s < Sfx.Ambient && _lastSound.TryGetValue(s, out var at) && StateTime - at < gap && StateTime >= at) return;
        _lastSound[s] = StateTime;
        Sound?.Invoke(s);
    }

    public void Say(string text, double seconds = 3.5)
    {
        Message = text;
        MessageTime = seconds;
    }

    private void Music(Sfx track)
    {
        string key = track.ToString();
        if (_music == key) return;
        _music = key;
        Play(track);
    }

    // ───────────────────────────── the loop ─────────────────────────────

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        bool first = true;
        while (_accumulator >= Iso.Step)
        {
            _accumulator -= Iso.Step;
            Tick(c, start, first ? Math.Min(dt, 0.25) : 0);
            first = false;
        }
    }

    public void Tick(Controls c = default, bool start = false, double scroll = Iso.Step)
    {
        double dt = Iso.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); _music = "TitleTune"; }
                _idle += dt;
                if (startPressed) NewCampaign();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                AttractTick(dt);
                if (startPressed) ReturnToTitle();
                break;
            case GameState.Geoscape:
                GeoTick(dt);
                break;
            case GameState.Briefing:
                if (startPressed) BeginBattle();
                break;
            case GameState.Battle:
                if (scroll > 0) Scroll(c, scroll);
                BattleTick(dt);
                break;
            case GameState.Debrief:
                if (startPressed && StateTime > 0.5) FinishDebrief();
                break;
            case GameState.GameOver:
                if (StateTime > OverSeconds)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        SetState(GameState.EnterInitials);
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, (Campaign?.MonthIndex ?? 0) + 1);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed || StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void SetState(GameState s)
    {
        State = s;
        StateTime = 0;
        _lastSound.Clear();
    }

    // ───────────────────────────── the flow of the game ─────────────────────────────

    /// <summary>A new war: the globe, and the first base to place.</summary>
    public void NewCampaign()
    {
        Campaign = new Campaign(_seed++);
        Autopilot = false;
        SetState(GameState.Geoscape);
        Screen = GeoScreen.Globe;
        PlacingBase = PlacingFirst = true;
        SpeedIndex = 0;
        Ticker.Clear();
        Music(Sfx.GeoMusic);
        Say("CHOOSE WHERE TO BUILD YOUR FIRST BASE", 8);
    }

    /// <summary>Carries on the most recent save.</summary>
    public bool Continue()
    {
        int slot = SaveFile.Latest(SaveFolder);
        return slot >= 0 && LoadSlot(slot);
    }

    public bool HasSave => SaveFile.Latest(SaveFolder) >= 0;

    public bool LoadSlot(int slot)
    {
        var c = SaveFile.Load(SaveFolder, slot);
        if (c is null || c.Over) { Play(Sfx.Refused); return false; }
        Campaign = c;
        Autopilot = false;
        Battle = null;
        SetState(GameState.Geoscape);
        Screen = GeoScreen.Globe;
        PlacingBase = PlacingFirst = false;
        SpeedIndex = 0;
        if (c.Bases.FirstOrDefault() is { } b) { View = b.At; SelectedBase = b; }
        Music(Sfx.GeoMusic);
        Say("CAMPAIGN LOADED");
        return true;
    }

    public bool SaveSlot(int slot)
    {
        if (Campaign is not { } c || State != GameState.Geoscape || c.Bases.Count == 0) return false;
        bool ok = SaveFile.Save(c, SaveFolder, slot);
        Say(ok ? $"SAVED IN SLOT {slot}" : "THE GAME COULD NOT BE SAVED");
        Play(ok ? Sfx.Click : Sfx.Refused);
        return ok;
    }

    public void ReturnToTitle()
    {
        SetState(GameState.Title);
        _idle = 0;
        _tunePending = true;
        _music = "";
        Play(Sfx.MusicStop);
        Message = null;
        Campaign = null;
        Battle = null;
        Air = null;
        Mission = null;
        Autopilot = false;
        Screen = GeoScreen.Globe;
    }

    private void ShowHighScores(int place)
    {
        SetState(GameState.HighScores);
        NewEntryIndex = place;
        _music = "";
    }

    private void EndWar()
    {
        SetState(GameState.GameOver);
        Play(Sfx.MusicStop);
        _music = "";
        Play(Campaign?.Won == true ? Sfx.Victory : Sfx.GameOver);
        for (int s = 0; s <= SaveFile.Slots; s++)
            if (SaveFile.Load(SaveFolder, s) is { } saved && saved.Seed == Campaign?.Seed) SaveFile.Delete(SaveFolder, s);
    }

    // ───────────────────────────── the geoscape ─────────────────────────────

    private void GeoTick(double dt)
    {
        var c = Campaign!;
        SpinGlobe(dt);
        if (Screen == GeoScreen.AirCombat && Air is { } air)
        {
            int before = air.Shots.Count, hits = air.Impacts;
            air.Step(dt);
            if (air.Impacts > hits) Play(Sfx.AirHit);
            if (air.Shots.Count > before) Play(air.Shots[^1].FromCraft ? air.Shots[^1].Weapon switch { ItemKind.LancerLauncher => Sfx.AirMissile, ItemKind.LaserCannon => Sfx.AirLaser, ItemKind.StormBeam => Sfx.AirStorm, _ => Sfx.AirCannon } : Sfx.AirStorm);
            if (air.Done && air.Over == 0) Play(air.Result is AirResult.Downed or AirResult.Destroyed ? Sfx.UfoCrash : air.Result == AirResult.CraftLost ? Sfx.BigExplosion : Sfx.Click);
            if (air.Done && air.Over > 2.2) CloseAir();
            return;
        }
        if (c.Over && Screen == GeoScreen.Globe && Event is null) { EndWar(); return; }
        if (!TimeRunning) return;
        c.Advance(Speeds[SpeedIndex] * dt);
        TakeEvents();
        if (c.Over && Screen == GeoScreen.Globe && Event is null) EndWar();
    }

    /// <summary>The events the clock turned up: the quiet ones go to the ticker, the first loud one stops time.</summary>
    private void TakeEvents()
    {
        var c = Campaign!;
        while (c.Events.Count > 0 && Event is null)
        {
            var e = c.Events[0];
            c.Events.RemoveAt(0);
            if (!e.Interrupt)
            {
                Ticker.Add(e.Text);
                if (Ticker.Count > 6) Ticker.RemoveAt(0);
                if (e.Kind == EventKind.FacilityDone || e.Kind == EventKind.Arrived) Play(Sfx.Build);
                continue;
            }
            ShowEvent(e);
        }
    }

    private void ShowEvent(GeoEvent e)
    {
        var c = Campaign!;
        Event = e;
        SpeedIndex = 0;
        switch (e.Kind)
        {
            case EventKind.UfoDetected:
                Play(Sfx.UfoDetected);
                Play(Sfx.SayUfoDetected);
                if (c.UfoById(e.Ufo) is { } u) SpinTo(u.At);
                break;
            case EventKind.Intercept:
                if (c.CraftById(e.Craft) is { } k && c.UfoById(e.Ufo) is { } ufo)
                {
                    Event = null;
                    Air = c.Engage(k, ufo);
                    Screen = GeoScreen.AirCombat;
                    Play(Sfx.Launch);
                    return;
                }
                Event = null;
                return;
            case EventKind.TerrorSite or EventKind.BaseAttack:
                Play(Sfx.Alarm);
                break;
            case EventKind.ResearchDone:
                Play(Sfx.ResearchDone);
                break;
            case EventKind.ProductionDone:
                Play(Sfx.Build);
                break;
            case EventKind.UfoDowned:
                Play(Sfx.UfoCrash);
                break;
            default:
                Play(Sfx.Click);
                break;
        }
        if (c.SiteById(e.Site) is { } s) SpinTo(s.At);
        Screen = GeoScreen.Event;
        if (Autopilot) Pilot.Manage(c);
    }

    /// <summary>Closes the event window, doing what its button asked: intercept, centre, begin the mission, defend, or see the screen it's about.</summary>
    public void Answer(int choice = 0)
    {
        if (Event is not { } e || Campaign is not { } c) return;
        Event = null;
        Screen = GeoScreen.Globe;
        Play(Sfx.Click);
        switch (e.Kind)
        {
            case EventKind.UfoDetected when choice == 1:
                if (c.UfoById(e.Ufo) is { } u) OpenIntercept(u, null);
                break;
            case EventKind.UfoDetected or EventKind.TerrorSite or EventKind.UfoDowned or EventKind.AlienBaseFound when choice == 2:
                if (c.UfoById(e.Ufo) is { } cu) SpinTo(cu.At);
                if (c.SiteById(e.Site) is { } cs) SpinTo(cs.At);
                break;
            case EventKind.TerrorSite or EventKind.UfoDowned or EventKind.AlienBaseFound when choice == 1:
                if (c.SiteById(e.Site) is { } site) OpenIntercept(null, site);
                break;
            case EventKind.CraftArrived:
                if (c.CraftById(e.Craft) is { } craft)
                {
                    if (choice == 1) c.SendHome(craft);
                    else Brief(c.MissionFor(craft));
                }
                break;
            case EventKind.BaseAttack:
                Brief(c.DefenceMission());
                break;
            case EventKind.ResearchDone when choice == 1:
                SelectedBase = c.BaseById(e.Base) ?? SelectedBase;
                Open(GeoScreen.Research);
                break;
            case EventKind.ProductionDone or EventKind.FacilityDone when choice == 1:
                SelectedBase = c.BaseById(e.Base) ?? SelectedBase;
                Open(GeoScreen.Base);
                break;
            case EventKind.MonthlyReport:
                if (State == GameState.Geoscape && !Autopilot) SaveFile.Save(c, SaveFolder, 0);
                break;
        }
        TakeEvents();
    }

    /// <summary>The choices an event offers (button labels).</summary>
    public static string[] Choices(GeoEvent e) => e.Kind switch
    {
        EventKind.UfoDetected => ["OK", "INTERCEPT", "CENTRE ON UFO"],
        EventKind.TerrorSite or EventKind.UfoDowned or EventKind.AlienBaseFound => e.Site >= 0 ? ["OK", "SEND TROOPS", "CENTRE"] : ["OK"],
        EventKind.CraftArrived => ["BEGIN MISSION", "RETURN TO BASE"],
        EventKind.BaseAttack => ["DEFEND THE BASE"],
        EventKind.ResearchDone => ["OK", "RESEARCH"],
        EventKind.ProductionDone or EventKind.FacilityDone => ["OK", "GO TO BASE"],
        _ => ["OK"],
    };

    public void Open(GeoScreen s)
    {
        if (State != GameState.Geoscape || Campaign is not { } c || c.Bases.Count == 0 && s != GeoScreen.Files) return;
        Screen = s;
        ListScroll = 0;
        Building = null;
        if (SelectedBase is null || !c.Bases.Contains(SelectedBase)) SelectedBase = c.Bases.FirstOrDefault();
        if (s == GeoScreen.Soldiers) SelectedSoldier = null;
        Play(Sfx.Click);
    }

    public void CloseScreen()
    {
        if (Screen is GeoScreen.Event or GeoScreen.AirCombat) return;
        if (Screen == GeoScreen.Base && Building is not null) { Building = null; return; }
        if (Screen == GeoScreen.Soldiers && SelectedSoldier is not null) { SelectedSoldier = null; return; }
        Screen = GeoScreen.Globe;
        Saving = false;
        Play(Sfx.Click);
    }

    public void OpenFiles(bool saving)
    {
        Saving = saving && Campaign is { Bases.Count: > 0 };
        Screen = GeoScreen.Files;
        Play(Sfx.Click);
    }

    public void SetSpeed(int i)
    {
        SpeedIndex = Math.Clamp(i, 0, Speeds.Length - 1);
        Play(Sfx.Click);
    }

    // The globe's view.

    public void SpinTo(GeoPoint p) => _spinTo = p;

    public void Rotate(double dLon, double dLat)
    {
        _spinTo = null;
        View = new GeoPoint(Math.Clamp(View.Lat + dLat, -85, 85), Geo.Wrap(View.Lon + dLon));
    }

    public void ZoomBy(int d)
    {
        ZoomIndex = Math.Clamp(ZoomIndex + d, 0, Zooms.Length - 1);
        Zoom = Zooms[ZoomIndex];
    }

    private void SpinGlobe(double dt)
    {
        if (_spinTo is not { } to) return;
        double dLon = Geo.Wrap(to.Lon - View.Lon), dLat = to.Lat - View.Lat;
        double k = Math.Min(1, dt * 5);
        if (Math.Abs(dLon) < 0.05 && Math.Abs(dLat) < 0.05) { View = to; _spinTo = null; return; }
        View = new GeoPoint(Math.Clamp(View.Lat + dLat * k, -85, 85), Geo.Wrap(View.Lon + dLon * k));
    }

    /// <summary>A click on the globe at a place: put a base there (when choosing), or look at what's there.</summary>
    public void GlobeClick(GeoPoint p)
    {
        if (Campaign is not { } c) return;
        if (PlacingBase)
        {
            if (!LandMask.IsLand(p)) { Say("BASES MUST BE BUILT ON LAND"); Play(Sfx.Refused); return; }
            if (PlacingFirst)
            {
                SelectedBase = c.FoundFirstBase(p);
                PlacingBase = PlacingFirst = false;
                Say($"{SelectedBase.Name} BASE IS OPERATIONAL", 5);
                Play(Sfx.Build);
                SaveFile.Save(c, SaveFolder, 0);
                return;
            }
            if (c.FoundBase(p) is { } why) { Say(why); Play(Sfx.Refused); return; }
            PlacingBase = false;
            SelectedBase = c.Bases[^1];
            Play(Sfx.Build);
            Open(GeoScreen.Base);
            return;
        }
        // Something on the globe near the click: a UFO, a site, a base.
        var ufo = c.Ufos.Where(u => u.Detected).OrderBy(u => Geo.Arc(u.At, p)).FirstOrDefault(u => Geo.Arc(u.At, p) < 4 * Geo.Deg * 300 / Zoom);
        if (ufo is not null) { OpenIntercept(ufo, null); return; }
        var site = c.Sites.Where(s => s.Known).OrderBy(s => Geo.Arc(s.At, p)).FirstOrDefault(s => Geo.Arc(s.At, p) < 4 * Geo.Deg * 300 / Zoom);
        if (site is not null) { OpenIntercept(null, site); return; }
        var b = c.Bases.OrderBy(b => Geo.Arc(b.At, p)).FirstOrDefault(b => Geo.Arc(b.At, p) < 4 * Geo.Deg * 300 / Zoom);
        if (b is not null) { SelectedBase = b; Open(GeoScreen.Base); return; }
        SpinTo(p);
    }

    public void StartNewBase()
    {
        if (Campaign is not { } c) return;
        if (c.Money < Rules.NewBaseCost) { Say("A NEW BASE COSTS $900,000"); Play(Sfx.Refused); return; }
        Screen = GeoScreen.Globe;
        PlacingBase = true;
        PlacingFirst = false;
        Say("CHOOSE WHERE TO BUILD THE NEW BASE", 6);
    }

    public void CancelPlacing()
    {
        if (!PlacingFirst) PlacingBase = false;
    }

    /// <summary>The interception list for a UFO or a site (the craft that can go after it).</summary>
    public void OpenIntercept(Ufo? u, Site? s)
    {
        TargetUfo = u;
        TargetSite = s;
        Screen = GeoScreen.Intercept;
        Play(Sfx.Click);
    }

    /// <summary>The craft that can be sent at the target: all craft, ready or flying.</summary>
    public List<Craft> InterceptChoices() => Campaign?.Crafts.Where(k => k.State != CraftState.Lost).OrderBy(k => k.BaseId).ThenBy(k => k.Id).ToList() ?? [];

    public string? LaunchAt(Craft k)
    {
        if (Campaign is not { } c) return "NO CAMPAIGN";
        if (TargetUfo is null && TargetSite is null)
        {
            // The INTERCEPT button with no target: pick out the craft (a patrol from the globe is not offered).
            return "CHOOSE A UFO OR A SITE ON THE GLOBE";
        }
        if (TargetSite is not null || TargetUfo is { State: UfoState.Landed })
        {
            // Fill a transport's empty seats from the fit soldiers at its base.
            if (k.Info.Seats > 0 && !c.CrewOf(k).Any(s => s.Fit) && c.BaseById(k.BaseId) is { } kb)
                foreach (var s in c.SoldiersAt(kb).Where(s => s.Fit && s.CraftId < 0).Take(k.Info.Seats)) s.CraftId = k.Id;
        }
        var why = c.Launch(k, TargetUfo, TargetSite);
        if (why is not null) { Say(why); Play(Sfx.Refused); return why; }
        Play(Sfx.Launch);
        Screen = GeoScreen.Globe;
        Say($"{k.Name} LAUNCHED");
        return null;
    }

    // The interception window.

    public void SetAirMode(AirMode m)
    {
        if (Air is { Done: false } a) { a.Mode = m; Play(Sfx.Click); }
    }

    private void CloseAir()
    {
        if (Air is not { } a || Campaign is not { } c) return;
        c.Resolve(a);
        Air = null;
        Screen = GeoScreen.Globe;
        TakeEvents();
    }

    // ───────────────────────────── the briefing and the battle ─────────────────────────────

    private void Brief(Mission? m)
    {
        if (m is null) { Say("THE CRAFT HAS NO SQUAD"); Play(Sfx.Refused); return; }
        Mission = m;
        SetState(GameState.Briefing);
        Screen = GeoScreen.Globe;
        Play(Sfx.TransportLand);
        if (Autopilot) BeginBattle();
    }

    public void BeginBattle()
    {
        if (Mission is not { } m) return;
        var stores = Campaign?.BaseById(m.BaseId);
        Battle = BattleGen.Make(m, stores);
        StartBattle(Battle);
    }

    /// <summary>Into a battle (made elsewhere: a mission's, a test's, the demonstration's).</summary>
    public void StartBattle(Battle b)
    {
        Battle = b;
        Mission = b.Mission;
        SetState(State == GameState.Attract ? GameState.Attract : GameState.Battle);
        Selected = b.Units.FirstOrDefault(u => u.Active && u.Side == Side.Player);
        Armed = Arm.None;
        Inventory = BattleMenu = false;
        Holding = null;
        ViewLevel = 0;
        _outcomeAt = 0;
        if (Selected is { } s) CenterOn(s.X, s.Y, s.Z);
        Music(Sfx.Ambient);
        Say(b.Night ? "NIGHT MISSION: USE FLARES" : b.Mission.Name, 4);
    }

    private void BattleTick(double dt)
    {
        var b = Battle!;
        var before = b.Active;
        Tac.Step(b, dt);
        if (Autopilot && b.Active == Side.Player && !b.Busy && b.Outcome is null)
        {
            Pilot.Act(b);
            if (b.Walker is { Team: Side.Player } w) Selected = w;
        }
        Hear(b);
        if (b.Active == Side.Player && before != Side.Player)
        {
            if (Selected is not { Active: true } || Selected.Team != Side.Player) Selected = b.Units.FirstOrDefault(u => u.Active && u.Team == Side.Player);
            if (Selected is { } s && !Autopilot) CenterOn(s.X, s.Y, s.Z);
            Say($"TURN {b.Turn}", 2);
        }
        if (Selected is { } sel && (!sel.Active || sel.Team != Side.Player) && b.Active == Side.Player) Selected = b.Units.FirstOrDefault(u => u.Active && u.Team == Side.Player);
        // The camera follows what can be seen moving on the aliens' turn, and everything in the demonstration.
        if (b.Active != Side.Player || Autopilot)
        {
            var (wx, wy, wz) = b.Active != Side.Player && b.Walker is { Seen: true } w ? (w.X, w.Y, w.Z) : b.Active != Side.Player ? (-1.0, -1.0, 0.0) : Pilot.Watch(b);
            if (b.Active == Side.Player || wx >= 0 && b.Walker is not null)
            {
                var (tx, ty) = Iso.ToScreen(wx + 0.5, wy + 0.5, wz);
                CamX += (tx - CamX) * 0.08;
                CamY += (ty - CamY) * 0.08;
            }
        }
        if (b.Outcome is not null)
        {
            if (_outcomeAt == 0)
            {
                _outcomeAt = StateTime;
                Armed = Arm.None;
                Inventory = false;
                if (b.Outcome == true) { Play(Sfx.SayMissionComplete); Say("MISSION SUCCESSFUL", 4); }
                else Say(b.Aborted ? "MISSION ABORTED" : "MISSION FAILED", 4);
            }
            if (StateTime - _outcomeAt > OutcomeSeconds) EndBattle();
        }
    }

    private void Hear(Battle b)
    {
        foreach (var s in b.Heard) Play(s);
        b.Heard.Clear();
        foreach (var n in b.News) Say(n, 2.5);
        b.News.Clear();
    }

    private void EndBattle()
    {
        var b = Battle!;
        Debrief = Debrief.Of(b);
        if (State == GameState.Attract) { Play(Sfx.MusicStop); _music = ""; ShowHighScores(-1); return; }
        SetState(GameState.Debrief);
        Music(Sfx.MusicStop);
        Play(Debrief.Won ? Sfx.Victory : Sfx.GameOver);
        if (Campaign is { } c && Mission is { } m) Debrief.Apply(c, m);
    }

    /// <summary>From the debrief back to the geoscape (or on to the end of the war).</summary>
    public void FinishDebrief()
    {
        if (State != GameState.Debrief) return;
        Play(Sfx.Click);
        Battle = null;
        if (Campaign is not { } c) { ReturnToTitle(); return; }
        if (c.Over) { EndWar(); return; }
        SetState(GameState.Geoscape);
        Screen = GeoScreen.Globe;
        Music(Sfx.GeoMusic);
        SaveFile.Save(c, SaveFolder, 0);
        TakeEvents();
    }

    // ───────────────────────────── the battle camera ─────────────────────────────

    public void CenterOn(double x, double y, int z)
    {
        var (sx, sy) = Iso.ToScreen(x + 0.5, y + 0.5, z);
        CamX = sx;
        CamY = sy;
        ViewLevel = Math.Clamp(z, 0, Battle.Levels - 1);
    }

    /// <summary>The arrows and the stick scroll the map; so does the mouse at the screen's edge. Up is -1.</summary>
    private void Scroll(Controls c, double dt)
    {
        int dx = c.Dx, dy = c.Dy;
        if (c.Point is var (px, py))
        {
            if (px <= EdgeScroll) dx = -1;
            else if (px >= Iso.Width - EdgeScroll) dx = 1;
            if (py <= EdgeScroll) dy = -1;
            else if (py >= Iso.Height - EdgeScroll) dy = 1;
        }
        if (dx == 0 && dy == 0 || Battle is not { } b) return;
        var (minX, _) = Iso.ToScreen(0, b.H, 0);
        var (maxX, _) = Iso.ToScreen(b.W, 0, 0);
        var (_, maxY) = Iso.ToScreen(b.W, b.H, 0);
        CamX = Math.Clamp(CamX + dx * ScrollSpeed * dt, minX, maxX);
        CamY = Math.Clamp(CamY + dy * ScrollSpeed * 0.75 * dt, -Battle.Levels * Iso.LevelH, maxY);
    }

    /// <summary>The map's origin on the screen (where tile 0,0's top corner is).</summary>
    public (double X, double Y) Origin => (Iso.Width / 2 - CamX, Iso.MapH / 2 - CamY);

    /// <summary>The tile under a point of the screen, on the storey in view.</summary>
    public Cell TileAt(double sx, double sy)
    {
        var (ox, oy) = Origin;
        var (x, y) = Iso.ToTile(sx - ox, sy - oy, ViewLevel);
        return new Cell(x, y, ViewLevel);
    }

    public void LevelUp() { if (ViewLevel < Battle.Levels - 1) { ViewLevel++; Play(Sfx.Click); } }
    public void LevelDown() { if (ViewLevel > 0) { ViewLevel--; Play(Sfx.Click); } }

    // ───────────────────────────── orders in battle ─────────────────────────────

    private bool PlayerTurn => State == GameState.Battle && Battle is { Active: Side.Player, Outcome: null } b && !b.Busy && !Autopilot;

    private void Refuse(string why)
    {
        Say(why, 2);
        Play(Sfx.Refused);
    }

    /// <summary>Picks a soldier out.</summary>
    public void Select(BUnit u)
    {
        if (u.Team != Side.Player || !u.Active) return;
        Selected = u;
        Armed = Arm.None;
        Play(Sfx.Click);
    }

    /// <summary>The next soldier with time units left (or, with <paramref name="back"/>, the one before).</summary>
    public void NextSoldier(bool back = false)
    {
        if (Battle is not { } b) return;
        var squad = b.Units.Where(u => u.Active && u.Team == Side.Player).ToList();
        if (squad.Count == 0) return;
        int i = Selected is null ? -1 : squad.IndexOf(Selected);
        for (int k = 1; k <= squad.Count; k++)
        {
            var u = squad[((i + (back ? -k : k)) % squad.Count + squad.Count) % squad.Count];
            if (u.Tu >= 4 || k == squad.Count)
            {
                Selected = u;
                Armed = Arm.None;
                CenterOn(u.X, u.Y, u.Z);
                Play(Sfx.Click);
                return;
            }
        }
    }

    public void CenterOnSelected()
    {
        if (Selected is { } u) CenterOn(u.X, u.Y, u.Z);
    }

    /// <summary>Arms a shot, a throw, a blow or a kit: the next click on the map is its target.</summary>
    public void ArmAction(Arm a, Item? item = null)
    {
        if (!PlayerTurn || Selected is not { } u) return;
        Armed = Armed == a && ArmedItem == item ? Arm.None : a;
        ArmedItem = item;
        Play(Sfx.Click);
    }

    public void Disarm() => Armed = Arm.None;

    /// <summary>A left click on a tile: pick out a soldier there, else carry out the armed action (or walk there).</summary>
    public void ClickTile(Cell c)
    {
        if (!PlayerTurn || Battle is not { } b) return;
        var there = b.UnitAt(c);
        if (Armed == Arm.None && there is { Team: Side.Player } mine && mine != Selected) { Select(mine); return; }
        if (Selected is not { } u || !u.Active) return;
        if (Armed == Arm.None && there is { Seen: true } foe && Tac.Hostile(u, foe)) { Refuse("CHOOSE AN ACTION FROM A HAND TO ATTACK"); return; }
        string? why = Armed switch
        {
            Arm.Snap => Tac.Shoot(b, u, c, FireMode.Snap),
            Arm.Aimed => Tac.Shoot(b, u, c, FireMode.Aimed),
            Arm.Auto => Tac.Shoot(b, u, c, FireMode.Auto),
            Arm.Throw => ArmedItem is { } g ? Tac.Throw(b, u, g, c) : "NOTHING TO THROW",
            Arm.Strike => there is null ? "NOBODY THERE" : Tac.Strike(b, u, there),
            Arm.Heal => there is null ? "NOBODY THERE" : Tac.UseKit(b, u, there, KitUse.Heal),
            Arm.Stimulant => (there ?? b.BodyAt(c.X, c.Y, c.Z)) is { } p ? Tac.UseKit(b, u, p, KitUse.Stimulant) : "NOBODY THERE",
            Arm.Painkiller => there is null ? "NOBODY THERE" : Tac.UseKit(b, u, there, KitUse.Painkiller),
            Arm.PsiPanic => there is null ? "NOBODY THERE" : Tac.Psi(b, u, there, false),
            Arm.PsiControl => there is null ? "NOBODY THERE" : Tac.Psi(b, u, there, true),
            _ => Tac.Walk(b, u, c),
        };
        if (why is not null) { Refuse(why); return; }
        if (Armed is not Arm.None and not Arm.Auto and not Arm.Aimed and not Arm.Snap) Armed = Arm.None;
        if (Armed != Arm.None && (u.Weapon is not { } w || Tac.ShotCost(u, w, Armed == Arm.Snap ? FireMode.Snap : Armed == Arm.Aimed ? FireMode.Aimed : FireMode.Auto) is var cost && (cost < 0 || u.Tu < cost))) Armed = Arm.None;
    }

    /// <summary>A right click on a tile: turn to face it (or put the armed action away).</summary>
    public void RightClickTile(Cell c)
    {
        if (Armed != Arm.None) { Armed = Arm.None; return; }
        if (!PlayerTurn || Battle is not { } b || Selected is not { } u) return;
        if (Tac.Face(b, u, c) is { } why) Refuse(why);
    }

    public void Kneel()
    {
        if (!PlayerTurn || Battle is not { } b || Selected is not { } u) return;
        if (Tac.Kneel(b, u) is { } why) Refuse(why);
        else Play(Sfx.Click);
    }

    public void Reload()
    {
        if (!PlayerTurn || Battle is not { } b || Selected is not { } u) return;
        if (Tac.Reload(b, u) is { } why) Refuse(why);
        else Play(Sfx.Click);
    }

    public void PrimeArmed(int fuse)
    {
        if (!PlayerTurn || Battle is not { } b || Selected is not { } u || ArmedItem is not { } g) return;
        if (Tac.Prime(b, u, g, fuse) is { } why) Refuse(why);
        else { Say(fuse == 0 ? "PRIMED: IT GOES OFF AT THE END OF THE TURN" : $"PRIMED: {fuse} TURN{(fuse == 1 ? "" : "S")}", 2.5); Play(Sfx.Click); }
    }

    public void EndTurn()
    {
        if (!PlayerTurn || Battle is not { } b) return;
        Armed = Arm.None;
        Inventory = false;
        Tac.EndTurn(b);
    }

    /// <summary>Leaves with whoever is aboard the transport (or gives up the base's defence).</summary>
    public void Abort()
    {
        if (State != GameState.Battle || Battle is not { Outcome: null } b) return;
        BattleMenu = false;
        b.Aborted = true;
        b.Outcome = false;
    }

    public void ToggleBattleMenu()
    {
        if (State != GameState.Battle) return;
        BattleMenu = !BattleMenu;
        Play(Sfx.Click);
    }

    // The inventory.

    public void ToggleInventory()
    {
        if (State != GameState.Battle || Battle is not { } b || Selected is null) return;
        if (!Inventory && (b.Active != Side.Player || b.Busy)) return;
        Inventory = !Inventory;
        Holding = null;
        Armed = Arm.None;
        Play(Sfx.Click);
    }

    /// <summary>A click on an item (picking it up) or on a place (putting what's held down there).</summary>
    public void InventoryClick(Slot slot, Item? item)
    {
        if (!Inventory || Battle is not { } b || Selected is not { } u) return;
        if (Holding is not { } held)
        {
            if (item is not null) { Holding = (item, slot); Play(Sfx.Click); }
            return;
        }
        if (held.From == slot && item == held.Item) { Holding = null; return; }
        var why = Tac.MoveItem(b, u, held.Item, held.From, slot);
        if (why is not null) { Refuse(why); return; }
        Holding = null;
        Play(Sfx.Click);
    }

    public void DropHolding() => Holding = null;

    // ───────────────────────────── the demonstration ─────────────────────────────

    private double _attractNext;
    private bool _attractBattle;

    private void StartAttract()
    {
        var c = new Campaign(_seed++);
        var d = new Dice(_seed);
        var city = Earth.Cities[d.Next(Earth.Cities.Length)];
        while (!LandMask.IsLand(city.At)) city = Earth.Cities[d.Next(Earth.Cities.Length)];
        var b = c.FoundFirstBase(city.At);
        c.Bases[0].Facilities.Add(new Facility { Type = FacilityType.LargeRadar, X = 3, Y = 3 });
        // A UFO not far off, on radar.
        var u = c.SpawnUfo(UfoMission.Research, UfoType.Raider);
        u.At = Geo.Toward(b.At, new GeoPoint(b.At.Lat + 20, b.At.Lon + 25), 900);
        u.Dest = Geo.Toward(b.At, new GeoPoint(b.At.Lat - 20, b.At.Lon - 30), 900);
        c.Detect(u);
        Campaign = c;
        Autopilot = true;
        View = b.At;
        SelectedBase = b;
        Screen = GeoScreen.Globe;
        SpeedIndex = 1;
        _attractBattle = false;
        _attractNext = 0;
        Battle = null;
        Air = null;
        Event = null;
        SetState(GameState.Attract);
        Music(Sfx.GeoMusic);
    }

    private void AttractTick(double dt)
    {
        if (!_attractBattle)
        {
            var c = Campaign!;
            SpinGlobe(dt);
            if (Screen == GeoScreen.AirCombat && Air is { } air)
            {
                if (air.Mode == AirMode.Standard && air.Time < 0.1) air.Mode = AirMode.Aggressive;
                air.Step(dt);
                if (air.Done && air.Over > 2) { c.Resolve(air); Air = null; Screen = GeoScreen.Globe; }
            }
            else if (Screen == GeoScreen.Event) { if (StateTime > _attractNext) { _attractNext = StateTime + 1.5; Answer(Event?.Kind == EventKind.UfoDetected ? 0 : 0); } }
            else
            {
                if (StateTime > _attractNext) { _attractNext = StateTime + 2; Pilot.Manage(c); }
                c.Advance(Speeds[SpeedIndex] * dt);
                foreach (var e in c.Events.ToList())
                {
                    c.Events.Remove(e);
                    if (e.Kind == EventKind.Intercept && c.CraftById(e.Craft) is { } k && c.UfoById(e.Ufo) is { } ufo)
                    {
                        Air = c.Engage(k, ufo);
                        Screen = GeoScreen.AirCombat;
                        Play(Sfx.Launch);
                    }
                }
                if (c.Ufos.FirstOrDefault(x => x.Detected) is { } tracked) SpinTo(tracked.At);
            }
            if (StateTime > AttractGeoSeconds && Screen != GeoScreen.AirCombat)
            {
                _attractBattle = true;
                var d = new Dice(_seed++);
                var terrains = new[] { Terrain.Farmland, Terrain.Forest, Terrain.Urban, Terrain.Desert };
                var battle = BattleGen.Skirmish(_seed++, terrains[d.Next(terrains.Length)], UfoType.Raider, Race.Pallid, soldiers: 8, kind: MissionKind.CrashSite);
                // The demonstration's squad are veterans in carapace armour, so there's a fight worth watching.
                var carapace = Rules.Of(ArmourType.Carapace);
                foreach (var u in battle.Squad)
                {
                    u.Accuracy += 25;
                    u.Reactions += 20;
                    u.Hp = u.MaxHp = u.MaxHp + 20;
                    u.Bravery = 80;
                    u.Armour = ArmourType.Carapace;
                    (u.Front, u.SideArmour, u.Rear, u.Under) = (carapace.Front, carapace.Side, carapace.Rear, carapace.Under);
                }
                Screen = GeoScreen.Globe;
                StartBattle(battle);
            }
            return;
        }
        var b = Battle!;
        BattleTick(dt);
        if (State == GameState.Attract && StateTime > AttractSeconds) { Play(Sfx.MusicStop); _music = ""; ShowHighScores(-1); }
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>A new war with its first base already placed (for tests).</summary>
    public Campaign QuickStart(string city = "LONDON")
    {
        NewCampaign();
        GlobeClick(Earth.Cities.First(c => c.Name == city).At);
        Message = null;
        return Campaign!;
    }
}
