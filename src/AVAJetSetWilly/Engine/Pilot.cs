namespace AVAJetSetWilly.Engine;

/// <summary>
/// The computer Willy, for the demonstration and the tests. It finds the nearest room with something left to collect
/// (by the doorways and gaps between rooms), and in each room knows every place it can stand and where each walk or
/// jump takes it (the room's <see cref="Graph"/>): it plans the quickest way to the room's items, or to the way out
/// toward the next room, and at last to bed. Before each move it plays the move out on a copy of the house,
/// guardians and all, and if a guardian would get it, it waits for the moment it wouldn't (or tries another way).
/// </summary>
public sealed class Pilot
{
    private readonly Queue<Controls> _plan = new();
    private Graph? _full;
    private HashSet<Spot> _beats = [];
    /// <summary>What a step onto a guardian's beat counts as, against going round.</summary>
    public const int BeatCost = 900;
    private int _for = -1;
    private bool _forDone;
    private int _crumbled = -1;
    /// <summary>The way out being made for, when that's the target.</summary>
    private Exit _exit;
    private int _room = -1;

    private enum Exit { None, Left, Right, Up, Down }
    public const int WaitStep = 6, MaxWait = 360, Settle = 40;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _plan.Clear();
        _full = null;
        _for = -1;
        _room = -1;
        Doing = "";
    }

    public Controls Decide(GameEngine g)
    {
        var w = g.World;
        var m = w.Willy;
        if (g.Phase != Phase.Play || m.Dead) { _plan.Clear(); return default; }
        // Into another room: whatever was planned was for the last one.
        if (w.Room != _room) { _plan.Clear(); _room = w.Room; }
        if (_plan.Count > 0) return _plan.Dequeue();
        if (m.Airborne) return default;
        // The map, again if a ledge has crumbled (or Maria has gone).
        int crumbled = 0;
        foreach (var t in w.Grid) if (t == Tile.Empty) crumbled++;
        if (_full == null || _for != w.Room || crumbled != _crumbled || _forDone != w.AllTaken)
        {
            // The guardians' beats: gone along only when there's no other way (a crumbled ledge can leave none).
            var beats = _beats = new HashSet<Spot>();
            foreach (var gd in w.Def.Guardians)
                if (!gd.Vertical)
                    for (int c = (int)(gd.Min / Field.Tile); c <= (int)(gd.Max / Field.Tile); c++)
                        beats.Add(new Spot(c, (int)Math.Round((gd.Y - Field.Top) / Field.Tile) - 1));
            _full = Graph.Build(w.House, w.Def, w);
            _for = w.Room;
            _forDone = w.AllTaken;
            _crumbled = crumbled;
        }
        var here = Spot.Of(m);
        if (!_full!.Edges.ContainsKey(here))
        {
            // Off the map (one foot on the end of a ledge, or just in at a doorway): onto the middle of a square.
            var near = new[] { here with { Col = here.Col - 1 }, here with { Col = here.Col + 1 }, here with { Col = here.Col - 2 }, here with { Col = here.Col + 2 } }
                .Where(_full.Edges.ContainsKey).OrderBy(s => Math.Abs(s.X - m.X)).FirstOrDefault();
            if (near != default)
            {
                Doing = "stepping on";
                int dx = Math.Sign(near.X - m.X);
                for (int i = 0; i < 24 && Math.Abs(m.X + dx * (i + 1) * Willy.Speed * Field.Step - near.X) < Math.Abs(m.X + dx * i * Willy.Speed * Field.Step - near.X); i++) _plan.Enqueue(new Controls(dx));
                if (_plan.Count > 0) return _plan.Dequeue();
            }
        }
        var targets = Targets(w);
        // How far every spot is from the nearest target.
        var far = Distances(_full, targets, _beats);
        if (!far.ContainsKey(here) && _full.Edges.ContainsKey(here))
        {
            // Cut off (a ledge has crumbled away): out by whatever way is still open, and back to find the room
            // as it was.
            foreach (var (e, spots) in Ways(w.Def))
            {
                var d = Distances(_full, spots, _beats);
                if (!d.ContainsKey(here)) continue;
                _exit = e;
                far = d;
                Doing = "out and back";
                break;
            }
        }
        int now = far.TryGetValue(here, out int h) ? h : int.MaxValue;
        if (now == 0 && Math.Abs(m.X - here.X) <= 2 && _exit != Exit.None)
        {
            // At the way out: through it, once it's safe to go.
            Doing = "leaving by the " + _exit.ToString().ToLowerInvariant();
            var go = ExitRun(_exit);
            if (Safe(w, go)) { foreach (var c in go) _plan.Enqueue(c); return _plan.Dequeue(); }
            Doing = "waiting to leave";
            for (int i = 0; i < WaitStep; i++) _plan.Enqueue(Hold(w));
            return _plan.Dequeue();
        }
        if (now == 0 && Math.Abs(m.X - here.X) > 2)
        {
            // On the target's square but off its middle (a jump can land him anywhere in it): into the middle.
            Doing = "lining up";
            int dx = Math.Sign(here.X - m.X);
            for (int i = 0; i < 12 && Math.Abs(m.X + dx * (i + 1) * Willy.Speed * Field.Step - here.X) < Math.Abs(m.X + dx * i * Willy.Speed * Field.Step - here.X); i++) _plan.Enqueue(new Controls(dx));
            if (_plan.Count > 0) return _plan.Dequeue();
        }
        // The moves from here, best first, each tried now and after waits.
        var options = new List<(Move Move, int Cost)>();
        if (_full.Edges.TryGetValue(here, out var edges))
            foreach (var (mv, to, ticks) in edges)
                // Only moves that get nearer (a step back and forth would never get past a guardian).
                if (far.TryGetValue(to, out int d) && (d < now || now == int.MaxValue)) options.Add((mv, ticks + d + (_beats.Contains(to) ? BeatCost : 0)));
        options.Sort((a, b) => a.Cost.CompareTo(b.Cost));
        int best = int.MaxValue;
        List<Controls>? pick = null;
        int pickWait = 0;
        // (If nothing is safe: the move that keeps him alive longest.)
        int longest = -1;
        List<Controls>? escape = null;
        foreach (var (mv, cost) in options.Take(4))
        {
            for (int wait = 0; wait <= MaxWait && wait + cost < best; wait += WaitStep)
            {
                var (run, alive) = Rehearse(w, mv, wait);
                if (run == null)
                {
                    if (wait == 0 && alive > longest && escapeRun != null) { longest = alive; escape = escapeRun; }
                    continue;
                }
                if (wait + cost < best) { best = wait + cost; pick = run; pickWait = wait; }
                break;
            }
        }
        if (pick == null)
        {
            // Every move from here on the way is deadly: any move at all that buys time.
            if (_full.Edges.TryGetValue(here, out var all))
                foreach (var (mv, _, _) in all)
                {
                    var (run, alive) = Rehearse(w, mv, 0);
                    if (run != null) { pick = run; break; }
                    if (alive > longest && escapeRun != null) { longest = alive; escape = escapeRun; }
                }
            if (pick == null && escape != null && longest > HoldAlive(w)) pick = escape;
        }
        if (pick == null)
        {
            Doing = "stuck";
            for (int i = 0; i < WaitStep; i++) _plan.Enqueue(Hold(w));
            return _plan.Dequeue();
        }
        Doing = pickWait > 0 ? "waiting" : "going";
        if (pickWait > 0)
        {
            for (int i = 0; i < Math.Min(pickWait, WaitStep); i++) _plan.Enqueue(Hold(w));
            return _plan.Dequeue();
        }
        foreach (var c in pick) _plan.Enqueue(c);
        return _plan.Count > 0 ? _plan.Dequeue() : default;
    }

    private List<Controls>? escapeRun;

    /// <summary>
    /// What to make for in this room: its items; or, with none left here, the way out toward the nearest room that has
    /// some; or, with everything collected, the way to bed.
    /// </summary>
    private List<Spot> Targets(World w)
    {
        var h = w.House;
        var r = w.Def;
        _exit = Exit.None;
        var here = r.Items.Where(i => !w.Taken[i]).Select(i => new Spot(h.Items[i].Col, h.Items[i].Row + 1)).ToList();
        if (here.Count > 0) { Doing = "after an item"; return here; }
        if (r.Bedroom && w.AllTaken) { Doing = "off to bed"; return [new Spot((h.BedCol0 + h.BedCol1) / 2, 14)]; }
        // The nearest room with something to do, by the fewest rooms on the way.
        var first = new Dictionary<int, Exit> { [w.Room] = Exit.None };
        var q = new Queue<int>([w.Room]);
        Exit way = Exit.None;
        while (q.Count > 0)
        {
            int at = q.Dequeue();
            var ar = h.Rooms[at];
            if (at != w.Room && (w.AllTaken ? ar.Bedroom : ar.Items.Any(i => !w.Taken[i]))) { way = first[at]; break; }
            foreach (var (to, e) in new[] { (ar.Left, Exit.Left), (ar.Right, Exit.Right), (ar.Up, Exit.Up), (ar.Down, Exit.Down) })
                if (to >= 0 && !first.ContainsKey(to)) { first[to] = at == w.Room ? e : first[at]; q.Enqueue(to); }
        }
        _exit = way;
        Doing = w.AllTaken ? "making for the bedroom" : "to the next room";
        return way == Exit.None ? [] : Ways(r).First(x => x.Exit == way).Spots;
    }

    /// <summary>The ways out of a room, and where to stand to take each.</summary>
    private static IEnumerable<(Exit Exit, List<Spot> Spots)> Ways(RoomDef r)
    {
        if (r.Left >= 0) yield return (Exit.Left, [new Spot(1, 14)]);
        if (r.Right >= 0) yield return (Exit.Right, [new Spot(Field.Cols - 2, 14)]);
        if (r.Up >= 0) yield return (Exit.Up, [new Spot(r.UpCol + 1, 1), new Spot(r.UpCol + 2, 1)]);
        if (r.Down >= 0) yield return (Exit.Down, [new Spot(r.DownCol + 1, 14)]);
    }

    /// <summary>The controls that take him out of the room from the way out's standing place.</summary>
    private static List<Controls> ExitRun(Exit e) => e switch
    {
        Exit.Left => Enumerable.Repeat(new Controls(-1), 40).ToList(),
        Exit.Right => Enumerable.Repeat(new Controls(1), 40).ToList(),
        Exit.Up => [new Controls(0, true), .. Enumerable.Repeat(default(Controls), 60)],
        _ => Enumerable.Repeat(new Controls(-1), 24).ToList(),
    };

    /// <summary>Whether a run of controls, played out on a copy, leaves him alive a little after.</summary>
    private static bool Safe(World start, List<Controls> run)
    {
        var w = start.Copy();
        foreach (var c in run)
        {
            Sim.Step(w, c);
            if (w.Willy.Dead) return false;
        }
        for (int i = 0; i < Settle; i++)
        {
            Sim.Step(w, Hold(w));
            if (w.Willy.Dead) return false;
        }
        return true;
    }

    /// <summary>How long he'd live just standing here.</summary>
    private static int HoldAlive(World start)
    {
        var w = start.Copy();
        for (int i = 0; i < 240; i++)
        {
            Sim.Step(w, Hold(w));
            if (w.Willy.Dead) return i;
        }
        return 240;
    }

    /// <summary>Standing still: on a conveyor, that means pushing against it.</summary>
    private static Controls Hold(World w)
    {
        var m = w.Willy;
        var under = w.AtPoint(m.X, m.Y + 1);
        return new Controls(under == Tile.ConveyorLeft ? 1 : under == Tile.ConveyorRight ? -1 : 0);
    }

    /// <summary>
    /// A move played out on a copy after a wait, then a moment standing: its controls if he comes through it; and,
    /// either way, how many steps he lived.
    /// </summary>
    private (List<Controls>? Run, int Alive) Rehearse(World start, Move mv, int wait)
    {
        var w = start.Copy();
        int alive = 0;
        escapeRun = null;
        for (int i = 0; i < wait; i++)
        {
            Sim.Step(w, Hold(w));
            alive++;
            if (w.Willy.Dead || w.Willy.Airborne) return (null, alive);
        }
        var controls = new List<Controls>();
        foreach (var c in Graph.Script(w, mv))
        {
            controls.Add(c);
            Sim.Step(w, c);
            alive++;
            if (w.Willy.Dead) { escapeRun = controls; return (null, alive); }
            if (w.Room != start.Room || w.Home) return (controls, alive);
        }
        for (int i = 0; i < Settle; i++)
        {
            Sim.Step(w, Hold(w));
            alive++;
            if (w.Willy.Dead) { escapeRun = controls; return (null, alive); }
            if (w.Room != start.Room || w.Home) break;
        }
        return (controls, alive);
    }

    /// <summary>How long (in steps) from every spot to the nearest of the targets, going backwards from them.</summary>
    private static Dictionary<Spot, int> Distances(Graph g, List<Spot> targets, HashSet<Spot> beats)
    {
        var back = new Dictionary<Spot, List<(Spot From, int Ticks)>>();
        foreach (var (from, list) in g.Edges)
            foreach (var (_, to, ticks) in list)
            {
                if (!back.TryGetValue(to, out var l)) back[to] = l = [];
                l.Add((from, ticks));
            }
        var dist = new Dictionary<Spot, int>();
        var pq = new PriorityQueue<Spot, int>();
        foreach (var t in targets) { dist[t] = 0; pq.Enqueue(t, 0); }
        while (pq.TryDequeue(out var s, out int d))
        {
            if (d > dist[s]) continue;
            if (!back.TryGetValue(s, out var l)) continue;
            foreach (var (from, ticks) in l)
            {
                int nd = d + ticks + (beats.Contains(s) ? BeatCost : 0);
                if (dist.TryGetValue(from, out int od) && od <= nd) continue;
                dist[from] = nd;
                pq.Enqueue(from, nd);
            }
        }
        return dist;
    }
}
