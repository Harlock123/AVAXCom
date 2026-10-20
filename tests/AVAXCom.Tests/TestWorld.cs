using AVAXCom.Engine;

namespace AVAXCom.Tests;

/// <summary>Little worlds for the tests: games kept in temporary folders, flat test fields, and squads to put on them.</summary>
internal static class TestWorld
{
    public static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaxcom-tests-" + Guid.NewGuid().ToString("N"));

    public static GameEngine NewGame(int seed = 3)
    {
        string f = TempFolder();
        return new GameEngine(HighScoreTable.Load(f), f, seed: seed) { Silent = true };
    }

    /// <summary>
    /// A flat empty field of grass, a few storeys high, in daylight, with no transport — and (unless asked not to) an
    /// alien guard in the far corner looking off the map, so the battle isn't over before it starts.
    /// </summary>
    public static Battle Flat(int size = 30, bool night = false, int seed = 7, bool keep = true)
    {
        var b = new Battle(size, size, seed) { Night = night, Instant = true };
        b.Mission = new Mission { Name = "TEST", Terrain = Terrain.Farmland, Night = night, Seed = seed };
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++) b.Floor[x, y, 0] = Piece.Grass;
        if (keep)
        {
            var k = Tac.MakeAlien(b, Race.Pallid, AlienRank.Soldier, 0);
            k.Facing = 0;
            k.Guard = true;
            k.Right = null;
            Tac.Place(b, k, new Cell(size - 1, 0, 0));
        }
        return b;
    }

    /// <summary>A soldier with known numbers (and a rifle, loaded), standing at a tile.</summary>
    public static BUnit Soldier(Battle b, int x, int y, int z = 0, int facing = 2, int tu = 60, int firing = 60, int reactions = 50, ItemKind weapon = ItemKind.Rifle)
    {
        var s = new Soldier { Id = b.NextId + 500, Name = $"TESTER {b.NextId}", Tu = tu, Health = 40, Reactions = reactions, Firing = firing, Throwing = 70, Strength = 40, Bravery = 50 };
        var u = Tac.FromSoldier(b, s);
        u.Facing = facing;
        u.Right = weapon == ItemKind.None ? null : Item.Make(weapon);
        Tac.Place(b, u, new Cell(x, y, z));
        return u;
    }

    public static BUnit Alien(Battle b, int x, int y, int z = 0, Race race = Race.Pallid, int facing = 6, AlienRank rank = AlienRank.Soldier)
    {
        var u = Tac.MakeAlien(b, race, rank, 0);
        u.Facing = facing;
        u.Belt.Clear();
        Tac.Place(b, u, new Cell(x, y, z));
        return u;
    }

    /// <summary>A brick house: walls round a rectangle on the ground floor and the first, a floor between, a roof, stairs.</summary>
    public static void House(Battle b, int x0, int y0, int w, int h, int storeys = 2)
    {
        for (int z = 0; z < storeys; z++)
        {
            for (int y = y0; y < y0 + h; y++)
            {
                b.West[x0, y, z] = Piece.Brick;
                b.West[x0 + w, y, z] = Piece.Brick;
                for (int x = x0; x < x0 + w; x++) b.Floor[x, y, z] = z == 0 ? Piece.TileFloor : Piece.WoodFloor;
            }
            for (int x = x0; x < x0 + w; x++)
            {
                b.North[x, y0, z] = Piece.Brick;
                b.North[x, y0 + h, z] = Piece.Brick;
            }
        }
        for (int y = y0; y < y0 + h; y++)
        for (int x = x0; x < x0 + w; x++) b.Floor[x, y, storeys] = Piece.Roof;
        for (int z = 0; z < storeys - 1; z++) b.Obj[x0, y0, z] = Piece.Stairs;
    }

    /// <summary>Runs the battle's animations out.</summary>
    public static void Settle(Battle b) => Tac.Settle(b);
}
