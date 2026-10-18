using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAJetSetWilly.Engine;

namespace AVAJetSetWilly.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAJetSetWilly/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused;

    private const double T = Field.Tile;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            case GameState.GameOver:
                if (g.World.Home) DrawBedtime(dc); else DrawBoot(dc);
                break;
            default:
                DrawRoom(dc, g.World);
                DrawPanel(dc);
                if (g.Message is { } msg && g.Phase is Phase.Start or Phase.Home)
                {
                    dc.FillRectangle(Brush(0xB0000000), new Rect(0, Field.Top + 214, Field.Width, 72));
                    Text(dc, msg, new Point(Field.Width / 2, Field.Top + 250), g.Phase == Phase.Home ? 44 : 56, 0xFFFFFFFF, center: true);
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, Field.Height - 16), 22, Yellow, center: true);
    }

    // ───────────────────────────── the room ─────────────────────────────

    private void DrawRoom(DrawingContext dc, World w)
    {
        var th = w.Def.Theme;
        dc.FillRectangle(Brush(th.Back), new Rect(0, Field.Top, Field.Width, Field.Rows * T));
        for (int c = 0; c < Field.Cols; c++)
        for (int r = 0; r < Field.Rows; r++)
            DrawTile(dc, w, c, r, th);
        if (w.Def.Bedroom) DrawBedroom(dc, w);
        foreach (int i in w.Def.Items)
        {
            var it = w.House.Items[i];
            if (!w.Taken[i]) DrawItem(dc, (it.Col + 0.5) * T, Field.Top + (it.Row + 0.5) * T, i);
        }
        foreach (var gd in w.Guardians) DrawGuardian(dc, gd);
        var m = w.Willy;
        if (!m.Dead || (int)(Engine.PhaseTime * 12) % 2 == 0) DrawWilly(dc, m.X, m.Y, m.Facing, m.Airborne ? 1 : m.Walk, m.Dead);
    }

    /// <summary>The bed, and Maria standing guard in front of it until everything is put away.</summary>
    private void DrawBedroom(DrawingContext dc, World w)
    {
        var h = w.House;
        double x0 = h.BedCol0 * T, x1 = (h.BedCol1 + 1) * T, floor = Field.Top + 15 * T;
        // The bed: a headboard, the mattress, the pillow and the quilt.
        dc.FillRectangle(Brush(0xFF6A3A1A), new Rect(x1 - 14, floor - 74, 12, 74));
        dc.FillRectangle(Brush(0xFF6A3A1A), new Rect(x0 + 2, floor - 40, 10, 40));
        dc.FillRectangle(Brush(0xFFF0F0F0), new Rect(x0 + 6, floor - 40, x1 - x0 - 20, 14), 4);
        dc.FillRectangle(Brush(0xFFFFFFFF), new Rect(x1 - 52, floor - 50, 36, 14), 6);
        dc.FillRectangle(Brush(0xFF4060C0), new Rect(x0 + 6, floor - 34, x1 - x0 - 60, 12), 3);
        for (int k = 0; k < 4; k++) dc.FillRectangle(Brush(0xFFE0C040), new Rect(x0 + 14 + k * 22, floor - 31, 8, 6));
        if (!w.MariaThere) return;
        // Maria: the housekeeper, arms folded.
        double mx = h.MariaX, bob = Math.Sin(_clock * 3) * 1.5;
        Poly(dc, 0xFF3A2A6A, 0xFF1A1030, 2, new Point(mx - 20, floor), new Point(mx - 12, floor - 62), new Point(mx + 12, floor - 62), new Point(mx + 20, floor));
        dc.FillRectangle(Brush(0xFFF0F0F0), new Rect(mx - 9, floor - 52, 18, 40), 3);
        dc.FillRectangle(Brush(0xFF3A2A6A), new Rect(mx - 14, floor - 52 + bob, 28, 7), 3);
        dc.FillEllipse(mx, floor - 72, 10, 0xFFF0C090);
        dc.DrawEllipse(Brush(0xFF6A6A70), null, new Point(mx, floor - 81), 11, 6);
        dc.FillEllipse(mx - 4, floor - 73, 1.6, 0xFF000000);
        dc.FillEllipse(mx + 4, floor - 73, 1.6, 0xFF000000);
        dc.DrawLine(Pen(0xFF802020, 2), new Point(mx - 4, floor - 66), new Point(mx + 4, floor - 66));
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "NOT TILL IT'S ALL PUT AWAY!", new Point(mx, floor - 104), 22, 0xFFFFFFFF, center: true);
    }

    private void DrawTile(DrawingContext dc, World w, int c, int r, Theme th)
    {
        var t = w.Grid[c, r];
        double x = c * T, y = Field.Top + r * T;
        switch (t)
        {
            case Tile.Wall:
            {
                dc.FillRectangle(Brush(th.Wall), new Rect(x, y, T, T));
                // Bricks.
                uint mortar = Alpha(0xFF000000, 0.45);
                dc.FillRectangle(Brush(mortar), new Rect(x, y + 15, T, 2));
                dc.FillRectangle(Brush(mortar), new Rect(x, y + 31, T, 1));
                dc.FillRectangle(Brush(mortar), new Rect(x + (r % 2 == 0 ? 8 : 24), y, 2, 15));
                dc.FillRectangle(Brush(mortar), new Rect(x + (r % 2 == 0 ? 24 : 8), y + 16, 2, 15));
                break;
            }
            case Tile.Floor:
                dc.FillRectangle(Brush(th.Floor), new Rect(x, y, T, 12));
                dc.FillRectangle(Brush(Alpha(0xFF000000, 0.35)), new Rect(x, y + 9, T, 3));
                for (int k = 0; k < 4; k++) dc.FillRectangle(Brush(Alpha(0xFFFFFFFF, 0.25)), new Rect(x + 2 + k * 8, y + 2, 4, 3));
                break;
            case Tile.Crumble:
            {
                double left = w.Crumble[c, r] / Sim.CrumbleTime;
                double h = 12 * left;
                dc.FillRectangle(Brush(Alpha(th.Floor, 0.6)), new Rect(x, y, T, h));
                for (int k = 0; k < 6; k++) dc.FillRectangle(Brush(th.Floor), new Rect(x + 1 + k * 5, y + (k % 2) * 3, 3, Math.Max(1, h - 4)));
                break;
            }
            case Tile.ConveyorLeft:
            case Tile.ConveyorRight:
            {
                dc.FillRectangle(Brush(Alpha(th.Floor, 0.85)), new Rect(x, y, T, 12));
                double shift = (_clock * 40 * (t == Tile.ConveyorLeft ? -1 : 1)) % 8;
                if (shift < 0) shift += 8;
                for (int k = -1; k < 5; k++) dc.FillRectangle(Brush(0xFF101010), new Rect(x + k * 8 + shift, y + 3, 3, 6));
                dc.DrawLine(Pen(0xFFFFFFFF, 1.5), new Point(x, y), new Point(x + T, y));
                dc.DrawLine(Pen(0xFFFFFFFF, 1.5), new Point(x, y + 12), new Point(x + T, y + 12));
                break;
            }
            case Tile.Nasty:
                DrawNasty(dc, x, y, th);
                break;
        }
    }

    private void DrawNasty(DrawingContext dc, double x, double y, Theme th)
    {
        switch (th.Nasty)
        {
            case 0:
                // A poisonous bush.
                dc.FillEllipse(x + 16, y + 24, 10, 0xFF20A030);
                dc.FillEllipse(x + 9, y + 22, 6, 0xFF30C040);
                dc.FillEllipse(x + 23, y + 21, 6, 0xFF30C040);
                for (int k = 0; k < 4; k++) dc.FillEllipse(x + 8 + k * 5, y + 16 + (k % 2) * 6, 2, 0xFFFF40FF);
                break;
            case 1:
                // Spikes of ice.
                for (int k = 0; k < 4; k++)
                    Poly(dc, 0xFFC0F0FF, 0xFF6090C0, 1, new Point(x + 2 + k * 7, y + T), new Point(x + 5.5 + k * 7, y + 10 + (k % 2) * 4), new Point(x + 9 + k * 7, y + T));
                break;
            default:
            {
                // A flame.
                double f = Math.Sin(_clock * 18 + x) * 3;
                Poly(dc, 0xFFFF6020, null, 0, new Point(x + 6, y + T), new Point(x + 10 + f, y + 12), new Point(x + 16, y + 4 - f), new Point(x + 22 - f, y + 12), new Point(x + 26, y + T));
                Poly(dc, 0xFFFFE040, null, 0, new Point(x + 11, y + T), new Point(x + 16, y + 14 + f), new Point(x + 21, y + T));
                break;
            }
        }
    }

    /// <summary>An item to collect, flashing: each a different thing about the house.</summary>
    private void DrawItem(DrawingContext dc, double x, double y, int i)
    {
        uint[] cycle = [0xFFFFFF40, 0xFF40FFFF, 0xFFFF40FF, 0xFF40FF40, 0xFFFFFFFF];
        uint c = cycle[((int)(_clock * 8) + i) % cycle.Length];
        switch (i % 6)
        {
            case 0:
                // A tap.
                dc.FillRectangle(Brush(c), new Rect(x - 10, y - 4, 16, 6));
                dc.FillRectangle(Brush(c), new Rect(x + 3, y - 4, 5, 13));
                dc.FillRectangle(Brush(c), new Rect(x - 6, y - 10, 4, 6));
                dc.FillRectangle(Brush(c), new Rect(x - 10, y - 12, 12, 3));
                break;
            case 1:
                // A wine glass.
                Poly(dc, c, null, 0, new Point(x - 8, y - 11), new Point(x + 8, y - 11), new Point(x + 2, y - 1), new Point(x - 2, y - 1));
                dc.FillRectangle(Brush(c), new Rect(x - 1.5, y - 1, 3, 9));
                dc.FillRectangle(Brush(c), new Rect(x - 6, y + 8, 12, 3));
                break;
            case 2:
                // A key.
                dc.DrawEllipse(null, Pen(c, 3), new Point(x - 5, y), 5, 5);
                dc.FillRectangle(Brush(c), new Rect(x, y - 1.5, 12, 3));
                dc.FillRectangle(Brush(c), new Rect(x + 7, y, 2.5, 5));
                break;
            case 3:
                // A bell.
                Poly(dc, c, null, 0, new Point(x - 9, y + 7), new Point(x - 6, y - 6), new Point(x, y - 10), new Point(x + 6, y - 6), new Point(x + 9, y + 7));
                dc.FillEllipse(x, y + 9, 3, c);
                break;
            case 4:
                // A pocket watch.
                dc.DrawEllipse(Brush(Alpha(c, 0.4)), Pen(c, 3), new Point(x, y + 2), 8, 8);
                dc.FillRectangle(Brush(c), new Rect(x - 2, y - 11, 4, 5));
                dc.DrawLine(Pen(c, 2), new Point(x, y + 2), new Point(x + 4, y - 2));
                break;
            default:
                // A bottle.
                dc.FillRectangle(Brush(c), new Rect(x - 6, y - 3, 12, 14), 3);
                dc.FillRectangle(Brush(c), new Rect(x - 2.5, y - 12, 5, 10));
                break;
        }
    }

    /// <summary>Willy: a top hat, a coat, and legs that stride.</summary>
    private void DrawWilly(DrawingContext dc, double x, double feet, int facing, double walk, bool dead = false)
    {
        double y = feet;
        int frame = (int)(walk * 10) % 4;
        double stride = frame switch { 0 => 0, 1 => 5, 2 => 0, _ => -5 };
        uint skin = 0xFFF0C090, coat = dead ? 0xFFFFFFFF : 0xFFE0E0F0, trousers = 0xFF2A2A40, hat = 0xFF181820;
        // Legs and shoes.
        dc.DrawLine(Pen(trousers, 5), new Point(x - 3, y - 18), new Point(x - 3 + stride, y - 3));
        dc.DrawLine(Pen(trousers, 5), new Point(x + 3, y - 18), new Point(x + 3 - stride, y - 3));
        dc.FillRectangle(Brush(0xFF101010), new Rect(x - 6 + stride, y - 4, 8, 4));
        dc.FillRectangle(Brush(0xFF101010), new Rect(x - 2 - stride, y - 4, 8, 4));
        // Coat and arms.
        dc.FillRectangle(Brush(coat), new Rect(x - 8, y - 38, 16, 21), 3);
        dc.FillRectangle(Brush(0xFFD02020), new Rect(x - 2, y - 38, 4, 8));
        dc.DrawLine(Pen(coat, 4), new Point(x, y - 34), new Point(x + facing * 10 - stride * 0.6, y - 22));
        // Head, nose and top hat.
        dc.FillEllipse(x + facing * 1, y - 44, 7, skin);
        dc.FillEllipse(x + facing * 7, y - 43, 2.5, skin);
        dc.FillEllipse(x + facing * 4, y - 46, 1.4, 0xFF101010);
        dc.FillRectangle(Brush(hat), new Rect(x - 11, y - 51, 22, 3));
        dc.FillRectangle(Brush(hat), new Rect(x - 7, y - 64, 14, 14));
        dc.FillRectangle(Brush(0xFF6A2A8A), new Rect(x - 7, y - 54, 14, 3));
    }

    private void DrawGuardian(DrawingContext dc, Guardian gd)
    {
        double x = gd.X, y = gd.Def.Vertical ? gd.Y + 13 : gd.Y;
        int f = gd.Dir;
        double bob = Math.Sin(_clock * 12 + x * 0.1) * 2;
        switch (gd.Def.Look)
        {
            case 0:
                // A clanking robot.
                dc.FillRectangle(Brush(0xFFC0C0C8), new Rect(x - 11, y - 34 + bob, 22, 22), 3);
                dc.FillRectangle(Brush(0xFF202028), new Rect(x - 6 + f * 2, y - 29 + bob, 12, 6));
                dc.FillEllipse(x - 2 + f * 3, y - 26 + bob, 2, 0xFFFF2020);
                dc.DrawLine(Pen(0xFF8A8A90, 2), new Point(x, y - 34 + bob), new Point(x, y - 40 + bob));
                dc.FillEllipse(x, y - 41 + bob, 2.5, 0xFFFFE040);
                dc.FillRectangle(Brush(0xFF6A6A70), new Rect(x - 8, y - 12, 6, 12));
                dc.FillRectangle(Brush(0xFF6A6A70), new Rect(x + 2, y - 12, 6, 12));
                break;
            case 1:
                // A penguin.
                dc.DrawEllipse(Brush(0xFF202028), null, new Point(x, y - 18), 11, 17);
                dc.DrawEllipse(Brush(0xFFF0F0F0), null, new Point(x + f * 3, y - 16), 7, 12);
                dc.FillEllipse(x + f * 4, y - 28, 1.6, 0xFF000000);
                Poly(dc, 0xFFFFA020, null, 0, new Point(x + f * 9, y - 26), new Point(x + f * 15, y - 24), new Point(x + f * 9, y - 22));
                dc.FillRectangle(Brush(0xFFFFA020), new Rect(x - 7, y - 3, 6, 3));
                dc.FillRectangle(Brush(0xFFFFA020), new Rect(x + 1, y - 3, 6, 3));
                break;
            case 2:
                // A floating lantern-ghost.
                dc.DrawEllipse(Brush(Alpha(0xFFFFE080, 0.35)), null, new Point(x, y - 22 + bob), 16, 16);
                dc.DrawRectangle(Brush(0xFFFFC040), Pen(0xFF6A4A10, 2), new Rect(x - 8, y - 32 + bob, 16, 20), 3, 3);
                dc.FillEllipse(x - 3, y - 24 + bob, 2, 0xFF402000);
                dc.FillEllipse(x + 3, y - 24 + bob, 2, 0xFF402000);
                break;
            case 3:
                // A rat.
                dc.DrawEllipse(Brush(0xFF8A7A6A), null, new Point(x, y - 9), 13, 8);
                dc.FillEllipse(x + f * 12, y - 11, 6, 0xFF8A7A6A);
                dc.FillEllipse(x + f * 15, y - 13, 1.6, 0xFFFF2020);
                dc.FillEllipse(x + f * 9, y - 17, 3.5, 0xFFE0A0A0);
                dc.DrawLine(Pen(0xFFE0A0A0, 2), new Point(x - f * 12, y - 8), new Point(x - f * 24, y - 14 + bob * 2));
                break;
            case 4:
            {
                // A clockwork cog on legs.
                double a = _clock * 4 * f;
                for (int k = 0; k < 8; k++)
                {
                    double ang = a + k * Math.PI / 4;
                    dc.FillRectangle(Brush(0xFFD0A040), new Rect(x + Math.Cos(ang) * 13 - 3, y - 24 + Math.Sin(ang) * 13 - 3, 6, 6));
                }
                dc.FillEllipse(x, y - 24, 11, 0xFFB08030);
                dc.FillEllipse(x, y - 24, 4, 0xFF302010);
                dc.DrawLine(Pen(0xFF6A5A3A, 3), new Point(x - 5, y - 14), new Point(x - 7 + bob * 2, y));
                dc.DrawLine(Pen(0xFF6A5A3A, 3), new Point(x + 5, y - 14), new Point(x + 7 - bob * 2, y));
                break;
            }
            case 6:
            {
                // A bird, flapping.
                double flap = Math.Sin(_clock * 14) * 9;
                dc.DrawEllipse(Brush(0xFF4060C0), null, new Point(x, y - 22 + bob), 12, 8);
                Poly(dc, 0xFF6080E0, null, 0, new Point(x - 4, y - 24 + bob), new Point(x - 14, y - 32 + bob - flap), new Point(x + 4, y - 24 + bob));
                dc.FillEllipse(x + f * 10, y - 26 + bob, 5, 0xFF4060C0);
                Poly(dc, 0xFFFFC020, null, 0, new Point(x + f * 14, y - 27 + bob), new Point(x + f * 20, y - 25 + bob), new Point(x + f * 14, y - 23 + bob));
                break;
            }
            case 7:
            {
                // A rolling barrel.
                double a = _clock * 6 * f;
                dc.DrawEllipse(Brush(0xFF8A5A2A), Pen(0xFF3A2A10, 2), new Point(x, y - 15), 15, 15);
                for (int k = 0; k < 3; k++)
                {
                    double ang = a + k * Math.PI / 3;
                    dc.DrawLine(Pen(0xFF3A2A10, 2), new Point(x + Math.Cos(ang) * 14, y - 15 + Math.Sin(ang) * 14), new Point(x - Math.Cos(ang) * 14, y - 15 - Math.Sin(ang) * 14));
                }
                break;
            }
            case 8:
                // A hooded monk.
                Poly(dc, 0xFF6A4A2A, 0xFF2A1A0A, 2, new Point(x - 13, y), new Point(x - 8, y - 32), new Point(x, y - 40), new Point(x + 8, y - 32), new Point(x + 13, y));
                dc.FillEllipse(x + f * 2, y - 30, 5, 0xFF1A1008);
                dc.FillEllipse(x + f * 3, y - 31, 1.3, 0xFFFFE060);
                break;
            case 9:
                // A teddy bear.
                dc.FillEllipse(x, y - 14, 11, 0xFFB07840);
                dc.FillEllipse(x, y - 31, 8, 0xFFB07840);
                dc.FillEllipse(x - 7, y - 38, 3.5, 0xFFB07840);
                dc.FillEllipse(x + 7, y - 38, 3.5, 0xFFB07840);
                dc.FillEllipse(x + f * 3, y - 29, 3, 0xFFE0C090);
                dc.FillEllipse(x + f * 1, y - 33, 1.4, 0xFF000000);
                break;
            case 10:
                // A rubber duck.
                dc.DrawEllipse(Brush(0xFFFFE020), null, new Point(x, y - 12 + bob), 14, 10);
                dc.FillEllipse(x + f * 7, y - 26 + bob, 7, 0xFFFFE020);
                Poly(dc, 0xFFFF8020, null, 0, new Point(x + f * 12, y - 27 + bob), new Point(x + f * 19, y - 25 + bob), new Point(x + f * 12, y - 23 + bob));
                dc.FillEllipse(x + f * 9, y - 28 + bob, 1.4, 0xFF000000);
                break;
            default:
                // A walking mushroom.
                dc.DrawEllipse(Brush(0xFFE04040), null, new Point(x, y - 26), 14, 10);
                for (int k = 0; k < 3; k++) dc.FillEllipse(x - 7 + k * 7, y - 29 + (k % 2) * 3, 2.5, 0xFFFFFFFF);
                dc.FillRectangle(Brush(0xFFF0E0C0), new Rect(x - 6, y - 18, 12, 14), 3);
                dc.FillEllipse(x + f * 2, y - 13, 1.4, 0xFF000000);
                dc.DrawLine(Pen(0xFFF0E0C0, 3), new Point(x - 3, y - 4), new Point(x - 5 + bob * 2, y));
                dc.DrawLine(Pen(0xFFF0E0C0, 3), new Point(x + 3, y - 4), new Point(x + 5 - bob * 2, y));
                break;
        }
    }

    // ───────────────────────────── the panel ─────────────────────────────

    private void DrawPanel(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        double y = Field.Bottom;
        dc.FillRectangle(Brush(0xFF2040C0), new Rect(0, y + 6, Field.Width, 36));
        Text(dc, w.Def.Theme.Name, new Point(Field.Width / 2, y + 24), 32, 0xFFFFFFFF, center: true, shadow: false);
        Text(dc, $"ITEMS COLLECTED {w.ItemsTaken:000} OF {w.House.Items.Count:000}", new Point(30, y + 64), 30, 0xFFFFE040);
        Text(dc, "TIME " + GameEngine.TimeOfDay(w.Minutes), new Point(Field.Width - 30, y + 64), 30, w.Minutes > Sim.Midnight - 60 && (int)(_clock * 3) % 2 == 0 ? Red : 0xFFFFFFFF, right: true);
        Text(dc, $"SCORE {g.Score:000000}   HIGH {Math.Max(g.HighScores.TopScore, g.Score):000000}", new Point(30, y + 100), 24, 0xFFB0C0FF);
        DrawMap(dc, w, Field.Width - 30 - 6 * 22, y + 90);
        for (int i = 0; i < Math.Min(g.Lives - 1, 12); i++) DrawWilly(dc, 40 + i * 40, y + 212, 1, _clock + i * 0.25);
    }

    /// <summary>A small plan of the house: the rooms, where Willy is, and which still have something in them.</summary>
    private void DrawMap(DrawingContext dc, World w, double x0, double y0)
    {
        const double cw = 22, ch = 20;
        foreach (var r in w.House.Rooms)
        {
            var box = new Rect(x0 + r.HX * cw, y0 + r.Floor * ch, cw - 3, ch - 3);
            bool left = r.Items.Any(i => !w.Taken[i]);
            uint fill = r.Index == w.Room ? ((int)(_clock * 4) % 2 == 0 ? 0xFFFFFFFF : 0xFFFFE040) : left ? 0xFF4060A0 : 0xFF202838;
            if (r.Bedroom && r.Index != w.Room) fill = w.AllTaken ? 0xFFE04040 : 0xFF603030;
            dc.FillRectangle(Brush(fill), box);
        }
    }

    // ───────────────────────────── title, game over and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        // The house at night, its windows lit.
        dc.FillRectangle(Brush(0xFF080C20), new Rect(0, 0, Field.Width, Field.Height));
        for (int k = 0; k < 40; k++) dc.FillEllipse(Hash(k, 1, 5) % 1024, Hash(k, 2, 5) % 360, 1 + Hash(k, 3, 5) % 2, 0xFFE0E0FF);
        double hx = cx - 300, hy = 440;
        Poly(dc, 0xFF5A2A2A, 0xFF2A1010, 3, new Point(hx - 30, hy), new Point(cx, hy - 110), new Point(hx + 630, hy));
        dc.FillRectangle(Brush(0xFF6A4A3A), new Rect(hx + 470, hy - 120, 40, 70));
        dc.FillRectangle(Brush(0xFF8A6A5A), new Rect(hx, hy, 600, 260));
        for (int f = 0; f < 2; f++)
        for (int k = 0; k < 6; k++)
        {
            bool lit = Hash(k, f, (int)(_clock * 0.7)) % 3 != 0;
            dc.FillRectangle(Brush(lit ? 0xFFFFE070 : 0xFF203040), new Rect(hx + 30 + k * 95, hy + 24 + f * 110, 50, 70));
            dc.DrawLine(Pen(0xFF4A2A1A, 3), new Point(hx + 55 + k * 95, hy + 24 + f * 110), new Point(hx + 55 + k * 95, hy + 94 + f * 110));
        }
        string title = "AVA JET SET WILLY";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 26 + 26 + i * 52, ty = 92 + Math.Sin(_clock * 2.4 + i * 0.45) * 4;
            uint[] colours = [0xFFFFE040, 0xFF40FFFF, 0xFFFF40FF, 0xFF40FF40, 0xFFFF4040];
            Text(dc, title[i].ToString(), new Point(tx, ty), 96, colours[i % colours.Length], center: true);
        }
        Text(dc, "AFTER MATTHEW SMITH'S 1984 ZX SPECTRUM GAME", new Point(cx, 160), 24, Dim, center: true);
        dc.FillRectangle(Brush(0xB0000000), new Rect(0, 186, Field.Width, 100));
        Text(dc, "WILLY'S PARTY IS OVER, AND THE HOUSE IS A MESS. MARIA THE HOUSEKEEPER WON'T", new Point(cx, 204), 24, White, center: true);
        Text(dc, "LET HIM INTO BED UNTIL EVERY FLASHING ITEM IN EVERY ROOM IS PUT AWAY.", new Point(cx, 232), 24, White, center: true);
        Text(dc, "IT'S SEVEN IN THE MORNING. HE HAS UNTIL MIDNIGHT.", new Point(cx, 260), 24, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER TO START", new Point(cx, 300), 40, Yellow, center: true);
        DrawWilly(dc, (_clock * 80) % 1100 - 40, 698, 1, _clock);
        dc.FillRectangle(Brush(0xC0000000), new Rect(0, 700, Field.Width, 68));
        Text(dc, "LEFT/RIGHT (OR O/P): WALK    SPACE, Z OR UP: JUMP    H: PAUSE    M: SOUND", new Point(cx, 718), 20, 0xFFB0A0D0, center: true);
        Text(dc, "MUSIC: MOONLIGHT SONATA (BEETHOVEN, 1801), AND A TUNE OF ITS OWN", new Point(cx, 746), 18, Dim, center: true);
    }

    /// <summary>The end of a good game: Willy in bed at last.</summary>
    private void DrawBedtime(DrawingContext dc)
    {
        double cx = Field.Width / 2, floor = 560;
        dc.FillRectangle(Brush(0xFF100818), new Rect(0, 0, Field.Width, Field.Height));
        dc.FillRectangle(Brush(0xFF3A1A2A), new Rect(0, floor, Field.Width, Field.Height - floor));
        dc.FillRectangle(Brush(0xFF6A3A1A), new Rect(cx + 160, floor - 130, 20, 130));
        dc.FillRectangle(Brush(0xFF6A3A1A), new Rect(cx - 180, floor - 70, 16, 70));
        dc.FillRectangle(Brush(0xFFF0F0F0), new Rect(cx - 170, floor - 70, 330, 22), 6);
        dc.FillRectangle(Brush(0xFFFFFFFF), new Rect(cx + 90, floor - 92, 64, 24), 10);
        dc.FillEllipse(cx + 110, floor - 92, 13, 0xFFF0C090);
        dc.FillRectangle(Brush(0xFF181820), new Rect(cx + 96, floor - 128, 28, 26));
        dc.FillRectangle(Brush(0xFF181820), new Rect(cx + 88, floor - 106, 44, 5));
        dc.FillRectangle(Brush(0xFF4060C0), new Rect(cx - 170, floor - 64, 270, 26), 6);
        for (int k = 0; k < 3; k++)
        {
            double t = (_clock * 0.6 + k / 3.0) % 1;
            Text(dc, "Z", new Point(cx + 140 + t * 80, floor - 140 - t * 120), 20 + t * 30, Alpha(0xFFFFFFFF, 1 - t), center: true, shadow: false);
        }
        Text(dc, "SWEET DREAMS, WILLY", new Point(cx, 180), 80, Yellow, center: true);
        Text(dc, $"EVERY ITEM PUT AWAY  -  SCORE {Engine.Score}", new Point(cx, 260), 32, White, center: true);
    }

    /// <summary>Game over: a giant boot comes down on the miner, the way it had to.</summary>
    private void DrawBoot(DrawingContext dc)
    {
        double cx = Field.Width / 2, ground = 560;
        double t = Engine.StateTime;
        double drop = Math.Min(1, t / GameEngine.StompSeconds);
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        dc.FillRectangle(Brush(0xFF404040), new Rect(cx - 120, ground, 240, 40));
        if (drop < 1) DrawWilly(dc, cx, ground, 1, 0);
        double by = 60 + (ground - 60 - 130) * drop;
        // The leg, and the boot.
        dc.FillRectangle(Brush(0xFF2A4A8A), new Rect(cx - 40, 0, 80, by + 20));
        Poly(dc, 0xFF4A2A10, 0xFF1A0A00, 3, new Point(cx - 50, by), new Point(cx + 46, by), new Point(cx + 46, by + 90), new Point(cx + 120, by + 100), new Point(cx + 130, by + 130), new Point(cx - 50, by + 130));
        dc.FillRectangle(Brush(0xFF1A0A00), new Rect(cx - 50, by + 120, 180, 10));
        if (drop >= 1) Text(dc, "GAME OVER", new Point(cx, 220), 96, (int)(_clock * 4) % 2 == 0 ? Red : Yellow, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "THE TIDIEST WILLIES", new Point(cx, 120), 64, 0xFFFFE040, center: true);
        Text(dc, "RANK NAME    SCORE   ITEMS", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
