# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

`metro` is a Unity rhythm-game prototype: four lanes (L / R on top, ZL / ZR on the bottom) carrying button notes and stick-flick notes, plus two analog-stick lasers flying through the space between the rows, judged against the audio DSP clock.

- Unity **6000.3.10f1**, URP 17.3, uGUI 2.0 (legacy `UnityEngine.UI.Text`, not TextMeshPro).
- **New Input System only** (`activeInputHandler: 1`). `UnityEngine.Input` throws at runtime; read devices via `Gamepad.current` / `Keyboard.current`. `Assets/InputSystem_Actions.inputactions` is the unused template asset — gameplay code polls devices directly.
- All gameplay scripts live in the default `Assembly-CSharp` (no `.asmdef` files, no namespaces).

## Working with the project

There is no build script, CI, lint config, or test suite. `com.unity.test-framework` is installed but no tests exist; adding tests means creating a test assembly (`.asmdef`) first, and since gameplay code has no asmdef of its own, it would need one too before a test assembly can reference it.

Everything runs through the Unity Editor:

- The only scene in the build is `Assets/Scenes/Game.unity`. Open it and press Play.
- The Editor is driven with the official **Unity CLI** (`unity`, installed at `%LOCALAPPDATA%\Unity\bin`) through the `com.unity.pipeline` package — there is no MCP server. It only works while the Editor is open on this project; `unity status` shows connected Editors, and `unity open .` launches one (takes a minute or so). If no Editor is connected, C# changes cannot be compile-checked.
  - Editor commands run as `unity command --project-path . "<group> <name>" [--param value]`. The command name is one quoted argument, and CLI options (`--project-path`, `--timeout`, global `--json`) must come *before* it — anything after it is forwarded to the Editor command as a parameter. `unity list --project-path .` lists every command; add `--json` for parameter details.
  - Compile check: `"script recompile"`, then `"console status"` (`groundTruth.compilationFailed` / `compiling`) and `"console get" --level error --tail 20`.
  - Play mode: `"editor.playmode start" --run-in-background true` / `"editor.playmode stop"`.
  - Screenshots: `"capture game_view" --output-path <png>` (also `capture scene_view`).
- `Assets/Screenshots/` holds verification captures of the stage layout.

Unity specifics that matter when editing files directly:

- Every asset has a sibling `.meta` holding its GUID. Move/rename the `.meta` together with the asset, and never regenerate one for an existing asset — scene references (e.g. `Game.unity` → script GUIDs, `demo_chart.json`) break silently.
- `Library/`, `Logs/`, `UserSettings/` are generated; do not edit or rely on them.
- `.git/` is currently an empty directory (not an initialized repository) and there is no `.gitignore`. If git is set up, add a Unity `.gitignore` before the first commit.

Template leftovers unrelated to the game: `Assets/Scenes/SampleScene.unity`, `Assets/TutorialInfo/`, `Assets/Readme.asset`, `Assets/_Recovery/`.

## Architecture

All runtime components sit on one `GameSystems` GameObject in `Game.unity`. Cross-references are serialized where set and otherwise resolved with `FindFirstObjectByType` in `Start`/`Awake`.

```
ChartLoader ──chart──▶ NoteSpawner ◀──consume── JudgeSystem ──events──▶ RhythmJudgementUI / ScoreUI
                          ▲    │                     ▲
SongClock ──SongTime──────┘    └─positions─▶ RhythmStageView (static geometry)
      └────────────SongTime──────────────────┘
```

- **`SongClock`** (`Scripts/Core`) — the single time source. `SongTime = AudioSettings.dspTime - dspStartTime`, with audio started via `PlayScheduled`. All spawning, movement and judgement use this `double`; never use `Time.time`/`deltaTime` for anything gameplay-timed. The scene currently has no `AudioSource` assigned, so the clock runs silently.
- **`ChartLoader`** (`Scripts/Core`) — parses a `TextAsset` with `JsonUtility` in `Awake`, sorts notes/lasers by time, sorts each laser's points, and drops lasers with fewer than two points. Also defines the chart data types (`SongChartData`, `TapNoteData`, `LaserNoteData`, `LaserPointData`, `NoteDirection`).
- **`StickInput`** (`Scripts/Core`) — static, the only place the sticks are read. `Read(side)` returns the stick clamped to the unit circle (arrow keys for both sides when no gamepad is connected); `GetDirection` turns a stick position into a `NoteDirection`.
- **`NoteSpawner`** (`Scripts/Gameplay`) — owns the live note lists. Spawns each note `travelTimeSeconds` before its target time, tells every `NoteView` its `progress` each frame, and feeds stick input into active lasers. It exposes the `TryConsume*` methods, which are the only way notes leave play (they remove from the list and `Destroy` the object).
- **`NoteView`** / **`LaserNoteView`** (`Scripts/Gameplay`) — one live note each. `NoteView.UpdateView(progress)` places the slab on its lane and, for a directional note, the camera-facing arrow (a sibling object it destroys in `OnDestroy`). `LaserNoteView` holds the laser's timed points, draws the not-yet-arrived part as two ribbon meshes (band + core) plus a target ring on the judgement plane, and accumulates tracking accuracy.
- **`JudgeSystem`** (`Scripts/Gameplay`) — polls lane buttons (pressed / held) and stick flicks, asks `NoteSpawner` to consume the closest matching note, sweeps expired notes as misses, and judges finished lasers. Holds the timing windows as `const` and reports through the `JudgementResolved` / `LaserJudgementResolved` events. It keeps no score state.
- **`RhythmStageView`** (`Scripts/Presentation`) — `[ExecuteAlways]`: builds the entire 3D stage from its serialized `StageLayout` (camera, depth, lane size, laser space, colours) in `OnEnable`, in edit mode too, and overrides the main camera's transform/FOV. The generated `RhythmStage_3D` objects are `DontSave | NotEditable` previews, rebuilt whenever a layout value changes (also during Play) — tune the stage via the Inspector values, not the objects. The far end (奥) is the dark panel in the screen centre and everything travels outward to the near end (前): the L / R lanes form the ceiling of a tunnel running to the top edge, the ZL / ZR lanes its floor running to the bottom edge, with no gap between the two lanes of a row. The open space between the rows is the laser space, marked by a faint ring / cross guide and the two stick cursors (left = diamond, right = square). Its static `GetLaneWorldPosition` / `GetLaneWidth` / `GetLaneRotation` / `GetLaneInward` / `GetLaserWorldPosition` / `GetDepthScale` are the source of truth for where things are at a given `progress` (0 = far, 1 = judgement line); `NoteView`, `LaserNoteView` and the HUD labels call them, so stage geometry constants and note motion stay in sync only through these functions. It also owns `CreateMaterial` / `CreateTransparentMaterial` and the stick colours (`GetStickColor`).
- **`RhythmJudgementUI`** (`Scripts/Presentation`) — tracks score/combo from the judge events and drives the HUD. The `RhythmHUD` canvas and its texts are saved in `Game.unity` (created once with the editor menu **Metro > Build HUD Into Scene**, `Scripts/Editor/MetroSceneTools.cs`) and referenced through serialized fields; if they are missing, `BuildHud` creates the same HUD at runtime. This is the HUD actually visible in the scene. The lane labels are created in `Start` (after the stage has placed the camera) by projecting stage positions with `WorldToViewportPoint`.
- **`ScoreUI`** (`Scripts/UI`) — an older inspector-wired score display with the same scoring logic. It is in the scene but its `Text` references are unassigned, so it is inert.

### Conventions that are easy to miss

- **Everything visual is procedural** (except the HUD canvas saved in the scene). There are no prefabs or material assets; `NoteSpawner` falls back to `CreatePrimitive`/`AddComponent` when its prefab fields are null (which they are), and materials are created at runtime by `RhythmStageView.CreateMaterial` from `Universal Render Pipeline/Unlit` (fallback `Unlit/Color`), setting both `_BaseColor` and `_Color`. Lasers use `CreateTransparentMaterial` (`Sprites/Default`, alpha-blended, no depth write). Changing the look means changing code, not assets.
- **Deliberate duplication to keep in sync:** the score table (Perfect 1000 / Great 700 / Good 400, Miss resets combo) is in both `RhythmJudgementUI` and `ScoreUI`; the timing-window and key-mapping text on the HUD is the `TimingWindows` Text in the scene, not derived from the `JudgeSystem` constants.
- **Four lanes.** Chart `lane` 0 = L, 1 = R (upper row), 2 = ZL, 3 = ZR (lower row). ZL sits under L and ZR under R, like the controller; `LowerRowCrossed` in `RhythmStageView` can swap the lower row (the original design sketch drew it crossed). `lane % 2` is the side: it picks the button-note colour (orange / cyan) and the stick that plays a directional note.
- **Stick colours.** Everything played with the left stick is cyan and with the right stick magenta: lasers, their target rings, the cursors and the slabs of directional notes.
- **Layout is tuned for 16:9.** The lanes span most of the screen width, so the HUD text sits at mid height on the left and right edges and the judgement text is small, on the far wall. On narrower views `ConfigureCamera` widens the FOV so the outer lane edges stay on screen.
- **Note judgement:** windows are ±35 / 70 / 110 / 160 ms for button and directional notes alike. An input only consumes a note within the 160 ms miss window; one between 110 and 160 ms resolves as `Miss`, and inputs with no matching note in range are ignored.
- **Directional notes** (`direction` ≠ None) need the lane button and the side's stick together, in either order: the note is hit by whichever input arrives second, and that moment is what gets judged. A button press consumes the closest directional note in its lane whose direction the stick already points in; a flick consumes one while the lane button is held. A flick is the frame the stick starts pointing in a direction (past 0.6 from the centre, or swinging to another direction; it re-arms below 0.35). A press with the stick centred or pointing elsewhere leaves the directional note alone.
- **Laser judgement** is positional. Each frame while the laser is crossing the judgement plane, the distance between the side's stick and the laser's current point is sampled: 1 within 0.30, falling to 0 at 0.75 (stick units). The average becomes `TrackingAccuracy` (≥0.90 Perfect, ≥0.70 Great, ≥0.45 Good), resolved once when the laser ends.
- **Input mapping:** lanes L / R / ZL / ZR = gamepad left shoulder / right shoulder / left trigger / right trigger, or keyboard `D` / `K` / `F` / `J`. Left stick = left laser and directional notes on L / ZL; right stick = right laser and directional notes on R / ZR. The arrow keys drive both sticks at once, but only when no gamepad is connected.

### Chart format

`Assets/Charts/demo_chart.json`, loaded through the `chartJson` reference on `ChartLoader`. Because it is parsed with `JsonUtility`, field names must match the C# fields exactly, enums are integers, and missing/extra fields fail silently.

```json
{
  "title": "Flick & Laser Demo",
  "bpm": 120,
  "notes":  [{ "lane": 0, "time": 1.50 }, { "lane": 1, "time": 2.00, "direction": 3 }],
  "lasers": [{ "side": 0, "points": [{ "time": 6.0, "x": -1.0, "y": 0.0 }, { "time": 7.0, "x": 1.0, "y": 0.0 }] }]
}
```

Times are seconds on the song clock (not beats; `bpm` is currently unused). `lane`: 0 L, 1 R, 2 ZL, 3 ZR. `direction`: 0 or omitted = plain button note, 1 Up, 2 Down, 3 Left, 4 Right. Laser `side`: 0 = left stick, 1 = right stick. A laser moves in a straight line between consecutive `points`; `x` / `y` are stick positions (-1..1, right and up positive) and are clamped to the unit circle.
