using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AVAXCom.Engine;

namespace AVAXCom.Views;

public sealed partial class GameCanvas
{
    private static WriteableBitmap? _backdrop;

    // ───────────────────────────── the backdrop ─────────────────────────────

    /// <summary>The Earth from orbit at night, its cities alight, filling the bottom of the screen under the stars.</summary>
    private static WriteableBitmap Backdrop()
    {
        if (_backdrop is not null) return _backdrop;
        var stars = GlobePainter.Stars((int)ScreenWidth, (int)ScreenHeight);
        var painter = new GlobePainter((int)ScreenWidth, (int)ScreenHeight);
        using var globe = painter.Render(new GeoPoint(8, 18), 900, 512, 1240, new DateTime(2031, 3, 20, 21, 30, 0, DateTimeKind.Utc));
        // Lay the globe over the stars.
        var p = new Pix((int)ScreenWidth, (int)ScreenHeight);
        Array.Copy(stars.P, p.P, p.P.Length);
        using (var fb = globe.Lock())
        unsafe
        {
            uint* src = (uint*)fb.Address;
            int stride = fb.RowBytes / 4;
            for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                uint c = src[y * stride + x];
                uint a = c >> 24;
                if (a == 0) continue;
                // Un-premultiply, then blend.
                uint Ch(int s) => (uint)Math.Min(255, ((c >> s) & 0xFF) * 255 / a);
                uint col = 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
                p.P[y * p.W + x] = Pix.Mix(p.P[y * p.W + x], col, a / 255.0);
            }
        }
        return _backdrop = p.ToBitmap();
    }

    private static void DrawBackdrop(DrawingContext dc) => dc.DrawImage(Backdrop(), new Rect(0, 0, ScreenWidth, ScreenHeight));

    // ───────────────────────────── the title ─────────────────────────────

    private static readonly Rect NewButton = new(362, 452, 300, 40), ContinueButton = new(362, 500, 300, 40), LoadButton = new(362, 548, 300, 40);

    private void DrawTitle(DrawingContext dc)
    {
        var g = Engine;
        DrawBackdrop(dc);
        // A wedge of UFOs crossing the sky toward the night side.
        double t = (_clock * 0.03) % 1.3;
        for (int i = 0; i < 5; i++)
        {
            int row = i == 0 ? 0 : (i + 1) / 2;
            int side = i == 0 ? 0 : i % 2 == 0 ? 1 : -1;
            var p = new Point(-120 + t * 1100 - row * 46, 410 - t * 90 + row * 22 + side * row * 16);
            dc.FillEllipse(p.X, p.Y + 6, 34, 12, 0x30FF6A3A);
            DrawSaucer(dc, p, 18 - row * 2, UfoType.Raider, 0xFF8A929E);
            dc.FillEllipse(p.X, p.Y + 4, 9, 3, Alpha(0xFFFF6A3A, 0.6 + 0.4 * Math.Sin(_clock * 8 + i)));
        }
        double cx = ScreenWidth / 2;
        // The title, lit from within.
        for (int k = 4; k >= 1; k--) Text(dc, Rules.Title, new Point(cx, 150), 170, Alpha(Cyan, 0.08 * k), center: true, shadow: false);
        Text(dc, Rules.Title, new Point(cx, 150), 160, 0xFFE8F8FF, center: true);
        Text(dc, Rules.Subtitle, new Point(cx, 238), 44, Amber, center: true);
        Text(dc, "THEY CAME OUT OF A CLEAR SKY. EVERY NATION SWORE IT WAS SOMEONE ELSE'S PROBLEM.", new Point(cx, 284), 22, White, center: true);
        Text(dc, "NOW THE COUNCIL HAS FUNDED ONE ORGANISATION TO STAND BETWEEN THE EARTH AND THE INVADERS.", new Point(cx, 308), 22, Dim, center: true);
        Panel(dc, new Rect(342, 432, 340, 172));
        Button(dc, NewButton, "NEW CAMPAIGN  (ENTER)", size: 24);
        Button(dc, ContinueButton, "CONTINUE  (C)", g.HasSave, 24);
        Button(dc, LoadButton, "LOAD GAME  (L)", g.HasSave, 24);
        dc.FillRectangle(Brush(0xD0000000), new Rect(0, 684, ScreenWidth, 84));
        Text(dc, "GEOSCAPE: CLICK THE GLOBE, DRAG TO TURN IT, WHEEL TO ZOOM; 1-6 TIME SPEED; B BASES R RESEARCH U MANUFACTURE S SOLDIERS", new Point(cx, 700), 18, 0xFFC8D8E0, center: true);
        Text(dc, "BATTLE: CLICK TO MOVE OR TARGET; HANDS FOR ACTIONS; TAB NEXT  K KNEEL  I INVENTORY  PGUP/PGDN LEVEL  E END TURN", new Point(cx, 721), 18, 0xFFC8D8E0, center: true);
        Text(dc, "M SOUND   CTRL+S SCREENSHOT   MUSIC: FUNERAL MARCH FROM PIANO SONATA NO. 2 (CHOPIN, 1839) AND TUNES OF ITS OWN", new Point(cx, 746), 17, Dim, center: true);
        if (_titleFiles) DrawSlots(dc, saving: false, title: false);
    }

    private void TitleClick(Point at)
    {
        var g = Engine;
        if (_titleFiles)
        {
            if (FilesClose.Contains(at)) { _titleFiles = false; return; }
            int slot = SlotAt(at);
            if (slot >= 0 && g.LoadSlot(slot)) _titleFiles = false;
            return;
        }
        if (NewButton.Contains(at)) g.NewCampaign();
        else if (ContinueButton.Contains(at)) g.Continue();
        else if (LoadButton.Contains(at) && g.HasSave) _titleFiles = true;
    }

    // ───────────────────────────── the briefing ─────────────────────────────

    private void DrawBriefing(DrawingContext dc)
    {
        var g = Engine;
        if (g.Mission is not { } m) return;
        DrawBackdrop(dc);
        dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, ScreenWidth, ScreenHeight));
        var r = new Rect(80, 60, 864, 640);
        Panel(dc, r, Amber);
        Heading(dc, r, "MISSION BRIEFING", Amber);
        Text(dc, m.Name, new Point(r.X + 30, r.Y + 80), 40, White);
        string kind = m.Kind switch
        {
            MissionKind.CrashSite => "A UFO HAS CRASHED. SECURE THE SITE, ELIMINATE THE SURVIVORS AND RECOVER WHAT YOU CAN.",
            MissionKind.LandedUfo => "A UFO HAS LANDED. ITS CREW IS INTACT AND ALERT. ELIMINATE THEM AND TAKE THE CRAFT.",
            MissionKind.Terror => "ALIENS ARE KILLING CIVILIANS IN THE STREETS. STOP THEM. EVERY LIFE SAVED COUNTS.",
            MissionKind.AlienBase => "AN ALIEN BASE LIES BELOW. FIGHT THROUGH IT AND DESTROY IT.",
            MissionKind.BaseDefence => "ALIENS HAVE BROKEN INTO THE BASE. DEFEND IT, OR LOSE EVERYTHING IN IT.",
            _ => "THE HIVE OF NYX. THE MIND BEHIND THE INVASION LIES AT ITS HEART. DESTROY IT.",
        };
        Paragraph(dc, kind, new Rect(r.X + 30, r.Y + 110, r.Width - 60, 60), 24, Amber);
        double y = r.Y + 180;
        void Line(string label, string value)
        {
            Text(dc, label, new Point(r.X + 30, y), 22, Dim);
            Text(dc, value, new Point(r.X + 260, y), 22, White);
            y += 30;
        }
        Line("TERRAIN", m.Terrain.ToString().ToUpperInvariant());
        Line("CONDITIONS", m.Night ? "NIGHT - VISIBILITY 9 TILES; USE FLARES" : "DAYLIGHT");
        bool known = g.Campaign is { } c && Rules.AutopsyOf(m.Race) is { } t && c.Researched.Contains(t);
        Line("HOSTILES", known ? Rules.Of(m.Race).Plural : "UNKNOWN");
        if (m.Ufo is { } ut) Line("CRAFT", $"{Rules.Of(ut).Name} ({Rules.Of(ut).Size})");
        Line("SQUAD", $"{m.Squad.Count} SOLDIERS");
        y += 10;
        foreach (var s in m.Squad.Take(12))
        {
            Text(dc, $"{s.RankName,-10} {s.Name}", new Point(r.X + 30, y), 20, White);
            Text(dc, $"{Rules.Of(s.Armour).Name}  /  {(s.Weapon == ItemKind.None ? "BEST WEAPON" : Rules.Of(s.Weapon).Name)}", new Point(r.X + 420, y), 18, Dim);
            y += 24;
        }
        DrawCraftTopDown(dc, new Point(r.Right - 110, r.Y + 210), m.Kind == MissionKind.BaseDefence ? CraftType.Falcon : CraftType.Raven, 1.2);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "CLICK OR PRESS ENTER TO DEPLOY", new Point(r.Center.X, r.Bottom - 30), 26, Green, center: true);
    }

    // ───────────────────────────── the debrief ─────────────────────────────

    private void DrawDebrief(DrawingContext dc)
    {
        var g = Engine;
        if (g.Debrief is not { } d) return;
        DrawBackdrop(dc);
        dc.FillRectangle(Brush(0xA0000000), new Rect(0, 0, ScreenWidth, ScreenHeight));
        var r = new Rect(40, 30, 944, 708);
        uint accent = d.Won ? Green : Red;
        Panel(dc, r, accent);
        Heading(dc, r, d.Won ? "MISSION SUCCESSFUL" : d.Aborted ? "MISSION ABORTED" : "MISSION FAILED", accent);
        Text(dc, $"{d.Name}  -  {d.Turns} TURNS", new Point(r.X + 24, r.Y + 64), 24, White);
        double k = Math.Clamp(g.StateTime / 1.5, 0, 1);
        double y = r.Y + 100;
        Text(dc, "RESULT", new Point(r.X + 24, y), 19, Dim);
        Text(dc, "NUMBER", new Point(r.X + 380, y), 19, Dim, right: true);
        Text(dc, "POINTS", new Point(r.X + 480, y), 19, Dim, right: true);
        y += 26;
        foreach (var (label, count, points) in d.Lines.Take(16))
        {
            Text(dc, label, new Point(r.X + 24, y), 21, White);
            Text(dc, $"{count}", new Point(r.X + 380, y), 21, White, right: true);
            Text(dc, $"{(int)(points * k)}", new Point(r.X + 480, y), 21, points >= 0 ? Green : Red, right: true);
            y += 25;
        }
        y += 8;
        dc.FillRectangle(Brush(0xFF2E6A8A), new Rect(r.X + 24, y - 10, 460, 2));
        Text(dc, "TOTAL", new Point(r.X + 24, y + 8), 28, Amber);
        Text(dc, $"{(int)(d.Points * k)}", new Point(r.X + 480, y + 8), 30, Amber, right: true);
        Text(dc, $"RATING: {d.Rating}", new Point(r.X + 24, y + 42), 24, accent);
        // The right: what came home, who didn't, and who was promoted.
        double x = r.X + 520;
        y = r.Y + 100;
        Text(dc, "RECOVERED", new Point(x, y), 19, Dim);
        y += 24;
        foreach (var (item, n) in d.Recovered.Where(kv => Rules.Of(kv.Key).Alien).OrderByDescending(kv => Rules.Of(kv.Key).Score).Take(9))
        {
            Text(dc, $"{n,4}  {Rules.Of(item).Name}", new Point(x, y), 20, Violet);
            y += 22;
        }
        foreach (var (race, rank) in d.Captured.Take(3)) { Text(dc, $"LIVE {Rules.Of(race).Name} {Rules.RankName(rank)}", new Point(x, y), 20, Green); y += 22; }
        y += 10;
        if (d.Dead.Count > 0)
        {
            Text(dc, "KILLED IN ACTION", new Point(x, y), 19, Red);
            y += 24;
            foreach (var u in d.Dead.Take(6)) { Text(dc, u.Name, new Point(x, y), 20, White); y += 22; }
            y += 6;
        }
        foreach (var note in d.Notes.Take(6)) { Paragraph(dc, note, new Rect(x, y - 8, 420, 44), 18, Amber); y += 22 * Wrap(note, 18, 420).Count; }
        if (g.StateTime > 1 && (int)(_clock * 2) % 2 == 0) Text(dc, "CLICK OR PRESS ENTER", new Point(r.Center.X, r.Bottom - 26), 26, White, center: true);
    }

    // ───────────────────────────── the end ─────────────────────────────

    private void DrawGameOver(DrawingContext dc)
    {
        var g = Engine;
        DrawBackdrop(dc);
        dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, ScreenWidth, ScreenHeight));
        bool won = g.Campaign?.Won ?? false;
        var r = new Rect(112, 200, 800, 320);
        Panel(dc, r, won ? Amber : Red);
        Text(dc, won ? "VICTORY" : "THE PROJECT IS OVER", new Point(512, 262), 64, (int)(_clock * 3) % 2 == 0 ? (won ? Amber : Red) : White, center: true);
        Paragraph(dc, g.Campaign?.Ending ?? "", new Rect(r.X + 40, 300, r.Width - 80, 80), 24, White);
        Text(dc, $"FINAL SCORE {g.Score}", new Point(512, 420), 44, Amber, center: true);
        Text(dc, $"{(g.Campaign?.MonthIndex ?? 0) + 1} MONTH{((g.Campaign?.MonthIndex ?? 0) == 0 ? "" : "S")} OF WAR", new Point(512, 468), 26, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = ScreenWidth / 2;
        Panel(dc, new Rect(cx - 330, 60, 660, 650));
        Text(dc, "THE COUNCIL'S ROLL OF HONOUR", new Point(cx, 112), 46, Amber, center: true);
        Text(dc, "RANK NAME   SCORE  MONTHS", new Point(cx, 180), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,7}   {e.Stage,4}", new Point(cx, 226 + i * 42), 34, mine ? Red : i == 0 ? Amber : White, center: true);
        }
        Text(dc, "PRESS ENTER", new Point(cx, 680), 28, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = ScreenWidth / 2;
        Panel(dc, new Rect(cx - 330, 140, 660, 460));
        Text(dc, "A PLACE ON THE ROLL OF HONOUR", new Point(cx, 200), 46, Amber, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS, COMMANDER", new Point(cx, 350), 32, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Amber, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
