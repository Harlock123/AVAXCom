# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its screens and
controls; this file covers how the code is put together and the rules that keep it working.

## What this is

A turn-based strategy game in the style of the 1994 X-COM: UFO Defense, called Vigil: The Silent Invasion in the
game, with names of its own. It is written in C# on .NET 10 and Avalonia 12 and drawn through Avalonia's `DrawingContext`. It has two
halves: the geoscape (a real-time globe with bases, research, manufacture, interception and funding) and the
battlescape (isometric, turn-based ground missions). One self-contained executable per platform (Windows, macOS and
Linux, x64 and ARM). All the art is drawn in code, and the sounds, voices and music are synthesised by
`tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAXCom.slnx                         # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAXCom.slnx                          # every test runs headless: no window opens
dotnet run --project src/AVAXCom                  # play it; --no-audio for silence
XCOM_SHOTS=$PWD/Images dotnet test tests/AVAXCom.Tests --filter ScreenshotTests   # refresh the README's pictures
XCOM_REVIEW=/some/folder dotnet test tests/AVAXCom.Tests --filter ReviewShots     # extra screens to look over
XCOM_PROBE=1 dotnet test tests/AVAXCom.Tests --filter PilotProbe --logger "console;verbosity=detailed"
python3 tools/make_sounds.py                      # rebuild src/AVAXCom/Sounds (standard-library Python only)
```

- The probe plays ground missions with the computer pilot.
  - `XCOM_PROBE=war` runs a whole war; `XCOM_PROBE=mission` runs one mission from a real campaign.
  - `XCOM_MISSIONS` (how many), `XCOM_ONLY` (one index), `XCOM_TRACE`, `XCOM_NIGHT`, `XCOM_UFO` and `XCOM_CRASH`
    tune it. Read `tests/AVAXCom.Tests/PilotProbe.cs` for the details.
- `tools/speech.py` is a small formant speech synthesiser that `make_sounds.py` imports for the voices.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAXCom/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, attract, geoscape, briefing, battle, debrief, game over, initials,
    high scores) and the geoscape screens. It also has `Controls`, the `Sfx` list, the `Iso` measures and the
    fixed-step loop.
  - Geoscape:
    - `Campaign.cs` is the war's state and clock: UFOs, sites, nations, events, missions and the monthly report.
    - `Base.cs` has facilities, craft, research and production jobs.
    - `AirCombat.cs` is the interception window.
    - `Geo.cs` has the globe's geometry, `LandMask`, nations and cities (`Earth`).
  - Battlescape:
    - `Battle.cs` is the battlefield (tiles of floor, walls and objects) and its units (`BUnit`).
    - `BattleGen.cs` builds each mission's map.
    - `Tac.cs`, `Tac.Fire.cs` and `Tac.Sight.cs` are its rules: time units, firing, sight, explosions and morale.
    - `PathFinder.cs` finds the way in time units; `AlienBrain.cs` plays the aliens' turn.
  - `Rules.cs` holds the data tables: items, races, UFOs, craft, facilities, research topics, manufacture and armour.
  - `Soldier.cs` has soldiers and the `Dice` generator; `Debrief.cs` scores a mission and applies it to the
    campaign.
  - `SaveFile.cs` saves campaigns as text in numbered slots (slot 0 is the autosave).
  - `Pilot.cs` is the computer player, used by the attract mode, the tests and the probe.
  - `InputHandler.cs` (scroll keys, mouse point, gamepad) and `ControllerManager.cs` (SDL2) produce `Controls`.
    One-shot keys and clicks are handled by `GameCanvas`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAXCom/Views/`: `GameCanvas` (split over partial files for the geoscape, base, battle, units and screens)
  draws a fixed 1024 x 768 picture scaled to the window and runs the 60 Hz timer. `GlobePainter`, `IsoArt`,
  `BaseArt` and `Pix` paint the globe, the isometric scenery and the base pixel by pixel. Ctrl+S saves a screenshot
  to the Pictures folder.
- `src/AVAXCom/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVAXCom.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`.
  - `TestWorld.cs` makes games in temporary folders and flat test fields.
  - The tests cover the engine, the geoscape, the battlescape (`TacticsTests`) and the canvas.

## Rules that keep it working

- **Fixed steps.** `GameEngine` advances in fixed 1/60 s steps (`Iso.Step`) from an accumulator, independent of
  the frame rate, so tests can play deterministically from a seed. Draw random numbers from the seeded `Dice` that
  owns the work: `Campaign.Rng` on the geoscape, `Battle.Rng` in battle, `AirCombat`'s own in an interception.
  Never use `Random.Shared`. `Dice` state is saved with a campaign, so a loaded game carries on the same way.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the game, draws it and turns input into orders.
- **Input goes through `InputHandler`.** Scrolling uses the engine's sign: up is -1. Test new controls through
  `InputHandler` or the canvas (`CanvasTests`), not by setting `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. A test in `EngineTests` covers this.
- **Save files.** `SaveFile` writes one line per thing, fields separated by bars. Changing a saved class means
  changing `SaveFile` too; the save and load tests in `EngineTests` must still pass.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores and saved campaigns live in the app-data folder
  (`AVAXCom`). Tests give the engine a fresh folder under the system temp path (`TestWorld.TempFolder()`) and run
  with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If you change battle rules, maps or the aliens' minds, run the probe above to see the computer squad still
  copes.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release; either then asks the portfolio site to rebuild.
