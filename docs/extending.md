# Writing your own algorithm

Part of [ScrewTweaks](../README.md).

The suite is built to be extended. Four real extension points, all public API.

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

A section is localised through the same table the suite uses. Strings are keyed by their English source,
so a missing translation falls back to readable English and a section can be translated one string at a
time:

```csharp
using ScrewTweaks.Panel;

// in Start(), next to PanelHost.Register:
Loc.Add(PanelLanguage.Japanese, ("hello", "こんにちは"));
Loc.Add(PanelLanguage.ChineseSimplified, ("hello", "你好"));

// add one for the tab title itself and the tab is localised too
Loc.Add(PanelLanguage.Japanese, ("My Section", "マイセクション"));
```

`Loc.T("hello")` inside the draw action then returns the translation, and `Loc.Tf` fills in a format
string. Nothing outside your own strings is touched.

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

### 3. A damper or tyre model

Two slots here, on the same patch point: `IDamperModel` for the damper's force law, and
`ITireVerticalModel` for the tyre's vertical force once the wheel has a degree of freedom of its own.

Implement `IDamperModel` and register it. It shows up in the **Suspension** dropdown immediately.

```csharp
using ScrewTweaks.Physics.Suspension;
using UnityEngine;

public sealed class MyDamper : IDamperModel
{
    public string Name => "MyDamper";
    public string Description => "Example: force goes with the square root of velocity.";

    // Return the magnitude of the resistive force in [N], never negative.
    // The host applies the sign (bump pushes the body up, rebound pulls it down) and the
    // contact normal, so direction is not your problem.
    public float Evaluate(in DamperState s)
    {
        // s.Compressing - true while the suspension is being compressed
        // s.Velocity    - |m/s|, never negative
        // s.GameCoefficient - the game's own C for this wheel [N*s/m], built from the car's mass,
        //                     the wheel count and the part's damperforce. Scale off it and your
        //                     model stays consistent with everything else in the game.
        // s.Travel, s.CompressionPercent, s.SpringForce, s.Wheel - the rest, if you want it.
        float c = s.GameCoefficient * (s.Compressing ? 1f : 2f);   // split it yourself
        return c * Mathf.Sqrt(s.Velocity);
    }
}

// in Start():
DamperModels.Register(new MyDamper());
```

Called once per grounded wheel per physics step, *after* the game has done the hit test and the geometry
but *before* the force is applied — so the tyre load, the chassis force and the bump sound all see your
number. Put a `0` in `GameCoefficient` (tank tracks) and a model scaled off it is a no-op there.

`ITireVerticalModel` is the other half: it is only consulted while `TireVertical/Model` is not `Native`,
and it is called once per **substep** rather than once per step.

```csharp
using ScrewTweaks.Physics.Suspension;
using UnityEngine;

public sealed class MyTyre : ITireVerticalModel
{
    public string Name => "MyTyre";
    public string Description => "Example: a tyre that stiffens as it is pressed in.";

    // Return the upward force the ground pushes the wheel with, in [N], never negative.
    public float Evaluate(in TireVerticalState s)
    {
        // s.Deflection         - how far into the ground the tyre is pressed [m]; negative in the air
        // s.DeflectionRate     - relative rate between wheel and ground [m/s]
        // s.UnsprungMass       - the wheel's own mass [kg]
        // s.ReferenceStiffness - the host's linear value for this wheel, a useful scale to work from
        float d = Mathf.Max(s.Deflection, 0f);
        return s.ReferenceStiffness * d * (1f + d * 20f);   // progressive
    }
}

// in Start():
TireVerticalModels.Register(new MyTyre());
```

### 4. An ABS or traction algorithm

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

