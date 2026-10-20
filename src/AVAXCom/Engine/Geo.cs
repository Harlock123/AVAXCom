namespace AVAXCom.Engine;

/// <summary>A point (or direction) in space: on the globe, x points at longitude 90°E, y at the north pole and z at
/// longitude 0 on the equator.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public double Dot(Vec3 o) => X * o.X + Y * o.Y + Z * o.Z;
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public Vec3 Unit => this * (1 / Math.Max(1e-12, Length));
}

/// <summary>A place on the globe, in degrees (north and east positive).</summary>
public readonly record struct GeoPoint(double Lat, double Lon)
{
    public override string ToString() => $"{Math.Abs(Lat):0}°{(Lat >= 0 ? "N" : "S")} {Math.Abs(Lon):0}°{(Lon >= 0 ? "E" : "W")}";
}

/// <summary>
/// The globe's geometry: points as vectors, distances along great circles (in nautical miles, as the original's
/// radar ranges were), moving from one place toward another, the sun's place for a time of year and day, and the
/// orthographic projection the geoscape draws the globe with (and its inverse, for clicks).
/// </summary>
public static class Geo
{
    public const double EarthNm = 3440;
    public const double Deg = Math.PI / 180;
    public static readonly DateTime Start = new(2031, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    public static Vec3 ToVec(GeoPoint p)
    {
        double la = p.Lat * Deg, lo = p.Lon * Deg;
        return new Vec3(Math.Cos(la) * Math.Sin(lo), Math.Sin(la), Math.Cos(la) * Math.Cos(lo));
    }

    public static GeoPoint FromVec(Vec3 v)
    {
        var u = v.Unit;
        return new GeoPoint(Math.Asin(Math.Clamp(u.Y, -1, 1)) / Deg, Wrap(Math.Atan2(u.X, u.Z) / Deg));
    }

    /// <summary>A longitude brought into -180..180.</summary>
    public static double Wrap(double lon)
    {
        lon %= 360;
        if (lon > 180) lon -= 360;
        if (lon < -180) lon += 360;
        return lon;
    }

    /// <summary>The angle between two places, in radians.</summary>
    public static double Arc(GeoPoint a, GeoPoint b) => Math.Acos(Math.Clamp(ToVec(a).Dot(ToVec(b)), -1, 1));

    public static double Nm(GeoPoint a, GeoPoint b) => Arc(a, b) * EarthNm;

    /// <summary>The place <paramref name="nm"/> nautical miles from <paramref name="a"/> along the great circle toward
    /// <paramref name="b"/> (b itself, if it's nearer than that).</summary>
    public static GeoPoint Toward(GeoPoint a, GeoPoint b, double nm)
    {
        double total = Arc(a, b), step = nm / EarthNm;
        if (step >= total || total < 1e-9) return b;
        double t = step / total, s = Math.Sin(total);
        var va = ToVec(a);
        var vb = ToVec(b);
        var v = va * (Math.Sin((1 - t) * total) / s) + vb * (Math.Sin(t * total) / s);
        return FromVec(v);
    }

    /// <summary>The place directly under the sun at a time: the season tilts it north and south, the day turns it west.</summary>
    public static GeoPoint Sun(DateTime t)
    {
        double day = t.DayOfYear + t.TimeOfDay.TotalHours / 24;
        double decl = -23.44 * Math.Cos(2 * Math.PI / 365.25 * (day + 10));
        double lon = -15 * (t.TimeOfDay.TotalHours - 12);
        return new GeoPoint(decl, Wrap(lon));
    }

    /// <summary>How high the sun stands at a place: 1 overhead, 0 on the horizon, below 0 at night.</summary>
    public static double Daylight(GeoPoint p, DateTime t) => ToVec(p).Dot(ToVec(Sun(t)));

    public static bool IsNight(GeoPoint p, DateTime t) => Daylight(p, t) < -0.05;

    // ───────────────────────────── the projection ─────────────────────────────

    /// <summary>
    /// A place seen on the globe from far above <paramref name="centre"/>: x to the right and y up, both -1..1 across
    /// the globe's disc, and whether it's on the near side.
    /// </summary>
    public static (double X, double Y, bool Front) Project(GeoPoint p, GeoPoint centre)
    {
        double la = p.Lat * Deg, dl = (p.Lon - centre.Lon) * Deg, la0 = centre.Lat * Deg;
        double x1 = Math.Cos(la) * Math.Sin(dl), y1 = Math.Sin(la), z1 = Math.Cos(la) * Math.Cos(dl);
        double y2 = y1 * Math.Cos(la0) - z1 * Math.Sin(la0), z2 = y1 * Math.Sin(la0) + z1 * Math.Cos(la0);
        return (x1, y2, z2 > 0);
    }

    /// <summary>The place under a point of the globe's disc (x right, y up, -1..1), or null off the globe.</summary>
    public static GeoPoint? Unproject(double x, double y, GeoPoint centre)
    {
        double r2 = x * x + y * y;
        if (r2 > 1) return null;
        double z2 = Math.Sqrt(1 - r2), la0 = centre.Lat * Deg;
        double y1 = y * Math.Cos(la0) + z2 * Math.Sin(la0), z1 = -y * Math.Sin(la0) + z2 * Math.Cos(la0);
        return new GeoPoint(Math.Asin(Math.Clamp(y1, -1, 1)) / Deg, Wrap(centre.Lon + Math.Atan2(x, z1) / Deg));
    }

    // ───────────────────────────── noise ─────────────────────────────

    private static double Hash(int x, int y, int z, int salt)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177 + salt * 1442695041);
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (double)0xFFFFFF;
    }

    /// <summary>Smooth value noise in three dimensions (0..1), so a pattern wraps the globe with no seam.</summary>
    public static double Noise(double x, double y, double z, int salt)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y), iz = (int)Math.Floor(z);
        double fx = x - ix, fy = y - iy, fz = z - iz;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        fz = fz * fz * (3 - 2 * fz);
        double L(double a, double b, double t) => a + (b - a) * t;
        double c00 = L(Hash(ix, iy, iz, salt), Hash(ix + 1, iy, iz, salt), fx), c10 = L(Hash(ix, iy + 1, iz, salt), Hash(ix + 1, iy + 1, iz, salt), fx);
        double c01 = L(Hash(ix, iy, iz + 1, salt), Hash(ix + 1, iy, iz + 1, salt), fx), c11 = L(Hash(ix, iy + 1, iz + 1, salt), Hash(ix + 1, iy + 1, iz + 1, salt), fx);
        return L(L(c00, c10, fy), L(c01, c11, fy), fz);
    }

    /// <summary>Fractal noise: a few octaves of <see cref="Noise"/>, each half as strong and twice as fine.</summary>
    public static double Fractal(Vec3 v, double scale, int octaves, int salt)
    {
        double sum = 0, amp = 1, norm = 0;
        for (int o = 0; o < octaves; o++)
        {
            sum += Noise(v.X * scale + 31.7, v.Y * scale + 17.3, v.Z * scale + 5.9, salt + o) * amp;
            norm += amp;
            amp *= 0.5;
            scale *= 2;
        }
        return sum / norm;
    }
}

/// <summary>
/// Which of the globe is land: a 72 by 36 mask of five-degree cells (row 0 at the north pole, column 0 at 180°W),
/// smoothed between the cells' centres and roughened with noise so the coasts wander naturally. The geoscape's painted
/// globe and the game's own questions (did the UFO come down on land?) both ask here, so they always agree.
/// </summary>
public static class LandMask
{
    public static readonly string[] Rows =
    [
        "........................................................................",
        "..................##############.............####......##...............",
        "............#########.##########......####.....##......####....##.......",
        "...#####...###########..########........##......###.####################",
        "..#####################..########.....##################################",
        "..###############...####..##.........################################...",
        "...##....########...####...........#.###########################...##...",
        "..........###############..........##############################.......",
        "...........############............############################.#.......",
        "...........###########............###.#######.################..........",
        "...........##########.............####..####################.###........",
        "............#########.............##########################..#.........",
        ".............#######.............###########################............",
        "..............###..##............##########.####..########..............",
        "...............###...#...........#############.....##..###..#...........",
        "..................#..............############......#....##..#...........",
        "....................####.........#############..........#...............",
        "....................######........##########...........##.##............",
        "....................#########.........######............#.###.####......",
        "....................#########.........######.............##.....##......",
        ".....................#######...........#####.#................#.#.......",
        "......................######..........######.#..............#####.......",
        "......................######...........####..#.............#######......",
        "......................####.............###.................########.....",
        "......................####..............##.................##..####...#.",
        ".....................####.......................................##.....#",
        ".....................##..........................................#....#.",
        ".....................##.................................................",
        ".....................#..................................................",
        "........................................................................",
        ".......................##...............................................",
        "######...............####.......########################################",
        "########################################################################",
        "########################################################################",
        "########################################################################",
        "########################################################################",
    ];

    private static double Cell(int col, int row)
    {
        row = Math.Clamp(row, 0, 35);
        col = ((col % 72) + 72) % 72;
        return Rows[row][col] == '#' ? 1 : 0;
    }

    /// <summary>Catmull-Rom weights: a smooth curve through the cells' values, so a lone cell is still an island.</summary>
    private static (double, double, double, double) Weights(double t)
    {
        double t2 = t * t, t3 = t2 * t;
        return ((-t3 + 2 * t2 - t) / 2, (3 * t3 - 5 * t2 + 2) / 2, (-3 * t3 + 4 * t2 + t) / 2, (t3 - t2) / 2);
    }

    /// <summary>How much a place is land, smoothed between the cells and roughened with noise: over 0.5 is land.</summary>
    public static double Land(GeoPoint p)
    {
        // Cell centres are at (col + 0.5) * 5 - 180 and 90 - (row + 0.5) * 5.
        double fc = (p.Lon + 180) / 5 - 0.5, fr = (90 - p.Lat) / 5 - 0.5;
        int c0 = (int)Math.Floor(fc), r0 = (int)Math.Floor(fr);
        var (x0, x1, x2, x3) = Weights(fc - c0);
        var (y0, y1, y2, y3) = Weights(fr - r0);
        double Row(int r) => Cell(c0 - 1, r) * x0 + Cell(c0, r) * x1 + Cell(c0 + 1, r) * x2 + Cell(c0 + 2, r) * x3;
        double v = Row(r0 - 1) * y0 + Row(r0) * y1 + Row(r0 + 1) * y2 + Row(r0 + 2) * y3;
        var vec = Geo.ToVec(p);
        double n = Geo.Fractal(vec, 7, 5, 11) - 0.5;
        return v + n * 0.7;
    }

    public static bool IsLand(GeoPoint p) => Land(p) > 0.5;

    /// <summary>What the land is like: ice at the poles, tundra and forest in the north, deserts in the dry belts either
    /// side of the tropics, jungle at the equator, and farmland and grass between.</summary>
    public static Climate ClimateAt(GeoPoint p)
    {
        double a = Math.Abs(p.Lat);
        var v = Geo.ToVec(p);
        double n = Geo.Fractal(v, 6, 3, 23), wet = Geo.Fractal(v, 4, 3, 29);
        if (a > 66 + n * 8 || p.Lat < -58) return Climate.Ice;
        if (a > 58 + n * 6) return Climate.Tundra;
        if (a > 47 + n * 6) return Climate.Forest;
        // The dry belts: the Sahara and Arabia, central Asia, the American south-west, the Kalahari and the outback.
        // The belts' edges wander with the noise, so no border is a straight line.
        double la = a + (n - 0.5) * 9, lo = p.Lon + (wet - 0.5) * 16, lat = p.Lat + (n - 0.5) * 9;
        bool dryBelt = la is > 14 and < 34 && (lo is > -18 and < 62 && p.Lat > 0 || lo is > 112 and < 146 && p.Lat < 0 || lo is > -116 and < -100 && p.Lat > 0 || lo is > 12 and < 28 && p.Lat < 0)
                       || lat is > 36 and < 48 && lo is > 55 and < 110;
        if (dryBelt && wet < 0.62) return Climate.Desert;
        if (a < 12 && wet > 0.38) return Climate.Jungle;
        return wet > 0.58 ? Climate.Forest : Climate.Grass;
    }
}

public enum Climate { Ice, Tundra, Forest, Grass, Desert, Jungle }

/// <summary>A nation that funds the organisation: its middle and reach on the globe, and its funding to begin with (in
/// thousands of dollars a month).</summary>
public sealed record NationInfo(string Name, string Short, double Lat, double Lon, double Reach, int Funding)
{
    public GeoPoint At => new(Lat, Lon);
}

public sealed record City(string Name, double Lat, double Lon)
{
    public GeoPoint At => new(Lat, Lon);
}

/// <summary>The funding nations and the cities, as the geoscape shows them.</summary>
public static class Earth
{
    public static readonly NationInfo[] Nations =
    [
        new("USA", "USA", 39, -98, 22, 620),
        new("CANADA", "CAN", 57, -100, 24, 140),
        new("BRAZIL", "BRA", -10, -52, 20, 170),
        new("ARGENTINA", "ARG", -36, -65, 16, 110),
        new("UNITED KINGDOM", "UK", 54, -2, 7, 260),
        new("FRANCE", "FRA", 46.5, 2.5, 7, 280),
        new("GERMANY", "GER", 51, 10.5, 6, 340),
        new("ITALY", "ITA", 42.5, 12.5, 6, 200),
        new("RUSSIA", "RUS", 60, 70, 40, 300),
        new("CHINA", "CHN", 34, 104, 20, 380),
        new("JAPAN", "JPN", 36.5, 138.5, 7, 400),
        new("INDIA", "IND", 22, 79, 14, 210),
        new("EGYPT", "EGY", 27, 30, 10, 90),
        new("NIGERIA", "NGA", 9.5, 8, 12, 90),
        new("SOUTH AFRICA", "RSA", -29, 25, 12, 110),
        new("AUSTRALIA", "AUS", -25, 134, 22, 170),
    ];

    public static readonly City[] Cities =
    [
        new("NEW YORK", 40.7, -74), new("WASHINGTON", 38.9, -77), new("CHICAGO", 41.9, -87.6), new("LOS ANGELES", 34, -118.2),
        new("HOUSTON", 29.8, -95.4), new("MIAMI", 25.8, -80.2), new("SEATTLE", 47.6, -122.3), new("TORONTO", 43.7, -79.4),
        new("VANCOUVER", 49.3, -123.1), new("MONTREAL", 45.5, -73.6), new("MEXICO CITY", 19.4, -99.1), new("BOGOTA", 4.7, -74.1),
        new("LIMA", -12, -77), new("RIO DE JANEIRO", -22.9, -43.2), new("SAO PAULO", -23.5, -46.6), new("BRASILIA", -15.8, -47.9),
        new("BUENOS AIRES", -34.6, -58.4), new("SANTIAGO", -33.4, -70.6), new("LONDON", 51.5, -0.1), new("EDINBURGH", 55.9, -3.2),
        new("PARIS", 48.9, 2.35), new("MARSEILLE", 43.3, 5.4), new("BERLIN", 52.5, 13.4), new("MUNICH", 48.1, 11.6),
        new("ROME", 41.9, 12.5), new("MILAN", 45.5, 9.2), new("MADRID", 40.4, -3.7), new("MOSCOW", 55.8, 37.6),
        new("NOVOSIBIRSK", 55, 82.9), new("VLADIVOSTOK", 43.1, 131.9), new("BEIJING", 39.9, 116.4), new("SHANGHAI", 31.2, 121.5),
        new("HONG KONG", 22.3, 114.2), new("TOKYO", 35.7, 139.7), new("OSAKA", 34.7, 135.5), new("DELHI", 28.6, 77.2),
        new("MUMBAI", 19.1, 72.9), new("CAIRO", 30, 31.2), new("LAGOS", 6.5, 3.4), new("NAIROBI", -1.3, 36.8),
        new("JOHANNESBURG", -26.2, 28), new("CAPE TOWN", -33.9, 18.4), new("SYDNEY", -33.9, 151.2), new("PERTH", -31.9, 115.9),
        new("ISTANBUL", 41, 29), new("TEHRAN", 35.7, 51.4), new("SINGAPORE", 1.35, 103.8), new("JAKARTA", -6.2, 106.8),
    ];

    /// <summary>The nation a place belongs to (the nearest whose reach it's in), or -1 for open sea and wild places.</summary>
    public static int NationAt(GeoPoint p)
    {
        int best = -1;
        double bestD = double.MaxValue;
        for (int i = 0; i < Nations.Length; i++)
        {
            double d = Geo.Arc(p, Nations[i].At) / Geo.Deg;
            if (d <= Nations[i].Reach && d / Nations[i].Reach < bestD) { bestD = d / Nations[i].Reach; best = i; }
        }
        return best;
    }

    /// <summary>The city nearest a place.</summary>
    public static City NearestCity(GeoPoint p) => Cities.MinBy(c => Geo.Arc(p, c.At))!;
}
