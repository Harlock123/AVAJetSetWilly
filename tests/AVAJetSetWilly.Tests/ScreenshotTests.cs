using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAJetSetWilly.Engine;
using AVAJetSetWilly.Views;
using Controls = AVAJetSetWilly.Engine.Controls;

namespace AVAJetSetWilly.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set WILLY_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avajetsetwilly-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("WILLY_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAJetSetWilly-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer Willy play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("WIL", 14230, 71);
        g.HighScores.Insert("AVA", 3700, 37);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 60 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>Willy put into a room (at its first way in) and played by the computer until something worth showing.</summary>
    private static void Visit(string room, string name, Func<GameEngine, bool> moment, int seconds = 120)
    {
        var (w, c, g) = Open();
        g.Go();
        g.SetLives(20);
        var h = g.World.House;
        var r = h.Rooms.First(x => x.Theme.Name == room);
        var at = MansionBook.Ways(h, r)[0];
        g.World.Willy.X = at.X;
        g.World.Willy.Y = at.Feet;
        Sim.Enter(g.World, r.Index);
        g.Pilot.Reset();
        double start = g.World.Clock;
        Assert.True(PlayUntil(g, c, g => g.World.Room == r.Index && g.World.Clock - start > 2 && moment(g), seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_bathroom()
    {
        var (w, c, g) = Open();
        g.Go();
        Assert.True(PlayUntil(g, c, g => g.World.Clock > 5 && !g.World.Willy.Airborne));
        Shot(w, c, "bathroom");
    }

    [AvaloniaFact]
    public void A_jump() => Visit("THE BALLROOM", "jump", g => g.World.Willy.Airborne && g.World.Willy.Vy > -150 && g.World.Willy.Vy < 0);

    [AvaloniaFact]
    public void The_wine_cellar() => Visit("THE WINE CELLAR", "cellar", g => g.World.ItemsTaken >= 1 && !g.World.Willy.Airborne);

    [AvaloniaFact]
    public void The_belfry() => Visit("THE BELFRY", "belfry", g => !g.World.Willy.Airborne && g.World.Clock > 6);

    [AvaloniaFact]
    public void The_kitchen() => Visit("THE KITCHEN", "kitchen", g => g.World.ItemsTaken >= 1 && !g.World.Willy.Airborne);

    [AvaloniaFact]
    public void Maria()
    {
        var (w, c, g) = Open();
        g.Go();
        var h = g.World.House;
        g.World.Willy.X = 6.5 * Field.Tile;
        Sim.Enter(g.World, h.Bedroom);
        for (int i = 0; i < 60 * 5; i++) { g.Tick(new Controls(1)); if (i % 3 == 0) c.Advance(0.05); }
        Shot(w, c, "maria");
    }

    [AvaloniaFact]
    public void Off_to_bed()
    {
        var (w, c, g) = Open();
        g.Go();
        var world = g.World;
        var h = world.House;
        Array.Fill(world.Taken, true);
        world.Score = world.Taken.Length * Sim.ItemPoints;
        world.Minutes = 15 * 60 + 25;
        world.Willy.X = 18.5 * Field.Tile;
        Sim.Enter(world, h.Bedroom);
        for (int i = 0; i < 60 * 8 && g.Phase != Phase.Home; i++) { g.Tick(new Controls(1)); if (i % 3 == 0) c.Advance(0.05); }
        for (int i = 0; i < 20; i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Assert.Equal(Phase.Home, g.Phase);
        Shot(w, c, "bed");
        for (int i = 0; i < 60 * 12 && !(g.State == GameState.GameOver && g.StateTime > 1); i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Shot(w, c, "sweet-dreams");
    }

    [AvaloniaFact]
    public void Game_over()
    {
        var (w, c, g) = Open();
        g.Go();
        g.SetLives(1);
        g.World.Score = 2300;
        Sim.Die(g.World, "test");
        for (int i = 0; i < 600 && !(g.State == GameState.GameOver && g.StateTime > 3.8); i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Shot(w, c, "game-over");
    }
}
