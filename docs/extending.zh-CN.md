# 编写自己的算法

Part of [ScrewTweaks](../README.md).

整套东西就是为了可扩展而搭的。四个真正的扩展点，全部是公开 API。

你的项目需要引用要扩展的那个插件 DLL，并设为**不本地复制**（BepInEx 已经提供了它）：

```xml
<Reference Include="ScrewTweaks.ECU">
  <HintPath>$(GameDir)\BepInEx\plugins\ScrewTweaks.ECU.dll</HintPath>
  <Private>false</Private>
</Reference>
```

并声明依赖，保证加载顺序正确：

```csharp
[BepInDependency("dev.dwecirn.screwtweaks.ecu")]
```

### 1. 面板板块

```csharp
using ScrewTweaks.Panel;

private void Start() => PanelHost.Register("My Section", DrawSection);

private void DrawSection()
{
    GUILayout.Label("hello");
    if (GUILayout.Button("do a thing")) { /* ... */ }
}
```

在 `Start()` 里调用 `PanelHost.Register(title, draw)`。绘制回调每帧在一个滚动视图内被调用一次；请使用 `GUILayout`。用同一个标题再次注册会替换掉原来的回调。

板块用和套件本身同一张翻译表来本地化。字符串以**英文原文为键**，所以缺译文时会退回到可读的英文，也可以一个字符串一个字符串地慢慢翻译：

```csharp
using ScrewTweaks.Panel;

// 在 Start() 里，紧跟 PanelHost.Register 之后：
Loc.Add(PanelLanguage.Japanese, ("hello", "こんにちは"));
Loc.Add(PanelLanguage.ChineseSimplified, ("hello", "你好"));

// 给标签页标题本身也加一条，标签页就是本地化的了
Loc.Add(PanelLanguage.ChineseSimplified, ("My Section", "我的板块"));
```

之后在绘制回调里 `Loc.T("hello")` 就会返回译文，`Loc.Tf` 用来填格式化占位符。除你自己的字符串之外，别人的东西不会被碰到。

### 2. 轮胎模型

实现 `ITireModel` 并注册。它会**立刻**出现在 **Tires** 下拉里。

```csharp
using ScrewTweaks.Physics.Tires;

public sealed class MyTire : ITireModel
{
    public string Name => "MyTire";
    public string Description => "What it does.";

    // 返回 true 表示你已经算出了力并积分了轮速。
    // 返回 false 表示这一步交给游戏自带的摩擦。
    public bool Apply(WheelController wheel, float dt)
    {
        // 速度已经替你读好了：
        float vx = wheel.forwardFriction.speed;
        float vy = wheel.sideFriction.speed;

        // ... 用你自己的模型算出 fx, fy ...

        wheel.forwardFriction.force = fx;
        wheel.sideFriction.force = fy;
        wheel.forwardFriction.slip = slipRatio;   // 运动学滑移，见"坑"一节
        wheel.sideFriction.slip = slipAngle;
        // 并且自己积分 wheel.wheel.angularVelocity
        return true;
    }
}

// 在 Start() 里：
TireModels.Register(new MyTire());
```

### 3. 阻尼或轮胎模型

这里有两个扩展点，落在同一处接缝上：`IDamperModel` 管阻尼的力规律，`ITireVerticalModel` 管轮子有了垂向自由度之后轮胎的垂向力。

实现 `IDamperModel` 并注册，它会立刻出现在 **Suspension** 下拉里。

```csharp
using ScrewTweaks.Physics.Suspension;
using UnityEngine;

public sealed class MyDamper : IDamperModel
{
    public string Name => "MyDamper";
    public string Description => "例子：力与速度的平方根成正比。";

    // 返回阻力的大小 [N]，永远非负。
    // 方向不归你管：宿主负责加符号（压缩向上顶、回弹向下拉）和接触法线。
    public float Evaluate(in DamperState s)
    {
        // s.Compressing - 悬挂正在被压缩时为 true
        // s.Velocity    - |m/s|，永远非负
        // s.GameCoefficient - 游戏为这个轮子算出的系数 C [N*s/m]，由车重、轮数和零件的
        //                     damperforce 构成。以它为基准，你的模型就和游戏其余部分一致。
        // s.Travel、s.CompressionPercent、s.SpringForce、s.Wheel - 需要就用。
        float c = s.GameCoefficient * (s.Compressing ? 1f : 2f);   // 分离自己算
        return c * Mathf.Sqrt(s.Velocity);
    }
}

// 在 Start() 里：
DamperModels.Register(new MyDamper());
```

每个物理步、每个接地轮调用一次，在游戏做完碰撞检测和几何之后、力真正施加之前——所以轮胎载荷、车身受力和减震器音效拿到的都是你的数值。系数为 `0` 的地方（坦克履带轮）以它为基础缩放的模型自然无效果。

`ITireVerticalModel` 是另一半：只有在 `TireVertical/Model` 不是 `Native` 时才会被调用，而且调用频率是每个**子步**一次，不是每个物理步一次。

```csharp
using ScrewTweaks.Physics.Suspension;
using UnityEngine;

public sealed class MyTyre : ITireVerticalModel
{
    public string Name => "MyTyre";
    public string Description => "例子：越压越硬的轮胎。";

    // 返回地面把轮子往上顶的力 [N]，永远非负。
    public float Evaluate(in TireVerticalState s)
    {
        // s.Deflection         - 轮胎压入地面多少 [m]，离地时为负
        // s.DeflectionRate     - 轮子与地面的相对速度 [m/s]
        // s.UnsprungMass       - 轮子自身质量 [kg]
        // s.ReferenceStiffness - 宿主为这个轮子算出的线性刚度，适合当基准
        float d = Mathf.Max(s.Deflection, 0f);
        return s.ReferenceStiffness * d * (1f + d * 20f);   // 渐进式
    }
}

// 在 Start() 里：
TireVerticalModels.Register(new MyTyre());
```

### 4. ABS / 牵引力算法

实现 `IBrakeAid` 或 `IDriveAid` 并注册。它会**自动**出现在 **ECU** 下拉里，并在配置里**按名字**保存。

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

        // ctx.Controller 就是 NWH 的轮子：载荷、半径、角速度、电机/刹车扭矩、
        // 当前生效的摩擦预设、最新的滑移值。非 NWH 后端时为 null。
        if (ctx.Controller == null) return desiredBrake;

        float peakSlip = 0.125f;                     // 游戏沥青曲线的峰值位置
        return Mathf.Abs(ctx.ForwardSlip) > peakSlip ? 0f : desiredBrake;
    }
}

// 或者用 IDriveAid：EcuAids.Register(new MyTraction());
EcuAids.Register(new MyAbs());
```

`IBrakeAid` / `IDriveAid` 每个轮子每个物理步被调用一次——**在游戏算完自己的扭矩之后、交给轮子之前**，所以你只需要返回修改后的值。该通道上游戏自带的辅助会被自动旁通。

