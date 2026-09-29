# ScrewTweaks

**English** | [中文](README.zh-CN.md) | [日本語](README.ja.md)

A BepInEx mod suite for **Screw Drivers**, aimed at improving the driving experience in a
sim-racing direction: real tire slip behaviour, driver aids built on top of it, and a few
quality-of-life fixes.

It is built around replaceable slots rather than one fixed solution. The tire model, the ABS and
traction algorithms and the panel are all public extension points, so the implementations that ship
are just the ones that happen to ship — another developer can drop in their own without touching
this code. See [Writing your own algorithm](#writing-your-own-algorithm).

The suite is split into independent plugins, so you can install, enable and tune them separately.
Everything is configured from one shared in-game panel plus plain `.cfg` files.

---

## Contents

- [Install](#install)
- [Building from source](#building-from-source)
- [Plugins and keys](#plugins-and-keys)
- [The in-game panel](#the-in-game-panel)
- [Tuning reference](#tuning-reference)
  - [Tire physics](#tire-physics)
  - [ECU](#ecu)
  - [Steering](#steering)
  - [Other plugins](#other-plugins)
- [Writing your own algorithm](#writing-your-own-algorithm)
  - [A panel section](#1-a-panel-section)
  - [A tire model](#2-a-tire-model)
  - [An ABS or traction algorithm](#3-an-abs-or-traction-algorithm)
- [Behaviour notes and gotchas](#behaviour-notes-and-gotchas)
- [Design notes](#design-notes)
- [Third-party](#third-party)

---

## Install

1. Install **BepInEx 5 (x64)** into your Screw Drivers folder if you have not already
   (`BepInEx/` and `winhttp.dll` next to `Screw Drivers.exe`).
2. Drop every `ScrewTweaks.*.dll` into `BepInEx/plugins/`.
3. Launch the game. A log line per plugin should appear in `BepInEx/LogOutput.log`.

> `ScrewTweaks.Panel.dll` **must** be present. It is the panel host; the feature plugins register their
> sections into it. Everything else is optional and independent.

---

## Building from source

Requires the .NET SDK and a local copy of the game (the build references `Assembly-CSharp.dll` and
`0Harmony.dll` out of the game folder). The game path is resolved in this order:

1. `-p:GameDir="C:\path\to\Screw Drivers"`
2. the `SCREWDRIVERS_DIR` environment variable
3. a `.env` file at the repo root (`SCREWDRIVERS_DIR=...`, see `.env.example`)

```powershell
dotnet build ScrewTweaks.sln --no-restore -m:1
```

Every plugin is copied into `BepInEx/plugins` automatically after a successful build.

> Plain `dotnet restore` can hang against this NuGet setup; `--no-restore` after a one-time restore
> is the reliable path. `-m:1` avoids a parallel-build quirk on some machines.

Key bindings are compile-time defaults in `keybinds.props` and can be overridden at runtime in the
plugin's `.cfg`.

### Versioning

Each plugin has its own version, set in exactly one place: `<Version>` in that plugin's `.csproj`.
The `[BepInPlugin]` version string and the DLL metadata are generated from it, so there is nothing
else to keep in sync.

A release is a *bundle*, not a single version number:

- a plugin's version only moves when that plugin changes. A plugin that did not change keeps its
  version, and that is the correct behaviour;
- the git tag is a date (`2026.09.29`) and identifies the bundle, so it cannot be mistaken for any
  component's version;
- [`BepInDependency`](https://docs.bepinex.dev) is where versions actually matter: a plugin can
  require a minimum version of another, and BepInEx will refuse to load it with a clear error rather
  than fail at run time.

`tools/release.ps1` does the tedious part — working out which plugins changed since the last tag:

```powershell
pwsh tools/release.ps1              # what changed, and the current version of each plugin
pwsh tools/release.ps1 -Package     # the same, then build and zip the bundle
```

To see every version without git history:

```powershell
dotnet build ScrewTweaks.sln --no-restore -m:1 -t:ListVersions
```

---

## Plugins and keys

| Assembly | Purpose | Key |
|---|---|---|
| `ScrewTweaks.Panel` | Shared F7 panel host. No features of its own. | **F7** |
| `ScrewTweaks.Physics.Tires` | Pluggable tire model slot (in-game selectable) | **F9** (record telemetry) |
| `ScrewTweaks.ECU` | ABS / traction control channels | — |
| `ScrewTweaks.Steering` | Instant Steering + Steering Limit Relax | — |
| `ScrewTweaks.AutoShift` | Faster automatic shifting (F7 → Auto Shift) | — |
| `ScrewTweaks.EngineSound` | Hybrid engine sound (both engine types audible) | — |
| `ScrewTweaks.PowerFactor` | Per-engine-type power factor, saved with the car (fully automatic) | — |

Other keys: **F8** dumps static tire/wheel data to `BepInEx/ScrewTweaks.tire-dump.txt`.

---

## The in-game panel

Press **F7** for a tabbed window. Each feature plugin registers a titled section:

- **ECU** — ABS and traction channels
- **Tires** — tire model selection, tuning, live per-wheel telemetry
- **Auto Shift** — on/off for the faster shift timing

The panel unlocks the cursor while open. Sections are listed in plugin load order.

---

## Tuning reference

All values are persisted in `BepInEx/config/dev.dwecirn.screwtweaks.<name>.cfg`.

### Tire physics

Select a model in **F7 → Tires**. The choice is remembered across sessions.

| Setting | Default | Meaning |
|---|---|---|
| `Model/Selected` | `Native` | `Native` = the game's own friction, unchanged |
| `Pacejka/GripScale` | `1.00` | Multiplier on peak grip. **1.0 = exactly the game's peak** |
| `Pacejka/CombinedSlip` | `1.00` | `0` = longitudinal and lateral independent, `1` = full friction ellipse |
| `Pacejka/RelaxationLength` | `0.30` | Distance [m] the tire needs to build slip, at a 0.30 m wheel. Scales with wheel radius. `0` = off |
| `Pacejka/CamberThrust` | `0.015` | Camber thrust as a fraction of wheel load per degree |
| `Pacejka/CamberDynamics` | `1.00` | How much camber softens the tire (peak moves later, lateral peak drops). `0` = thrust only |
| `Pacejka/GeometryShapeCoupling` | `0.00` | How strongly the contact patch (radius × width) moves the curve peak. **Off by default** |

Everything else the model needs comes from the game itself and is read per wheel and per step:
`Grip` → `maximumTireGripForce`, `loadGripCurve`, the tire's own `FrictionPresetAsphalt` /
`FrictionPresetSand` (picked per surface), `forceCoefficient`, `camberAngle`, motor and brake torque.

**Peak grip is matched to the native model**, so switching models changes *behaviour*, not how much
grip the car has. `GripScale` is the multiplier if you want to change grip itself.

**The `Tires` section also shows live per-wheel telemetry:**

```
PartWheelDirt2 g= 0.30  BCDE=(  7.0, 1.10, 0.83, 1.00)  k= 0.021/ 0.000 a=  -4.1/  -3.9 y=0.51 ...
```

`k` / `a` are shown as *relaxed / kinematic*; `BCDE` is the friction preset actually in use, which
changes as you drive onto a different surface — so you can watch the per-tire curves switch.

**F9** records 30 s of every wheel to `BepInEx/ScrewTweaks.tire-telemetry.csv`
(`t, wheel, body, tire, tireGrip, BCDE, kappa, alphaDeg, kappaRaw, alphaRawDeg, Fx, Fy, Fz, vx, omega, fwdMax, sideMax, radius, sigma, peak, camberDeg, camberFx`).

### ECU

Two independent channels, each with a **registered algorithm** or one of two reserved modes:

- **`Native`** — leave the game's own aid alone. *(default)*
- **`Off`** — disable it, without replacing it.
- anything else — the name of a registered algorithm (built in: **`Progressive`**).

| Setting | Default | Meaning |
|---|---|---|
| `Channels/Abs` | `Native` | ABS algorithm |
| `Channels/Traction` | `Native` | Traction algorithm |
| `Abs/TargetSlip` | `0.12` | Slip the progressive ABS holds. The game's asphalt curve peaks at ~0.125 |
| `Abs/Gain` | `6.0` | How hard the brake is cut as slip exceeds the target |
| `Abs/Floor` | `0.10` | Minimum fraction of the brake kept while slipping |
| `Traction/TargetSlip` | `0.12` | Slip the progressive traction control holds |
| `Traction/Gain` | `3.0` | How hard the drive torque is cut as slip exceeds the target |

Selecting any algorithm **neutralises the game's own TCS/ABS** on the managed channel so the two do
not fight. Engine braking runs through the same brake channel as the foot brake, so the ABS covers
coasting as well.

### Steering

- **Instant Steering** — a per-suspension on/off property in the car builder. Binary (keyboard /
  d-pad) input is applied immediately instead of being ramped by `FrontWheelsSteerer`, the same way
  an analog stick already behaves.
- **Steering Limit Relax** — a slider on the game's own controls settings page, below *Ignore Steer
  Angle Limit*. Blends the game's speed-sensitive steering limit toward "no limit". `0` = vanilla.

### Other plugins

- **Auto Shift** — `General/Enabled` in `dev.dwecirn.screwtweaks.autoshift.cfg`, and the same entry
  as the **F7 → Auto Shift** checkbox. Shifts are taken 0.2 s after the RPM threshold is crossed
  instead of 1 s, with a shorter cooldown and a shorter torque cut. Unticking the box writes the
  game's own values back.
- **Engine Sound** — restores sound for the engine type the stock selector drops on hybrids, and
  mixes the two by instantaneous torque.
- **Power Factor** — a per-engine-type power factor that travels with the car's saved file. Fully
  automatic: it injects and persists through Harmony patches and needs no key.

---

## Writing your own algorithm

The suite is built to be extended. Three real extension points, all public API.

Reference the plugin DLL you are extending from your own project and set it to **not** copy locally
(the game's BepInEx already provides it):

```xml
<Reference Include="ScrewTweaks.ECU">
  <HintPath>$(GameDir)\BepInEx\plugins\ScrewTweaks.ECU.dll</HintPath>
  <Private>false</Private>
</Reference>
```

and declare the dependency so load order is correct:

```csharp
[BepInDependency("dev.dwecirn.screwtweaks.ecu")]
```

### 1. A panel section

```csharp
using ScrewTweaks.Panel;

private void Start() => PanelHost.Register("My Section", DrawSection);

private void DrawSection()
{
    GUILayout.Label("hello");
    if (GUILayout.Button("do a thing")) { /* ... */ }
}
```

`PanelHost.Register(title, draw)` is called from your `Start()`. The draw action re-runs every frame
inside a scroll view; use `GUILayout`. Registering the same title again replaces the action.

### 2. A tire model

Implement `ITireModel` and register it. It shows up in the **Tires** dropdown immediately.

```csharp
using ScrewTweaks.Physics.Tires;

public sealed class MyTire : ITireModel
{
    public string Name => "MyTire";
    public string Description => "What it does.";

    // Return true if you produced the forces and integrated the wheel spin.
    // Return false to let the game's own friction run this step.
    public bool Apply(WheelController wheel, float dt)
    {
        // Speeds are already filled in for you:
        float vx = wheel.forwardFriction.speed;
        float vy = wheel.sideFriction.speed;

        // ... compute fx, fy from your own model ...

        wheel.forwardFriction.force = fx;
        wheel.sideFriction.force = fy;
        wheel.forwardFriction.slip = slipRatio;   // kinematic slip, see gotchas
        wheel.sideFriction.slip = slipAngle;
        // and integrate wheel.wheel.angularVelocity yourself
        return true;
    }
}

// in Start():
TireModels.Register(new MyTire());
```

### 3. An ABS or traction algorithm

Implement `IBrakeAid` or `IDriveAid` and register it. It appears in the **ECU** dropdown
automatically, and is saved to the config **by name**.

```csharp
using ScrewTweaks.ECU;
using UnityEngine;

public sealed class MyAbs : IBrakeAid
{
    public string Name => "MyAbs";
    public string Description => "Example: bang-bang around the peak slip.";

    public float Apply(in AidContext ctx, float desiredBrake)
    {
        if (desiredBrake <= 0f) return desiredBrake;

        // ctx.Controller is the NWH wheel: load, radius, angular velocity, motor/brake torque,
        // the active friction preset, the latest slip values. Null off the NWH backend.
        if (ctx.Controller == null) return desiredBrake;

        float peakSlip = 0.125f;                     // where the game's asphalt curve peaks
        return Mathf.Abs(ctx.ForwardSlip) > peakSlip ? 0f : desiredBrake;
    }
}

// or: EcuAids.Register(new MyTraction());  for IDriveAid
EcuAids.Register(new MyAbs());
```

`IBrakeAid` / `IDriveAid` are called once per wheel per physics step, *after* the game has computed
its own torque but *before* it is handed to the wheel, so returning a modified value is all that is
needed. The game's own aid on that channel is neutralised for you.

---

## Behaviour notes and gotchas

Collected because they are easy to get wrong and the symptoms are confusing.

**The tire slot owns more than the forces.**
`WheelController.FrictionUpdate` also publishes the contact speeds, the wheel RPM and the
`wheelHit` slip values that the game's drivetrain and TCS read back. The slot host does all of that
for you, before and after your model runs — that is why a model only has to fill the forces, the
slips and the wheel spin.

**Publish the *kinematic* slip, not your internal one.**
`forwardFriction.slip` / `sideFriction.slip` are the game's feedback channel: `MechanicalOutputWheel`
reads them for its own ABS/TCS, and the ECU aids read them too. With a relaxation-length model,
publishing the relaxed value makes every aid react a relaxation length late — the wheels lock before
the ABS responds. Publish what the wheel is kinematically doing.

**Tire identity is resolved lazily, at the first physics step.**
It comes from `WheelController.PartConfigurationWheel`, so a car that was already spawned before the
plugin loaded will not have an identity until it is spawned again. The panel's **Tires seen** list
shows what has been captured.

**A missing aid falls back to `Native`, never to "managed but empty".**
If the config names an algorithm whose plugin is no longer installed, the channel reverts to the
game's own aid. Neutralising the game's aid with nothing to replace it would silently remove ABS.

**Grip magnitude.**
With `GripScale = 1` the peak matches the game's own pre-clamp numbers. Note the native model
additionally clamps the *total* force vector to `loadCoefficient`, so its effective longitudinal
peak is `~loadCoefficient` while ours is `|D| * loadCoefficient * forceCoefficient` (~25% higher with
`forceCoefficient = 1.35`). That follows from replacing the native circle clamp with a real friction
ellipse; it is deliberate and one setting away from parity.

**Local reference material is not committed.**
`ScrewTweaks/reference/` (a Project Chrono clone) and `ScrewTweaks/ScrewDrivers/` (the decompiled
game) are gitignored. Keep them that way.

---

## Design notes

The reasoning behind the tire model, the measured game data and the deferred suspension plan live in
[`docs/tire-model-spec.md`](docs/tire-model-spec.md). If you are going to change the tire model, read
§15–§19 first — they record what the game actually provides.

Where the numbers come from:

- **Taken from the game:** peak grip magnitude, slip curve shape (per tire, per surface), slip units,
  camber input, torque input.
- **Added by this suite:** camber thrust, camber softening, relaxation length, own wheel-spin
  integration, a real combined-slip friction ellipse.
- **Left alone on purpose:** the game's own tire balance, and any control the game already exposes to
  the player.

The last point has already dropped two ideas: an external brake bias (the game already has per-wheel
brake strength through the brake part's `brakeforce` property) and a geometry-derived "off-road tires
are worse on asphalt" penalty. In both cases the game already had a mechanism for it.

---

## Third-party

- The combined-slip **ADAMS friction ellipse** is adapted from
  [Project Chrono](https://github.com/projectchrono/chrono)'s `ChPac02Tire::CalcFxyMz`
  (`src/chrono_vehicle/wheeled_vehicle/tire/ChPac02Tire.cpp`), BSD-3-Clause. No Chrono code is
  built or shipped; only the formula was ported.
- The tire slot, panel registry and aid slots are original to this project.

## License

BSD-3-Clause. See [`LICENSE`](LICENSE).

Use, modify, redistribute and sell it, including as part of a closed-source mod. The only conditions
are to keep the copyright notice, and not to use the author's name to endorse or promote a derived
product without permission.

The tire model ports a formula from Project Chrono, which is under the same license, so there is no
mixed licensing to worry about.
