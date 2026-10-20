using Avalonia.Media.Imaging;
using AVAXCom.Engine;

namespace AVAXCom.Views;

/// <summary>
/// The base's facilities seen from above, drawn pixel by pixel once each: the lift's hazard-ringed platform, the
/// hangar's bay with its landing circle, bunks, laboratory benches and specimen tanks, machine shops, crates, the glowing
/// tubes of alien containment, radar dishes, gun and missile emplacements, shield emitters, the decoder's antennae and
/// the psi lab's ring of chairs. Also the rock the base is dug into.
/// </summary>
public static class BaseArt
{
    public const int Cell = 96;
    private static readonly Dictionary<FacilityType, WriteableBitmap> Cache = [];
    private static WriteableBitmap? _rock;

    public static WriteableBitmap Rock()
    {
        if (_rock is not null) return _rock;
        var p = new Pix(Cell, Cell);
        for (int y = 0; y < Cell; y++)
        for (int x = 0; x < Cell; x++)
        {
            double n = Geo.Noise(x / 14.0, y / 14.0, 0.5, 5) * 0.7 + Geo.Noise(x / 4.0, y / 4.0, 1.5, 6) * 0.3;
            p.P[y * Cell + x] = Pix.Mix(0xFF2A241E, 0xFF5A4E40, n);
        }
        p.Grain(0, 0, Cell, Cell, 0.12, 3);
        return _rock = p.ToBitmap();
    }

    public static WriteableBitmap Of(FacilityType t)
    {
        if (Cache.TryGetValue(t, out var b)) return b;
        int s = Rules.Of(t).Size * Cell;
        var p = new Pix(s, s);
        Floor(p, s);
        switch (t)
        {
            case FacilityType.Lift: Lift(p, s); break;
            case FacilityType.Hangar: Hangar(p, s); break;
            case FacilityType.Quarters: Quarters(p); break;
            case FacilityType.Lab: Lab(p); break;
            case FacilityType.Workshop: Workshop(p); break;
            case FacilityType.Stores: Stores(p); break;
            case FacilityType.Containment: Containment(p); break;
            case FacilityType.SmallRadar: Dish(p, 30, 0xFFB8C4CC); break;
            case FacilityType.LargeRadar: Dish(p, 40, 0xFFD0D8E0); break;
            case FacilityType.MissileDefence: Turret(p, 0xFF6A7A5A, 0xFFE0E0D0, missiles: true); break;
            case FacilityType.LaserDefence: Turret(p, 0xFF5A6070, 0xFFFF4A4A, missiles: false); break;
            case FacilityType.StormDefence: Turret(p, 0xFF4A5A50, 0xFF6AFF9A, missiles: false); break;
            case FacilityType.GravShield: Emitter(p, 0xFF5AA8FF); break;
            case FacilityType.MindShield: Emitter(p, 0xFFB07AFF); break;
            case FacilityType.SignalDecoder: Decoder(p); break;
            case FacilityType.PsiLab: PsiLab(p); break;
        }
        // The walls round the room.
        p.Frame(0, 0, s, s, 0xFF0A0E12);
        p.Frame(1, 1, s - 2, s - 2, 0xFF6A7A88);
        p.Frame(2, 2, s - 4, s - 4, 0xFF3A4652);
        return Cache[t] = p.ToBitmap();
    }

    private static void Floor(Pix p, int s)
    {
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            uint c = (x % 24 == 0 || y % 24 == 0) ? 0xFF2A323A : 0xFF3A444E;
            p.P[y * s + x] = c;
        }
        p.Grain(0, 0, s, s, 0.06, 11);
    }

    private static void Lift(Pix p, int s)
    {
        double c = s / 2.0;
        for (int i = 0; i < 48; i++)
        {
            double a0 = i * Math.PI * 2 / 48;
            uint col = i % 2 == 0 ? 0xFFE8C030 : 0xFF141414;
            for (double r = 34; r < 40; r += 0.5)
            for (double a = a0; a < a0 + Math.PI * 2 / 48; a += 0.02)
                p.Set((int)(c + Math.Cos(a) * r), (int)(c + Math.Sin(a) * r), col);
        }
        p.Disc(c, c, 33, 0xFF5A646E);
        p.Ring(c, c, 22, 24, 0xFF7A8690);
        p.Disc(c, c, 10, 0xFF46505A);
        for (int k = 0; k < 4; k++)
        {
            double a = k * Math.PI / 2;
            int x0 = (int)(c + Math.Cos(a) * 14), y0 = (int)(c + Math.Sin(a) * 14);
            p.Line(x0, y0, (int)(c + Math.Cos(a) * 28), (int)(c + Math.Sin(a) * 28), 0xFFE8C030);
        }
    }

    private static void Hangar(Pix p, int s)
    {
        p.Fill(8, 8, s - 16, s - 16, 0xFF4A4E52);
        p.Grain(8, 8, s - 16, s - 16, 0.08, 21);
        double c = s / 2.0;
        p.Ring(c, c, 62, 66, 0xFFE8C030);
        p.Ring(c, c, 40, 42, 0xFFE8C030);
        for (int i = 16; i < s - 16; i += 12)
        {
            p.Fill(i, 12, 6, 3, 0xFFFFF0A0);
            p.Fill(i, s - 15, 6, 3, 0xFFFFF0A0);
        }
        p.Fill(c - 3 < 0 ? 0 : (int)c - 3, 12, 6, 30, 0xFFE8E8E8);
    }

    private static void Quarters(Pix p)
    {
        for (int row = 0; row < 3; row++)
        for (int col = 0; col < 4; col++)
        {
            int x = 10 + col * 20, y = 12 + row * 26;
            p.Block(x, y, 14, 18, 4, 0xFF8090A0, 0xFF4A5560);
            p.Fill(x + 2, y - 2, 10, 4, 0xFFE8E8F0);
            p.Fill(x + 1, y + 6, 12, 10, row % 2 == 0 ? 0xFF3A5A8A : 0xFF5A3A3A);
        }
        p.Fill(8, 86, 80, 4, 0xFF5A6A40);
    }

    private static void Lab(Pix p)
    {
        p.Block(10, 14, 76, 12, 6, 0xFFE0E4E8, 0xFF8A9098);
        p.Block(10, 50, 76, 12, 6, 0xFFE0E4E8, 0xFF8A9098);
        for (int i = 0; i < 4; i++)
        {
            p.Fill(14 + i * 18, 9, 10, 6, 0xFF1A3A5A);
            p.Fill(15 + i * 18, 10, 8, 4, 0xFF5AE0FF);
            p.Fill(14 + i * 18, 45, 10, 6, 0xFF1A3A5A);
            p.Fill(15 + i * 18, 46, 8, 4, 0xFF6AFF9A);
        }
        for (int i = 0; i < 3; i++)
        {
            p.Disc(22 + i * 26, 80, 8, 0xFF2A3A30);
            p.Disc(22 + i * 26, 80, 6, 0xA05AE07A);
            p.Disc(20 + i * 26, 78, 2, 0xFFE0FFE0);
        }
    }

    private static void Workshop(Pix p)
    {
        p.Block(8, 14, 34, 26, 10, 0xFF8A6A3A, 0xFF5A4428);
        p.Block(54, 14, 34, 26, 10, 0xFF6A7A88, 0xFF3A4652);
        p.Fill(8, 60, 80, 10, 0xFF2A2A2A);
        for (int x = 10; x < 88; x += 8) p.Fill(x, 62, 4, 6, 0xFF4A4A4A);
        p.Block(14, 76, 18, 12, 5, 0xFFB09060, 0xFF6A5030);
        p.Block(62, 76, 22, 12, 5, 0xFF7A8A6A, 0xFF4A5A3A);
        p.Disc(24, 26, 6, 0xFFFFB040);
        p.Disc(70, 24, 4, 0xFF5AE0FF);
    }

    private static void Stores(Pix p)
    {
        var d = new Dice(9);
        for (int row = 0; row < 4; row++)
        for (int col = 0; col < 4; col++)
        {
            if (d.Chance(0.2)) continue;
            int x = 8 + col * 21, y = 10 + row * 21;
            uint top = d.Pick(new uint[] { 0xFF8A7048, 0xFF6A7A48, 0xFF5A6A7A, 0xFF9A8A60 });
            p.Block(x, y, 16, 14, 4 + d.Next(6), top, Pix.Scale(top, 0.6));
            p.Line(x + 1, y + 1, x + 14, y + 12, Pix.Scale(top, 0.75));
        }
    }

    private static void Containment(Pix p)
    {
        for (int i = 0; i < 4; i++)
        {
            int x = 24 + i % 2 * 48, y = 24 + i / 2 * 48;
            p.Disc(x, y, 18, 0xFF1A2420);
            p.Disc(x, y, 15, 0xC03AA858);
            p.Disc(x, y, 9, 0xFF0A3A1A);
            p.Disc(x - 1, y - 3, 4, 0xFF8AA0A8);
            p.Ring(x, y, 15, 17, 0xFF8A98A0);
            p.Disc(x - 6, y - 7, 3, 0x90FFFFFF);
        }
    }

    private static void Dish(Pix p, double r, uint colour)
    {
        p.Block(36, 66, 24, 18, 6, 0xFF5A646E, 0xFF3A444E);
        p.Dome(48, 44, r, colour);
        p.Ring(48, 44, r * 0.3, r * 0.35, 0xFF7A8690);
        for (int k = 0; k < 6; k++)
        {
            double a = k * Math.PI / 3;
            p.Line(48, 44, (int)(48 + Math.Cos(a) * r * 0.95), (int)(44 + Math.Sin(a) * r * 0.95), Pix.Scale(colour, 0.8));
        }
        p.Disc(48, 44, 4, 0xFFE04030);
    }

    private static void Turret(Pix p, uint body, uint accent, bool missiles)
    {
        p.Disc(48, 48, 34, 0xFF2A3036);
        p.Ring(48, 48, 30, 34, 0xFFE8C030);
        p.Dome(48, 48, 24, body);
        if (missiles)
            for (int i = 0; i < 4; i++)
            {
                p.Fill(30 + i * 10, 26, 7, 30, Pix.Scale(body, 0.7));
                p.Disc(33.5 + i * 10, 28, 3, accent);
            }
        else
        {
            p.Fill(44, 10, 8, 34, Pix.Scale(body, 0.7));
            p.Disc(48, 12, 5, accent);
            p.Disc(48, 12, 2, 0xFFFFFFFF);
        }
    }

    private static void Emitter(Pix p, uint colour)
    {
        for (int r = 40; r > 8; r -= 8) p.Ring(48, 48, r - 2, r, Pix.Mix(0xFF1A2430, colour, 1 - r / 48.0));
        p.Dome(48, 48, 12, colour);
    }

    private static void Decoder(Pix p)
    {
        for (int row = 0; row < 3; row++)
        for (int col = 0; col < 3; col++)
        {
            int x = 18 + col * 30, y = 18 + row * 30;
            p.Disc(x, y, 9, 0xFF5A646E);
            p.Line(x - 8, y, x + 8, y, 0xFFD0D8E0);
            p.Line(x, y - 8, x, y + 8, 0xFFD0D8E0);
            p.Disc(x, y, 2, 0xFF5AE0FF);
        }
    }

    private static void PsiLab(Pix p)
    {
        p.Disc(48, 48, 36, 0xFF20142A);
        p.Ring(48, 48, 34, 36, 0xFFB07AFF);
        for (int k = 0; k < 8; k++)
        {
            double a = k * Math.PI / 4;
            p.Block((int)(48 + Math.Cos(a) * 26) - 5, (int)(48 + Math.Sin(a) * 26) - 4, 10, 8, 4, 0xFF6A5A7A, 0xFF3A2A4A);
        }
        p.Disc(48, 48, 10, 0xFF8A5AD0);
        p.Disc(48, 48, 5, 0xFFE0C8FF);
    }
}
