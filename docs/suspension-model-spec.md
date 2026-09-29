# Suspension Model Spec

Design record for the suspension physics in **ScrewTweaks**, the module that lives in
`src/ScrewTweaks.Physics.Suspension`. Like the tire model it is a replaceable slot: what is documented
here is the implementation that happens to ship, not the only one the suite accepts. Field names are
English on purpose.

> **Status.** Damper slot implemented and selectable (`DamperModels` / `IDamperModel`). The spring is
> deliberately left alone. Sections 2 to 4 are the measurements this is based on, section 5 is what the
> module does, section 7 is what is not done yet.

---

## 1. Scope

Decided with the user: polish the **existing** spring and damper only. No anti-roll bar, no toe, no
added geometry, no rebalancing of the game's own data. Check for an existing game mechanism before
adding a control.

That check came out clean: the game's own `Damper` class already has separate `bumpForce` and
`reboundForce` fields and a serializable `curve`, and the suspension parts already carry a
`SuspensionDamper` value. The engine supports more than the game uses - see section 4.

## 2. What the game does

`WheelController.SuspensionUpdate`, every grounded wheel, every physics step:

```
length             = distance from the wheel mount to the contact point, clamped to [0, maxLength]
compressionPercent = (maxLength - length) / maxLength
velocity           = (length - prevLength) / dt                  // negative while compressing

spring.force = maxForce * forceCurve.Evaluate(compressionPercent)          // [N], no length term
if velocity <= 0:  damper.force =  bumpForce * curve.Evaluate(|velocity|)
else:              damper.force = -reboundForce * curve.Evaluate(|velocity|)

applied = clamp(spring.force + damper.force, 0, +inf)
rigidbody.AddForceAtPosition(applied * hitNormal * cos(angle), mountPoint)
```

The setup comes from `WheelPropertiesSetter.GetProperties`, the normal (non-tank) wheel path:

```
maxForce  = 60000 * (4 / wheelCount) * (mass / 936) * 0.1275 * 0.55
            * part.SuspensionStiffness * (springforce / 100)
maxLength = part.SuspensionDistance * carScale                     // carScale = 0.125 (GlobalCarScale)
curve     = CalculatedPartWheelSuspension.SampleProgressivenessCurve()
C         = mass * 1.3333 * (4 / wheelCount) * (damperforce / 100)
bumpForce = reboundForce = C
```

Three things follow from `maxForce ∝ mass`:

- **Static deflection is ~55% of travel, independent of mass, wheel count and the spring property.**
  Equilibrium is where `maxForce * c = m * g / N`; substituting `maxForce` cancels `m` and `N` and
  leaves `c = 9.81 * 936 / (60000 * 0.1275 * 0.55 * 4) ≈ 0.5456 / (stiffness * springforce/100)`.
  Same idea as Unity's `JointSpring.targetPosition = 0.5`.
- **Ride frequency depends only on travel and the part's stiffness**: `omega = sqrt(17.98 * stiffness / maxLength)`.
- **Damping ratio depends only on travel and `damperforce`**: `zeta = 2.6667 * (damperforce/100) / omega`.

Which gives this, at the default `springforce = 100`, `damperforce = 100`:

| Suspension | Travel (grid) | Travel (m) | Stiffness | Ride freq | Damping ratio |
|---|---|---|---|---|---|
| Small | 0.675 | 0.084 | 1.0 | 2.32 Hz | 0.18 |
| 1x1 Stiff | 0.8 | 0.100 | 1.0 | 2.13 Hz | 0.20 |
| Street / StreetBig / 2x4 | 1.0 | 0.125 | 1.0 | 1.91 Hz | 0.22 |
| **Race 1 / 2 / 3** | 2.0 | 0.250 | **2.0** | **1.91 Hz** | 0.22 |
| DirtSmall / Motorcycle | 2.5 | 0.313 | 1.3 | 1.38 Hz | 0.31 |
| DirtBig / ACSmall | 3.0 | 0.375 | 1.4 | 1.30 Hz | 0.33 |
| ACBig | 4.0 | 0.500 | 1.4 | 1.13 Hz | 0.38 |

The Race parts are the interesting line: their doubled stiffness exactly cancels their doubled travel,
so they sit at the *same* ride frequency as the street parts and differ only in travel. The parts were
tuned to a common frequency target, not filled in at random. `SuspensionStiffness` is the only part
value that changes anything on the spring side.

Because the spring is used over its whole `[0,1]` domain with no extrapolation, the curve shape is
meaningful and nothing here needs changing. The spring side is fine.

## 3. What the damper does not do

1. **No bump/rebound split.** `WheelPropertiesSetter` writes the same number into `bumpForce` and
   `reboundForce` (`wheelProperties.reboundRate = suspensionSpring.damper`). The engine has both fields
   and the game fills them identically, so "stiffer rebound than bump" is impossible in the game.
2. **The curve is the identity.** `damper.curve` is never assigned on the normal wheel path; `Awake ->
   Initialize -> SetDefaults` installs the generated two-key line `(0,0) -> (1,1)`, so the law is
   exactly `C * |v|` - linear everywhere, no blow-off. Only the tank path
   (`SetTankWheelProperties`) assigns a shaped curve, and that one is a blow-off shape:
   `(0,0) / (0.6,0.7) / (1,1)`. The developers knew; they only wired it up for tanks.
3. **The curve is fed m/s into a curve documented as [0,1].** `Damper.maxVelocity = 100f` exists and is
   never read. With the identity curve this is harmless by accident (it extrapolates to exactly `v`),
   but any shaped curve would be extrapolated past 1 m/s using its last tangent.
   `TankTracksSuspensionsUpdater` has the same problem and guards it with `Mathf.Min(1f, |v|)` in one
   branch only.
4. **Rebound is clamped away.** `clamp(spring + damper, 0, +inf)` means a rebound force larger than the
   spring force is truncated to zero, so the damper can never pull the body down. At rest the spring
   carries `m*g/N`, so with default settings the cap starts biting around 1.8 m/s. This is the `ReboundFloor`
   setting - see section 5.3.
5. **`damper.force` is not re-evaluated while bottomed out.** The damper is only computed in the
   `else if (hasHit)` branch, so while the suspension sits on its bump stop the previous step's force is
   reused. The module evaluates it there instead.

Dead data and dead code found on the way:

- **`Part.SuspensionDamper` is never read.** Parts declare it (Race = 1.3, everything else = 1.0) and
  `GetProperties` uses the `damperforce` property instead. "The race damper is 30% stronger" is in the
  game's own data and does nothing.
- **`ConfigWheelPropertySetterCurves.MapSpringForce` / `MapDamperForce` are never called.** The
  ScriptableObject carries designer-authored curves mapping the 0-100 property to a force; an inline
  formula replaced them.
- **`progressiveness` only moves tangents.** `SampleProgressivenessCurve()` lerps `inTangent` /
  `outTangent` between the Default and Maximum curves but always takes the key *values* from Default, so
  the curve's shape in value space never changes - only its smoothing. The in-game graph uses the same
  function, so the graph is honest about it.
- **Soft spring/damper values snap to maximum.** `value = property * 0.01; if (value < 0.1) value = 1;`
  makes 1-9 behave like 100%, and 10 jump to 10%.

None of the three are touched by this module. They are recorded because they are things a suspension
mod might reasonably want, and they are the game's own data if anyone does.

## 4. The ceiling: no unsprung mass

The wheel has no vertical degree of freedom. `spring.length` is *assigned* the distance to the contact
point every step, so the tire is rigid and the wheel is massless vertically. There is no unsprung mass
and no tire vertical compliance. Consequences:

- kerbs, expansion joints and landings are pure impulses; only the damper can absorb them,
- a real tire is a ~200 kN/m spring in series, so without it every load spike goes straight into the
  chassis,
- the contact patch load changes instantly, with no tire-side filtering.

The real fix is a two-mass quarter car (sprung mass + unsprung mass + tire spring), which means owning
`SuspensionUpdate` rather than correcting one term of it. That is deliberately not attempted here - see
section 7.

## 5. What this module does

### 5.1 Interception

Only the damper term is replaced. A Harmony **postfix** on `SuspensionUpdate` lets the game do the hit
test, the geometry, the travel clamp, the bottom-out path and the force application - all of it easy to
get subtly wrong and none of it the subject of this module - and then:

1. overwrites `damper.force` with the model's value, so `WheelUpdate` (which runs next and recomputes
   `wheel.load = spring.force + damper.force`) and the bump sound both see the truth;
2. adds the difference between what the game applied and what we want applied, at the same mount point
   and along the contact normal.

Only public `WheelController` members are touched - the freshly averaged `wheelHit.raycastHit.normal`
rather than the private `_raycastHitNormal` that the game's own force lags one step behind on - so a game
update that renames a private field cannot break the module, and no transpiler has to hold together.

Nothing accumulates: the damper is re-evaluated from scratch every step, and when the two agree the
correction is skipped. With `Native` selected the original method is not touched at all, so the default
install of this plugin cannot change how the game drives.

Rejected alternative: expressing the new law through `damper.curve`. It works for a shape shared by bump
and rebound (the split is a scale factor, and `bumpForce` / `reboundForce` are separate fields), and it
would need no patch on `SuspensionUpdate` at all. It was rejected because it cannot express a model with
different bump and rebound knee behaviour, which is exactly the kind of thing a third-party model should
be able to do.

### 5.2 The shipped model: `Digressive`

```
g(v) = v                                 v <= knee
g(v) = knee + (v - knee) * blowOff       v >  knee
F    = C * lowSpeedGain * g(v) * (bump ? 1 : reboundRatio)
```

Everything is a multiple of the game's own coefficient `C`, so the game's mass, wheel-count and
`damperforce` scaling is preserved. Defaults:

| Setting | Default | Meaning |
|---|---|---|
| `Damper/ReboundRatio` | 2.00 | Rebound coefficient as a multiple of bump. The game uses 1.00 for both |
| `Damper/LowSpeedGain` | 1.60 | Damping multiplier below the knee |
| `Damper/KneeVelocity` | 0.10 | Velocity [m/s] where the shim stack opens |
| `Damper/BlowOffRatio` | 0.25 | Slope above the knee, as a fraction of the slope below |

`LowSpeedGain = 1`, `BlowOffRatio = 1`, `ReboundRatio = 1` reproduces the game exactly, which is the A/B
switch. The knee velocity is a shaft property and does not scale with the car, which is why it is an
absolute m/s value.

For the default car (936 kg, 4 wheels, street suspension) this takes the damping ratio from 0.22 to about
0.35 in the low-speed region, and roughly halves the force on a sharp 1 m/s hit. That is the intended
trade: more control of the body, less harshness on kerbs.

### 5.3 `ReboundFloor`

The knob for gap 4 above. It sets how far below zero the total suspension force may go, as a fraction of
the wheel's nominal static load:

- `0` (default) - the game's clamp. The damper never pulls the body down, and any rebound force above
  the spring force is silently truncated.
- `1` - up to about 1 g of downward pull, which is roughly where the tire would leave the ground anyway.

`clamp` is a host concern rather than a model concern, so it lives in the module and applies to whatever
model is selected. It is off by default: it changes the game's integrator semantics, not just the damper
law, and it should only be turned up once rebounds are felt to run out of authority at the top of travel.

The nominal static load is `maxForce * forceCurve(0.55)` - the spring force at the compression the game's
own scaling aims for (section 2).

### 5.4 Extending it

`IDamperModel` is public, and `DamperModels.Register` accepts models from other plugins the same way
`ITireModel` / `TireModels` do. An unregistered name in the config falls back to `Native` - a damper that
is silently wrong is worse than the game's own.

A model returns the resistive force **magnitude** in [N]; the host owns the sign and the ground geometry.
One caveat for authors: tank track wheels have their coefficient set to zero by
`TankTracksSuspensionsUpdater`, so a model expressed as a multiple of `GameCoefficient` is a no-op there,
but a model that returns an absolute force regardless of the coefficient will also affect tank tracks.

Multiplayer: the wheel sync message carries only suspension distances and steering angles, so the damper
force is computed locally on each machine. Both ends need the same model selected or the car will behave
differently on each side. The same is true of the tire slot.

## 6. What to measure in game

The numbers in section 2 are derived from the code and have not been verified in a running game.
Specifically:

- **Static deflection.** `wheelCount` is `cachedTransform.parent.GetComponentsInChildren<WheelController>().Length`
  while the mass is the *connected part parent group's* weight. If a car is built with the wheels split
  across separate parents, the 55% equilibrium does not hold and cars could be sitting on their bump
  stops. The live table in the panel shows `comp` per wheel; it should read around 50-60% at rest.
- **The identity curve.** `SetDefaults` only installs the generated line when the prefab's curve is null
  or empty, so a curve authored into the wheel prefab would survive. The panel's `game N` column makes
  this visible at a glance: it is computed as `bumpForce * |v|`, so a mismatch means the curve is not the
  identity.
- **Whether the split is felt.** With `ReboundFloor = 0` the clamp caps rebound near full extension.

## 7. Deferred

1. **Two-mass quarter car** (unsprung mass + tire vertical spring in series with the suspension). The
   real fix for kerbs and landings; requires owning `SuspensionUpdate` instead of correcting one term.
   This is the natural next step and the reason the interception is a postfix on that method - the same
   patch point can grow into a full replacement.
2. **Spring model slot.** Nothing to fix today, and `progressiveness` nominally covers shape. A slot
   would only be worth adding alongside a working progressiveness or an unsprung-mass model.
3. **Honouring `Part.SuspensionDamper`** (the dead 1.3 on Race parts). It restores the game's own data
   rather than rebalancing it, but it does change car balance, so it is off the table unless the user
   asks for it.
4. **Fixing `progressiveness`** so it moves the curve values and not only the tangents.
5. **Per-direction knee and blow-off** (real dampers have different shim stacks in bump and rebound).
   The model API allows it; the shipped model shares one shape scaled by `ReboundRatio` to keep the
   panel to four sliders.
