# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A flip-screen platform game round a mansion, in the style of Matthew Smith's 1984 ZX Spectrum game Jet Set
Willy, in C# on .NET 10 and Avalonia 12, drawn through Avalonia's `DrawingContext`. One self-contained executable per platform
(Windows, macOS and Linux, x64 and ARM). Everything in it is original: the rooms and graphics are made in code,
and the sounds, tunes and speech are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAJetSetWilly.slnx                  # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAJetSetWilly.slnx                   # every test runs headless: no window opens
dotnet run --project src/AVAJetSetWilly           # play it; add -- --no-audio for silence
WILLY_SHOTS=$PWD/Images dotnet test tests/AVAJetSetWilly.Tests --filter ScreenshotTests   # refresh the README's pictures
WILLY_PROBE=1 dotnet test tests/AVAJetSetWilly.Tests --filter PilotProbe --logger "console;verbosity=detailed"
python3 tools/make_sounds.py                      # rebuild src/AVAJetSetWilly/Sounds (standard-library Python only)
```

The probe plays the computer Willy round the whole house, for tuning; `WILLY_PROBE=lazy` runs one that walks
right and jumps now and then. Without the variable it does nothing. `tools/speech.py` is the small formant speech
synthesiser that `make_sounds.py` imports for the spoken words.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAJetSetWilly/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, attract, play, pause, game over, initials, high scores) and the
    fixed-step loop.
  - `Sim.cs` steps the house (`Sim.Step`): Willy walking, jumping and falling from room to room, conveyors,
    crumbling ledges and guardians.
  - `World.cs` holds everything that moves or changes, kept together so it can be copied whole, plus the copyable
    `Dice` random source.
  - `Mansion.cs` has the screen measures (`Field`), `Controls`, and builds the house (`Mansion.House`): its rooms,
    guardians and items.
  - `Graph.cs` maps every place Willy can stand in a room and where each move takes him. A room is kept only if
    every way in leads to every way out, and the computer player plans its route by it.
  - `Pilot.cs` is the computer Willy, used by the attract mode and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepad) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAJetSetWilly/Views/`: `GameCanvas` (split over partial files) draws a fixed 1024x768 picture scaled to
  the window and runs the 60 Hz timer.
- `src/AVAJetSetWilly/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVAJetSetWilly/Sounds/`: the generated WAV files.
- `tests/AVAJetSetWilly.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `MansionTests` checks the house's shape
  and that every room can be crossed.

## Rules that keep it working

- **Fixed steps.** The simulation advances in fixed 1/60 s steps (`Field.Step`), independent of the frame rate,
  so tests play deterministically. The house is built once from a fixed `Dice(1984)`, and play itself draws no
  random numbers; never use `Random.Shared`.
- **Keep `World` copyable.** Any new state that changes during play belongs in `World` and must be copied with it.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** `Controls` is only `Dx` (negative is left) and `Jump`; Up, W and the
  D-pad's up jump. Test new controls through `InputHandler`, not by building `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAJetSetWilly`). A
  `GameEngine` built without a table loads that file, so tests load the table from a temporary folder and set
  `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If you change a room, `MansionTests` must still find every way in and every item reachable.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release. After a release it asks the portfolio site to rebuild, if the `SITE_REFRESH_TOKEN` secret is set.
