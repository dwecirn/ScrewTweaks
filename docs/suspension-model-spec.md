# Suspension Model Spec

Design record for the suspension physics in **ScrewTweaks**, the module that lives in
`src/ScrewTweaks.Physics.Suspension`. Like the tire model it is a replaceable slot: what is documented
here is the implementation that happens to ship, not the only one the suite accepts. Field names are
English on purpose.

> **Status.** Two slots implemented and selectable: the damper (`DamperModels` / `IDamperModel`) and the
> wheel's vertical freedom (`TireVerticalModels` / `ITireVerticalModel`). The spring is deliberately left
> alone. Sections 2 to 4 are the measurements this is based on, section 5 is what the module does,
> section 7 is what is not done yet.

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

## 4. The wheel has no vertical degree of freedom

`spring.length` is *assigned* the distance to the contact point every step, so the tire is rigid and the
wheel has no vertical mass of its own. Consequences:

- kerbs, expansion joints and landings are pure impulses; only the damper can absorb them,
- a real tire is a ~200 kN/m spring in series, so without it every load spike goes straight into the
  chassis,
- the contact patch load changes instantly, with no tire-side filtering.

It also cannot be fixed from outside the method. The game's bottom-out test, its force and the wheel's
visible position all fall out of the length it has just written, so correcting one term of
`SuspensionUpdate` - which is what the damper slot does - always leaves a wheel that is still rigid. A
vertical degree of freedom means owning the method; see section 5.5.

What the game does hand over is the signal. The raycast already reports how far the tyre is from the
ground (`WheelHit.distanceFromTire`) and the assignment throws it away.

### 4.1 Why it is not free: stiffness against the step size

The physics step is 0.02 s in single player - `AdjustTimeScale` resets `Time.fixedDeltaTime` to exactly
that, and the per-platform value lives in a ScriptableObject the game logs as `Set fixed timestep to ...`
- and 0.005 s in multiplayer.

A real tire is stiff, so the unsprung mode is fast: `omega = sqrt(k/m)`. The shipped stiffness - 6x the
suspension's wheel rate - puts the default car at about 11 Hz, which is where a real car's wheel hop sits,
and the same rule on a light wheel with a stiff short-travel suspension reaches 30 Hz and beyond. Explicit
integration is only stable below `omega*dt = 2`, which at 50 Hz means 15.9 Hz, so most of the range would
blow up.

That is why the substep count is not a setting: `TireVerticalTuning.SubstepsFor` computes it from the
wheel's own `omega*dt` and only ever raises it. The stiffness itself is free to be whatever the part data
says it should be, because the integration step follows from it rather than the other way round.

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

### 5.2 The shipped model: `Four way`

Bump and rebound, each with its own low-speed and high-speed coefficient, meeting at one knee velocity:

```
F = C * ( v <= knee ? low * v : low * knee + high * (v - knee) )
```

with `low` / `high` taken from the row matching the direction of travel. The plot is piecewise linear and
continuous at the knee, which is what a damper dyno plot looks like: a steep bleed, then the shim stack
opens and the force rises much more slowly.

Everything is a multiple of the game's own coefficient `C`, so the game's mass, wheel-count and
`damperforce` scaling is preserved, and the numbers are dimensionless - absolute N.s/m would be two orders
of magnitude apart between a go-kart and a truck. Defaults:

| Setting | Default | Meaning |
|---|---|---|
| `Damper/BumpLow` | 1.60 | Bump below the knee. The slope of the bleed, and what controls the body over slow inputs |
| `Damper/BumpHigh` | 0.40 | Bump above the knee. What a kerb sees: lower absorbs the hit |
| `Damper/ReboundLow` | 3.20 | Rebound below the knee. What arrests the body after a bump |
| `Damper/ReboundHigh` | 0.80 | Rebound above the knee. Raising it stops a car launching off its springs after a landing |
| `Damper/KneeVelocity` | 0.10 | Velocity [m/s] where the shim stack opens, shared by both directions |

**All four at 1.00 reproduces the game exactly**, which is the A/B switch. The knee velocity is a shaft
property and does not scale with the car, which is why it is an absolute m/s value.

An earlier revision had only three numbers - a rebound-to-bump ratio, a low-speed gain and a slope ratio -
which meant bump and rebound shared one curve shape and could only be scaled, not shaped. That is not a
four-way damper, and it read that way. The four coefficients are independent now; only the knee is still
shared, which the model API does not force.

For the default car (936 kg, 4 wheels, street suspension) the defaults take the body's damping ratio from
0.22 to about 0.35 and roughly halve the force on a sharp 1 m/s hit. That is the intended trade: more
control of the body, less harshness on kerbs.

One characteristic of the piecewise law with the shipped defaults: blow-off applies to *both* directions,
so the rebound is at its weakest exactly when the suspension is moving fastest - which is a landing. That
is what `ReboundHigh` is for, and raising it trades a bounce after a drop against a harsher kerb.

### 5.3 `ReboundFloor`

The knob for gap 4 above. It sets how far below zero the total suspension force may go, as a fraction of
the wheel's nominal static load:

- `0` (default) - the game's clamp. The damper never pulls the body down, and any rebound force above
  the spring force is silently truncated.
- `1` - up to about 1 g of downward pull, which is roughly where the tire would leave the ground anyway.

`clamp` is a host concern rather than a model concern, so it lives in the module and applies to whatever
model is selected. It is off by default: it changes the game's integrator semantics, not just the damper
law, and it should only be turned up once rebounds are felt to run out of authority at the top of travel.

**It is not applied at all while a vertical model is selected** (section 5.5). The clamp exists because a
rigid wheel has no inertia, so a damper pulling the body down would have nothing behind it. Once the wheel
can hang, a negative suspension force is the real reaction to the wheel's mass and the bound is the tyre's
own one-sided force. Leaving it in place throws away the entire rebound half of every wheel hop, which no
amount of damper setting can recover.

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

### 5.5 The wheel's vertical freedom: `TireVerticalModels` / `ITireVerticalModel`

With a non-Native model selected, `WheelVerticalSlot` **replaces** `SuspensionUpdate` outright and gives
each wheel a two-state vertical model: the suspension length becomes an integrated state instead of an
assignment, and the tyre pushes back through the model.

Kept as the game has it:

- the hit test and the wheel-hit averaging,
- the geometry that works out the wheel centre a tyre would need in order to just touch. That is what
  turns the ground into a *suspension length*, and it is where the deflection comes from,
- the force application point (the mount) and the contact-normal cosine.

Deliberately different:

- the length is integrated, in substeps, under the tyre force, the suspension force and gravity;
- the chassis is also pulled by the wheel while airborne, instead of the suspension going quiet. `hasHit`
  no longer zeroes the spring and damper forces: a hanging wheel is a real load, and so is a drooping one;
- the travel stops are inelastic, and whatever force they have to carry is transmitted to the chassis.
  Without that, a fully bottomed-out suspension would let the car sink through its own wheel;
- the game's separate bottom-out force term is gone. The tyre is a progressive bump stop now, and it is
  the thing that should stop the car;
- the wheel's load is the **tyre's** vertical force rather than the suspension's, written back in a
  postfix on `WheelUpdate` before `FrictionUpdate` reads it. Grip follows the contact patch, and the two
  forces are no longer the same number.

The deflection a model sees is `length - groundLength`, so a wheel sitting exactly on the ground reads 0.
The rate is the *relative* rate between wheel and ground - the geometry's own rate is subtracted - which
is what makes a chassis dropping onto a stationary wheel count.

| Setting | Default | Meaning |
|---|---|---|
| `TireVertical/Model` | `Native` | `Native` = the game's rigid wheel, untouched. `Linear` = the spring-and-damper tyre |

Everything the `Linear` model needs is derived, and none of it is a setting:

| Quantity | Where it comes from |
|---|---|
| Unsprung mass | the wheel part's own `Mass` times 2. The game's number is the wheel on its own; a real corner also carries the hub, the brake and half the arms, and how heavy the corner is relative to the tyre is what decides whether a bump throws the wheel clear of the ground |
| Tyre stiffness | 6 x the suspension's wheel rate, taken as `spring.maxForce / spring.maxLength` |
| Tyre damping | 0.2 of critical |
| Substep count | computed from `omega * dt` in `SubstepsFor`, so it cannot be made unstable |

**The stiffness being a multiple of the suspension's wheel rate is the part that matters.** Real cars put
a tyre's vertical rate at roughly 5 to 10 times the wheel rate, which is what keeps the unsprung mode
clear of the body mode. Expressing it that way means the tyre scales with the car exactly the way the
game's springs already do - a heavy car with long travel gets a soft big tyre, a go-kart a stiff small
one - with no car-specific constant anywhere. Together with the corner mass it lands the default car at
about 200 kN/m on a 40 kg corner: a 13 mm static deflection and an 11 Hz wheel hop, both of which are
what a real corner does.

**And the game's rebound clamp is not applied here at all.** That is the other half of the wheel hop
story. `clamp(spring + damper, 0, +inf)` means a damper pulling the body down is thrown away whenever the
spring force is small - and during a hop the spring is weak exactly at the top of the excursion, which is
when the damper is the only thing left that could stop the wheel coming back down. The result was wheels
that bounced twice off a small bump however hard the damper was set, because only the compression half of
each hop was ever damped. The clamp has no purpose once the wheel has inertia: the tyre's one-sided force
is the real bound.

An earlier revision of this shipped three of these as sliders, and got the damping wrong at the same
time: 0.07 is a *suspension* damping ratio, not a tyre one. That is what made the wheels visibly hop for
a dozen cycles after every bump. The tyre was doing almost nothing, and a digressive damper has already
blown off by the time the wheel is moving fast enough for the hop to matter - at a 1.2 m/s hop velocity
the shipped damper model produces about a third of the linear force. Tyres, not dampers, are what control
wheel hop, which is why the damping ratio is the number to revisit first if hop is ever too visible
again.

Everything above the wheel is untouched. `hasHit`, `wheelHit`, the surface and road-colour detection and
the ~70 systems that read them - dust, skidmarks, particles, sound, damage, torque distribution - keep
their own meaning, so a wheel that is off the ground in the model is still "grounded" to them.

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
- **The step size.** The whole stability argument in section 4.1 assumes 0.02 s. The game logs
  `Set fixed timestep to ...`, and the panel's summary line prints the value it is actually using.
- **The vertical model.** With `TireVertical/Model = Linear` the panel prints each wheel's deflection, its
  tyre force and the wheel hop frequency it ended up with. On the default car the deflection should settle
  around 13 mm - about `staticLoad / stiffness` - and the frequency should read 11 to 12 Hz. Neither should
  grow: `SubstepsFor` is supposed to have made that impossible, so growth would be a bug rather than a
  setting. A wheel that is genuinely off the ground should read a negative deflection, and after a normal
  bump it should be back on the ground within about one hop, not two or three.

## 7. Deferred

1. **Spring model slot.** Nothing to fix today: the spring is used over its whole domain and the parts are
   already tuned to a common frequency. Only worth adding alongside item 2.
2. **Fixing `progressiveness`** so it moves the curve values and not only the tangents.
3. **Anti-squat / anti-dive.** `WheelController.squat = 0.2f` is live - `UpdateForces` turns
   `forwardFriction.force * radius * squat` into a couple on the chassis - but it has no part property and
   no UI, so it cannot be adjusted. Note the coupling: it scales the *tire force*, so changing tire model
   changes the car's pitch under power. `IsChassisTorqueEnabled` is the neighbouring dead field (never
   assigned, so its branch never runs), and `DummyRigidbody` is written but never read.
4. **Honouring `Part.SuspensionDamper`** (the dead 1.3 on Race parts). It restores the game's own data
   rather than rebalancing it, but it does change car balance, so it is off the table unless the user asks
   for it.
5. **Per-direction knee velocity** (real dampers have separate bump and rebound shim stacks, and therefore
   separate knees). The four coefficients are already independent; this is the last shared number.
6. **A tyre that is not linear in the vertical** - progressivity, load dependence, separation. The slot
   accepts it; nothing ships it.
