namespace AVAJetSetWilly.Engine;

/// <summary>
/// The house, step by step: Willy walking, jumping (a fixed arc, as a jump should be) and falling, from room to room
/// through the doorways and the gaps between floors; the conveyors and crumbling ledges, the guardians on their beats,
/// the items, the clock, and Maria, who won't let him near his bed until every item is put away.
/// </summary>
public static class Sim
{
    private const double Dt = Field.Step, T = Field.Tile;
    /// <summary>How long a crumbling square bears Willy before it goes.</summary>
    public const double CrumbleTime = 0.6;
    /// <summary>Game minutes to a second: seven in the morning to midnight is 1020 minutes.</summary>
    public const double MinutesPerSecond = 1.5, Midnight = 17 * 60;
    public const int ItemPoints = 100;

    public static void Start(World w, Mansion h)
    {
        w.House = h;
        w.Taken = new bool[h.Items.Count];
        w.Minutes = 0;
        w.Clock = 0;
        w.Home = false;
        w.Cause = "";
        w.Score = 0;
        w.Willy = new Willy { X = (h.Start.Col + 0.5) * T, Y = Field.Top + (h.Start.Row + 1) * T };
        Enter(w, h.StartRoom);
    }

    /// <summary>Into a room (Willy already placed): its squares as built, its guardians at their posts.</summary>
    public static void Enter(World w, int room, bool guardians = true)
    {
        w.Room = room;
        var r = w.Def;
        w.Grid = (Tile[,])r.Grid.Clone();
        for (int x = 0; x < Field.Cols; x++)
        for (int y = 0; y < Field.Rows; y++)
            w.Crumble[x, y] = CrumbleTime;
        w.Guardians = guardians ? r.Guardians.Select(g => new Guardian { Def = g, X = g.X, Y = g.Y }).ToList() : [];
        w.SafeSet = false;
    }

    /// <summary>After a death: back where he first stood in the room (the room as it was when he came in).</summary>
    public static void Respawn(World w)
    {
        Enter(w, w.SafeRoom);
        w.Willy = new Willy { X = w.SafeX, Y = w.SafeY, Facing = w.SafeFacing };
        w.Cause = "";
        w.SafeSet = true;
    }

    public static void Step(World w, Controls c)
    {
        w.Clock += Dt;
        foreach (var g in w.Guardians) Patrol(g);
        var m = w.Willy;
        if (m.Dead || w.Home || w.Exited) return;
        if (!w.Probe) w.Minutes += MinutesPerSecond * Dt;
        if (!m.Airborne) Ground(w, m, c);
        if (m.Airborne) Air(w, m);
        m.JumpPrev = c.Jump;
        if (m.Dead) return;
        if (!m.Airborne && !w.SafeSet && !w.Probe)
        {
            w.SafeSet = true;
            (w.SafeRoom, w.SafeX, w.SafeY, w.SafeFacing) = (w.Room, m.X, m.Y, m.Facing);
        }
        Touch(w, m);
        if (!m.Dead) Through(w, m);
    }

    /// <summary>Out of the room: through a doorway, or up or down through a gap, into the next.</summary>
    private static void Through(World w, Willy m)
    {
        var r = w.Def;
        int next = -1;
        if (m.X < 0 && r.Left >= 0) { next = r.Left; m.X += Field.Width; }
        else if (m.X >= Field.Width && r.Right >= 0) { next = r.Right; m.X -= Field.Width; }
        else if (m.Y < Field.Top && r.Up >= 0) { next = r.Up; m.Y += 15 * T; m.FallFrom += 15 * T; }
        else if (m.Y > Field.Top + 15 * T + 1 && m.Airborne && r.Down >= 0) { next = r.Down; m.Y -= 15 * T; m.FallFrom -= 15 * T; }
        if (next < 0) return;
        if (w.Probe) { w.Exited = true; return; }
        Enter(w, next);
        w.Heard.Add(Sfx.Room);
    }

    private static void Patrol(Guardian g)
    {
        var d = g.Def;
        if (d.Vertical)
        {
            g.Y += g.Dir * d.Speed * Dt;
            if (g.Y > d.Max) { g.Y = d.Max; g.Dir = -1; }
            if (g.Y < d.Min) { g.Y = d.Min; g.Dir = 1; }
        }
        else
        {
            g.X += g.Dir * d.Speed * Dt;
            if (g.X > d.Max) { g.X = d.Max; g.Dir = -1; }
            if (g.X < d.Min) { g.X = d.Min; g.Dir = 1; }
        }
    }

    /// <summary>The square under a foot.</summary>
    private static (int C, int R) Under(double x, double feet) => ((int)Math.Floor(x / T), (int)Math.Floor((feet + 1 - Field.Top) / T));

    /// <summary>Whether there's something to stand on under either foot, exactly at foot level.</summary>
    public static bool Footing(World w, double x, double feet)
    {
        if (Math.Abs((feet - Field.Top) / T - Math.Round((feet - Field.Top) / T)) > 0.001) return false;
        foreach (double fx in new[] { x - 6, x + 6 })
        {
            var (c, r) = Under(fx, feet);
            if (CavernRules.Standable(w.At(c, r))) return true;
        }
        return false;
    }

    /// <summary>Maria, by the bed: as tall as a doorway, and not to be got past.</summary>
    public static (double X0, double X1, double Y0) Maria(World w) => (w.House.MariaX - 14, w.House.MariaX + 14, Field.Top + 12 * T);

    /// <summary>
    /// Whether brick (or Maria) stops him moving to x, going the way dx says: only his leading side counts, so a jump
    /// that lands him with a shoulder in the brick never pins him there.
    /// </summary>
    private static bool Blocked(World w, double x, double feet, int dx)
    {
        double fx = x + Math.Sign(dx) * Willy.HalfWidth;
        for (double y = feet - Willy.Height + 2; y < feet - 1; y += 12)
            if (w.AtPoint(fx, y) == Tile.Wall) return true;
        if (w.AtPoint(fx, feet - 2) == Tile.Wall) return true;
        if (w.MariaThere)
        {
            var (x0, x1, y0) = Maria(w);
            if (x + Willy.HalfWidth > x0 && x - Willy.HalfWidth < x1 && feet > y0) return true;
        }
        return false;
    }

    private static void Ground(World w, Willy m, Controls c)
    {
        if (!Footing(w, m.X, m.Y))
        {
            // Off the edge: straight down.
            m.Airborne = true;
            m.Vy = 0;
            m.Jdx = 0;
            m.FallFrom = m.Y;
            return;
        }
        if (c.Jump && !m.JumpPrev)
        {
            m.Airborne = true;
            m.Vy = -Willy.JumpSpeed;
            m.Jdx = c.Dx;
            if (c.Dx != 0) m.Facing = c.Dx;
            m.FallFrom = double.NaN;
            w.Heard.Add(Sfx.Jump);
            return;
        }
        // A conveyor carries him; pushing against it only holds him still.
        var (uc, ur) = Under(m.X, m.Y);
        var under = w.At(uc, ur);
        int belt = under == Tile.ConveyorLeft ? -1 : under == Tile.ConveyorRight ? 1 : 0;
        int dx = c.Dx;
        if (belt != 0) dx = dx == -belt ? 0 : belt;
        if (dx != 0)
        {
            m.Facing = c.Dx != 0 ? c.Dx : dx;
            double nx = m.X + dx * Willy.Speed * Dt;
            if (!Blocked(w, nx, m.Y, dx)) { m.X = nx; m.Walk += Dt; }
        }
        // Crumbling ledges give way under him.
        foreach (double fx in new[] { m.X - 6, m.X + 6 })
        {
            var (fc, fr) = Under(fx, m.Y);
            if (fc < 0 || fc >= Field.Cols || fr < 0 || fr >= Field.Rows || w.Grid[fc, fr] != Tile.Crumble) continue;
            w.Crumble[fc, fr] -= Dt / 2;
            if (w.Crumble[fc, fr] <= 0) { w.Grid[fc, fr] = Tile.Empty; w.Heard.Add(Sfx.Crumble); }
        }
    }

    private static void Air(World w, Willy m)
    {
        double nx = m.X + m.Jdx * Willy.Speed * Dt;
        if (m.Jdx != 0)
        {
            if (Blocked(w, nx, m.Y, m.Jdx)) m.Jdx = 0;
            else m.X = nx;
        }
        double vy = m.Vy + Willy.Gravity * Dt, ny = m.Y + vy * Dt;
        if (vy < 0)
        {
            // A head against brick stops the climb.
            double head = ny - Willy.Height;
            if (w.AtPoint(m.X - 6, head) == Tile.Wall || w.AtPoint(m.X + 6, head) == Tile.Wall) { vy = 0; ny = m.Y; }
        }
        if (m.Vy < 0 && vy >= 0 || double.IsNaN(m.FallFrom) && vy >= 0) m.FallFrom = m.Y;
        if (vy > 0)
        {
            // Down onto the first thing to stand on between where his feet were and where they'd be.
            for (int r = (int)Math.Ceiling((m.Y - Field.Top) / T - 0.001); r <= (int)Math.Floor((ny - Field.Top) / T); r++)
            {
                double top = Field.Top + r * T;
                if (top < m.Y - 0.001 || !Footing(w, m.X, top)) continue;
                m.Y = top;
                m.Vy = 0;
                m.Airborne = false;
                if (m.Y - m.FallFrom > Willy.MaxFall) Die(w, "A FALL");
                else w.Heard.Add(Sfx.Land);
                return;
            }
        }
        m.Vy = vy;
        m.Y = ny;
    }

    private static void Touch(World w, Willy m)
    {
        double x0 = m.X - Willy.HalfWidth + 3, x1 = m.X + Willy.HalfWidth - 3, y0 = m.Y - Willy.Height + 4, y1 = m.Y - 2;
        // Nasties.
        for (int c = (int)Math.Floor(x0 / T); c <= (int)Math.Floor(x1 / T); c++)
        for (int r = (int)Math.Floor((y0 - Field.Top) / T); r <= (int)Math.Floor((y1 - Field.Top) / T); r++)
            if (w.At(c, r) == Tile.Nasty)
            {
                // The nasty fills the lower part of its square.
                double ny0 = Field.Top + r * T + 10, nx0 = c * T + 6, nx1 = c * T + T - 6;
                if (y1 > ny0 && x1 > nx0 && x0 < nx1) { Die(w, "A NASTY"); return; }
            }
        foreach (var g in w.Guardians)
        {
            var (hw, hh) = g.Half;
            double gy = g.Def.Vertical ? g.Y : g.Y - hh;
            if (x1 > g.X - hw + 3 && x0 < g.X + hw - 3 && y1 > gy - hh + 3 && y0 < gy + hh - 3) { Die(w, "A GUARDIAN"); return; }
        }
        if (w.Probe) return;
        foreach (int i in w.Def.Items)
        {
            if (w.Taken[i]) continue;
            var it = w.House.Items[i];
            double kx = (it.Col + 0.5) * T, ky = Field.Top + (it.Row + 0.5) * T;
            if (x1 > kx - 10 && x0 < kx + 10 && y1 > ky - 10 && y0 < ky + 10)
            {
                w.Taken[i] = true;
                w.Score += ItemPoints;
                w.Heard.Add(w.AllTaken ? Sfx.AllTaken : Sfx.Item);
            }
        }
        // Into bed, with everything put away.
        if (w.Def.Bedroom && w.AllTaken && !m.Airborne && m.X > w.House.BedCol0 * T && m.X < (w.House.BedCol1 + 1) * T) w.Home = true;
    }

    public static void Die(World w, string cause)
    {
        if (w.Willy.Dead) return;
        w.Willy.Dead = true;
        w.Cause = cause;
        w.Heard.Add(Sfx.Die);
    }
}
