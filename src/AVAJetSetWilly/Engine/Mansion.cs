namespace AVAJetSetWilly.Engine;

/// <summary>Left or right, and jump.</summary>
public readonly record struct Controls(int Dx = 0, bool Jump = false);

/// <summary>The screen: a room, 32 squares by 16, then its name, the items, the clock and the lives below.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768, Top = 40;
    public const double Tile = 32;
    public const int Cols = 32, Rows = 16;
    public const double Bottom = Top + Rows * Tile;
    /// <summary>The house: six rooms across, four floors (the roof, the bedrooms, the ground floor, the cellars).</summary>
    public const int HouseCols = 6, Floors = 4;
    /// <summary>The rows of a doorway in a side wall.</summary>
    public const int DoorTop = 12, DoorBottom = 14;
}

/// <summary>What a square of a room is.</summary>
public enum Tile : byte
{
    Empty,
    /// <summary>A ledge: stood on from above, jumped up through from below.</summary>
    Floor,
    /// <summary>Brick: nothing passes.</summary>
    Wall,
    /// <summary>A ledge that crumbles away underfoot.</summary>
    Crumble,
    /// <summary>A conveyor belt running left (or right): it carries Willy along.</summary>
    ConveyorLeft,
    ConveyorRight,
    /// <summary>Something deadly to touch.</summary>
    Nasty,
}

/// <summary>A guardian: patrolling to and fro along a ledge, or up and down.</summary>
public readonly record struct GuardianDef(bool Vertical, double X, double Y, double Min, double Max, double Speed, int Look);

/// <summary>How a room looks: its name, its colours, and what its nasties and guardians are.</summary>
public sealed record Theme(string Name, uint Back, uint Floor, uint Wall, uint Accent, int Nasty, int Guardian);

/// <summary>An item to collect: which room, and the square it hangs in.</summary>
public readonly record struct ItemDef(int Room, int Col, int Row);

/// <summary>
/// A room: its squares, its way out on each side (doorways in the side walls at floor level, and a gap in the floor
/// or ceiling where a room above or below joins it), its items and guardians.
/// </summary>
public sealed class RoomDef
{
    public int Index, HX, Floor;
    public Theme Theme = MansionBook.Themes[0];
    public Tile[,] Grid = new Tile[Field.Cols, Field.Rows];
    /// <summary>The rooms beyond each side (-1 for none).</summary>
    public int Left = -1, Right = -1, Up = -1, Down = -1;
    /// <summary>
    /// Where a gap joins this room to the one above (or below): the column of the open square to drop through. Ledge
    /// runs either side of it in place of brick (to land on coming up, and so there's headroom below), and the room
    /// below has a ledge two squares down from it to jump up from.
    /// </summary>
    public int UpCol = -1, DownCol = -1;
    /// <summary>Indices into the mansion's items.</summary>
    public List<int> Items = [];
    public List<GuardianDef> Guardians = [];
    /// <summary>The master bedroom: Maria, and the bed.</summary>
    public bool Bedroom;

    public Tile At(int c, int r)
    {
        if (c < 0) return Left >= 0 && r is >= Field.DoorTop and <= Field.DoorBottom ? Tile.Empty : Tile.Wall;
        if (c >= Field.Cols) return Right >= 0 && r is >= Field.DoorTop and <= Field.DoorBottom ? Tile.Empty : Tile.Wall;
        if (r < 0) return UpCol >= 0 && c >= UpCol - 1 && c <= UpCol + 3 ? Tile.Empty : Tile.Wall;
        if (r >= Field.Rows) return DownCol >= 0 && c >= DownCol - 1 && c <= DownCol + 3 ? Tile.Empty : Tile.Wall;
        return Grid[c, r];
    }
}

/// <summary>The whole house: its rooms, its items, where Willy starts, and where his bed is.</summary>
public sealed class Mansion
{
    public RoomDef[] Rooms = [];
    public List<ItemDef> Items = [];
    public int StartRoom;
    public (int Col, int Row) Start;
    public int Bedroom;
    /// <summary>Where Maria stands, barring the way to bed until every item is collected.</summary>
    public double MariaX;
    /// <summary>The bed's columns.</summary>
    public int BedCol0, BedCol1;

    public RoomDef Room(int floor, int hx) => Rooms[floor * Field.HouseCols + hx];
}

/// <summary>The mansion: each room built from its own seed, then proved to be passable before it's kept.</summary>
public static class MansionBook
{
    public static readonly Theme[] Themes =
    [
        // The roof.
        new("THE WEATHERVANE", 0xFF101838, 0xFFB0B0C0, 0xFF6A5040, 0xFFFFE040, 1, 6),
        new("CHIMNEY POTS", 0xFF141030, 0xFFC08060, 0xFF8A3A2A, 0xFFFFA040, 2, 7),
        new("THE WATER TANK", 0xFF001028, 0xFF60A0E0, 0xFF405070, 0xFF80FFFF, 1, 3),
        new("THE BELFRY", 0xFF100818, 0xFFC0A060, 0xFF605048, 0xFFFFFF80, 0, 8),
        new("THE ATTIC", 0xFF180C04, 0xFFA07040, 0xFF6A4A2A, 0xFFE0C080, 0, 3),
        new("THE TELESCOPE", 0xFF000818, 0xFFA0A0D0, 0xFF404868, 0xFFFFFFFF, 1, 4),
        // The bedrooms.
        new("THE NURSERY", 0xFF180818, 0xFFFF90C0, 0xFF8A4A8A, 0xFFFFE0FF, 0, 9),
        new("THE LANDING", 0xFF100800, 0xFFC09060, 0xFF7A4A2A, 0xFFFFE080, 2, 2),
        new("THE GUEST ROOM", 0xFF001010, 0xFF60C0A0, 0xFF306060, 0xFFC0FFE0, 0, 5),
        new("THE LINEN CUPBOARD", 0xFF0C0C14, 0xFFE0E0F0, 0xFF707088, 0xFFFFC0C0, 1, 2),
        new("THE BATHROOM", 0xFF001018, 0xFF80E0FF, 0xFF4080A0, 0xFFFFFFFF, 1, 10),
        new("THE MASTER BEDROOM", 0xFF180808, 0xFFE0A0A0, 0xFF8A3A3A, 0xFFFFE0A0, 0, 0),
        // The ground floor.
        new("THE BALLROOM", 0xFF100010, 0xFFE0C0FF, 0xFF6A4A8A, 0xFFFFC040, 2, 4),
        new("THE LIBRARY", 0xFF0C0800, 0xFFC08040, 0xFF5A3A1A, 0xFFFFE040, 2, 8),
        new("THE KITCHEN", 0xFF101010, 0xFFE0E0E0, 0xFF606070, 0xFFFF6040, 2, 1),
        new("THE FRONT HALL", 0xFF100808, 0xFFD0B080, 0xFF6A5A4A, 0xFFFFFFFF, 0, 6),
        new("THE CONSERVATORY", 0xFF001000, 0xFF60C040, 0xFF406020, 0xFFFF80FF, 0, 5),
        new("THE BILLIARD ROOM", 0xFF001408, 0xFF40A060, 0xFF2A5A3A, 0xFFFFFFFF, 1, 4),
        // The cellars.
        new("THE WINE CELLAR", 0xFF080004, 0xFFA04060, 0xFF4A2A2A, 0xFFFF80A0, 0, 3),
        new("THE BOILER ROOM", 0xFF180400, 0xFFFF6020, 0xFF802010, 0xFFFFFF40, 2, 0),
        new("THE COAL HOLE", 0xFF000000, 0xFF808080, 0xFF404040, 0xFFFFE040, 0, 3),
        new("THE LAUNDRY", 0xFF000814, 0xFF80C0FF, 0xFF4060A0, 0xFFFFFFFF, 1, 1),
        new("THE WORKSHOP", 0xFF080808, 0xFFC0A060, 0xFF5A5040, 0xFFFF6040, 2, 0),
        new("THE OLD WELL", 0xFF000810, 0xFF4080A0, 0xFF203A50, 0xFF80FFFF, 1, 7),
    ];

    /// <summary>The ledges' rows, every two squares up from the floor (not so near the ceiling that there is no room to jump).</summary>
    private static readonly int[] Tiers = [13, 11, 9, 7, 5];

    /// <summary>The gaps between floors: (the upper floor, the house column).</summary>
    private static readonly (int Floor, int HX)[] Links = [(0, 0), (0, 3), (1, 2), (1, 4), (2, 1), (2, 5)];

    public const int StartFloor = 1, StartHX = 4, BedFloor = 1, BedHX = 5;

    public static readonly Mansion House = Build();

    private static Mansion Build()
    {
        var h = new Mansion();
        var d = new Dice(1984);
        h.Rooms = new RoomDef[Field.Floors * Field.HouseCols];
        for (int f = 0; f < Field.Floors; f++)
        for (int x = 0; x < Field.HouseCols; x++)
        {
            int i = f * Field.HouseCols + x;
            h.Rooms[i] = new RoomDef
            {
                Index = i, HX = x, Floor = f, Theme = Themes[i],
                Left = x > 0 ? i - 1 : -1, Right = x < Field.HouseCols - 1 ? i + 1 : -1,
            };
        }
        foreach (var (f, x) in Links)
        {
            var up = h.Room(f, x);
            var down = h.Room(f + 1, x);
            int col = 6 + d.Next(19);
            up.Down = down.Index;
            up.DownCol = col;
            down.Up = up.Index;
            down.UpCol = col;
        }
        h.StartRoom = h.Room(StartFloor, StartHX).Index;
        // In the middle of the bathroom floor, clear of any gap in it.
        int gap = h.Rooms[h.StartRoom].DownCol;
        h.Start = (gap >= 0 && Math.Abs(gap + 1 - 16) < 6 ? (gap < 16 ? 25 : 7) : 16, 14);
        h.Bedroom = h.Room(BedFloor, BedHX).Index;
        foreach (var r in h.Rooms)
        {
            if (r.Index == h.Bedroom) { BuildBedroom(h, r); continue; }
            for (int attempt = 0; ; attempt++)
                if (TryRoom(h, r, 5000 + r.Index * 131 + attempt * 7919)) break;
        }
        return h;
    }

    private static void Frame(RoomDef s)
    {
        for (int c = 0; c < Field.Cols; c++)
        for (int r = 0; r < Field.Rows; r++)
            s.Grid[c, r] = c == 0 || c == Field.Cols - 1 || r == 0 || r == Field.Rows - 1 ? Tile.Wall : Tile.Empty;
        for (int r = Field.DoorTop; r <= Field.DoorBottom; r++)
        {
            if (s.Left >= 0) s.Grid[0, r] = Tile.Empty;
            if (s.Right >= 0) s.Grid[Field.Cols - 1, r] = Tile.Empty;
        }
    }

    /// <summary>The gaps to the rooms above and below, and the ledges that serve them.</summary>
    private static void Gaps(RoomDef s)
    {
        if (s.UpCol >= 0)
        {
            int a = s.UpCol;
            for (int c = a - 1; c <= a + 3; c++) s.Grid[c, 0] = c == a ? Tile.Empty : Tile.Floor;
            // Under the gap, a ledge to land on coming down and to jump from going up; two squares under that, a
            // ledge to jump up onto it from (anywhere else, the ceiling is too low to jump at all).
            for (int c = Math.Max(1, a - 3); c <= Math.Min(Field.Cols - 2, a + 5); c++)
            {
                s.Grid[c, 1] = Tile.Empty;
                s.Grid[c, 2] = c >= a && c <= a + 2 ? Tile.Floor : Tile.Empty;
                s.Grid[c, 3] = Tile.Empty;
                s.Grid[c, 4] = c >= a - 2 && c <= a + 4 ? Tile.Floor : Tile.Empty;
            }
            // And a step down (and up) on either side.
            for (int c = Math.Max(1, a - 6); c <= Math.Min(Field.Cols - 2, a + 8); c++)
                if (c <= a - 3 || c >= a + 5) s.Grid[c, 5] = Tile.Floor;
        }
        if (s.DownCol >= 0)
        {
            int b = s.DownCol;
            for (int c = b - 1; c <= b + 3; c++) s.Grid[c, 15] = c == b ? Tile.Empty : Tile.Floor;
            for (int c = b - 2; c <= b + 4; c++) { s.Grid[c, 14] = Tile.Empty; s.Grid[c, 13] = Tile.Empty; }
        }
    }

    /// <summary>The standing places that matter in a room: its ways in and out, and where Willy starts.</summary>
    public static List<Spot> Ways(Mansion h, RoomDef s)
    {
        var ways = new List<Spot>();
        if (s.Left >= 0) ways.Add(new Spot(1, 14));
        if (s.Right >= 0) ways.Add(new Spot(Field.Cols - 2, 14));
        if (s.UpCol >= 0) { ways.Add(new Spot(s.UpCol, 1)); ways.Add(new Spot(s.UpCol + 1, 1)); }
        if (s.DownCol >= 0) ways.Add(new Spot(s.DownCol + 1, 14));
        if (s.Index == h.StartRoom) ways.Add(new Spot(h.Start.Col, h.Start.Row));
        return ways;
    }

    private static bool TryRoom(Mansion h, RoomDef s, int seed)
    {
        var d = new Dice(seed);
        s.Items.Clear();
        s.Guardians.Clear();
        h.Items.RemoveAll(it => it.Room == s.Index);
        Frame(s);
        int level = s.Floor == 0 ? 2 : s.Floor;
        // Ledges along every tier, with gaps.
        var segments = new List<(int Row, int C0, int C1)>();
        foreach (int row in Tiers)
        {
            int col = 1 + d.Next(3);
            while (col < Field.Cols - 2)
            {
                int len = 3 + d.Next(6);
                int c1 = Math.Min(Field.Cols - 2, col + len - 1);
                for (int c = col; c <= c1; c++) s.Grid[c, row] = Tile.Floor;
                segments.Add((row, col, c1));
                col = c1 + 2 + d.Next(3);
            }
        }
        bool Clear(int c) => c > 3 && c < Field.Cols - 4 && (s.DownCol < 0 || c < s.DownCol - 2 || c > s.DownCol + 4) && (s.Index != h.StartRoom || Math.Abs(c - h.Start.Col) > 2);
        // A brick block or two on the floor.
        for (int k = 0; k < 1 + d.Next(2); k++)
        {
            int c = 5 + d.Next(Field.Cols - 10);
            if (Clear(c)) s.Grid[c, 14] = Tile.Wall;
        }
        // Conveyors and crumbling ledges.
        var ledges = segments.Where(g => g.C1 - g.C0 >= 3 && g.Row > 3).ToList();
        for (int k = 0; k < d.Next(2 + level) / 2 && ledges.Count > 0; k++)
        {
            var g = ledges[d.Next(ledges.Count)];
            ledges.Remove(g);
            var t = d.Next(2) == 0 ? Tile.ConveyorLeft : Tile.ConveyorRight;
            for (int c = g.C0; c <= g.C1; c++) s.Grid[c, g.Row] = t;
        }
        for (int k = 0; k < 1 + d.Next(level) && ledges.Count > 0; k++)
        {
            var g = ledges[d.Next(ledges.Count)];
            ledges.Remove(g);
            int a = g.C0 + d.Next(Math.Max(1, g.C1 - g.C0 - 1));
            for (int c = a; c <= Math.Min(g.C1, a + 2); c++) s.Grid[c, g.Row] = Tile.Crumble;
        }
        Gaps(s);
        // Nasties standing on ledges and the floor.
        for (int k = 0; k < 2 + level; k++)
        {
            int c = 2 + d.Next(Field.Cols - 4), r = Tiers[d.Next(Tiers.Length)] - 1;
            if (d.Next(3) == 0) r = 14;
            if (r == 14 && !Clear(c)) continue;
            if (s.UpCol >= 0 && c >= s.UpCol - 3 && c <= s.UpCol + 5 && r <= 5) continue;
            if (CavernRules.Standable(s.At(c, r + 1)) && s.At(c, r) == Tile.Empty) s.Grid[c, r] = Tile.Nasty;
        }
        // Every way in and out must lead to every other.
        var graph = Graph.Build(h, s);
        var ways = Ways(h, s);
        if (ways.Any(w => !graph.Edges.ContainsKey(w))) return false;
        var from = graph.Reach(ways[0]);
        from.RemoveWhere(n => !graph.Edges.ContainsKey(n));
        if (!ways.All(from.Contains)) return false;
        // Items: at head height at places reached, spread out.
        var spots = from.Where(n => graph.Edges.ContainsKey(n) && !ways.Contains(n) && n.Row >= 2).ToList();
        int count = 2 + d.Next(2);
        var items = new List<(int Col, int Row)>();
        for (int tries = 0; items.Count < count && tries < 400 && spots.Count > 0; tries++)
        {
            var n = spots[d.Next(spots.Count)];
            var k = (n.Col, n.Row - 1);
            if (items.Any(o => Math.Abs(o.Col - k.Item1) + Math.Abs(o.Row - k.Item2) < 7)) continue;
            if (s.At(k.Item1, k.Item2) != Tile.Empty) continue;
            items.Add(k);
        }
        if (items.Count < 2) return false;
        // No dead ends: from every way and item, every other can still be reached.
        var targets = items.Select(k => new Spot(k.Col, k.Row + 1)).Concat(ways).Distinct().ToList();
        foreach (var t in targets)
        {
            var on = graph.Reach(t);
            if (!targets.All(on.Contains)) return false;
        }
        // Guardians: along ledges (not by a way in), and up and down open shafts.
        int guards = 1 + d.Next(2) + (level >= 2 ? 1 : 0);
        var avoid = new HashSet<Spot>();
        var patrols = segments.Where(g => g.C1 - g.C0 >= 4 && g.Row > 3 && !ways.Any(w => w.Row == g.Row - 1 && w.Col >= g.C0 - 1 && w.Col <= g.C1 + 1)).ToList();
        for (int k = 0; k < guards && patrols.Count > 0; k++)
        {
            var g = patrols[d.Next(patrols.Count)];
            patrols.Remove(g);
            if (Enumerable.Range(g.C0, g.C1 - g.C0 + 1).Any(c => s.Grid[c, g.Row] is not (Tile.Floor or Tile.Crumble or Tile.ConveyorLeft or Tile.ConveyorRight))) { k--; continue; }
            double y = Field.Top + g.Row * Field.Tile;
            double speed = 50 + d.Next(40) + level * 5;
            var beat = new HashSet<Spot>(avoid);
            for (int c = g.C0; c <= g.C1; c++) beat.Add(new Spot(c, g.Row - 1));
            var without = Graph.Build(h, s, avoid: beat);
            if (!targets.All(t => targets.All(without.Reach(t).Contains))) { k--; continue; }
            avoid = beat;
            s.Guardians.Add(new GuardianDef(false, (g.C0 + 0.5) * Field.Tile, y, (g.C0 + 0.5) * Field.Tile, (g.C1 + 0.5) * Field.Tile, speed, s.Theme.Guardian));
        }
        if (d.Next(3) > 0)
        {
            for (int tries = 0; tries < 20; tries++)
            {
                int c = 6 + d.Next(Field.Cols - 12);
                if (s.UpCol >= 0 && Math.Abs(c - s.UpCol - 1) < 5 || s.DownCol >= 0 && Math.Abs(c - s.DownCol - 1) < 5) continue;
                if (s.Index == h.StartRoom && Math.Abs(c - h.Start.Col) < 5) continue;
                s.Guardians.Add(new GuardianDef(true, (c + 0.5) * Field.Tile, Field.Top + 5 * Field.Tile, Field.Top + 4 * Field.Tile, Field.Top + 11 * Field.Tile, 50 + level * 6 + d.Next(20), (s.Theme.Guardian + 3) % 11));
                break;
            }
        }
        foreach (var it in items)
        {
            s.Items.Add(h.Items.Count);
            h.Items.Add(new ItemDef(s.Index, it.Col, it.Row));
        }
        return true;
    }

    /// <summary>The master bedroom: a plain room, Maria by the door to the bed, and the bed.</summary>
    private static void BuildBedroom(Mansion h, RoomDef s)
    {
        s.Bedroom = true;
        Frame(s);
        for (int c = 4; c <= 9; c++) s.Grid[c, 11] = Tile.Floor;
        for (int c = 12; c <= 16; c++) s.Grid[c, 9] = Tile.Floor;
        h.MariaX = 20.5 * Field.Tile;
        h.BedCol0 = 25;
        h.BedCol1 = 29;
    }
}

/// <summary>What can be stood on.</summary>
public static class CavernRules
{
    public static bool Standable(Tile t) => t is Tile.Floor or Tile.Wall or Tile.Crumble or Tile.ConveyorLeft or Tile.ConveyorRight;
}
