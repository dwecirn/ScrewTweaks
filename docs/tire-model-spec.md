# Tire Model Spec (working draft)

Design reference for a drop-in **tire model slot** that can replace the game's NWH
`WheelController3D` friction. Target model: **PAC2002 / MF 5.2** (Pacejka Magic Formula),
including combined slip, relaxation length and wheel spin dynamics.

> Status: research/spec only. No code yet. Field names are English on purpose.

---

## 1. Why

The game's active tire model (NWH `WheelController3D` `Friction`) is semi-empirical:

- longitudinal limit is a **constant** (`loadCoefficient * BCDE.z * forceCoefficient * 0.8`) — no slip curve, no force drop on lock;
- combined slip is a **magnitude clamp** `Vector2.ClampMagnitude((Fx, Fy), loadCoefficient)`, not a friction ellipse;
- load sensitivity is a single generic `loadGripCurve`;
- camber only changes geometry, not force;
- no relaxation length, no aligning torque (Mz).

The game also ships `CWPacejka` (legacy CW wheel), which is Pacejka-89 **pure slip** only
(a0-a17 / b0-b13 + load sensitivity + camber, but no combined slip, no relaxation, no Mz).

Goal: a real MF-class tire model, integrated at the same seam, selected per channel/slot
(`Native` fallback + `Pacejka2002`), with data from standard `.tir` files.

---

## 2. Integration seam

`WheelController.Step()` order (NWH WheelController3D):

```
SuspensionUpdate()   -> wheel.load                      (keep)
CalculateWheelDirectionsAndRotations()                  (keep)
WheelUpdate()                                           (keep)
FrictionUpdate()     -> slip, Fx, Fy, omega integrate    <<< REPLACE
UpdateForces()       -> applies forwardFriction.force / sideFriction.force at contact
```

`FrictionUpdate` is private; patch it with a Harmony **Prefix** that computes the model and
returns `false` to skip the original body. `UpdateForces` then applies our forces unchanged.

The player car does **not** use external slip calculation (`VehicleControllerSapp` only uses
`WheelComponent` for effects), so `FrictionUpdate` is live.

### Data available at the seam

| Quantity | Source |
|---|---|
| vertical load `Fz` [N] | `wheel.load` (= spring.force + damper.force) |
| contact velocity (world) | `ActiveRigidbody.GetPointVelocity(wheel.worldPosition - wheel.up * wheel.radius)` (minus ground rigidbody velocity if any) |
| forward / side dirs | `wheelHit.forwardDir`, `wheelHit.sidewaysDir` |
| wheel radius / inertia | `wheel.radius`, `wheel.inertia` |
| wheel angular velocity | `wheel.angularVelocity` [rad/s] |
| motor / brake torque | `wheel.motorTorque`, `wheel.brakeTorque` (set by `MechanicalOutputWheel`) |
| camber | `wheel.camberAngle` [deg] |
| surface | `activeFrictionPreset` (asphalt/sand) |
| per-wheel friction coeffs | `forwardFriction.forceCoefficient`, `sideFriction.forceCoefficient` |

### Data we must write back

- `forwardFriction.force`  = `Fx` (longitudinal, +forward)
- `sideFriction.force`     = `Fy` (lateral, +right)
- `forwardFriction.slip`   = slip ratio κ      (used by telemetry AND the ECU ABS/TCS)
- `sideFriction.slip`      = slip angle (normalised) for telemetry
- `wheel.angularVelocity`  = integrated

Do **not** run the original `Vector2.ClampMagnitude`: the model does its own combined slip.

---

## 3. Model: PAC2002 (MF 5.2)

Magic Formula:

```
y(x) = D * sin( C * atan( B*x - E * (B*x - atan(B*x)) ) ) + Sv
x    = X + Sh
```

`Fz0` = nominal load from the `.tir`. Load increment `dfz = (Fz - Fz0) / Fz0`.

### 3.1 Longitudinal (pure)

```
Fx0 = Dx * sin( Cx * atan( Bx*κx - Ex*(Bx*κx - atan(Bx*κx)) ) ) + Svx
κx  = κ + Shx
Cx  = PCX1
Dx  = Fz * (PDX1 + PDX2*dfz)
Ex  = (PEX1 + PEX2*dfz + PEX3*dfz^2) * (1 - PEX4*sign(κx))
Kx  = Fz * (PKX1 + PKX2*dfz) * exp(PKX3*dfz)          // slip stiffness
Bx  = Kx / (Cx * Dx)
Shx = PHX1 + PHX2*dfz
Svx = Fz * (PVX1 + PVX2*dfz)
```

### 3.2 Lateral (pure)

```
Fy0 = Dy * sin( Cy * atan( By*αy - Ey*(By*αy - atan(By*αy)) ) ) + Svy
αy  = α + Shy
Cy  = PCY1
Dy  = Fz * (PDY1 + PDY2*dfz) * (1 - PDY3*γ^2)
Ey  = (PEY1 + PEY2*dfz) * (1 - (PEY3 + PEY4*γ)*sign(αy))
Ky  = PKY1 * Fz0 * sin(2*atan(Fz / (PKY2*Fz0))) * (1 - PKY3*|γ|) * LFZO
By  = Ky / (Cy * Dy)
Shy = PHY1 + PHY2*dfz + PHY3*γ
Svy = Fz * ((PVY1 + PVY2*dfz) + (PVY3 + PVY4*dfz)*γ)
```

### 3.3 Aligning torque (optional, phase 2)

`Mz = Mzt + Mzr` with `QBZ1..QBZ10, QCZ1, QDZ1..QDZ9, QEZ1..QEZ5, QHZ1..QHZ4, SSZ1..SSZ4,
QTZ1`. Gives steering feel (self-aligning, trail, residual). Not needed for grip itself.

### 3.4 Combined slip

Two options; pick one, expose the other later.

**(a) MF 5.2 weighting (recommended, coefficients exist in .tir):**

```
Fx = Gxa * Fx0
Fy = Gyk * Fy0 + Svyk

Gxa = cos(Cxa*atan(Bxa*αs - Exa*(Bxa*αs - atan(Bxa*αs)))) / Gxa0
Gxa0 = cos(Cxa*atan(Bxa*Shxa))
Bxa = RBX1 * cos(atan(RBX2*κ)) * LXA
Cxa = RCX1 ;  Exa = REX1 + REX2*dfz ;  Shxa = RHX1
αs  = α + Shxa

Gyk = cos(Cyk*atan(Byk*κs - Eyk*(Byk*κs - atan(Byk*κs)))) / Gyk0
Gyk0 = cos(Cyk*atan(Byk*Shyk))
Byk = RBY1 * cos(atan(RBY2*(α - RBY3))) * LYKA
Cyk = RCY1 ;  Eyk = REY1 + REY2*dfz ;  Shyk = RHY1
κs  = κ + Shyk
```

**(b) Similarity method** (Pacejka, "Tyre and Vehicle Dynamics" ch. 4.2.2):

```
σx  = κ / (1 + κ)
σy  = tan(α) / (1 + κ)
σ   = max(sqrt(σx^2 + σy^2), eps)
Fx  = (σx/σ) * Fx0(σ * σx_hat)
Fy  = (σy/σ) * Fy0(σ * σy_hat)
```

where `σx_hat`, `σy_hat` are the peak slip values (from the pure curves).
Simpler, no extra coefficients; good first implementation.

---

## 4. Relaxation length (transient)

Real tires build force over a distance `σ` (relaxation length), not instantly. Without it the
car feels "instantaneous"/nervous. Standard form (single first-order lag per direction):

```
dκ/dt = ( Vx - |Vx|*κ - ω*r ) / σx        (longitudinal)
dα/dt = ( Vy - |Vx|*tan(α) ) / σy         (lateral)
```

or the simpler "slip speed / relaxation length" form:

```
κ_eff = κ_ss * (1 - exp(-s/σ))    (per distance travelled)
```

Use `RELAXATION_LENGTH` from the `.tir` (typ. 0.05–0.15 m). This is a **major** feel lever;
AC exposes exactly this as `RELAXATION_LENGTH`.

---

## 5. Wheel spin dynamics

```
I * dω/dt = T_drive - T_brake*sign(ω) - Fx * r
```

- `I` = `wheel.inertia`
- `T_drive`, `T_brake` = `wheel.motorTorque`, `wheel.brakeTorque` (already set by the game's
  drivetrain — do **not** recompute them)
- integrate with `Time.fixedDeltaTime` (use the controller's `_fixedDeltaTime` accumulator if
  slow-down factor > 1)

---

## 6. Low-speed / singularity handling

- clamp the slip normalisation denominator: `denom = max(|Vx|, ~0.5 m/s)` (VDrift uses 0.1... pick per test);
- at very low speed blend towards a "static friction follows demand" behaviour to avoid jitter;
- guard `Fz <= 0` (wheel in air / unloaded) -> zero forces.

---

## 7. Surface & scaling

The `.tir` defines a reference surface. For asphalt/sand, scale `Dx`, `Dy` (and stiffness)
with per-surface multipliers `LMUX`, `LMUY` (exposed as a slot parameter). The game's
`activeFrictionPreset` already picks asphalt vs sand; map each preset to a scaling pair.

---

## 8. `.tir` file (TNO MF-Tyre / MF-Swift standard)

Open text format. Sections we care about (names may vary slightly by exporter):

```
[UNITS]                     ; LENGTH=METER FORCE=NEWTON ANGLE=RADIAN ...
[MODEL]                     ; e.g. PAC2002 / MF52
[DIMENSION]                 ; WIDTH, RIM_RADIUS, R0/RE ...
[VERTICAL]                  ; FZ0 (nominal load), FNOMIN, ...
[SCALING_COEFFICIENTS]      ; LFZO, LCX, LMUX, LEX, LKX, LHX, LVX, LCY, LMUY, LEY, LKY, LHY, LVY, LTR, LRES, LGA, LYKA, LVYKA, LS
[LONGITUDINAL_COEFFICIENTS] ; PCX1 PDX1 PDX2 PEX1..4 PKX1..3 PHX1 PHX2 PVX1 PVX2 RBX1..3 RCX1 REX1 REX2 RHX1
[LATERAL_COEFFICIENTS]      ; PCY1 PDY1..3 PEY1..4 PKY1..3 PHY1..3 PVY1..4 RBY1..3 RCY1 REY1 REY2 RHY1 RVY1..6
[ALIGNING_COEFFICIENTS]     ; QBZ1..10 QCZ1 QDZ1..9 QEZ1..5 QHZ1..4 SSZ1..4 QTZ1
```

Plan: ship a few built-in sets (street / semi-slick / slick) and allow loading a user `.tir`
from a folder (defaults derived from the game's vanilla grip so nothing breaks).

---

## 9. Mapping to the game's quantities

| Model | Game |
|---|---|
| `Fz` [N] | `wheel.load` |
| `Vx` [m/s] | dot(contactVelocity, forwardDir) |
| `Vy` [m/s] | dot(contactVelocity, sidewaysDir) |
| `ω` [rad/s] | `wheel.angularVelocity` |
| `r` [m] | `wheel.radius` |
| `I` [kg m²] | `wheel.inertia` |
| `γ` [rad] | `wheel.camberAngle * Deg2Rad` |
| `T` [Nm] | `wheel.motorTorque`, `wheel.brakeTorque` |
| surface μ scale | `activeFrictionPreset` (asphalt/sand) |

Units are already SI-ish in the game (N, kg, m, rad), so no re-scaling needed — but
**verify** `maximumTireGripForce`/`maximumTireLoad` are consistent with `Fz` before shipping.

---

## 10. Reference implementations (to read, not copy)

**Primary port source: Project Chrono (`Chrono::Vehicle`, BSD-3-Clause).** Permissive license,
so its tire code can be ported (with attribution). Handling models available:

| Chrono class | Model | Notes |
|---|---|---|
| `ChPacejkaTire` (base) | Pacejka | reads a Pacejka parameter file, **transient slip by default**, `GetTireForce_combinedSlip`, camber, driven-wheel flag |
| `ChPac02Tire` / `Pac02Tire` | Pacejka 2002 | the target model |
| `ChPac89Tire` / `Pac89Tire` | Pacejka 89 | closest to the game's `CWPacejka` |
| `ChFialaTire` / `FialaTire` | Fiala brush model | transient, low-speed friendly, couples lat/long — good lightweight option |
| `TMeasy`, `RigidTire`, `ANCFTire`, `ReissnerTire`, `ChFEATire` | — | other reference points |

Source tree: `src/chrono_vehicle/wheeled_vehicle/tire/` (e.g. `ChPacejkaTire.cpp/.h`,
`PacejkaTire.cpp/.h`). A `ChTire` is a **force element**: given wheel body position/velocity it
returns ground force/moment — the same abstraction as `WheelController.FrictionUpdate`.

**Integration choice (recommended: port, not embed).** Port the Pacejka (and/or Fiala) math to
C# and drop it into the tire slot. Do **not** build Chrono as a native plugin: its rigid-body /
contact solver is not usable here (the game's cars are Unity rigidbodies) and would add a large
native dependency for no benefit.

Secondary references (formula cross-check):

| Source | Use |
|---|---|
| **python `vdrift-tools/tirepn.py`** | compact PAC2002 `PacejkaFx/Fy/Mz/Gx/Gy/Svy` — best formula reference |
| **VDrift `cartire.h`** | `Pacejka_Fx/Fy/Mz` + Beckman combined slip + friction-circle methods (GPL: read only) |
| **Racer (racer.nl) pacejka page** | MF5.2 combined-slip (`Gxa`, `Gyk`) formulas + similarity method (Pacejka ch.4.2.2) |
| **Tread (JS) / PAC2002** | another readable PAC2002 |
| **Pacejka, *Tyre and Vehicle Dynamics*** | the source of all of the above |
| Assetto Corsa `tyres.ini` docs | parameter *concepts* (RELAXATION_LENGTH, FLEX, CAMBER_GAIN, COMBINED_FACTOR, thermal) — model is closed, ideas are not |

**Attribution**: if code is ported from Project Chrono, keep its BSD-3 copyright header in the
ported source and add a note in the repo (e.g. `THIRD_PARTY_NOTICES`).

---

## 11. Phased plan

1. **Slot skeleton**: `ITireModel` + `Native` (calls original) + `Pacejka2002` stub; select via
   cfg/panel (same pattern as `ScrewTweaks.ECU`).
2. **Pure slip Fx/Fy + combined slip + wheel spin dynamics**, ported from Project Chrono's
   `ChPacejkaTire` / `Pac02Tire` (BSD-3; keep the copyright header). Straight-line lock-up and
   steady-state cornering must match vanilla grip order of magnitude.
3. **Relaxation length** + low-speed handling (this is where the feel upgrade lands).
4. **Load sensitivity / camber scaling** + Mz (aligning torque).
5. **Surface scaling** (asphalt/sand) + optional thermal/pressure (AC-style, later).
6. **ECU integration**: ABS/TCS operate on our κ/α instead of the game's slip fields.

## 12. Open questions

- Which `Fz0`/grip level to calibrate to (vanilla `maximumTireGripForce`, or real-car μ)?
- Can we keep the game's per-wheel `forwardFriction/sideFriction.forceCoefficient` as extra
  multipliers (tuning compatibility), or ignore them?
- Multiplayer / ghost replays: does changing tire physics desync recorded runs?
- Performance: MF per wheel per physics step is cheap, but confirm at 50 Hz × many wheels.
- `.tir` licensing: ship our own coefficient sets (derived/tuned), don't redistribute others'.

---

## 13. Local reference (Project Chrono clone)

A shallow clone of https://github.com/projectchrono/chrono lives at `reference/chrono`
(**gitignored**, not part of the repo). ~7529 files / 1.84 GB (`data/` ~1.19 GB, `src/` ~72 MB).

Key paths:

| Path (under `reference/chrono/`) | What |
|---|---|
| `src/chrono_vehicle/wheeled_vehicle/tire/ChPac02Tire.cpp/.h` | **Pacejka 2002** vehicle wrapper (~70 KB) — primary port source |
| `src/chrono_vehicle/wheeled_vehicle/tire/Pac02Tire.cpp/.h` | Pacejka 2002 model core (~32 KB) |
| `src/chrono_vehicle/wheeled_vehicle/tire/ChPac89Tire.cpp/.h`, `Pac89Tire.cpp/.h` | Pacejka 89 |
| `src/chrono_vehicle/wheeled_vehicle/tire/ChFialaTire.cpp/.h`, `FialaTire.cpp/.h` | Fiala (transient brush) |
| `src/chrono_vehicle/wheeled_vehicle/tire/ChTMeasyTire.*`, `ChTMsimpleTire.*`, `TMeasyTire.*`, `TMsimpleTire.*` | TMeasy / TMsimple (few-parameter) |
| `src/chrono_vehicle/wheeled_vehicle/tire/ChRigidTire.*`, `ChANCFTire.*`, `ChFEATire.*`, `ChReissnerTire.*` | rigid / FEA tiers |
| `src/chrono_vehicle/wheeled_vehicle/tire/ChTire.*`, `ChForceElementTire.*` | the force-element abstraction we map to |

**Real `.tir` data files shipped in the clone** (PAC2002/PAC89 parameter sets):

```
data/vehicle/sedan/tire/Sedan_Pac02Tire.tir                 (passenger car)
data/vehicle/audi/json/audi_Pac02Tire.tir                   (passenger car)
data/vehicle/Nissan_Patrol/json/suv_Pac02Tire.tir           (SUV)
data/vehicle/VW_microbus/json/mf_185_80R14.tir              (van)
data/vehicle/Polaris/Polaris_Pac02Tire.tir                  (ATV / off-road)
data/vehicle/hmmwv/tire/HMMWV_Pac02Tire.tir                 (military truck)
data/vehicle/generic/tire/Generic_Pac02Tire.tir             (generic)
data/vehicle/feda/tires/335_65R22_5_G275MSA_*.tir           (truck, multiple pressures)
```

These give us real coefficients to start from (pick/scale toward a track tire), without
redistributing anything (the files stay in the local clone; we ship derived sets or none).

Note: `data/` is ~1.2 GB and is mostly meshes/terrain; it can be deleted to slim the clone if
disk is a concern — only the `.tir` files under `data/vehicle/**` are valuable for us.

---

## 14. Chrono tire model family (inventory & recommendation)

`Chrono::Vehicle` ships a whole family. Architecture: `ChTire` → `ChForceElementTire` → concrete
models. `Synchronize(time, terrain)` computes kinematics; `Advance(step)` integrates internal
state (each model has its own `m_stepsize` → sub-stepping may be needed at our 50 Hz).

### Handling (semi-empirical) — relevant to us

| Model | Basis | Params | Transient / low speed | Combined slip | Notes |
|---|---|---|---|---|---|
| **`ChPac02Tire`** | Pacejka 2002 (Pacejka's book) | full MF-Tire subset via **`.tir`** (ADAMS/Car compatible) | **relaxation length** `CalcSigmaK/CalcSigmaA` + **Dahl friction** at standstill (bristle `brx/bry`) + Coulomb blend (`vcoulomb`, `frblend_*`) | **friction ellipse (default) or Pacejka method** (`use_mode` 3/4) | also Mx overturning / My rolling / Mz aligning; optional inflation pressure; validated (FED-Alpha, KRC) |
| `ChPac89Tire` | Pacejka 89 | A0-A13 / B0-B13 / C0-C17 | Dahl standstill | `CombinedCoulombForces` | simpler; no pressure, no Mx/My |
| **`ChTMeasyTire`** | TMeasy (Rill) | characteristic points at PN and 2·PN (`dfx0`, `sxm`, `fxm`, `sxs`, `fxs`, …) | Dahl standstill; optional contact smoothing (Sui & Hershey) | `tmxy_combined` | **parameter guessing** (`GuessPassCar70Par` / `GuessTruck80Par`) from tire size; validated (KRC) |
| `ChTMsimpleTire` | TMsimple (Hirschberg) | very few | Dahl standstill; smoothing option | same algorithm as TMeasy | simplest handling model |
| `ChFialaTire` | Fiala brush | few (`CSLIP`, `CALPHA`, `UMIN/UMAX`, relaxation) | **transient slip state equations** (from ADAMS/tire); couples lat/long | inside the brush model | low-speed friendly |

### Other tiers (not handling)

- Rigid: `ChRigidTire` (rigid cylinder + terrain contact; needs rigid-contact terrain).
- Deformable / FEA: `ChANCFTire` / `ANCFTire` / `ANCFToroidalTire`, `ChReissnerTire`,
  `ChFEATire` (for ride / obstacle / FEA accuracy, not real-time handling).

### Orthogonal options

- Contact algorithm: `SINGLE_POINT`, `FOUR_POINTS` (TMeasy), `ENVELOPE` (Sui & Hershey).
- **Dahl friction standstill model** is present in every handling model — this is the low-speed
  jitter solution (bristle states + damping), directly reusable for our low-speed handling.

### Recommendation

- **Primary: port `ChPac02Tire`** — most complete, `.tir`-driven, ellipse/Pacejka combined slip,
  relaxation length + Dahl standstill, aligning/overturning/rolling moments. Matches this spec.
- **Alternative: `ChTMeasyTire`** — few, intuitive parameters plus parameter *guessing* from tire
  size (great for arbitrary cars), also validated.
- `ChFialaTire` as a lightweight fallback; `ChPac89Tire` as the simplest MF.

Files: `src/chrono_vehicle/wheeled_vehicle/tire/ChPac02Tire.cpp` (~70 KB) + `Pac02Tire.cpp`
(~32 KB). The `.tir` loader (`SetMFParamsByFile`, `LoadSection*`) lives in `ChPac02Tire.cpp` and
can be ported or replaced with a small C# TIR parser.

---

## 15. Measured game tire data (2026-09, from the F8 dump)

The game exposes **no `.tir`-like data**. What a wheel actually carries:

| Quantity | Measured | Note |
|---|---|---|
| `Grip` (per tire prefab) | −0.2 … 0.4 (mostly 0.1–0.3) | the only per-tire grip input |
| `FrictionForward/Sideways` | **null on all wheel parts** | unused |
| `maximumTireGripForce` | 8281.85 / 9689.76 | derived from `Grip` + mass by `WheelPropertiesSetter` |
| `maximumTireLoad` | 8000 | |
| `loadGripCurve` | (0:0) (0.4183:0.6691) (0.7228:0.843) (0.9993:0.9989) | load sensitivity, per wheel |
| `gripFactorByWeight` | (0:1) (5057.1:1.8) (10094.25:2.4124) | super-linear in mass |
| `forceCoefficient` | fwd 1.35 / 1.0, side 1.15 / 0.423 | shared prefab defaults |
| `slipCoefficient` | fwd 1.0, side 0.423 | |
| radius / mass / inertia | 0.3073 / 30 / 2.8332 | |

Per-surface pure-slip curves are **shared `FrictionPreset`s** (`B, C, D, E`):

| Surface | B | C | D | E | peak slip | peak μ (D) |
|---|---|---|---|---|---|---|
| asphalt | 11 | 2.05 | 0.925 | 0.97 | ≈ 0.125 | 0.925 |
| dirt | 11 | 2.05 | 0.87 | 0.97 | ≈ 0.125 | 0.87 |
| TankTracks2 | 11 | 2.05 | 0.7 | 0.97 | ≈ 0.125 | 0.7 |
| spikes | 6 | 2 | 0.3 | 1 | ≈ 0.167 | 0.3 |
| sand | 4.2 | 1.1 | 0.8 | 1 | ≈ 0.238 | 0.8 |

**Conclusion:** a full PAC2002 / real `.tir` parameter set is **not derivable** from this data
(no per-tire coefficients, no `Fz0`, no stiffnesses, no relaxation length). What we *do* get:
one MF-shaped curve per surface + one load-sensitivity curve + a grip scalar. The model must
therefore be **generated** from those, not loaded from a `.tir`.

## 16. Decision — the "Pacejka-lite" model

Family: **Pacejka-89-style magic formula with camber**, fed by a **parameter generator**
(`Grip` + geometry + surface `BCDE` → model parameters).

Implemented (2026-09, `src/ScrewTweaks.Physics.Tires/PacejkaTireModel.cs`):

- pure slip `y = D*sin(C*atan(Bx - E*(Bx - atan(Bx))))` using the surface `BCDE`
  (D factored into the max, shape from B/C/E) for **both** Fx and Fy;
- peak grip matched to vanilla:
  `peak = |D| * loadGripCurve(Fz/Fz0) * maximumTireGripForce * forceCoefficient`;
- own wheel-spin integration (`I·dω/dt = Tdrive − Tbrake·sgn(ω) − Fx·r`);
- combined slip = **friction ellipse** (vector normalisation);
- **camber thrust** `Fy += k_camber · γ · Fz` (`k_camber` exposed as a slider);
- slip inputs scaled by the game's `slipCoefficient`.

Deferred to later iterations: relaxation length, camber-aware load sensitivity, aligning
torque `Mz`, Dahl low-speed bristles, per-surface μ scaling beyond `BCDE`, `.tir` import.

Rejected: porting `ChPac02Tire` verbatim — it needs `.tir` coefficients the game does not have.
`ChFialaTire` stays excluded (Chrono docs: assumes zero camber).


---

## 17. Diagnosis from the first telemetry recording (2026-09)

Per-wheel telemetry (F9 -> `BepInEx/ScrewTweaks.tire-telemetry.csv`, 30 s of mixed driving).

**Front axle:** slip ratio is ~0 (86% of samples within +/-0.02), lateral utilisation reaches
~0.89-0.91 of `sideMax`. The front is therefore *not* being robbed of grip by combined slip - it is
simply saturated, and it spends a large fraction of the time at 8-22 deg slip angle.

**Rear axle:** slip ratio is 0.12-0.44 (positive = spinning) for most samples and lateral
utilisation is capped at ~0.5-0.7 of `sideMax`. The ADAMS ellipse correctly converts that drive slip
into a large lateral loss, which reproduces the "cannot hold power, snaps into oversteer" symptom.

**Root cause - torque capacity mismatch.** The native model bounds the wheel-spin reaction with
`num7 = loadCoefficient * D * forceCoefficient * 0.8` used as a *torque*
(`angularVelocity -= num8 / inertia * dt`), and only afterwards converts to force via
`force = num8 / radius` (which is then clamped to `loadCoefficient`). Its implied wheel torque
capacity is therefore `~1.0 * loadCoefficient`. Our model is unit-consistent
(`torque = Fx * radius`), giving `0.925 * loadCoefficient * 1.35 * radius ~= 0.38 * loadCoefficient`
- about **2.6x less torque capacity** than the native. The game's drivetrain was calibrated against
the native number, so under real tire physics the rear wheels spin almost continuously.

**Resolution:** do not touch tyre data. Add a traction-control algorithm at the drive-torque layer
(`ScrewTweaks.ECU` -> `ProgressiveTraction`), holding the slip ratio near the longitudinal peak so
the rear keeps its lateral grip.

---

## 18. Fidelity policy (decided 2026-09)

The model must **not** change the game's own tire data or balance. It only adds physics the game
does not have.

**Kept exactly as the game provides it:**

| Input | Source |
|---|---|
| peak grip magnitude | `Grip` -> `maximumTireGripForce`, `loadGripCurve`, `forceCoefficient` |
| slip curve shape | the tire's own `FrictionPresetAsphalt` / `FrictionPresetSand`, picked per surface |
| slip units | `slipCoefficient` (the game's own normalisation) |
| camber input | `wheel.camberAngle` (already suspension-driven) |
| torque input | `wheel.motorTorque` / `wheel.brakeTorque` |
| tire identity | `WheelController.PartConfigurationWheel` |

**Added (new physics, not data):** camber thrust, relaxation length, own wheel-spin integration,
ADAMS friction ellipse.

**Deliberately NOT added:** rebalancing for the fact that the game's own numbers make off-road
tires near-strictly better than street tires (only ~6-10% worse on asphalt, much stronger on sand).
The user chose to stay faithful to the game rather than introduce authored penalties.

**Known deviation worth remembering:** the native model clamps the *total* force vector to
`loadCoefficient`, so its effective longitudinal peak is ~`loadCoefficient` while ours is
`|D| * loadCoefficient * forceCoefficient` (~25% higher with forceCoefficient 1.35). This follows
from replacing the native circle clamp with a real friction ellipse; a clamp can be re-added if
strict parity is ever wanted.

### 18.1 Check for an existing game mechanism first (brake bias lesson)

An external brake-bias control was added and then removed: the game already has one.

`SimpleCar2` sets `MechanicalOutputWheel.BrakeStrength` per wheel from the attached brake part
(`AttachedSuspension.AttachedBrake.BrakeForce`), defaulting to `0.8` when no brake part is fitted:

    PartBrake1        BrakeForceMin = 1, BrakeForceMax = 1   (fixed)
    PartBrake2/3      BrakeForceMin = 1, BrakeForceMax = 4   (tunable via the "brakeforce" property)
    no brake part     0.8

`CarAnalyzer2` maps the part's `brakeforce` property (a 0-100% slider) onto that range, so the
player already has a continuous, per-wheel 0.8x .. 4x brake strength - i.e. full front/rear bias,
in the car builder.

Lesson: before adding an ECU control, search for the existing mechanism. Adding one the game
already has violates the fidelity policy above and duplicates tuning the player already owns.

### 18.2 Camber (2026-09)

The game supplies camber but does nothing with it:

- `camberangle` is a **suspension-part** property (`CarAnalyzer2` -> `Camber`,
  `WheelPropertiesSetter` -> degrees as `Camber * 0.1`). Every wheel part has
  `CamberTop`/`CamberBottom` = 0, so all camber comes from the suspension part.
- `WheelController.WheelUpdate` recomputes `wheel.camberAngle` every physics step from the spring
  compression (`Lerp(camberAtTop, camberAtBottom, 1 - compressionPercent)`), i.e. the suspension
  setting plus the travel-induced change, and `CalculateWheelDirectionsAndRotations` tilts the
  contact frame with it - so the slip angle already sees camber - but it produces **no force**.

The model adds, all driven by `wheel.camberAngle`:

| effect | formula | MF equivalent |
|---|---|---|
| camber thrust | `Fy += k * camberDeg * Fz` | `Svy` |
| camber softens the tire | `peakSlipScale *= 1 - 1.2 * |gamma_rad|` | `PKY3` (cornering stiffness down -> peak later) |
| lateral peak drops | `sideMax *= 1 - 3 * gamma_rad^2` | `PDY3` |

`CamberDynamics` scales all three (0 = thrust only, 1 = typical coefficients). Nothing changes at
zero camber, which is the default, so this only shows up for cars that actually run camber.
