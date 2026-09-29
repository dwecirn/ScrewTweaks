# ScrewTweaks

**English** | [中文](README.zh-CN.md) | [日本語](README.ja.md)

A BepInEx mod suite for **Screw Drivers** that rebuilds the tyre and suspension force models out of the
game's own part data, and adds a four-way damper, a wheel with vertical freedom and closed-loop ABS and
traction control on top of it.

Every model reads the numbers the parts already carry — `FrictionPreset` B/C/D/E, `loadGripCurve`,
`maximumTireGripForce`, `springforce`, `damperforce`, `SuspensionStiffness` — so a car behaves the way its
parts say it should, and a street build and a race build differ for the reasons the game already intended.

---

## What it changes

**Tyre force from a slip curve.** A Pacejka-89 Magic Formula evaluated against the tyre's own per-surface
B/C/D/E, its peak scaled by the game's `loadGripCurve`. Wheel spin is integrated by the model, so drive and
brake torque act on rotational inertia.

**Combined slip.** An ADAMS friction ellipse, the formulation Project Chrono's `ChPac02Tire` uses:
longitudinal and lateral demand draw on one budget set by the tyre's own limits, and a wheel past its
longitudinal peak gives up lateral force.

**Transient slip.** A relaxation length on both slip components, so force builds over distance. The
kinematic slip is published back to the game, which is what the drivetrain and the aids read.

**Vertical freedom.** A two-mass quarter car: unsprung mass from the wheel part, a tyre vertical rate and
damping derived from the suspension's own wheel rate, substepped from the ω·dt stability criterion.

**A four-way damper.** Independent bump and rebound coefficients either side of a blow-off velocity,
piecewise-linear and continuous at the knee, stored on the suspension part so a car carries its own setup.

**Closed-loop aids.** Slip-ratio targets with a gain and a floor, evaluated per wheel per step.

The models are replaceable slots: `ITireModel`, `IDamperModel`, `ITireVerticalModel`,
`IBrakeAid`/`IDriveAid` and the panel host are public API, and
[docs/extending.md](docs/extending.md) shows each one at work.

The reasoning and the measured game data behind the two physics modules are in
[`docs/tire-model-spec.md`](docs/tire-model-spec.md) and
[`docs/suspension-model-spec.md`](docs/suspension-model-spec.md).


---
## Modules

Every plugin is independent — install, enable and tune them separately. The panel is the one exception:
`ScrewTweaks.Panel.dll` is what the others register their tabs into, so it has to be there.

| Module | What it does | Reference |
|---|---|---|
| `ScrewTweaks.Panel` | The shared in-game panel and its own settings | [Panel](#panel) |
| `ScrewTweaks.Physics.Tires` | Tyre force model: slip curves, combined slip, relaxation length | [Tire physics](#tire-physics) |
| `ScrewTweaks.Physics.Suspension` | Four-way damper and the wheel's vertical freedom | [Suspension physics](#suspension-physics) |
| `ScrewTweaks.ECU` | ABS and traction control channels | [ECU](#ecu) |
| `ScrewTweaks.Steering` | Instant steering, steering limit relax | [Steering](#steering) |
| `ScrewTweaks.AutoShift` | Faster automatic shifting | [Auto Shift](#auto-shift) |
| `ScrewTweaks.EngineSound` | Hybrid engine sound | [Other plugins](#other-plugins) |
| `ScrewTweaks.PowerFactor` | Per-engine power factor, saved with the car | [Other plugins](#other-plugins) |

Keys: **F7** opens the panel, **F8** dumps static tire and wheel data to
`BepInEx/ScrewTweaks.tire-dump.txt`, **F9** records 30 s of per-wheel tyre telemetry to
`BepInEx/ScrewTweaks.tire-telemetry.csv`.

## Install

1. Install **[BepInEx 5 (x64)](https://github.com/BepInEx/BepInEx/releases)** into your Screw Drivers folder if you have not already
   (`BepInEx/` and `winhttp.dll` next to `Screw Drivers.exe`).
2. Drop every `ScrewTweaks.*.dll` into `BepInEx/plugins/`.
3. Launch the game. A log line per plugin should appear in `BepInEx/LogOutput.log`.

> `ScrewTweaks.Panel.dll` **must** be installed. Every plugin depends on it for configuration.
> The rest are optional and independent unless noted otherwise.

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

Each plugin carries its own version, printed to `BepInEx/LogOutput.log` when it loads and listed in the
panel's Settings tab. The bundle as a whole is only dated; the tags on GitHub are the releases.

**Local reference material is not committed.** `ScrewTweaks/reference/` (a Project Chrono clone) and
`ScrewTweaks/ScrewDrivers/` (the decompiled game) are gitignored.

## Panel

Press **F7** for a floating window that opens against the **right edge of the screen, vertically
centred**. Drag the title bar to move it. The tab column sizes itself to the longest caption and the
content column takes the rest of the width. Each feature plugin registers a titled section:

- **ECU** — ABS and traction channels
- **Tires** — tire model selection, tuning, live per-wheel telemetry
- **Suspension** — damper and tyre vertical model selection, tuning, live per-wheel travel and force
- **Auto Shift** — on/off for the faster shift timing
- **Steering** — Instant Steering and the steering limit relax
- **Settings** — the panel's own settings, first in the list: side, language, and the loaded modules with their versions

### Panel Settings

The **Settings** tab is the panel's own, and both of its settings are saved to
`dev.dwecirn.screwtweaks.panel.cfg`:

| Setting | Default | Meaning |
|---|---|---|
| `Panel/TabSide` | `Left` | Which side of the window the tab column sits on. `Left` or `Right` |
| `Panel/Language` | `English` | Panel language. `English`, `ChineseSimplified` or `Japanese` |

The dotted grip that resizes the window sits in whichever corner is free to follow the mouse: normally
the lower-right, but the lower-left while the window is up against the right edge of the screen, because
growing to the right is not possible there.

---


All values are persisted in `BepInEx/config/dev.dwecirn.screwtweaks.<name>.cfg`.

## Tire physics

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

> **The slot owns more than the forces.** `WheelController.FrictionUpdate` also publishes the contact
> speeds, the wheel RPM and the `wheelHit` slip values that the drivetrain and the TCS read back. The host
> does that around your model, which is why a model only fills the forces, the slips and the wheel spin.
>
> **Publish the kinematic slip.** `forwardFriction.slip` / `sideFriction.slip` are the feedback channel the
> drivetrain and the aids read. Publishing the relaxed value makes every aid react a relaxation length
> late, and the wheels lock before the ABS responds.
>
> **Identity is resolved lazily**, from `WheelController.PartConfigurationWheel`, so a car spawned before
> the plugin loaded has none until it is spawned again. The panel's *Tires seen* list shows what has been
> captured.
>
> **Peak force** with `GripScale = 1` matches the native model's pre-clamp numbers. The native model then
> clamps the total force vector to `loadCoefficient`, so its effective longitudinal peak is
> `~loadCoefficient` while this one reaches `|D| * loadCoefficient * forceCoefficient` (~25% more at
> `forceCoefficient = 1.35`). That is the friction ellipse taking its share.
>
> **NWH backend only.** The legacy wheel paths are untouched.

## Suspension physics

Select a damper model in **F7 → Suspension**. The choice is remembered across sessions. `Native` leaves
the game's own damper untouched and is the default, so installing the plugin changes nothing until you
pick a model.

The spring is deliberately not touched — the part data turned out to be sane already
(`docs/suspension-model-spec.md`). What the game has no way to express is the damper, which is
`coefficient × |velocity|` with **the same coefficient in bump and rebound** and no blow-off at all.

The model is a **four-way** damper: bump and rebound, each with its own low-speed and high-speed
coefficient, meeting at one knee velocity.

```
                below the knee   above the knee
  Bump               1.60           0.40
  Rebound            3.20           0.80
  Knee velocity      0.10 m/s
```

The low-speed numbers are the slopes of the steep part of the plot — the bleed, which is what controls the
body over slow inputs like roll and pitch. The high-speed numbers are the slopes above the knee, where the
shim stack is open — what a kerb or a landing sees, and lower means the hit is absorbed instead of passed
into the chassis.

The four coefficients are **saved with the car, not with the mod**: they are properties of the suspension
part, edited in the car builder right below the game's own *Spring Force* and *Damper Force*. Every car
keeps its own setup, and a car you share carries it.

| Property | Default | Meaning |
|---|---|---|
| `damperbumplow` | 160% | Bump below the knee. The bleed, which controls the body over slow inputs |
| `damperbumphigh` | 40% | Bump above the knee. What a kerb sees: lower absorbs the hit |
| `damperreboundlow` | 320% | Rebound below the knee |
| `damperreboundhigh` | 80% | Rebound above the knee. Raise it if the car pogoes after a landing |

They are percentages of the damping the game computed for that wheel, so they mean the same thing on a
go-kart and on a truck, and all four at 100% reproduces the game exactly. The game's *Damper Force* is not
replaced — it is still the base, and these four are its shape.

| Setting | Default | Meaning |
|---|---|---|
| `Model/Selected` | `Native` | `Native` = the game's own damper, unchanged |
| `Damper/ReboundFloor` | `0.00` | How far the damper may pull the body *down*, as a fraction of the wheel's static load. Not applied at all while a vertical model is selected |

**The four properties are only read while a damper model is selected.** `Native` leaves the game's own
damper in charge and does not read them — but they are saved either way, so switching models never costs a
car its setup. (Injecting them only when the model needs them would make a shared car's setup depend on the
sender's mod configuration, which is exactly what keeping the data on the car is meant to avoid.)

**The `Suspension` section also shows live per-wheel suspension state:**

```
  wheel              comp     C Ns/m   dir  v m/s    damper N    game N   spring N
  PartWheelDirt2      52%      1248  bump   -0.31        412       387      2310
```

`damper N` is what the model produced and `game N` is what the game's own damper would have produced at
the same velocity, so the difference is visible while driving.

**`ReboundFloor` needs a word of explanation.** The game clamps the total suspension force at zero, so a
rebound force larger than the spring force is truncated to nothing and the damper can never pull the body
down. That stops a car being sucked into the ground, but it also means rebound damping loses its
authority exactly when the suspension is near full extension — which is when it matters most, over a
crest. `0` keeps the game's clamp, and with no vertical model selected the damper is the only thing that
can stand in for the wheel's inertia, so raising it is the only way to get that authority back.

**With a vertical model selected it is not applied at all.** The clamp exists because a rigid wheel has no
inertia, so a damper pulling the body down has nothing behind it; once the wheel can hang, that pull is
real and the tyre's one-sided force is the bound. Leaving the clamp in place throws away the entire rebound
half of every wheel hop, which is what made the wheels bounce twice off a small bump no matter how hard the
damper was set.

#### The wheel's vertical freedom

The other gap is larger. The game's wheel has **no vertical freedom at all**: `SuspensionUpdate` reads the
ground out of the raycast and assigns the suspension length so the tire sits exactly on it, which makes
the tire rigid and gives the wheel no mass of its own. That is why kerbs, expansion joints and landings
are pure impulses, and why only the damper can absorb anything.

`TireVertical/Model = Linear` gives the wheel a vertical degree of freedom: the tire becomes a spring and
damper against the ground, with the wheel hanging on the suspension above it.

**Nothing else to set — the tyre's numbers are derived from the parts you already fitted:**

- **Unsprung mass** — the wheel part's own mass × 2. The game's number is the wheel alone; a real corner
  also carries the hub, the brake and half the suspension arms, and how heavy the corner is decides
  whether a bump throws the wheel clear of the ground.
- **Tyre stiffness** — 6 × the suspension's wheel rate (`maxForce / maxLength`). Real cars put a tyre at 5
  to 10 times the wheel rate, so a heavy car with long travel gets a soft big tyre and a go-kart a stiff
  small one, with no car-specific constant anywhere.
- **Tyre damping** — 0.2 of critical, in the middle of the measured range for tyres (0.1 to 0.3).
- **Substep count** — computed from how fast the wheel hop is, so it cannot be made unstable.

On the default car that comes out at about 200 kN/m on a 40 kg corner: a 13 mm static deflection and an
11 Hz wheel hop, both of which are what a real corner does. The panel prints what each wheel actually
ended up with.

| Setting | Default | Meaning |
|---|---|---|
| `TireVertical/Model` | `Native` | `Native` = the game's rigid wheel. `Linear` = the spring-and-damper tyre |

With it on, the wheels follow the road and can leave it, and sharp loads are absorbed by the tyre instead
of being passed straight to the chassis. `docs/suspension-model-spec.md` §5.5 has the reasoning —
including why **tyre damping, not the damper, is what controls wheel hop**, which is the thing to revisit
if hop is ever too visible again.

> **Bottomed out.** The game computes `damper.force` only in its `else if (hasHit)` branch, so on the bump
> stop it reuses the previous step's value and a stale force goes into the total. This module evaluates it
> there. With a non-`Native` model selected, that is the only place it changes the game's behaviour beyond
> the damper law itself.
>
> **Scope.** Anti-roll bars and toe are out of frame; the work polishes the spring and damper that are
> already there.

## ECU

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

> **A missing aid falls back to `Native`.** If the config names an algorithm whose plugin is no longer
> installed, the channel reverts to the game's own aid. Neutralising the game's aid with nothing to
> replace it would silently remove ABS.

## Steering

Both settings appear twice — as a row each on the game's own controls settings page, below *Ignore Steer
Angle Limit*, and in **F7 → Steering**. Either place edits the same entry, so the two cannot disagree.

| Setting | Default | Meaning |
|---|---|---|
| `Steering/InstantSteering` | `false` | Apply binary (keyboard / d-pad) steering input in one frame instead of ramping it, the way an analog stick already behaves |
| `Steering/LimitRelax` | `0.00` | Blend the game's speed-sensitive steering limit toward none. `0` = vanilla, `1` = the same as the game's *Ignore Steer Angle Limit* |

Both live in `dev.dwecirn.screwtweaks.steering.cfg`, and a change takes effect on the cars in the world
without re-spawning them.

Instant Steering used to be a per-suspension on/off property in the car builder. It is a setting now, so
it applies to every car at once; cars saved with the old property keep an unused line in their file, which
the game ignores.

## Auto Shift

`General/Enabled` in `dev.dwecirn.screwtweaks.autoshift.cfg`, and the same entry as the **F7 → Auto Shift** checkbox. Shifts are taken 0.2 s after the RPM threshold is crossed instead of 1 s, with a shorter cooldown and a shorter torque cut. Unticking the box writes the game's own values back.

## Other plugins

- **Engine Sound** — restores sound for the engine type the stock selector drops on hybrids, and
  mixes the two by instantaneous torque.
- **Power Factor** — a per-engine-type power factor that travels with the car's saved file. Fully
  automatic: it injects and persists through Harmony patches and needs no key.

## Third-party

- The combined-slip **ADAMS friction ellipse** is adapted from
  [Project Chrono](https://github.com/projectchrono/chrono)'s `ChPac02Tire::CalcFxyMz`
  (`src/chrono_vehicle/wheeled_vehicle/tire/ChPac02Tire.cpp`), BSD-3-Clause. No Chrono code is
  built or shipped; only the formula was ported.
- The tire slot, panel registry and aid slots are original to this project.
