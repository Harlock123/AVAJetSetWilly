namespace AVAJetSetWilly.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>A moment before the game starts.</summary>
    Start,
    Play,
    /// <summary>Caught: a moment, then back where he first stood in the room, or the game is over.</summary>
    Dying,
    /// <summary>In bed, every item put away: the time left is counted into the score.</summary>
    Home,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Jump, Land, Item, AllTaken, Room, Crumble, Die, Tick, Stomp, Chime,
    SayBed,
    Music, MusicStop, TitleTune, GameOver, Victory,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 45;
    public const double HighScoreSeconds = 9;
    public const double StartSeconds = 1.6, DyingSeconds = 1.6, StompSeconds = 3.5, HomeSeconds = 6;
    public const int Lives0 = 7, MinutePoints = 10, HomeBonus = 5000;

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true, _music;
    private int _hour;

    public GameEngine(HighScoreTable? table = null)
    {
        HighScores = table ?? HighScoreTable.Load();
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public int Lives { get; private set; }
    public int Score => World.Score;
    /// <summary>Items collected this game (kept with a high score).</summary>
    public int Cleared => World.ItemsTaken;
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || c.Jump && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Music(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > StompSeconds + 1)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Cleared);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame()
    {
        World = new World();
        Lives = Lives0;
        Sim.Start(World, MansionBook.House);
        _hour = 0;
        SetPhase(Phase.Start);
        Message = World.Def.Theme.Name;
        MessageTime = StartSeconds;
        Pilot.Reset();
    }

    /// <summary>The time as the clock shows it, from 7:00am.</summary>
    public static string TimeOfDay(double minutes)
    {
        int m = 7 * 60 + (int)minutes;
        int hh = m / 60 % 24, mm = m % 60;
        return $"{(hh % 12 == 0 ? 12 : hh % 12)}:{mm:00}{(hh < 12 ? "am" : "pm")}";
    }

    public void StartGame()
    {
        NewGame();
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
    }

    private void StartAttract()
    {
        NewGame();
        State = GameState.Attract;
        StateTime = 0;
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Music(false);
        Message = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play) Music(true);
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds) { SetPhase(Phase.Play); Music(true); }
                break;
            case Phase.Play:
            {
                Sim.Step(w, c);
                // The clock chimes the hours.
                int hour = (int)(w.Minutes / 60);
                if (hour != _hour) { _hour = hour; Play(Sfx.Chime); }
                if (w.Minutes >= Sim.Midnight && !w.Home)
                {
                    // Midnight, and the night's over before it began.
                    Message = "MIDNIGHT";
                    MessageTime = 3;
                    End();
                    return;
                }
                if (w.Willy.Dead) { SetPhase(Phase.Dying); Music(false); }
                else if (w.Home)
                {
                    SetPhase(Phase.Home);
                    Music(false);
                    w.Score += HomeBonus;
                    Play(Sfx.SayBed);
                    Message = "EVERYTHING PUT AWAY - TO BED, WILLY!";
                    MessageTime = HomeSeconds;
                }
                break;
            }
            case Phase.Dying:
                if (PhaseTime > DyingSeconds)
                {
                    Lives--;
                    if (Lives <= 0) { End(); return; }
                    Sim.Respawn(w);
                    Pilot.Reset();
                    SetPhase(Phase.Play);
                    Music(true);
                }
                break;
            case Phase.Home:
                // The time to spare, counted in.
                if (w.Minutes < Sim.Midnight && PhaseTime > 0.5)
                {
                    double take = Math.Min(Sim.Midnight - w.Minutes, dt * 300);
                    int before = (int)w.Minutes;
                    w.Minutes += take;
                    w.Score += ((int)w.Minutes - before) * MinutePoints;
                    if (before / 10 != (int)w.Minutes / 10) Play(Sfx.Tick);
                }
                if (PhaseTime > HomeSeconds && w.Minutes >= Sim.Midnight - 0.001)
                {
                    Play(Sfx.Victory);
                    if (State == GameState.Attract) { ShowHighScores(-1); return; }
                    State = GameState.GameOver;
                    StateTime = 0;
                    return;
                }
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void End()
    {
        Play(Sfx.Stomp);
        Play(Sfx.GameOver);
        Music(false);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into the house, past the opening moment.</summary>
    public void Go()
    {
        NewGame();
        State = GameState.Playing;
        SetPhase(Phase.Play);
        Message = null;
    }

    public void SetLives(int n) => Lives = n;
}
