namespace AVAJetSetWilly.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>Willy: where his feet are, which way he faces, and whether he's in the air.</summary>
public sealed class Willy
{
    public const double HalfWidth = 9, Height = 56, Speed = 150, JumpSpeed = 436, Gravity = 1322;
    /// <summary>A fall further than this (from the top of a jump, or the edge he walked off) is the end of him.</summary>
    public const double MaxFall = 4 * Field.Tile + 8;
    public double X, Y, Vy, FallFrom, Walk;
    public int Facing = 1, Jdx;
    public bool Airborne, Dead, JumpPrev;
    public Willy Copy() => (Willy)MemberwiseClone();
}

/// <summary>A guardian on its beat.</summary>
public sealed class Guardian
{
    public GuardianDef Def;
    public double X, Y;
    public int Dir = 1;
    public Guardian Copy() => (Guardian)MemberwiseClone();
    public (double W, double H) Half => Def.Vertical ? (13, 13) : (13, 20);
}

/// <summary>
/// Everything in the house that moves or changes, kept together so it can be copied whole: the room Willy is in (its
/// squares as they are now, its guardians), the items taken, and the time.
/// </summary>
public sealed class World
{
    public Mansion House = null!;
    public int Room;
    public Tile[,] Grid = new Tile[Field.Cols, Field.Rows];
    /// <summary>How much each crumbling square has left (in seconds of standing on).</summary>
    public double[,] Crumble = new double[Field.Cols, Field.Rows];
    public Willy Willy = new();
    public List<Guardian> Guardians = [];
    public bool[] Taken = [];
    /// <summary>Minutes past seven in the morning; at midnight the game is over.</summary>
    public double Minutes, Clock;
    public int Score;
    /// <summary>Into bed: the game is won.</summary>
    public bool Home;
    public string Cause = "";
    public List<Sfx> Heard = [];
    /// <summary>Where Willy comes back after a death: the room, and where he first stood in it.</summary>
    public int SafeRoom;
    public double SafeX, SafeY;
    public int SafeFacing = 1;
    public bool SafeSet;
    /// <summary>A world used only to try moves out: items are left alone, and Willy never leaves the room.</summary>
    public bool Probe;
    /// <summary>(In a probe) he'd have left the room.</summary>
    public bool Exited;

    public RoomDef Def => House.Rooms[Room];
    public int ItemsLeft => Taken.Count(t => !t);
    public int ItemsTaken => Taken.Count(t => t);
    public bool AllTaken => Taken.All(t => t);
    public bool MariaThere => Def.Bedroom && !AllTaken;

    public World Copy() => new()
    {
        House = House, Room = Room, Grid = (Tile[,])Grid.Clone(), Crumble = (double[,])Crumble.Clone(), Willy = Willy.Copy(),
        Guardians = Guardians.Select(g => g.Copy()).ToList(), Taken = (bool[])Taken.Clone(),
        Minutes = Minutes, Clock = Clock, Score = Score, Home = Home, Cause = Cause,
        SafeRoom = SafeRoom, SafeX = SafeX, SafeY = SafeY, SafeFacing = SafeFacing, SafeSet = SafeSet,
    };

    public Tile At(int c, int r) => c < 0 || c >= Field.Cols || r < 0 || r >= Field.Rows ? Def.At(c, r) : Grid[c, r];
    public Tile AtPoint(double x, double y) => At((int)Math.Floor(x / Field.Tile), (int)Math.Floor((y - Field.Top) / Field.Tile));
}
