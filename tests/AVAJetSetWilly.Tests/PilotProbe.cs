using AVAJetSetWilly.Engine;

namespace AVAJetSetWilly.Tests;

/// <summary>How the computer Willy gets on round the house (set WILLY_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_Willy_does()
    {
        if (Environment.GetEnvironmentVariable("WILLY_PROBE") is not { } mode) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var h = MansionBook.House;
        output.WriteLine($"house built in {sw.ElapsedMilliseconds} ms: {h.Rooms.Length} rooms, {h.Items.Count} items, {h.Rooms.Sum(r => r.Guardians.Count)} guardians");
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "willy-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        g.Go();
        g.SetLives(mode == "lazy" ? 7 : 60);
        var deaths = new Dictionary<string, int>();
        var doing = new Dictionary<string, int>();
        var inRoom = new Dictionary<string, int>();
        var last = g.Phase;
        int t = 0;
        for (; t < 60 * 60 * 25 && g.State == GameState.Playing; t++)
        {
            var c = mode == "lazy" ? new Controls(1, t % 40 == 0) : g.Pilot.Decide(g);
            g.Tick(c);
            if (g.Phase == Phase.Dying && last != Phase.Dying) { var k = $"{g.World.Cause} in {g.World.Def.Theme.Name}"; deaths[k] = deaths.GetValueOrDefault(k) + 1; }
            last = g.Phase;
            doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
            inRoom[g.World.Def.Theme.Name] = inRoom.GetValueOrDefault(g.World.Def.Theme.Name) + 1;
        }
        var w = g.World;
        output.WriteLine($"{(w.Home ? "IN BED" : "not home")} at {GameEngine.TimeOfDay(w.Minutes)} after {t / 60}s; items {w.ItemsTaken}/{h.Items.Count}; score {g.Score}; lives left {g.Lives}; {deaths.Values.Sum()} deaths");
        foreach (var d in deaths.OrderByDescending(d => d.Value)) output.WriteLine($"   {d.Value} x {d.Key}");
        output.WriteLine("doing: " + string.Join(", ", doing.OrderByDescending(d => d.Value).Take(8).Select(d => $"{d.Key} {d.Value / 60}s")));
        output.WriteLine("rooms: " + string.Join(", ", inRoom.OrderByDescending(d => d.Value).Take(8).Select(d => $"{d.Key} {d.Value / 60}s")));
        output.WriteLine("left in: " + string.Join(", ", h.Rooms.Where(r => r.Items.Any(i => !w.Taken[i])).Select(r => r.Theme.Name)));
        output.WriteLine($"total {sw.ElapsedMilliseconds} ms");
    }
}
