using Avalonia.Media.Imaging;
using AVAXCom.Engine;

namespace AVAXCom.Views;

/// <summary>
/// The globe, painted pixel by pixel. A map of the world is made once from the land mask: seas shading from shallow to
/// deep, land coloured by its climate (ice, tundra, forest, grass and farmland, desert, jungle) with relief, and the
/// lights of cities. Each view of the globe (where it's turned to, how near) is worked out once — which place lies under
/// each pixel — and the light of the moment is laid over it: the day side lit by the sun with a glint on the sea, the
/// night side dark but for the cities' lights, a soft terminator between, and the blue of the atmosphere at the rim.
/// </summary>
public sealed class GlobePainter
{
    public const int TexW = 1440, TexH = 720;
    private static uint[]? _tex;
    private static byte[]? _lights;
    private static Pix? _stars;

    private readonly int _w, _h;
    private double[] _nx = [], _ny = [], _nz = [], _depth = [];
    private uint[] _base = [];
    private byte[] _lit = [], _water = [];
    private (double Lat, double Lon, double R, double Cx, double Cy) _viewKey = (999, 999, 0, 0, 0);
    private (int, int) _sunKey = (9999, 9999);
    private WriteableBitmap? _bitmap;
    private Pix? _pix;

    public GlobePainter(int w, int h)
    {
        _w = w;
        _h = h;
    }

    // ───────────────────────────── the world map ─────────────────────────────

    private static float[]? _landV;
    private static uint[]? _seaC;

    /// <summary>
    /// The world map, in three layers: how much each texel is land (smoothly varying, so a coast can be found between
    /// texels at any zoom), the colour it would have as land, and the colour it would have as sea.
    /// </summary>
    private static void MakeTexture()
    {
        if (_tex is not null) return;
        var tex = new uint[TexW * TexH];
        var sea = new uint[TexW * TexH];
        var landV = new float[TexW * TexH];
        var lights = new byte[TexW * TexH];
        Parallel.For(0, TexH, j =>
        {
            for (int i = 0; i < TexW; i++)
            {
                var p = new GeoPoint(90 - (j + 0.5) * 180.0 / TexH, -180 + (i + 0.5) * 360.0 / TexW);
                var v = Geo.ToVec(p);
                double land = LandMask.Land(p);
                landV[j * TexW + i] = (float)land;
                double detail = Geo.Fractal(v, 22, 3, 41);
                // As land: coloured by its climate, with relief and mountains.
                var climate = LandMask.ClimateAt(p);
                double relief = Geo.Fractal(v, 14, 4, 53);
                uint c = climate switch
                {
                    Climate.Ice => Pix.Mix(0xFFDCE6EE, 0xFFF6FAFF, detail),
                    Climate.Tundra => Pix.Mix(0xFF6E7660, 0xFF9A9A84, detail),
                    Climate.Forest => Pix.Mix(0xFF1E4424, 0xFF3A6232, detail),
                    Climate.Desert => Pix.Mix(0xFFB08A52, 0xFFDCBC7E, detail),
                    Climate.Jungle => Pix.Mix(0xFF14481E, 0xFF2A6A2A, detail),
                    _ => Pix.Mix(0xFF48703A, 0xFF7E9A4A, detail),
                };
                if (relief > 0.62 && climate != Climate.Ice) c = Pix.Mix(c, 0xFF7A6A58, Math.Min(1, (relief - 0.62) * 4));
                if (relief > 0.74) c = Pix.Mix(c, 0xFFF0F0F0, Math.Min(1, (relief - 0.74) * 6));
                if (land < 0.55 && climate != Climate.Ice) c = Pix.Mix(c, 0xFFB8A878, 0.35);
                tex[j * TexW + i] = Pix.Scale(c, 0.9 + relief * 0.25);
                // As sea: shallow by the coasts, deep far out, iced near the poles.
                double depth = Math.Clamp((0.5 - land) * 2.2, 0, 1);
                uint w = Pix.Mix(0xFF2A6C96, 0xFF0A2350, Math.Sqrt(depth));
                w = Pix.Mix(w, 0xFF0E3460, detail * 0.25);
                if (Math.Abs(p.Lat) > 68) w = Pix.Mix(w, 0xFFC8D8E6, Math.Min(1, (Math.Abs(p.Lat) - 68) / 6) * 0.7);
                sea[j * TexW + i] = w;
                // People: how settled the land is, brightest near the great cities.
                if (land > 0.45)
                {
                    double people = Geo.Fractal(v, 30, 2, 61) * (climate is Climate.Grass or Climate.Forest or Climate.Jungle ? 1 : 0.6);
                    double near = 0;
                    foreach (var city in Earth.Cities)
                    {
                        double a = Geo.Arc(p, city.At) / Geo.Deg;
                        if (a < 4) near = Math.Max(near, 1 - a / 4);
                    }
                    double lit = Math.Max(0, people - 0.55) * 2.2 + near * near * 1.1;
                    lights[j * TexW + i] = (byte)Math.Clamp(lit * 255, 0, 255);
                }
            }
        });
        _lights = lights;
        _seaC = sea;
        _landV = landV;
        _tex = tex;
    }

    /// <summary>The colour of a place: the land value interpolated to find the coast smoothly, then land's or sea's
    /// colour interpolated on its side of it.</summary>
    private static uint Sample(double lat, double lon, out byte light, out byte water)
    {
        double fx = (lon + 180) / 360 * TexW - 0.5, fy = (90 - lat) / 180 * TexH - 0.5;
        int x0 = (int)Math.Floor(fx), y0 = Math.Clamp((int)Math.Floor(fy), 0, TexH - 1);
        double tx = fx - x0, ty = Math.Clamp(fy - y0, 0, 1);
        int x1 = (x0 + 1) % TexW, y1 = Math.Min(TexH - 1, y0 + 1);
        x0 = (x0 % TexW + TexW) % TexW;
        int a = y0 * TexW + x0, b = y0 * TexW + x1, c = y1 * TexW + x0, d = y1 * TexW + x1;
        var lv = _landV!;
        double land = (lv[a] * (1 - tx) + lv[b] * tx) * (1 - ty) + (lv[c] * (1 - tx) + lv[d] * tx) * ty;
        var t = _tex!;
        var s = _seaC!;
        uint landC = Pix.Mix(Pix.Mix(t[a], t[b], tx), Pix.Mix(t[c], t[d], tx), ty);
        uint seaC = Pix.Mix(Pix.Mix(s[a], s[b], tx), Pix.Mix(s[c], s[d], tx), ty);
        // A soft edge a hair wide, so the coast is smooth at any zoom.
        double k = Math.Clamp((land - 0.5) / 0.012 + 0.5, 0, 1);
        int ni = Math.Clamp((int)Math.Round(fy), 0, TexH - 1) * TexW + ((int)Math.Round(fx) % TexW + TexW) % TexW;
        light = k > 0.5 ? _lights![ni] : (byte)0;
        water = k < 0.5 ? (byte)1 : (byte)0;
        return Pix.Mix(seaC, landC, k);
    }

    /// <summary>The stars behind the globe.</summary>
    public static Pix Stars(int w, int h)
    {
        if (_stars is { } s && s.W == w && s.H == h) return s;
        var p = new Pix(w, h, 0xFF02040A);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            double n = Geo.Noise(x / 140.0, y / 140.0, 0.3, 77);
            uint c = Pix.Mix(0xFF02040A, 0xFF0C1430, Math.Max(0, n - 0.45) * 1.6);
            double r = Pix.Rand(x, y, 91);
            if (r > 0.9975) c = Pix.Mix(c, 0xFFFFFFFF, 0.4 + (r - 0.9975) * 240);
            else if (r > 0.995) c = Pix.Mix(c, 0xFFA8C0FF, 0.35);
            p.P[y * w + x] = c;
        }
        return _stars = p;
    }

    // ───────────────────────────── a view of it ─────────────────────────────

    /// <summary>
    /// The globe as seen now: centred at (cx, cy) on the picture with radius r, turned to <paramref name="view"/>, lit for
    /// the time given. Redrawn only when the view or the sun has moved enough to show.
    /// </summary>
    public WriteableBitmap Render(GeoPoint view, double r, double cx, double cy, DateTime when)
    {
        MakeTexture();
        var key = (Math.Round(view.Lat, 2), Math.Round(view.Lon, 2), r, cx, cy);
        var sun = Geo.Sun(when);
        var sunKey = ((int)Math.Round(sun.Lat * 4), (int)Math.Round(sun.Lon * 4));
        bool viewChanged = key != _viewKey;
        if (!viewChanged && sunKey == _sunKey && _bitmap is not null) return _bitmap;
        if (viewChanged) Map(view, r, cx, cy);
        _viewKey = key;
        _sunKey = sunKey;
        Light(Geo.ToVec(sun), Geo.ToVec(view), r, cx, cy);
        _bitmap?.Dispose();
        _bitmap = _pix!.ToBitmap();
        return _bitmap;
    }

    /// <summary>Works out what lies under every pixel of the globe for a view.</summary>
    private void Map(GeoPoint view, double r, double cx, double cy)
    {
        int n = _w * _h;
        if (_nx.Length != n)
        {
            _nx = new double[n]; _ny = new double[n]; _nz = new double[n]; _depth = new double[n];
            _base = new uint[n]; _lit = new byte[n]; _water = new byte[n];
        }
        double la0 = view.Lat * Geo.Deg, cl = Math.Cos(la0), sl = Math.Sin(la0);
        Parallel.For(0, _h, y =>
        {
            for (int x = 0; x < _w; x++)
            {
                int i = y * _w + x;
                double u = (x + 0.5 - cx) / r, v = -(y + 0.5 - cy) / r, d2 = u * u + v * v;
                if (d2 > 1) { _depth[i] = -1; continue; }
                double z2 = Math.Sqrt(1 - d2);
                double y1 = v * cl + z2 * sl, z1 = -v * sl + z2 * cl;
                double lat = Math.Asin(Math.Clamp(y1, -1, 1)), dlon = Math.Atan2(u, z1);
                double lon = view.Lon * Geo.Deg + dlon;
                _nx[i] = Math.Cos(lat) * Math.Sin(lon);
                _ny[i] = y1;
                _nz[i] = Math.Cos(lat) * Math.Cos(lon);
                _depth[i] = z2;
                double latD = lat / Geo.Deg, lonD = Geo.Wrap(lon / Geo.Deg);
                _base[i] = Sample(latD, lonD, out byte density, out _water[i]);
                // City lights: fine points scattered by how settled the land is, fixed to the ground so they don't shimmer.
                double speck = Pix.Rand((int)Math.Floor(lonD * 9), (int)Math.Floor(latD * 9), 7);
                _lit[i] = speck < density / 255.0 * 0.5 ? (byte)Math.Clamp(90 + density * 0.65 + speck * 300, 0, 255) : (byte)0;
            }
        });
    }

    /// <summary>Lays the light of the moment over the mapped globe, and the atmosphere's glow round it.</summary>
    private void Light(Vec3 sun, Vec3 eye, double r, double cx, double cy)
    {
        _pix ??= new Pix(_w, _h);
        var h = (sun + eye).Unit;
        var p = _pix.P;
        Parallel.For(0, _h, y =>
        {
            for (int x = 0; x < _w; x++)
            {
                int i = y * _w + x;
                double depth = _depth[i];
                if (depth < 0)
                {
                    // The atmosphere's halo just outside the rim.
                    double dx = x + 0.5 - cx, dy = y + 0.5 - cy, dist = Math.Sqrt(dx * dx + dy * dy) / r;
                    if (dist < 1.06)
                    {
                        double k = 1 - (dist - 1) / 0.06;
                        p[i] = Alpha(0xFF5AA8FF, k * k * 0.7);
                    }
                    else p[i] = 0;
                    continue;
                }
                double d = _nx[i] * sun.X + _ny[i] * sun.Y + _nz[i] * sun.Z;
                double day = Math.Clamp((d + 0.08) / 0.2, 0, 1);
                day = day * day * (3 - 2 * day);
                uint c = _base[i];
                uint lit = Pix.Scale(c, 0.62 + 0.55 * Math.Pow(Math.Max(0, d), 0.6));
                if (_water[i] == 1)
                {
                    double spec = Math.Max(0, _nx[i] * h.X + _ny[i] * h.Y + _nz[i] * h.Z);
                    spec = Math.Pow(spec, 90) * 0.45;
                    if (spec > 0.01) lit = Pix.Mix(lit, 0xFFFFF4D8, spec);
                }
                uint dark = Pix.Mix(Pix.Scale(c, 0.3), 0xFF0A1630, 0.4);
                if (_lit[i] > 0) dark = Pix.Mix(dark, 0xFFFFD27A, _lit[i] / 255.0 * 0.85);
                uint col = Pix.Mix(dark, lit, day);
                // The terminator glows faintly orange.
                double dusk = Math.Max(0, 1 - Math.Abs(d - 0.02) / 0.07);
                if (dusk > 0) col = Pix.Mix(col, 0xFFFF9A50, dusk * 0.18);
                // Haze toward the rim, blue on the day side.
                double rim = Math.Pow(1 - depth, 3);
                col = Pix.Mix(col, day > 0.3 ? 0xFF8CC8FF : 0xFF14284A, rim * (0.25 + 0.4 * day));
                p[i] = col;
            }
        });
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);
}
