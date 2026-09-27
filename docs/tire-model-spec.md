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
