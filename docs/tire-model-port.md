# Tire Model — C# Port Spec (Project Chrono Pac02)

Porting notes for replacing NWH `WheelController3D` friction with a real Magic Formula tire,
based on **Project Chrono `ChPac02Tire`** (BSD-3; keep the copyright header).

Reference (local clone): `reference/chrono/src/chrono_vehicle/wheeled_vehicle/tire/ChPac02Tire.cpp`

- `CalcFxyMz`            lines 146-323  — steady-state Fx / Fy / Mz (pure + combined)
- `CombinedCoulombForces` lines 86-144  — Dahl bristle standstill friction
- `GetNormalStiffnessForce/DampingForce` lines 67-84 (Chrono only; we do NOT use these)
- `Initialize`           lines 1700-1754
- `Synchronize`          lines 1756-1837 — kinematics + slip state
- `Advance`              lines 1839-1896 — force/moment output + low-speed blend

## 1. What the Chrono model actually is

- **Steady-state** Magic Formula, Pacejka 2002 subset, parameters from a `.tir` file.
- Combined slip: `use_mode 4`, via **friction ellipse (ADAMS style, default)** or the
  **Pacejka `Gxa/Gyk` coefficients**.
- Low speed / standstill: **Dahl friction bristle model**, blended with the MF result by
  `frblend = SineStep(|vx|, frblend_begin=1.0, 0.0, frblend_end=3.0, 1.0)`:
  `F = (1-frblend)*F_dahl + frblend*F_mf`.
- Produces `Fx, Fy, Fz` and `Mx (overturning), My (rolling), Mz (aligning)`.
- **No relaxation-length integration**: `CalcSigmaK/CalcSigmaA` exist but are never called.
  Transient feel comes from (a) the Dahl bristles at low speed and (b) quasi-steady MF above.

> Decision: port the model as-is first (faithful), then consider adding a relaxation-length
> filter as a separate enhancement (section 8).

## 2. Mapping to our game seam

| Chrono | Our game |
|---|---|
| `Synchronize()` contact detection, `GetNormalStiffnessForce` | **skip** — we use the game's raycast suspension and take `Fz = wheel.load` |
| `wheel_state.lin_vel` in contact frame | contact velocity via `GetPointVelocity(wheel.worldPosition - up*r)` |
| `wheel_state.omega` | `wheel.angularVelocity` |
| `m_data.depth` (deflection) | not available directly → approximate, see §5 |
| `mu_road` from terrain | map game surface (`activeFrictionPreset` asphalt/sand) to a μ |
| output `m_tireforce.force/moment` | set `forwardFriction.force = Fx`, `sideFriction.force = Fy`, apply `Mz` to steering |

`ω` integration: Chrono's tire does **not** integrate wheel spin (the wheel body does). The
game's original `Friction.CalculateLongitudinalSlip` does. Since we replace it, **we must
integrate ω ourselves**:

```
ω += (T_drive - T_brake*sign(ω) - Fx*r) / I * dt      // I = wheel.inertia
```

## 3. Parameters (`Pac02Params`) — from `.tir`

Sections to parse (Chrono's `LoadSection*` is the reference implementation):
`[UNITS] [MODEL] [DIMENSION] [VERTICAL] [SCALING_COEFFICIENTS] [LONGITUDINAL_COEFFICIENTS]
[OVERTURNING_COEFFICIENTS] [LATERAL_COEFFICIENTS] [ROLLING_COEFFICIENTS]
[ALIGNING_COEFFICIENTS]` (+ optional `[TIRE_CONDITIONS]`, vertical/bottoming tables).

Minimum needed for handling: `UNLOADED_RADIUS`, `WIDTH`, `FNOMIN`, `IP`, `IP_NOM`,
`LFZO LMUX LMUY LKX LKY LHX LHY LVX LVY LEX LEY LCX LCY LGAY LGAX LTR LRES LGAZ LXAL LYKA LVYKA LS`,
the `P*X*`/`P*Y*`/`R*` coefficients and the `Q*Z*`/`S*` aligning set. Units: SI assumed
(Chrono implements conversion but only tested SI).

Reference data sets already in the clone (use to start): `data/vehicle/sedan/tire/Sedan_Pac02Tire.tir`,
`.../audi/json/audi_Pac02Tire.tir`, `.../Nissan_Patrol/json/suv_Pac02Tire.tir`,
`.../VW_microbus/json/mf_185_80R14.tir`, `.../Polaris/Polaris_Pac02Tire.tir`.

## 4. State (`Pac02State`, per wheel, persisted)

```
kappa, alpha, gamma, vx, vsx, vsy, omega, R_eff,
Fz0_prime, dfz0, Pi0_prime, dpi, mu_scale, mu_road,
brx, bry,                 // Dahl bristle deformation
grip_sat_x, grip_sat_y    // diagnostics / sound
```

## 5. Per-physics-step update order

1. **Fz** ← `wheel.load` (skip Chrono vertical model). If `Fz <= 0` → zero forces.
2. **Contact frame**: forward/side dirs from `wheelHit.forwardDir/sidewaysDir`; contact velocity
   `vx = dot(v, fwd)`, `vy = dot(v, side)`.
3. **Effective radius** (Chrono uses a Rill estimate with deflection):
   `R_eff = (2*R0 + (R0 - depth)) / 3`.
   We have no `depth` → options: (a) `R_eff = radius`; (b) estimate `depth` from `Fz` and a
   vertical stiffness (`depth ≈ Fz / (CZ)`), or (c) read the suspension compression. **TBD — §8.**
4. **Slip** (epsilon avoids singularity at v=0):
   ```
   vsx = vx - ω*R_eff
   vsy = -vy
   kappa = clamp(-vsx / (vx + 0.1), -1, 1)
   alpha = clamp(atan2(vsy, vx + 0.1), -π/2+0.01, π/2-0.01)
   Fz0_prime = FNOMIN * LFZO
   dfz0 = (Fz - Fz0_prime) / Fz0_prime
   dpi  = (IP - IP_NOM*LIP) / (IP_NOM*LIP)
   mu_scale = mu_road / mu0          // mu0 = 0.8 default
   gamma = clamp(camber, ±gamma_limit=3°)
   ```
5. **Low-speed blend factor**: `frblend = SineStep(vx, frblend_begin, 0, frblend_end, 1)`.
6. **Dahl (standstill) forces** `Fx_d, Fy_d` — see §6; integrate bristles with `h = dt`.
7. **Steady-state MF** `Fx_ss, Fy_ss, Mz` via `CalcFxyMz` (see main spec §3) with combined
   slip enabled (`use_mode 4`).
8. **Blend**: `Fx = (1-frblend)*Fx_d + frblend*Fx_ss` (same for Fy). `My = CalcMy`, `Mx = CalcMx`.
9. **Wheel spin integration** (§2).
10. **Output**: apply `Fx` (forward), `Fy` (lateral), `Mz` (aligning → steering feel / wheel
    torque); write `forwardFriction.slip = kappa`, `sideFriction.slip` for telemetry + ECU.

## 6. Dahl standstill friction (`CombinedCoulombForces`, lines 86-144)

```
muscale = mu_road / mu0
fc      = Fz * muscale
brx_dot = vsx - sigma0*brx*|vsx|/fc
bry_dot = vsy - sigma0*bry*|vsy|/fc
F.x() = -(sigma0*brx + sigma1*brx_dot)
F.y() = -(sigma0*bry + sigma1*bry_dot)
// trapezoidal (A-stable):
brx = (2*brx*fc + 2*fc*h*vsx - brx*h*sigma0*|vsx|) / (2*fc + h*sigma0*|vsx|)   // same for bry
// friction circle clamp
if (F.Length() > fz*muscale) { F.Normalize(); F *= fz*muscale; }
```
`h` **must equal the vehicle step** (Chrono comment). Defaults `sigma0 = 100000`, `sigma1 = 5000`.
This is what stops low-speed jitter / creep — port it faithfully.

## 7. Combined slip (friction ellipse, default, lines 256-270)

```
kappa_c = kappa + Shx + Svx/Kx
alpha_c = alpha + Shy + Svy/Ky
alpha_s = sin(alpha_c)
beta    = acos(|kappa_c| / hypot(kappa_c, alpha_s))
mu_x_act = |(Fx0 - Svx)/Fz| ; mu_y_act = |(Fy0 - Svy)/Fz|
mu_x_max = Dx/Fz ; mu_y_max = Dy/Fz
mu_x_c = 1 / hypot(1/mu_x_act, tan(beta)/mu_y_max)
mu_y_c = tan(beta) / hypot(1/mu_x_max, tan(beta)/mu_y_act)
Fx = Fx0 * mu_x_c/mu_x_act ; Fy = Fy0 * mu_y_c/mu_y_act
Mz = -t*Fy + Mzr
```
(The `Gxa/Gyk` Pacejka alternative is lines 271-320; keep both behind a flag.)

## 8. Decisions / deviations to make for our integration

1. **R_eff without deflection** — pick (a) `R_eff = radius` (simplest), or estimate depth.
   Test lock-up/rolling behaviour; this affects `kappa` scaling.
2. **Relaxation length at speed** — Chrono Pac02 has none. If the car feels too instant, add a
   first-order lag on `kappa`/`alpha` (per distance travelled) as a separate layer. The
   `CalcSigmaK/CalcSigmaA` formulas are already in the source to parameterize it.
3. **Sign conventions** — Chrono outputs ISO with `-` on some axes; the game's
   `forwardFriction.force`/`sideFriction.force` have their own signs (the original code uses
   `+forwardFriction.force` and `-sidewaysDir*sideFriction.force`). Verify empirically.
4. **Left/right mirroring** — `.tir` tires are measured for one side; Chrono flips ~20 coefficients
   when mounted on the other side (lines 1712-1741). We must do the same or use symmetric data.
5. **`m_stepsize` / sub-stepping** — Chrono integrates Dahl with `h = step`. At 50 Hz `h = 0.02 s`;
   trapezoidal is A-stable so should hold, but if it jitters, sub-step the Dahl ODE internally.
6. **`use_mode`** — must be `4` (combined). `ChPac02Tire` defaults it to 0; set explicitly.
7. **`mu_road`** — map game surface presets to μ; `mu0 = 0.8` default reference.

## 9. Port plan

1. `ScrewTweaks.Tires` plugin + `ITireModel` slot (`Native` calls the original `FrictionUpdate`).
2. `TirFile` parser (C#) → `Pac02Params` (port Chrono's `LoadSection*` logic; or a compact parser).
3. `Pacejka2002` model: `CalcFxyMz` (pure + ellipse combined) + `CombinedCoulombForces` + blend +
   ω integration. Wire into the `FrictionUpdate` prefix.
4. Calibrate against vanilla grip order of magnitude on: straight-line lock-up, constant-radius
   cornering, and standstill. Compare decel / lat-g.
5. Add relaxation-length filter (optional) and ECU integration (ABS/TCS use our κ/α).
6. Attribution: keep BSD-3 headers on ported code + `THIRD_PARTY_NOTICES`.
