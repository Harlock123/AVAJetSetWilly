using Avalonia.Input;
using AVAJetSetWilly.Engine;

namespace AVAJetSetWilly.Tests;

public class GameplayTests
{
    private const double T = Field.Tile;
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avajetsetwilly-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly double Ground = Field.Top + 15 * T;

    private static RoomDef Bare(int index, Action<RoomDef>? build = null)
    {
        var r = new RoomDef { Index = index, Theme = MansionBook.Themes[index] };
        for (int x = 0; x < Field.Cols; x++)
        for (int y = 0; y < Field.Rows; y++)
            r.Grid[x, y] = x == 0 || x == Field.Cols - 1 || y == 0 || y == Field.Rows - 1 ? Tile.Wall : Tile.Empty;
        build?.Invoke(r);
        return r;
    }

    /// <summary>
    /// A little house: two rooms side by side with a doorway between, and a third under the first, joined by a gap at
    /// column 10 (ledge either side). One item in the second room.
    /// </summary>
    private static Mansion Little(Action<RoomDef>? first = null)
    {
        var a = Bare(0, first);
        var b = Bare(1);
        var c = Bare(2);
        a.Right = 1;
        b.Left = 0;
        for (int row = Field.DoorTop; row <= Field.DoorBottom; row++) { a.Grid[Field.Cols - 1, row] = Tile.Empty; b.Grid[0, row] = Tile.Empty; }
        a.Down = 2;
        a.DownCol = 10;
        c.Up = 0;
        c.UpCol = 10;
        for (int x = 9; x <= 13; x++) a.Grid[x, 15] = c.Grid[x, 0] = x == 10 ? Tile.Empty : Tile.Floor;
        for (int x = 10; x <= 12; x++) c.Grid[x, 2] = Tile.Floor;
        for (int x = 8; x <= 14; x++) c.Grid[x, 4] = Tile.Floor;
        var h = new Mansion { Rooms = [a, b, c], StartRoom = 0, Start = (3, 14), Bedroom = -1 };
        h.Items.Add(new ItemDef(1, 20, 14));
        b.Items.Add(0);
        return h;
    }

    private static World In(Mansion h, double col = 3.5, int row = 14, int room = 0)
    {
        var w = new World();
        Sim.Start(w, h);
        w.Willy.X = col * T;
        w.Willy.Y = Field.Top + (row + 1) * T;
        if (room != 0) Sim.Enter(w, room);
        return w;
    }

    private static World In(RoomDef r, double col = 3.5, int row = 14) => In(new Mansion { Rooms = [r], Start = (3, 14), Bedroom = -1 }, col, row);

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    private static readonly Controls Right = new(1), Left = new(-1);

    // ───────────────────────────── walking and jumping ─────────────────────────────

    [Fact]
    public void He_walks_at_a_steady_pace()
    {
        var w = In(Bare(0), 5.5);
        Run(w, 60, Right);
        Assert.Equal(5.5 * T + Willy.Speed, w.Willy.X, 3);
        Run(w, 60, Left);
        Assert.Equal(5.5 * T, w.Willy.X, 3);
        Assert.Equal(-1, w.Willy.Facing);
    }

    [Fact]
    public void A_jump_is_a_fixed_arc_that_pushing_the_other_way_does_not_change()
    {
        var w = In(Bare(0), 5.5);
        Sim.Step(w, new Controls(1, true));
        double top = w.Willy.Y;
        for (int i = 0; i < 200 && w.Willy.Airborne; i++)
        {
            Sim.Step(w, Left);
            top = Math.Min(top, w.Willy.Y);
        }
        Assert.Equal(Ground, w.Willy.Y, 3);
        Assert.InRange(Ground - top, 2 * T, 2.5 * T);
        Assert.InRange(w.Willy.X - 5.5 * T, 2.8 * T, 3.4 * T);
    }

    [Fact]
    public void He_jumps_up_through_a_ledge_and_lands_on_it()
    {
        var w = In(Bare(0, r => { for (int x = 4; x <= 9; x++) r.Grid[x, 13] = Tile.Floor; }), 6.5);
        Sim.Step(w, new Controls(0, true));
        Run(w, 90);
        Assert.False(w.Willy.Airborne);
        Assert.Equal(Field.Top + 13 * T, w.Willy.Y, 3);
    }

    [Fact]
    public void A_short_drop_is_survived_and_a_long_one_is_not()
    {
        var low = In(Bare(0, r => { for (int x = 1; x <= 6; x++) r.Grid[x, 11] = Tile.Floor; }), 5.5, 10);
        Run(low, 90, Right);
        Assert.False(low.Willy.Dead);
        var high = In(Bare(0, r => { for (int x = 1; x <= 6; x++) r.Grid[x, 9] = Tile.Floor; }), 5.5, 8);
        Run(high, 120, Right);
        Assert.True(high.Willy.Dead);
        Assert.Equal("A FALL", high.Cause);
    }

    [Fact]
    public void A_conveyor_carries_him_and_a_crumbling_ledge_gives_way()
    {
        var w = In(Bare(0, r => { for (int x = 3; x <= 20; x++) r.Grid[x, 14] = Tile.ConveyorRight; }), 6.5, 13);
        Run(w, 30);
        Assert.Equal(6.5 * T + Willy.Speed / 2, w.Willy.X, 3);
        var c = In(Bare(0, r => { for (int x = 4; x <= 9; x++) r.Grid[x, 13] = Tile.Floor; r.Grid[6, 13] = Tile.Crumble; }), 6.5, 12);
        Run(c, 80);
        Assert.Equal(Tile.Empty, c.Grid[6, 13]);
        Assert.Equal(Ground, c.Willy.Y, 3);
    }

    [Fact]
    public void Nasties_and_guardians_are_the_end_of_him()
    {
        var w = In(Bare(0, r => r.Grid[8, 14] = Tile.Nasty));
        Run(w, 120, Right);
        Assert.Equal("A NASTY", w.Cause);
        var g = In(Bare(0, r => r.Guardians.Add(new GuardianDef(false, 12 * T, Ground, 12 * T, 20 * T, 80, 0))));
        Run(g, 300, Right);
        Assert.Equal("A GUARDIAN", g.Cause);
    }

    // ───────────────────────────── the house ─────────────────────────────

    [Fact]
    public void Through_a_doorway_into_the_next_room_and_back()
    {
        var w = In(Little(), 28.5);
        Run(w, 60, Right);
        Assert.Equal(1, w.Room);
        Assert.InRange(w.Willy.X, 0, 5 * T);
        Assert.Equal(Ground, w.Willy.Y, 3);
        Run(w, 60, Left);
        Assert.Equal(0, w.Room);
        Assert.InRange(w.Willy.X, Field.Width - 5 * T, Field.Width);
    }

    [Fact]
    public void A_wall_with_no_doorway_keeps_him_in()
    {
        var w = In(Little(), 4.5);
        Run(w, 120, Left);
        Assert.Equal(0, w.Room);
        Assert.InRange(w.Willy.X, T, 2 * T);
    }

    [Fact]
    public void Down_through_the_gap_and_up_again()
    {
        var w = In(Little(), 11.5);
        Run(w, 40, Left);
        Run(w, 60);
        Assert.Equal(2, w.Room);
        Assert.False(w.Willy.Dead);
        Assert.False(w.Willy.Airborne);
        Assert.Equal(Field.Top + 2 * T, w.Willy.Y, 3);
        // Along the ledge under the gap, and a jump straight up.
        w.Willy.X = 11.5 * T;
        Sim.Step(w, new Controls(0, true));
        Run(w, 90);
        Assert.Equal(0, w.Room);
        Assert.Equal(Ground, w.Willy.Y, 3);
        Assert.False(w.Willy.Dead);
    }

    [Fact]
    public void An_item_is_collected_by_walking_into_it_and_scores()
    {
        var w = In(Little(), 3.5, 14, 1);
        Run(w, 240, Right);
        Assert.True(w.Taken[0]);
        Assert.Equal(Sim.ItemPoints, w.Score);
        Assert.True(w.AllTaken);
    }

    [Fact]
    public void A_death_brings_him_back_where_he_first_stood_in_the_room()
    {
        var h = Little();
        h.Rooms[1].Grid[15, 14] = Tile.Nasty;
        var w = In(h, 28.5);
        Run(w, 50, Right);
        Assert.Equal(1, w.Room);
        double x = w.SafeX;
        Run(w, 200, Right);
        Assert.True(w.Willy.Dead);
        Sim.Respawn(w);
        Assert.Equal(1, w.Room);
        Assert.Equal(x, w.Willy.X, 3);
        Assert.False(w.Willy.Dead);
    }

    [Fact]
    public void Maria_bars_the_way_to_bed_until_everything_is_collected()
    {
        var h = MansionBook.House;
        var w = new World();
        Sim.Start(w, h);
        Sim.Enter(w, h.Bedroom);
        w.Willy.X = 5.5 * T;
        w.Willy.Y = Ground;
        Run(w, 60 * 6, Right);
        Assert.True(w.Willy.X < h.MariaX);
        Assert.False(w.Home);
        Array.Fill(w.Taken, true);
        Run(w, 60 * 6, Right);
        Assert.True(w.Home);
    }

    [Fact]
    public void The_clock_runs_from_seven_in_the_morning()
    {
        Assert.Equal("7:00am", GameEngine.TimeOfDay(0));
        Assert.Equal("12:30pm", GameEngine.TimeOfDay(330));
        Assert.Equal("11:59pm", GameEngine.TimeOfDay(Sim.Midnight - 1));
        var w = In(Bare(0));
        Run(w, 600);
        Assert.Equal(10 * Sim.MinutesPerSecond, w.Minutes, 3);
    }

    // ───────────────────────────── the game ─────────────────────────────

    private static GameEngine Engine() => new(HighScoreTable.Load(TempFolder())) { Silent = true };

    [Fact]
    public void A_life_lost_and_he_carries_on_and_the_last_one_is_game_over()
    {
        var g = Engine();
        g.Go();
        Sim.Die(g.World, "A NASTY");
        g.Run(1);
        Assert.Equal(Phase.Dying, g.Phase);
        g.Run((int)(60 * GameEngine.DyingSeconds) + 2);
        Assert.Equal(GameEngine.Lives0 - 1, g.Lives);
        Assert.Equal(Phase.Play, g.Phase);
        g.SetLives(1);
        Sim.Die(g.World, "A NASTY");
        g.Run((int)(60 * GameEngine.DyingSeconds) + 4);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void Midnight_ends_the_game()
    {
        var g = Engine();
        g.Go();
        g.World.Minutes = Sim.Midnight - 0.01;
        g.Run(2);
        Assert.Equal(GameState.GameOver, g.State);
        Assert.False(g.World.Home);
    }

    [Fact]
    public void Into_bed_scores_a_bonus_and_the_time_to_spare_then_the_high_scores()
    {
        var g = Engine();
        g.Go();
        var w = g.World;
        Array.Fill(w.Taken, true);
        w.Score = w.Taken.Length * Sim.ItemPoints;
        w.Minutes = Sim.Midnight - 100;
        Sim.Enter(w, w.House.Bedroom);
        w.Willy.X = (w.House.BedCol0 + 1.5) * T;
        w.Willy.Y = Ground;
        g.Run(2);
        Assert.Equal(Phase.Home, g.Phase);
        g.Run((int)(60 * GameEngine.HomeSeconds) + 10);
        Assert.Equal(GameState.GameOver, g.State);
        Assert.InRange(g.Score, w.Taken.Length * Sim.ItemPoints + GameEngine.HomeBonus + 98 * GameEngine.MinutePoints, w.Taken.Length * Sim.ItemPoints + GameEngine.HomeBonus + 100 * GameEngine.MinutePoints);
        g.Run(60 * 6);
        Assert.Equal(GameState.EnterInitials, g.State);
    }

    [Fact]
    public void The_title_gives_way_to_the_demonstration_and_enter_starts_a_game()
    {
        var g = Engine();
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 10);
        Assert.True(g.World.Willy.X != (g.World.House.Start.Col + 0.5) * T || g.World.Room != g.World.House.StartRoom);
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(GameEngine.Lives0, g.Lives);
    }

    [Fact]
    public void Paused_nothing_moves()
    {
        var g = Engine();
        g.Go();
        g.Run(10, Right);
        g.Pause();
        double x = g.World.Willy.X;
        g.Run(60, Right);
        Assert.Equal(x, g.World.Willy.X);
        g.Resume();
        g.Run(10, Right);
        Assert.NotEqual(x, g.World.Willy.X);
    }

    [Fact]
    public void The_keys_walk_and_jump()
    {
        using var input = new InputHandler();
        foreach (var (key, check) in new (Key, Func<Controls, bool>)[]
                 {
                     (Key.Left, c => c.Dx == -1), (Key.Right, c => c.Dx == 1), (Key.O, c => c.Dx == -1), (Key.P, c => c.Dx == 1),
                     (Key.Space, c => c.Jump), (Key.Up, c => c.Jump), (Key.Z, c => c.Jump),
                 })
        {
            input.OnKeyDown(key);
            Assert.True(check(input.Player), key.ToString());
            input.OnKeyUp(key);
        }
        var w = In(Bare(0), 5.5);
        input.OnKeyDown(Key.Right);
        Run(w, 30, input.Player);
        Assert.True(w.Willy.X > 5.5 * T + 60);
    }
}
