namespace AVAXCom.Engine;

/// <summary>A small random number generator whose state can be kept (and saved).</summary>
public struct Dice
{
    public ulong State;
    public Dice(int seed) => State = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        State ^= State << 13;
        State ^= State >> 7;
        State ^= State << 17;
        return (State >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => n <= 0 ? 0 : (int)(Next() * n);
    public int Range(int a, int b) => a + Next(b - a + 1);
    public double Range(double a, double b) => a + Next() * (b - a);
    public bool Chance(double p) => Next() < p;
    public T Pick<T>(IReadOnlyList<T> list) => list[Next(list.Count)];
}

public enum Rank { Rookie, Squaddie, Sergeant, Captain, Colonel, Commander }

/// <summary>
/// One of the organisation's soldiers: a name and a nationality, the numbers that grow with experience (time units,
/// health, reactions, firing and throwing accuracy, strength, bravery and, once found, psionic strength and skill), a
/// rank earned by missions and kills, the armour and weapon they go into battle with, and the craft they're assigned to.
/// </summary>
public sealed class Soldier
{
    public int Id;
    public string Name = "";
    public int Nation;
    public bool Female;
    public int Tu, Health, Reactions, Firing, Throwing, Strength, Bravery, PsiStrength, PsiSkill;
    public Rank Rank;
    public int Missions, Kills;
    /// <summary>Days still to recover from wounds; a wounded soldier can't fly.</summary>
    public double WoundDays;
    public ArmourType Armour;
    /// <summary>The weapon chosen for the soldier (None: the best the stores hold).</summary>
    public ItemKind Weapon = ItemKind.None;
    public int BaseId, CraftId = -1;
    public bool PsiTraining;
    /// <summary>Whether a psi lab has measured the soldier's psionic strength yet.</summary>
    public bool PsiKnown;

    public bool Fit => WoundDays <= 0;
    public string RankName => Rules.RankNames[(int)Rank];
    public string Country => Earth.Nations[Nation].Short;

    /// <summary>A new recruit's numbers, rolled as the original rolled them.</summary>
    public static Soldier Recruit(ref Dice d, int id, int baseId)
    {
        int nation = d.Next(Earth.Nations.Length);
        bool female = d.Chance(0.4);
        var names = Names.For(nation);
        return new Soldier
        {
            Id = id, Nation = nation, Female = female, BaseId = baseId,
            Name = d.Pick(female ? names.Women : names.Men) + " " + d.Pick(names.Surnames),
            Tu = d.Range(50, 60), Health = d.Range(25, 40), Reactions = d.Range(30, 60), Firing = d.Range(40, 70),
            Throwing = d.Range(50, 80), Strength = d.Range(24, 44), Bravery = 10 * d.Range(1, 7), PsiStrength = d.Range(0, 100),
        };
    }

    /// <summary>Points toward promotion: a mission is worth one, a kill two.</summary>
    public int Merit => Missions + Kills * 2;

    public static Rank RankFor(int merit) => merit switch { >= 34 => Rank.Colonel, >= 20 => Rank.Captain, >= 10 => Rank.Sergeant, >= 3 => Rank.Squaddie, _ => Rank.Rookie };
}

/// <summary>A soldier who fell, for the memorial.</summary>
public sealed record Fallen(string Name, Rank Rank, int Nation, int Missions, int Kills, DateTime Date, string Where);

/// <summary>Names for recruits, by nation.</summary>
public static class Names
{
    public sealed record Set(string[] Men, string[] Women, string[] Surnames);

    private static readonly Set[] Sets =
    [
        new(["JOHN", "MIKE", "DAVID", "JAMES", "ROBERT", "TYLER"], ["SARAH", "EMILY", "JESSICA", "AMANDA", "RACHEL", "KATE"], ["CARTER", "MILLER", "BROOKS", "HAYES", "WALKER", "REED", "PARKER", "COLE"]),
        new(["LIAM", "NOAH", "ETHAN", "PIERRE", "LUC", "OWEN"], ["CHLOE", "MAYA", "AVERY", "JULIE", "NADIA", "CLAIRE"], ["TREMBLAY", "GAGNON", "ROY", "MACLEOD", "CAMPBELL", "BOUCHARD"]),
        new(["JOAO", "PEDRO", "LUCAS", "RAFAEL", "THIAGO", "DIEGO"], ["ANA", "BEATRIZ", "LARISSA", "CAMILA", "JULIA", "MARIANA"], ["SILVA", "SANTOS", "OLIVEIRA", "SOUZA", "COSTA", "PEREIRA", "ALMEIDA"]),
        new(["MATEO", "SANTIAGO", "FACUNDO", "TOMAS", "NICOLAS", "MARTIN"], ["SOFIA", "VALENTINA", "LUCIA", "MARTINA", "ELENA", "PAULA"], ["GONZALEZ", "RODRIGUEZ", "FERNANDEZ", "LOPEZ", "DIAZ", "ROMERO"]),
        new(["OLIVER", "HARRY", "GEORGE", "ALFIE", "JACK", "ARCHIE"], ["AMELIA", "ISLA", "POPPY", "GRACE", "FREYA", "IMOGEN"], ["SMITH", "JONES", "TAYLOR", "WRIGHT", "EVANS", "HUGHES", "BAKER", "STEWART"]),
        new(["LOUIS", "HUGO", "JULES", "ARTHUR", "THEO", "MAXIME"], ["MANON", "CAMILLE", "LEA", "INES", "ZOE", "ELISE"], ["MARTIN", "BERNARD", "DUBOIS", "MOREAU", "LAURENT", "GIRARD", "FONTAINE"]),
        new(["LUKAS", "FELIX", "JONAS", "MAXIMILIAN", "PAUL", "STEFAN"], ["LENA", "HANNAH", "MIA", "ANNA", "KATRIN", "SOPHIE"], ["MULLER", "SCHMIDT", "WEBER", "WAGNER", "BECKER", "HOFFMANN", "KOCH"]),
        new(["MARCO", "LUCA", "MATTEO", "ANDREA", "DAVIDE", "PAOLO"], ["GIULIA", "CHIARA", "FRANCESCA", "SARA", "ALESSIA", "MARTA"], ["ROSSI", "RUSSO", "FERRARI", "ESPOSITO", "BIANCHI", "ROMANO", "COLOMBO"]),
        new(["IVAN", "DMITRI", "SERGEI", "ALEXEI", "MIKHAIL", "YURI"], ["OLGA", "NATALIA", "IRINA", "TATIANA", "SVETLANA", "ANYA"], ["IVANOV", "PETROV", "SOKOLOV", "VOLKOV", "MOROZOV", "ORLOV", "KOZLOV"]),
        new(["WEI", "JUN", "HAO", "LEI", "MING", "TAO"], ["LI NA", "XIU", "MEI", "YAN", "JING", "LAN"], ["WANG", "LI", "ZHANG", "LIU", "CHEN", "YANG", "HUANG", "ZHAO"]),
        new(["HIROSHI", "KENJI", "TAKESHI", "RYO", "DAISUKE", "SHO"], ["YUKI", "HARUKA", "AYAKO", "MISAKI", "AOI", "REI"], ["SATO", "SUZUKI", "TAKAHASHI", "TANAKA", "WATANABE", "ITO", "KATO"]),
        new(["ARJUN", "RAHUL", "VIKRAM", "SANJAY", "ROHAN", "KABIR"], ["PRIYA", "ANANYA", "DIVYA", "MEERA", "KAVYA", "NEHA"], ["SHARMA", "PATEL", "SINGH", "GUPTA", "REDDY", "KUMAR", "IYER"]),
        new(["AHMED", "OMAR", "KARIM", "YOUSSEF", "TAREK", "HASSAN"], ["NOUR", "MONA", "SALMA", "LAILA", "AMIRA", "DINA"], ["HASSAN", "MAHMOUD", "SAID", "FARAG", "NASSER", "ABDEL"]),
        new(["CHIDI", "EMEKA", "TUNDE", "OBINNA", "SEGUN", "KUNLE"], ["NGOZI", "ADAEZE", "FUNMI", "CHIOMA", "YEWANDE", "AMAKA"], ["OKAFOR", "ADEBAYO", "EZE", "OKONKWO", "BELLO", "NWOSU"]),
        new(["THABO", "SIPHO", "PIETER", "JOHAN", "LUNGA", "KAGISO"], ["LERATO", "ZANELE", "ANNA", "NOMSA", "THANDI", "ELSA"], ["NKOSI", "DLAMINI", "VAN WYK", "BOTHA", "MOKOENA", "NAIDOO"]),
        new(["JACK", "LACHLAN", "COOPER", "BRODIE", "LIAM", "HAMISH"], ["CHARLOTTE", "MIA", "RUBY", "MATILDA", "ZARA", "TAHLIA"], ["WILLIAMS", "BROWN", "WILSON", "THOMPSON", "RYAN", "KELLY"]),
    ];

    public static Set For(int nation) => Sets[Math.Clamp(nation, 0, Sets.Length - 1)];
}
