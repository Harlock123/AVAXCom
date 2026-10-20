using Avalonia;
using Avalonia.Media;
using AVAXCom.Engine;

namespace AVAXCom.Views;

public sealed partial class GameCanvas
{
    // ───────────────────────────── soldiers, aliens, civilians ─────────────────────────────

    private static readonly uint[] Shirts = [0xFF3A6AB8, 0xFFB83A3A, 0xFFD8C060, 0xFF4A8A4A, 0xFF8A4A9A, 0xFFE0E0E0, 0xFF6A4A2A];
    private static readonly uint[] Skins = [0xFFF0C8A0, 0xFFD8A878, 0xFFA87048, 0xFF6A4228];

    /// <summary>
    /// A unit standing on a tile (feet at <paramref name="f"/>), turned the way it faces: soldiers in their armour's
    /// colours, civilians in their own clothes, and each alien race in its own shape. Stunned units lie on the ground.
    /// </summary>
    private void DrawUnit(DrawingContext dc, BUnit u, Point f, bool selected, int shade)
    {
        using (dc.PushTransform(Matrix.CreateTranslation(-f.X, -f.Y) * Matrix.CreateScale(1.15, 1.15) * Matrix.CreateTranslation(f.X, f.Y)))
            DrawUnitBody(dc, u, f, selected, shade);
    }

    private void DrawUnitBody(DrawingContext dc, BUnit u, Point f, bool selected, int shade)
    {
        double k = shade switch { 0 => 1, 1 => 0.85, 2 => 0.65, _ => 0.5 };
        uint S(uint c) => k >= 1 ? c : Pix.Mix(Pix.Scale(c, k), 0xFF101A30, (1 - k) * 0.3);
        var (dx, dy) = Tac.Dirs[u.Facing];
        double fx = dx - dy, fy = (dx + dy) * 0.5, fl = Math.Sqrt(fx * fx + fy * fy);
        fx /= fl;
        fy /= fl;
        double m = fx < -0.01 || Math.Abs(fx) < 0.01 && fy < 0 ? -1 : 1;
        bool back = fy < -0.2;
        dc.FillEllipse(f.X, f.Y, 13, 5, 0x50000000);
        if (u.Out)
        {
            uint body = u.IsHuman ? S(Rules.Of(u.Armour).Colour) : S(RaceColour(u.Race));
            dc.FillEllipse(f.X, f.Y - 3, 15, 5, body);
            dc.FillEllipse(f.X + 13 * m, f.Y - 4, 5, u.IsHuman ? S(Skins[u.Id % 4]) : S(Blend(RaceColour(u.Race), 0xFFFFFFFF, 0.2)));
            Text(dc, "z", new Point(f.X + 6, f.Y - 20 - Math.Sin(_clock * 2) * 3), 18, Dim, center: true);
            return;
        }
        if (u.Team != u.Side) dc.DrawEllipse(null, Pen(Alpha(Violet, 0.6 + 0.4 * Math.Sin(_clock * 5)), 2), new Point(f.X, f.Y - 18), 16, 24);
        if (u.Side != Side.Alien) DrawHuman(dc, u, f, fx, fy, m, back, S);
        else DrawAlien(dc, u, f, fx, fy, m, back, S);
        if (selected)
        {
            double bob = Math.Sin(_clock * 5) * 3;
            double top = f.Y - (u.Kneeling ? 40 : 52) + bob;
            Poly(dc, Amber, 0xFF000000, 1, new Point(f.X - 7, top - 8), new Point(f.X + 7, top - 8), new Point(f.X, top));
        }
        if (u.Panic != Panic.None) Text(dc, "!", new Point(f.X + 10, f.Y - 52), 26, Red, center: true);
    }

    private void DrawHuman(DrawingContext dc, BUnit u, Point f, double fx, double fy, double m, bool back, Func<uint, uint> S)
    {
        double h = u.Kneeling ? 0.72 : 1;
        bool civilian = u.Side == Side.Civilian;
        uint armour = civilian ? Shirts[u.Id % Shirts.Length] : Rules.Of(u.Armour).Colour;
        uint legs = civilian ? 0xFF3A4A6A : Shade(armour, 0.7);
        uint skin = Skins[(u.Soldier?.Nation ?? u.Id) % 4];
        if (u.Side == Side.Player && u.Armour == ArmourType.Strider) skin = 0xFF9AA0B0;
        // Legs (bent when kneeling).
        if (u.Kneeling)
        {
            Poly(dc, S(legs), null, 0, new Point(f.X - 6, f.Y - 1), new Point(f.X - 1, f.Y - 1), new Point(f.X + 2 * m, f.Y - 14), new Point(f.X - 5, f.Y - 14));
            Poly(dc, S(Shade(legs, 0.85)), null, 0, new Point(f.X + 1, f.Y - 8), new Point(f.X + 10 * m, f.Y - 8), new Point(f.X + 9 * m, f.Y - 14), new Point(f.X + 1, f.Y - 14));
        }
        else
        {
            double stride = Engine.Battle?.Walker == u ? Math.Sin(_clock * 14) * 3 : 0;
            dc.FillRectangle(Brush(S(legs)), new Rect(f.X - 5 + stride, f.Y - 15, 4.5, 15));
            dc.FillRectangle(Brush(S(Shade(legs, 0.8))), new Rect(f.X + 0.5 - stride, f.Y - 15, 4.5, 15));
            dc.FillRectangle(Brush(S(0xFF202020)), new Rect(f.X - 6 + stride, f.Y - 2, 6, 3));
            dc.FillRectangle(Brush(S(0xFF202020)), new Rect(f.X - stride, f.Y - 2, 6, 3));
        }
        double hip = f.Y - 15 * h, chest = f.Y - 29 * h;
        // The body, with armour plates.
        dc.FillRectangle(Brush(S(armour)), new Rect(f.X - 7, chest, 14, hip - chest + 1), 3);
        if (!civilian)
        {
            dc.FillRectangle(Brush(S(Shade(armour, 1.25))), new Rect(f.X - 6, chest + 1, 12, 4), 2);
            dc.FillRectangle(Brush(S(Shade(armour, 0.6))), new Rect(f.X - 7, hip - 4, 14, 3));
        }
        // The head, and a helmet (or hair).
        double hy = chest - 6;
        dc.FillEllipse(f.X, hy, 5.5, S(skin));
        if (civilian) dc.FillEllipse(f.X - (back ? 0 : m * 1.2), hy - 2.5, 5.8, 3.6, S(u.Female ? 0xFF4A2A18 : 0xFF2A1A10));
        else
        {
            uint helmet = u.Armour == ArmourType.Vest ? 0xFF3A4A30 : Shade(armour, 0.85);
            dc.DrawEllipse(Brush(S(helmet)), null, new Point(f.X, hy - 1.5), 6.5, 5);
            if (u.Armour == ArmourType.Strider && !back) dc.FillRectangle(Brush(S(0xFF5AE0FF)), new Rect(f.X - 4 + m * 2, hy - 1, 6, 2.5));
            else if (!back) dc.FillEllipse(f.X + m * 2.5, hy + 0.5, 1.2, S(0xFF202020));
        }
        // The arms and what they hold, pointing the way the unit faces.
        var shoulder = new Point(f.X + m * 3, chest + 5);
        var w = u.Right ?? u.Left;
        double len = w is null ? 7 : w.Info.Size switch { >= 3 => 20, 2 => 16, _ => 10 };
        var hand = new Point(shoulder.X + fx * len, shoulder.Y + fy * len * 0.6 + 2);
        if (w is not null && w.Info.Class is ItemClass.Weapon)
        {
            uint gun = w.Kind switch
            {
                ItemKind.LaserPistol or ItemKind.LaserRifle or ItemKind.LaserLance => 0xFFD8D8E0,
                ItemKind.StormPistol or ItemKind.StormRifle or ItemKind.StormCannon => 0xFF5A8A6A,
                _ => 0xFF2A2A2A,
            };
            dc.DrawLine(Pen(S(0xFF101010), w.Info.Size >= 3 ? 6 : 4.5), new Point(shoulder.X - fx * 4, shoulder.Y - fy * 2), hand);
            dc.DrawLine(Pen(S(gun), w.Info.Size >= 3 ? 4 : 3), new Point(shoulder.X - fx * 4, shoulder.Y - fy * 2), hand);
            if (w.Kind is ItemKind.LaserRifle or ItemKind.LaserLance or ItemKind.LaserPistol) dc.FillEllipse(hand.X, hand.Y, 1.6, 0xFFFF4A4A);
            if (w.Kind is ItemKind.StormRifle or ItemKind.StormCannon or ItemKind.StormPistol) dc.FillEllipse(hand.X, hand.Y, 1.8, 0xFF6AFF9A);
        }
        dc.DrawLine(Pen(S(Shade(armour, 0.9)), 3.5), shoulder, new Point((shoulder.X + hand.X) / 2, (shoulder.Y + hand.Y) / 2 + 1));
        if (civilian && u.Panic == Panic.None) dc.DrawLine(Pen(S(skin), 2.5), new Point(f.X - m * 6, chest + 3), new Point(f.X - m * 8, chest + 12));
    }

    private static uint RaceColour(Race r) => r switch
    {
        Race.Pallid => 0xFFC8C8C0, Race.Skimmer => 0xFF8A7A6A, Race.Coilback => 0xFF9A7A3A, Race.Brakk => 0xFF5A6A3A,
        Race.Veiled => 0xFF5A1E3A, Race.Husker => 0xFF3A2A4A, Race.Husk => 0xFF7A8A6A, _ => 0xFF6A7480,
    };

    private void DrawAlien(DrawingContext dc, BUnit u, Point f, double fx, double fy, double m, bool back, Func<uint, uint> S)
    {
        uint c = RaceColour(u.Race);
        var w = u.Right;
        switch (u.Race)
        {
            case Race.Pallid:
            {
                dc.DrawLine(Pen(S(Shade(c, 0.8)), 3), new Point(f.X - 3, f.Y), new Point(f.X - 2, f.Y - 11));
                dc.DrawLine(Pen(S(Shade(c, 0.7)), 3), new Point(f.X + 3, f.Y), new Point(f.X + 2, f.Y - 11));
                dc.FillRectangle(Brush(S(Shade(c, 0.9))), new Rect(f.X - 5, f.Y - 21, 10, 11), 3);
                dc.FillEllipse(f.X, f.Y - 30, 8.5, 9.5, S(c));
                if (!back)
                {
                    foreach (int side in new[] { -1, 1 })
                        Poly(dc, S(0xFF060606), null, 0, new Point(f.X + m * 2 + side * 1.5, f.Y - 30), new Point(f.X + m * 2 + side * 6.5, f.Y - 33), new Point(f.X + m * 2 + side * 5.5, f.Y - 28));
                }
                GunArm(dc, w, new Point(f.X + m * 3, f.Y - 18), fx, fy, S, 8);
                break;
            }
            case Race.Skimmer:
            {
                double bob = Math.Sin(_clock * 3 + u.Id) * 2;
                double y = f.Y - 12 + bob;
                dc.FillEllipse(f.X, y + 2, 13, 5, S(0xFF4A5058));
                dc.FillEllipse(f.X, y + 4, 9, 2.5, Alpha(0xFF5AC8FF, 0.6 + 0.3 * Math.Sin(_clock * 20)));
                dc.FillRectangle(Brush(S(c)), new Rect(f.X - 6, y - 14, 12, 15), 4);
                dc.FillEllipse(f.X, y - 19, 5.5, S(Shade(c, 1.2)));
                if (!back) dc.FillRectangle(Brush(S(0xFFE04A30)), new Rect(f.X - 2 + m * 2, y - 21, 4, 2));
                GunArm(dc, w, new Point(f.X + m * 4, y - 9), fx, fy, S, 12);
                break;
            }
            case Race.Coilback:
            {
                for (int i = 0; i < 3; i++) dc.FillEllipse(f.X - m * (4 - i * 2), f.Y - 3 - i * 4, 12 - i * 2, 4.5, S(Shade(c, 0.8 + i * 0.1)));
                Poly(dc, S(c), null, 0, new Point(f.X - 5, f.Y - 12), new Point(f.X + 5, f.Y - 12), new Point(f.X + 4 + m * 2, f.Y - 30), new Point(f.X - 3 + m * 2, f.Y - 30));
                Poly(dc, S(Shade(c, 1.15)), null, 0, new Point(f.X - 7 + m * 2, f.Y - 30), new Point(f.X + 8 + m * 2, f.Y - 30), new Point(f.X + m * 4, f.Y - 40));
                if (!back) dc.FillEllipse(f.X + m * 4, f.Y - 33, 1.6, S(0xFFFFE040));
                GunArm(dc, w, new Point(f.X + m * 3, f.Y - 22), fx, fy, S, 14);
                break;
            }
            case Race.Brakk:
            {
                dc.FillRectangle(Brush(S(Shade(c, 0.7))), new Rect(f.X - 9, f.Y - 15, 7, 15), 2);
                dc.FillRectangle(Brush(S(Shade(c, 0.6))), new Rect(f.X + 2, f.Y - 15, 7, 15), 2);
                dc.FillRectangle(Brush(S(c)), new Rect(f.X - 11, f.Y - 36, 22, 22), 5);
                dc.FillEllipse(f.X - 11, f.Y - 33, 6, 5, S(Shade(c, 1.2)));
                dc.FillEllipse(f.X + 11, f.Y - 33, 6, 5, S(Shade(c, 1.1)));
                dc.FillEllipse(f.X, f.Y - 40, 5, S(0xFF4A5A30));
                if (!back) dc.FillRectangle(Brush(S(0xFFFF3020)), new Rect(f.X - 3 + m * 1.5, f.Y - 41, 6, 2));
                GunArm(dc, w, new Point(f.X + m * 7, f.Y - 26), fx, fy, S, 20);
                break;
            }
            case Race.Veiled:
            {
                Poly(dc, S(c), null, 0, new Point(f.X - 11, f.Y), new Point(f.X + 11, f.Y), new Point(f.X + 6, f.Y - 34), new Point(f.X - 6, f.Y - 34));
                Poly(dc, S(Shade(c, 0.7)), null, 0, new Point(f.X + m * 2, f.Y), new Point(f.X + m * 11, f.Y), new Point(f.X + m * 6, f.Y - 34));
                dc.FillEllipse(f.X, f.Y - 39, 7, 8, S(Shade(c, 0.85)));
                if (!back) dc.FillEllipse(f.X + m * 2, f.Y - 38, 4, 5.5, S(0xFFD8E0F0));
                dc.FillRectangle(Brush(S(0xFFC8A040)), new Rect(f.X - 7, f.Y - 22, 14, 2));
                GunArm(dc, w, new Point(f.X + m * 4, f.Y - 26), fx, fy, S, 12);
                break;
            }
            case Race.Husker:
            {
                for (int i = -1; i <= 1; i++)
                {
                    dc.DrawLine(Pen(S(Shade(c, 0.8)), 2.5), new Point(f.X + i * 6, f.Y - 10), new Point(f.X + i * 10 - 6, f.Y));
                    dc.DrawLine(Pen(S(Shade(c, 0.7)), 2.5), new Point(f.X + i * 6, f.Y - 10), new Point(f.X + i * 10 + 6, f.Y + 1));
                }
                dc.FillEllipse(f.X, f.Y - 14, 13, 8, S(c));
                dc.FillEllipse(f.X - m * 3, f.Y - 18, 8, 5, S(Shade(c, 1.3)));
                dc.FillEllipse(f.X + m * 12, f.Y - 18, 6, 5, S(Shade(c, 1.15)));
                dc.DrawLine(Pen(S(0xFFE8E0C8), 2), new Point(f.X + m * 16, f.Y - 18), new Point(f.X + m * 20, f.Y - 12));
                dc.DrawLine(Pen(S(0xFFE8E0C8), 2), new Point(f.X + m * 16, f.Y - 16), new Point(f.X + m * 19, f.Y - 8));
                if (!back) dc.FillEllipse(f.X + m * 13, f.Y - 20, 1.5, S(0xFFFF2020));
                break;
            }
            case Race.Husk:
            {
                dc.FillRectangle(Brush(S(0xFF3A3A2A)), new Rect(f.X - 5, f.Y - 14, 10, 14));
                Poly(dc, S(0xFF5A5040), null, 0, new Point(f.X - 7, f.Y - 14), new Point(f.X + 7, f.Y - 14), new Point(f.X + 9 + m * 3, f.Y - 27), new Point(f.X - 4 + m * 3, f.Y - 28));
                dc.FillEllipse(f.X + m * 7, f.Y - 30, 5, S(c));
                dc.DrawLine(Pen(S(c), 2.5), new Point(f.X + m * 6, f.Y - 24), new Point(f.X + m * 14, f.Y - 18));
                break;
            }
            default:
            {
                // The Warden: a lens of armour hovering, its rim lit, its eye glowing.
                double bob = Math.Sin(_clock * 2 + u.Id) * 2;
                double y = f.Y - 22 + bob;
                dc.FillEllipse(f.X, y, 22, 9, S(Shade(c, 0.6)));
                dc.FillEllipse(f.X, y - 3, 21, 8, S(c));
                dc.FillEllipse(f.X, y - 6, 12, 5, S(Shade(c, 1.3)));
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4 + _clock;
                    dc.FillEllipse(f.X + Math.Cos(a) * 19, y - 2 + Math.Sin(a) * 6, 1.6, Alpha(0xFF6AFF9A, 0.5 + 0.5 * Math.Sin(a)));
                }
                dc.FillEllipse(f.X + fx * 10, y + 1 + fy * 3, 4, S(0xFF6AFF9A));
                dc.FillEllipse(f.X + fx * 10, y + 1 + fy * 3, 2, S(0xFFE0FFE0));
                break;
            }
        }
    }

    private static void GunArm(DrawingContext dc, Item? w, Point shoulder, double fx, double fy, Func<uint, uint> S, double len)
    {
        if (w is null) return;
        var hand = new Point(shoulder.X + fx * len, shoulder.Y + fy * len * 0.6);
        uint gun = w.Info.Alien ? 0xFF5A8A6A : 0xFF2A2A2A;
        dc.DrawLine(Pen(S(0xFF101010), 4.5), new Point(shoulder.X - fx * 3, shoulder.Y), hand);
        dc.DrawLine(Pen(S(gun), 3), new Point(shoulder.X - fx * 3, shoulder.Y), hand);
        if (w.Info.Alien) dc.FillEllipse(hand.X, hand.Y, 1.8, 0xFF6AFF9A);
    }

    /// <summary>What lies on a tile's floor: corpses as bodies, the rest as a small glinting heap.</summary>
    private void DrawGroundItems(DrawingContext dc, List<Item> items, Point c, int shade)
    {
        double k = shade == 0 ? 1 : shade == 3 ? 0.5 : 0.7;
        int n = 0;
        foreach (var i in items)
        {
            if (i.Info.Class == ItemClass.Corpse)
            {
                var race = Rules.Races.FirstOrDefault(r => r.Corpse == i.Kind)?.Race ?? Race.Pallid;
                uint col = Pix.Scale(RaceColour(race), k);
                dc.FillEllipse(c.X - 2, c.Y - 2, 14, 5, col);
                dc.FillEllipse(c.X + 11, c.Y - 3, race == Race.Pallid ? 7 : 5, 4.5, Pix.Scale(Blend(RaceColour(race), 0xFFFFFFFF, 0.2), k));
                dc.FillEllipse(c.X - 6, c.Y, 6, 2.5, Pix.Scale(0xFF2A6A1A, k));
                continue;
            }
            if (n++ > 2) continue;
            var at = new Point(c.X - 8 + n * 7, c.Y + 2 - n * 2);
            dc.FillRectangle(Brush(Pix.Scale(i.Fuse >= 0 ? 0xFFE04030 : i.Info.Alien ? 0xFF5A9A6A : 0xFF8A8A7A, k)), new Rect(at.X - 4, at.Y - 3, 9, 5), 1);
            if (i.Kind == ItemKind.Flare) dc.FillEllipse(at.X, at.Y - 2, 6, Alpha(0xFFFFE8A0, 0.5 + 0.3 * Math.Sin(_clock * 9)));
        }
    }

    // ───────────────────────────── item pictures ─────────────────────────────

    /// <summary>A picture of an item, fitted in a box.</summary>
    private static void DrawItemIcon(DrawingContext dc, Rect r, ItemKind k)
    {
        var info = Rules.Of(k);
        var c = r.Center;
        double s = Math.Min(r.Width, r.Height) / 50;
        Point P(double x, double y) => new(c.X + x * s, c.Y + y * s);
        uint metal = k switch
        {
            ItemKind.LaserPistol or ItemKind.LaserRifle or ItemKind.LaserLance => 0xFFC8CCD8,
            _ when info.Alien => 0xFF4A7A5A,
            _ => 0xFF4A4E54,
        };
        uint accent = k switch
        {
            ItemKind.LaserPistol or ItemKind.LaserRifle or ItemKind.LaserLance => 0xFFFF4A4A,
            _ when info.Alien => 0xFF6AFF9A,
            _ => 0xFF8A6A3A,
        };
        switch (info.Class)
        {
            case ItemClass.Weapon:
                double len = info.Size switch { 1 => 20, 2 => 36, _ => 44 };
                double thick = info.Size >= 3 ? 9 : 6;
                Poly(dc, metal, 0xFF000000, 1, P(-len / 2, -thick / 2), P(len / 2, -thick / 2), P(len / 2, thick / 2), P(-len / 2, thick / 2));
                if (info.Size >= 2) Poly(dc, accent, 0xFF000000, 1, P(-len / 2 - 8, -2), P(-len / 2, -thick / 2), P(-len / 2, thick / 2 + 3), P(-len / 2 - 8, thick / 2 + 5));
                Poly(dc, Shade(metal, 0.7), 0xFF000000, 1, P(-len / 2 + 4, thick / 2), P(-len / 2 + 10, thick / 2), P(-len / 2 + 8, thick / 2 + 10), P(-len / 2 + 2, thick / 2 + 10));
                if (info.Ammo != ItemKind.None) dc.FillRectangle(Brush(Shade(metal, 1.3)), new Rect(c.X - 2 * s, c.Y + thick / 2 * s, 6 * s, 9 * s));
                dc.FillEllipse(c.X + len / 2 * s, c.Y, 2 * s, accent);
                if (k == ItemKind.Launcher) dc.DrawEllipse(Brush(0xFF2A2A2A), Pen(0xFF000000, 1), P(len / 2, 0), 5 * s, 6 * s);
                break;
            case ItemClass.Ammo:
                dc.FillRectangle(Brush(info.Alien ? 0xFF3A6A4A : 0xFF6A6A3A), new Rect(c.X - 8 * s, c.Y - 12 * s, 16 * s, 24 * s), 3 * s);
                dc.FillRectangle(Brush(info.Alien ? 0xFF6AFF9A : 0xFFD8B850), new Rect(c.X - 5 * s, c.Y - 14 * s, 10 * s, 5 * s));
                break;
            case ItemClass.Grenade:
                uint body = k switch { ItemKind.Flare => 0xFFE8D060, ItemKind.DemoCharge => 0xFF8A6A3A, ItemKind.PulseGrenade => 0xFF4A7A9A, ItemKind.StunBomb => 0xFF5A8AC8, _ => 0xFF4A6A3A };
                if (k == ItemKind.DemoCharge) dc.FillRectangle(Brush(body), new Rect(c.X - 12 * s, c.Y - 8 * s, 24 * s, 16 * s), 2);
                else if (k == ItemKind.Flare) dc.FillRectangle(Brush(body), new Rect(c.X - 4 * s, c.Y - 14 * s, 8 * s, 28 * s), 3);
                else dc.FillEllipse(c.X, c.Y + 2 * s, 11 * s, body);
                dc.FillRectangle(Brush(0xFF8A8A8A), new Rect(c.X - 3 * s, c.Y - 13 * s, 6 * s, 5 * s));
                dc.DrawEllipse(null, Pen(0xFFB0B0B0, 1.5), new Point(c.X + 5 * s, c.Y - 12 * s), 3 * s, 3 * s);
                break;
            case ItemClass.Melee:
                dc.DrawLine(Pen(0xFF2A2A2A, 6 * s), P(-18, 10), P(14, -10));
                dc.DrawLine(Pen(0xFF5AC8FF, 3 * s), P(8, -6), P(16, -11));
                break;
            case ItemClass.Medical:
                dc.FillRectangle(Brush(0xFFE8E8E8), new Rect(c.X - 14 * s, c.Y - 10 * s, 28 * s, 20 * s), 3 * s);
                dc.FillRectangle(Brush(0xFFD83A3A), new Rect(c.X - 2.5 * s, c.Y - 7 * s, 5 * s, 14 * s));
                dc.FillRectangle(Brush(0xFFD83A3A), new Rect(c.X - 7 * s, c.Y - 2.5 * s, 14 * s, 5 * s));
                break;
            case ItemClass.Psi:
                dc.DrawEllipse(null, Pen(0xFF8A8AA0, 3 * s), c, 14 * s, 8 * s);
                Poly(dc, 0xFFB07AFF, 0xFF000000, 1, P(0, -12), P(6, -2), P(0, 6), P(-6, -2));
                break;
            case ItemClass.Corpse:
                dc.FillEllipse(c.X, c.Y, 18 * s, 7 * s, 0xFF7A8A6A);
                dc.FillEllipse(c.X + 16 * s, c.Y - 2 * s, 7 * s, 0xFF9AAA8A);
                break;
            default:
                dc.FillEllipse(c.X, c.Y, 12 * s, k == ItemKind.Vorium ? 0xFFB07AFF : 0xFF8A949E);
                break;
        }
    }

    // ───────────────────────────── the inventory ─────────────────────────────

    private const double InvCell = 58;
    private static readonly Rect InvRight = new(200, 170, 110, 170), InvLeft = new(560, 170, 110, 170);
    private static Rect BeltCell(int i) => new(270 + i * InvCell, 410, InvCell - 4, InvCell - 4);
    private static Rect PackCell(int i) => new(730 + i % 3 * InvCell, 170 + i / 3 * InvCell, InvCell - 4, InvCell - 4);
    private static Rect GroundCell(int i) => new(30 + i * InvCell, 620, InvCell - 4, InvCell - 4);
    private static readonly Rect InvDone = new(880, 700, 124, 44);

    private void DrawInventory(DrawingContext dc, Battle b)
    {
        var g = Engine;
        if (g.Selected is not { } u) return;
        dc.FillRectangle(Brush(0xF4040A12), new Rect(0, 0, ScreenWidth, ScreenHeight));
        Panel(dc, new Rect(8, 8, 1008, 752), PanelEdge, 0x00000000);
        Text(dc, u.IsSoldier ? $"{u.Soldier!.RankName} {u.Name}" : u.Title, new Point(30, 36), 32, White);
        bool free = Tac.FreeKit(b, u);
        Text(dc, $"TIME UNITS {u.Tu}{(free ? "   (FREE WHILE ABOARD ON THE FIRST TURN)" : "")}", new Point(30, 66), 21, Green);
        Text(dc, $"WEIGHT {u.Weight} / STRENGTH {u.Strength}", new Point(30, 90), 21, u.Weight > u.Strength ? Red : Dim);
        // The soldier, large.
        var f = new Point(435, 380);
        using (dc.PushTransform(Matrix.CreateTranslation(-f.X, -f.Y) * Matrix.CreateScale(5, 5) * Matrix.CreateTranslation(f.X, f.Y)))
        {
            int facing = u.Facing;
            u.Facing = 3;
            bool kneel = u.Kneeling;
            u.Kneeling = false;
            DrawUnit(dc, u, f, false, 0);
            u.Facing = facing;
            u.Kneeling = kneel;
        }
        void Slot(Rect r, string label, Item? item, bool hot)
        {
            dc.FillRectangle(Brush(hot ? 0xFF16364A : 0xFF0C1822), r, 3);
            dc.DrawRectangle(null, Pen(hot ? Cyan : 0xFF2E6A8A, 1.5), r, 3, 3);
            if (label.Length > 0) Text(dc, label, new Point(r.X + 4, r.Y - 10), 16, Dim);
            if (item is not null && g.Holding?.Item != item)
            {
                DrawItemIcon(dc, r.Deflate(6), item.Kind);
                if (item.Clip is { } c) Text(dc, $"{c.Rounds}", new Point(r.Right - 4, r.Bottom - 10), 16, Amber, right: true);
                if (item.Fuse >= 0) Text(dc, $"P{item.Fuse}", new Point(r.X + 4, r.Bottom - 10), 16, Red);
            }
        }
        Slot(InvRight, "RIGHT HAND", u.Right, InvRight.Contains(_mouse));
        Slot(InvLeft, "LEFT HAND", u.Left, InvLeft.Contains(_mouse));
        Text(dc, "BELT", new Point(270, 396), 16, Dim);
        for (int i = 0; i < Tac.BeltRoom; i++) Slot(BeltCell(i), "", i < u.Belt.Count ? u.Belt[i] : null, BeltCell(i).Contains(_mouse));
        Text(dc, "BACKPACK", new Point(730, 156), 16, Dim);
        var packCells = new List<Item?>();
        foreach (var i in u.Backpack) { packCells.Add(i); for (int k = 1; k < i.Info.Size; k++) packCells.Add(null); }
        for (int i = 0; i < Tac.BackpackRoom; i++) Slot(PackCell(i), "", i < packCells.Count ? packCells[i] : null, PackCell(i).Contains(_mouse));
        var ground = b.ItemsAt(new Cell(u.X, u.Y, u.Z));
        Text(dc, "GROUND", new Point(30, 606), 16, Dim);
        for (int i = 0; i < 16; i++) Slot(GroundCell(i), "", i < ground.Count ? ground[i] : null, GroundCell(i).Contains(_mouse));
        // What's under the mouse (or held).
        var shown = g.Holding?.Item ?? ItemUnder(b, u, _mouse).Item;
        if (shown is not null)
        {
            var info = shown.Info;
            var box = new Rect(730, 320, 270, 250);
            Panel(dc, box, 0xFF1E4A60);
            Text(dc, info.Name, new Point(box.X + 14, box.Y + 22), 22, Amber);
            double y = box.Y + 50;
            void L(string s) { Text(dc, s, new Point(box.X + 14, y), 18, White); y += 21; }
            L($"WEIGHT {shown.Weight}");
            if (info.Fires)
            {
                var (power, type, radius) = shown.Shot;
                L($"DAMAGE {power} {type.ToString().ToUpperInvariant()}{(radius > 0 ? $", BLAST {radius}" : "")}");
                if (info.Snap > 0) L($"SNAP {info.Snap}% ACC, {info.SnapTu}% TU");
                if (info.Aimed > 0) L($"AIMED {info.Aimed}% ACC, {info.AimedTu}% TU");
                if (info.Auto > 0) L($"AUTO {info.Auto}% ACC, {info.AutoTu}% TU");
                L(info.Ammo != ItemKind.None ? $"AMMO: {(shown.Clip is { } c ? $"{c.Rounds} ROUNDS" : "EMPTY")}" : "NEVER RUNS DRY");
            }
            if (info.Class == ItemClass.Grenade) L(shown.Fuse >= 0 ? $"PRIMED: {shown.Fuse} TURNS" : $"POWER {info.Power}, BLAST {info.Radius}");
            if (info.Class == ItemClass.Medical) L($"WOUNDS {shown.Heals} STIMS {shown.Stims} PAIN {shown.Pains}");
        }
        Text(dc, g.Holding is null ? "CLICK AN ITEM TO PICK IT UP" : "CLICK WHERE IT SHOULD GO (RIGHT-CLICK: PUT IT BACK)", new Point(30, 728), 20, Dim);
        Button(dc, InvDone, "OK", size: 24);
        if (g.Holding is { } h) DrawItemIcon(dc, new Rect(_mouse.X - 26, _mouse.Y - 26, 52, 52), h.Item.Kind);
    }

    /// <summary>The place (and item) under a point of the inventory screen.</summary>
    private (Slot? Slot, Item? Item) ItemUnder(Battle b, BUnit u, Point at)
    {
        if (InvRight.Contains(at)) return (Slot.RightHand, u.Right);
        if (InvLeft.Contains(at)) return (Slot.LeftHand, u.Left);
        for (int i = 0; i < Tac.BeltRoom; i++) if (BeltCell(i).Contains(at)) return (Slot.Belt, i < u.Belt.Count ? u.Belt[i] : null);
        int cell = 0;
        foreach (var item in u.Backpack)
        {
            for (int k = 0; k < item.Info.Size; k++, cell++)
                if (cell < Tac.BackpackRoom && PackCell(cell).Contains(at)) return (Slot.Backpack, item);
        }
        for (int i = 0; i < Tac.BackpackRoom; i++) if (PackCell(i).Contains(at)) return (Slot.Backpack, null);
        var ground = b.ItemsAt(new Cell(u.X, u.Y, u.Z));
        for (int i = 0; i < 16; i++) if (GroundCell(i).Contains(at)) return (Slot.Ground, i < ground.Count ? ground[i] : null);
        return (null, null);
    }

    private void InventoryClickAt(Point at, Battle b)
    {
        var g = Engine;
        if (InvDone.Contains(at)) { g.ToggleInventory(); return; }
        if (g.Selected is not { } u) return;
        var (slot, item) = ItemUnder(b, u, at);
        if (slot is { } s) g.InventoryClick(s, item);
    }
}
