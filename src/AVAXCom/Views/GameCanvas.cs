using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AVAXCom.Engine;

namespace AVAXCom.Views;

/// <summary>
/// The screen: a fixed 1024×768 picture scaled to fit the window. On the geoscape the globe fills the left and the
/// command panel the right; the base, research, workshop and other screens open over it. In battle the isometric map
/// fills the screen above the soldier's panel. Runs the engine from a 60 Hz timer and turns the mouse, the keys and the
/// gamepad into its orders.
/// </summary>
public sealed partial class GameCanvas : Control
{
    public const double ScreenWidth = 1024;
    public const double ScreenHeight = 768;

    private readonly DispatcherTimer? _timer;
    private DateTime _last = DateTime.Now;
    private bool _padPausePrev, _padBackPrev, _padUpPrev, _padDownPrev, _padLeftPrev, _padRightPrev, _padAPrev, _padBPrev, _padXPrev, _padYPrev;
    private double _clock;
    /// <summary>The mouse on the screen (-1: off it), and where a drag of the globe began.</summary>
    private Point _mouse = new(-1, -1);
    private Point? _dragFrom;
    private bool _dragged;
    /// <summary>The gamepad's own cursor, shown once the stick has moved it.</summary>
    private Point _padCursor = new(400, 400);
    private bool _padActive;
    /// <summary>The hand whose action menu is open in battle (-1 none, 0 right, 1 left).</summary>
    private int _handMenu = -1;
    private bool _titleFiles;

    public GameEngine Engine { get; }
    public SoundManager Sound { get; }
    public InputHandler Input { get; }

    public GameCanvas(bool silent = false) : this(new GameEngine(), new SoundManager(silent), startTimer: true) { }

    /// <summary>For tests: an engine of your own, and no timer (call <see cref="Tick"/>).</summary>
    public GameCanvas(GameEngine engine, SoundManager sound, bool startTimer, bool gamepad = true)
    {
        Engine = engine;
        Sound = sound;
        Input = new InputHandler(gamepad && startTimer);
        Engine.Sound += Sound.Play;
        Focusable = true;
        ClipToBounds = true;
        if (startTimer)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (_, _) => Tick();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer?.Start();
        Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        Sound.Dispose();
        Input.Dispose();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        Input.Clear();
    }

    /// <summary>One frame: read the controls, run the game, redraw.</summary>
    public void Tick()
    {
        var now = DateTime.Now;
        double dt = Math.Min(0.1, (now - _last).TotalSeconds);
        _last = now;
        _clock += dt;
        Input.Update();
        HandlePad(dt);
        var g = Engine;
        if (g.State == GameState.Geoscape && g.Screen == GeoScreen.Globe)
        {
            var c = Input.Player;
            if (c.Dx != 0 || c.Dy != 0) g.Rotate(c.Dx * 60 * dt * 300 / g.Zoom, -c.Dy * 45 * dt * 300 / g.Zoom);
        }
        g.Update(dt, Input.Player, Input.Start);
        if (g.State != GameState.Battle) _handMenu = -1;
        InvalidateVisual();
    }

    /// <summary>For tests: moves the drawing clock (blinking lights, turning dishes) without running the game.</summary>
    public void Advance(double dt)
    {
        _clock += dt;
        InvalidateVisual();
    }

    /// <summary>For tests and screenshots: where the mouse is.</summary>
    public void PointAt(Point p)
    {
        _mouse = p;
        Input.PointAt((p.X, p.Y));
    }

    // ───────────────────────────── the gamepad ─────────────────────────────

    private void HandlePad(double dt)
    {
        bool Edge(bool now, ref bool prev) { bool e = now && !prev; prev = now; return e; }
        var g = Engine;
        if (Edge(Input.PadPause, ref _padPausePrev)) PressKey(g.State == GameState.Battle ? Key.E : Key.Enter);
        if (Edge(Input.PadBack, ref _padBackPrev)) PressKey(Key.Escape);
        if (g.State == GameState.EnterInitials && g.Initials is { } entry)
        {
            if (Edge(Input.PadUp, ref _padUpPrev)) entry.Cycle(1);
            if (Edge(Input.PadDown, ref _padDownPrev)) entry.Cycle(-1);
            if (Edge(Input.PadLeft, ref _padLeftPrev)) entry.Back();
            if (Edge(Input.PadRight, ref _padRightPrev)) entry.Move(1);
            if (Edge(Input.PadA, ref _padAPrev)) entry.Advance();
            return;
        }
        // The left stick moves the gamepad's cursor; A clicks with it, B right-clicks, X picks the next soldier, Y kneels.
        var (sx, sy) = Input.PadStick;
        if (sx != 0 || sy != 0)
        {
            _padActive = true;
            double speed = 760 * dt;
            _padCursor = new Point(Math.Clamp(_padCursor.X + sx * Math.Abs(sx) * speed, 0, ScreenWidth - 1), Math.Clamp(_padCursor.Y + sy * Math.Abs(sy) * speed, 0, ScreenHeight - 1));
            Input.PointAt((_padCursor.X, _padCursor.Y));
            _mouse = _padCursor;
        }
        if (Edge(Input.PadA, ref _padAPrev) && _padActive) { LeftDown(_padCursor, false); LeftUp(_padCursor); }
        if (Edge(Input.PadB, ref _padBPrev) && _padActive) RightDown(_padCursor);
        if (Edge(Input.PadX, ref _padXPrev)) PressKey(Key.Tab);
        if (Edge(Input.PadY, ref _padYPrev)) PressKey(Key.K);
    }

    // ───────────────────────────── keys ─────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var g = Engine;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (e.Key == Key.S && ctrl)
        {
            SaveScreenshot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), $"AVAXCom_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png"));
            return;
        }
        if (g.State == GameState.EnterInitials && g.Initials is { } entry)
        {
            InitialsKey(entry, e.Key);
            return;
        }
        PressKey(e.Key, e.PhysicalKey, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
    }

    /// <summary>A key pressed (for tests too): the keys that do one thing once, then the held keys.</summary>
    public void PressKey(Key key, PhysicalKey physical = PhysicalKey.None, bool shift = false)
    {
        var g = Engine;
        shift |= Input.Shift;
        if (key == Key.M) { Sound.IsMuted = !Sound.IsMuted; return; }
        switch (g.State)
        {
            case GameState.Title:
                if (_titleFiles)
                {
                    if (key == Key.Escape) _titleFiles = false;
                    else if (key is >= Key.D0 and <= Key.D6) { _titleFiles = false; g.LoadSlot(key - Key.D0); }
                    return;
                }
                if (key == Key.C) { g.Continue(); return; }
                if (key == Key.L) { _titleFiles = true; return; }
                if (key == Key.Escape) { (TopLevel.GetTopLevel(this) as Window)?.Close(); return; }
                break;
            case GameState.Attract or GameState.HighScores:
                if (key is Key.Escape or Key.Space) { g.ReturnToTitle(); return; }
                break;
            case GameState.Briefing:
                if (key is Key.Space) g.BeginBattle();
                return;
            case GameState.Debrief:
                if (key is Key.Space or Key.Escape) g.FinishDebrief();
                return;
            case GameState.Geoscape:
                if (GeoKey(key, shift)) return;
                break;
            case GameState.Battle:
                if (BattleKey(key, shift)) return;
                break;
        }
        Input.OnKeyDown(key, physical);
    }

    /// <summary>The geoscape's keys. True if the key was used up.</summary>
    private bool GeoKey(Key key, bool shift)
    {
        var g = Engine;
        if (g.Screen == GeoScreen.Event)
        {
            if (key is Key.Enter or Key.Space or Key.Escape) g.Answer(0);
            else if (key is >= Key.D1 and <= Key.D3) g.Answer(key - Key.D1);
            return true;
        }
        if (g.Screen == GeoScreen.AirCombat)
        {
            switch (key)
            {
                case Key.D1: g.SetAirMode(AirMode.Standoff); break;
                case Key.D2: g.SetAirMode(AirMode.Cautious); break;
                case Key.D3: g.SetAirMode(AirMode.Standard); break;
                case Key.D4: g.SetAirMode(AirMode.Aggressive); break;
                case Key.D5 or Key.Escape: g.SetAirMode(AirMode.Disengage); break;
            }
            return true;
        }
        if (g.Screen == GeoScreen.Files)
        {
            if (key == Key.Escape) g.CloseScreen();
            else if (key is >= Key.D0 and <= Key.D6) FileSlot(key - Key.D0);
            return true;
        }
        if (g.Screen != GeoScreen.Globe)
        {
            if (key is Key.Escape or Key.Back) { g.CloseScreen(); return true; }
            if (g.Screen == GeoScreen.Base && key is Key.Left or Key.Right) { CycleBase(key == Key.Left ? -1 : 1); return true; }
            if (key is Key.PageUp or Key.Up) { g.ListScroll = Math.Max(0, g.ListScroll - 1); return true; }
            if (key is Key.PageDown or Key.Down) { g.ListScroll++; return true; }
            return false;
        }
        switch (key)
        {
            case >= Key.D1 and <= Key.D6: g.SetSpeed(key - Key.D1); return true;
            case Key.Escape:
                if (g.PlacingBase) g.CancelPlacing();
                return true;
            case Key.OemPlus or Key.Add or Key.PageUp: g.ZoomBy(1); return true;
            case Key.OemMinus or Key.Subtract or Key.PageDown: g.ZoomBy(-1); return true;
            case Key.I: g.OpenIntercept(null, null); return true;
            case Key.B: g.Open(GeoScreen.Base); return true;
            case Key.R: g.Open(GeoScreen.Research); return true;
            case Key.U: g.Open(GeoScreen.Manufacture); return true;
            case Key.P: g.Open(GeoScreen.Purchase); return true;
            case Key.S: g.Open(GeoScreen.Soldiers); return true;
            case Key.F: g.Open(GeoScreen.Funding); return true;
            case Key.F5: g.OpenFiles(true); return true;
            case Key.F9: g.OpenFiles(false); return true;
            case Key.H:
                if (g.Campaign?.Bases.FirstOrDefault() is { } home) g.SpinTo(home.At);
                return true;
        }
        return false;
    }

    /// <summary>The battle's keys. True if the key was used up.</summary>
    private bool BattleKey(Key key, bool shift)
    {
        var g = Engine;
        if (g.BattleMenu)
        {
            if (key is Key.Escape) g.ToggleBattleMenu();
            else if (key == Key.A) g.Abort();
            return true;
        }
        if (g.Inventory)
        {
            if (key is Key.Escape or Key.I or Key.Enter) g.ToggleInventory();
            return true;
        }
        switch (key)
        {
            case Key.Escape:
                if (_handMenu >= 0) _handMenu = -1;
                else if (g.Armed != Arm.None) g.Disarm();
                else g.ToggleBattleMenu();
                return true;
            case Key.Tab or Key.N: g.NextSoldier(shift); return true;
            case Key.K: g.Kneel(); return true;
            case Key.R: g.Reload(); return true;
            case Key.I: g.ToggleInventory(); return true;
            case Key.C or Key.Home: g.CenterOnSelected(); return true;
            case Key.E or Key.Enter: g.EndTurn(); return true;
            case Key.PageUp or Key.OemPlus or Key.Add: g.LevelUp(); return true;
            case Key.PageDown or Key.OemMinus or Key.Subtract: g.LevelDown(); return true;
            case Key.D1: g.ArmAction(Arm.Snap); return true;
            case Key.D2: g.ArmAction(Arm.Aimed); return true;
            case Key.D3: g.ArmAction(Arm.Auto); return true;
            case Key.T:
                if (g.Selected is { } u && (u.Left ?? u.Right) is { } item && item.Info.Class == ItemClass.Grenade) g.ArmAction(Arm.Throw, u.Left?.Info.Class == ItemClass.Grenade ? u.Left : u.Right);
                return true;
        }
        return false;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        Input.OnKeyUp(e.Key, e.PhysicalKey);
        e.Handled = true;
    }

    private static void InitialsKey(InitialsEntry entry, Key key)
    {
        switch (key)
        {
            case Key.Up: entry.Cycle(1); break;
            case Key.Down: entry.Cycle(-1); break;
            case Key.Left or Key.Back: entry.Back(); break;
            case Key.Right: entry.Move(1); break;
            case Key.Space: entry.Advance(); break;
            case Key.Enter: entry.Finish(); break;
            case Key.OemPeriod or Key.Decimal: entry.Type('.'); break;
            case >= Key.A and <= Key.Z: entry.Type((char)('A' + (key - Key.A))); break;
            case >= Key.D0 and <= Key.D9: entry.Type((char)('0' + (key - Key.D0))); break;
            case >= Key.NumPad0 and <= Key.NumPad9: entry.Type((char)('0' + (key - Key.NumPad0))); break;
        }
    }

    // ───────────────────────────── the mouse ─────────────────────────────

    /// <summary>A point in the window to the fixed screen.</summary>
    private Point ToScreen(Point p)
    {
        var (s, ox, oy) = Fit();
        return s <= 0 ? new Point(-1, -1) : new Point((p.X - ox) / s, (p.Y - oy) / s);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var at = ToScreen(e.GetPosition(this));
        if (_dragFrom is { } from && Engine.State == GameState.Geoscape && Engine.Screen == GeoScreen.Globe)
        {
            double dx = at.X - _mouse.X, dy = at.Y - _mouse.Y;
            if (Math.Abs(at.X - from.X) + Math.Abs(at.Y - from.Y) > 6) _dragged = true;
            if (_dragged) Engine.Rotate(-dx / Engine.Zoom * 57.3, dy / Engine.Zoom * 57.3);
        }
        _mouse = at;
        _padActive = false;
        Input.PointAt((_mouse.X, _mouse.Y));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var at = ToScreen(e.GetPosition(this));
        _mouse = at;
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed) RightDown(at);
        else if (props.IsLeftButtonPressed) LeftDown(at, e.KeyModifiers.HasFlag(KeyModifiers.Shift) || Input.Shift);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        LeftUp(ToScreen(e.GetPosition(this)));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Input.PointAt(null);
        _mouse = new Point(-1, -1);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var g = Engine;
        int d = Math.Sign(e.Delta.Y);
        if (g.State == GameState.Geoscape && g.Screen == GeoScreen.Globe) g.ZoomBy(d);
        else if (g.State == GameState.Geoscape) g.ListScroll = Math.Max(0, g.ListScroll - d);
        else if (g.State == GameState.Battle && !g.Inventory) { if (d > 0) g.LevelUp(); else if (d < 0) g.LevelDown(); }
    }

    /// <summary>The left button (or the gamepad's A) down at a point of the screen.</summary>
    public void LeftDown(Point at, bool shift)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Title: TitleClick(at); return;
            case GameState.Briefing: g.BeginBattle(); return;
            case GameState.Debrief: if (g.StateTime > 0.5) g.FinishDebrief(); return;
            case GameState.Attract or GameState.HighScores: g.ReturnToTitle(); return;
            case GameState.Geoscape: GeoClick(at, shift); return;
            case GameState.Battle: BattleClick(at, shift); return;
        }
    }

    public void LeftUp(Point at)
    {
        var g = Engine;
        if (_dragFrom is { } from && !_dragged && g.State == GameState.Geoscape && g.Screen == GeoScreen.Globe && GlobeArea.Contains(at))
        {
            if (GlobePoint(from) is { } p) g.GlobeClick(p);
        }
        _dragFrom = null;
        _dragged = false;
    }

    /// <summary>The right button (or the gamepad's B).</summary>
    public void RightDown(Point at)
    {
        var g = Engine;
        switch (g.State)
        {
            case GameState.Geoscape:
                if (g.Screen == GeoScreen.Globe && g.PlacingBase) g.CancelPlacing();
                else if (g.Screen != GeoScreen.Globe) GeoRightClick(at);
                break;
            case GameState.Battle:
                if (g.Inventory) { g.DropHolding(); return; }
                if (_handMenu >= 0) { _handMenu = -1; return; }
                if (at.Y < Iso.MapH) g.RightClickTile(g.TileAt(at.X, at.Y));
                break;
        }
    }

    /// <summary>Saves what's on screen as a PNG.</summary>
    public void SaveScreenshot(string path)
    {
        var size = new PixelSize((int)Math.Max(1, Bounds.Width), (int)Math.Max(1, Bounds.Height));
        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(this);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }

    /// <summary>How the fixed screen fits the window: scale and letterbox offsets.</summary>
    private (double Scale, double X, double Y) Fit()
    {
        double s = Math.Min(Bounds.Width / ScreenWidth, Bounds.Height / ScreenHeight);
        return (s, (Bounds.Width - ScreenWidth * s) / 2, (Bounds.Height - ScreenHeight * s) / 2);
    }

    public override void Render(DrawingContext dc)
    {
        dc.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        var (s, ox, oy) = Fit();
        if (s <= 0) return;
        using (dc.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(ox, oy)))
        using (dc.PushClip(new Rect(0, 0, ScreenWidth, ScreenHeight)))
            DrawScreen(dc);
    }
}
