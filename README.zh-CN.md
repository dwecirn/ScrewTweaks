# ScrewTweaks

[English](README.md) | **中文** | [日本語](README.ja.md)

一个面向 **Screw Drivers** 的 BepInEx 模组套件，目标是在 sim racing 方向上**优化**这款游戏的驾驶体验：真实的轮胎滑移行为、建立在它之上的驾驶辅助，以及若干体验改进。

它不提供一套固定方案，而是围绕**可替换的插槽**搭建：轮胎模型、ABS / 牵引力算法、面板都是公开的扩展点。随套件附带的那几个实现只是"碰巧由本项目提供的那一套"——其他开发者可以随时换用自己的算法，而不必改动本项目的代码。见[编写自己的算法](#编写自己的算法)。

整个套件拆成互相独立的插件，可以单独安装、单独开关、单独调参；所有设置集中在一个游戏内面板和普通的 `.cfg` 文件里。

---

## 目录

- [安装](#安装)
- [从源码构建](#从源码构建)
- [插件与键位](#插件与键位)
- [游戏内面板](#游戏内面板)
- [调参参考](#调参参考)
  - [轮胎物理](#轮胎物理)
  - [ECU](#ecu)
  - [转向](#转向)
  - [其他插件](#其他插件)
- [编写自己的算法](#编写自己的算法)
  - [1. 面板板块](#1-面板板块)
  - [2. 轮胎模型](#2-轮胎模型)
  - [3. ABS / 牵引力算法](#3-abs--牵引力算法)
- [行为约定与坑](#行为约定与坑)
- [设计说明](#设计说明)
- [第三方](#第三方)

---

## 安装

1. 如果还没装，先给 Screw Drivers 目录装好 **BepInEx 5 (x64)**（`Screw Drivers.exe` 旁边要有 `BepInEx/` 和 `winhttp.dll`）。
2. 把所有 `ScrewTweaks.*.dll` 放进 `BepInEx/plugins/`。
3. 启动游戏。`BepInEx/LogOutput.log` 里应该每个插件各有一行日志。

> `ScrewTweaks.Panel.dll` **必须存在**。它是面板宿主，其他功能插件把各自的板块注册进去。其余插件都是可选的、互相独立的。

---

## 从源码构建

需要 .NET SDK 和一份本地游戏（构建会从游戏目录引用 `Assembly-CSharp.dll` 和 `0Harmony.dll`）。游戏路径按以下顺序解析：

1. `-p:GameDir="C:\path\to\Screw Drivers"`
2. 环境变量 `SCREWDRIVERS_DIR`
3. 仓库根目录的 `.env` 文件（`SCREWDRIVERS_DIR=...`，见 `.env.example`）

```powershell
dotnet build ScrewTweaks.sln --no-restore -m:1
```

构建成功后每个插件会自动复制到 `BepInEx/plugins`。

> 在当前这套 NuGet 配置下，单独跑 `dotnet restore` 可能会卡住；一次性还原之后一直用 `--no-restore` 是最可靠的方式。`-m:1` 用于规避某些机器上的并行构建问题。

键位是 `keybinds.props` 里的编译期默认值，可在插件自己的 `.cfg` 里运行时覆盖。

### 版本号

每个插件有**自己独立的版本**，并且只在一个地方定义：该插件 `.csproj` 里的 `<Version>`。`[BepInPlugin]` 的版本字符串和 DLL 元数据都由它生成，所以没有第二处需要同步。

**一次发布是一个"包"，而不是一个统一版本号：**

- 插件的版本号**只在它自己变了时才动**。没变的插件保持原版本，这才是正确的行为；
- **git 标签用日期**（`2026.09.29`），它标识的是"这次打包"，不会被误当成某个组件的版本；
- **版本号真正起作用的地方是 `BepInDependency`**：插件可以要求另一个插件的最低版本，不满足时 BepInEx 会**明确报错并拒绝加载**，而不是运行到一半崩掉。

`tools/release.ps1` 负责最烦的那一步——算出**自上次标签以来哪些插件变了**：

```powershell
pwsh tools/release.ps1              # 哪些变了 + 各插件当前版本
pwsh tools/release.ps1 -Package     # 同上，并构建打包成 zip
```

不想看 git 历史，只想要全部版本号：

```powershell
dotnet build ScrewTweaks.sln --no-restore -m:1 -t:ListVersions
```

---

## 插件与键位

| 程序集 | 用途 | 键 |
|---|---|---|
| `ScrewTweaks.Panel` | 共享的 F7 面板宿主，自身没有功能 | **F7** |
| `ScrewTweaks.Physics.Tires` | 可插拔的轮胎模型插槽（游戏内可选） | **F9**（录制遥测） |
| `ScrewTweaks.Physics.Suspension` | 可插拔的阻尼模型插槽（游戏内可选） | — |
| `ScrewTweaks.ECU` | ABS / 牵引力控制通道 | — |
| `ScrewTweaks.Steering` | Instant Steering + Steering Limit Relax | — |
| `ScrewTweaks.AutoShift` | 更快的自动换挡（F7 → Auto Shift） | — |
| `ScrewTweaks.EngineSound` | 混合动力车的引擎声音（两种引擎都能听到） | — |
| `ScrewTweaks.PowerFactor` | 按引擎类型设置的功率系数，随车存档（全自动） | — |

其他键位：**F8** 把静态轮胎/轮子数据导出到 `BepInEx/ScrewTweaks.tire-dump.txt`。

---

## 游戏内面板

按 **F7** 打开一个浮动窗口，默认贴在**屏幕右侧、垂直居中**；拖标题栏移动。标签栏宽度按最长的标题自适应，剩下的宽度全部给内容区。每个功能插件注册一个带标题的板块：

- **ECU** —— ABS 与牵引力通道
- **Tires** —— 轮胎模型选择、调参、每轮实时遥测
- **Suspension** —— 阻尼与轮胎垂向模型选择、调参、每轮行程与受力实时显示
- **Auto Shift** —— 更快换挡时序的开关
- **Settings** —— 面板自身的设置，永远在最后

面板打开时会解锁鼠标。板块按插件加载顺序排列。

### 面板设置

**Settings** 是面板自己的标签页，两项设置都保存在 `dev.dwecirn.screwtweaks.panel.cfg`：

| 设置 | 默认 | 含义 |
|---|---|---|
| `Panel/TabSide` | `Left` | 标签栏在窗口内的哪一侧。`Left` 或 `Right` |
| `Panel/Language` | `English` | 面板语言。`English`、`ChineseSimplified` 或 `Japanese` |

缩放手柄（点状）画在**能跟着鼠标走的那一个角**：通常右下角；但当窗口贴在屏幕右缘时改画在左下角，因为那时向右生长是不可能的。

语言作用于**所有板块**，不只是面板本身——某个插件的板块如果没提供译文，它就保持英文，所以切换语言不会变成半中半英的样子。算法名和模型名**不翻译**：它们就是配置文件里存的那串字符，也是传给 `EcuAids.Register` / `TireModels.Register` / `DamperModels.Register` 的字符串。

README 和 `.cfg` 文件里的注释不受此影响，始终是英文。

---

## 调参参考

所有数值都保存在 `BepInEx/config/dev.dwecirn.screwtweaks.<name>.cfg`。

### 轮胎物理

在 **F7 → Tires** 里选择模型。选择会被记住，重启后仍然生效。

| 设置 | 默认 | 含义 |
|---|---|---|
| `Model/Selected` | `Native` | `Native` = 游戏自带摩擦，完全不变 |
| `Pacejka/GripScale` | `1.00` | 峰值抓地力倍率。**1.0 = 与游戏完全一致** |
| `Pacejka/CombinedSlip` | `1.00` | `0` = 纵向与侧向独立，`1` = 完整摩擦椭圆 |
| `Pacejka/RelaxationLength` | `0.30` | 轮胎建立滑移所需的距离 [m]，以 0.30 m 的轮子为基准。随轮子半径缩放。`0` = 关闭 |
| `Pacejka/CamberThrust` | `0.015` | 外倾推力，每度外倾角占轮荷的比例 |
| `Pacejka/CamberDynamics` | `1.00` | 外倾使轮胎"变软"的程度（峰值后移、侧向峰值下降）。`0` = 只保留推力 |
| `Pacejka/GeometryShapeCoupling` | `0.00` | 接触面（半径 × 宽度）对峰值位置的影响强度。**默认关闭** |

模型需要的其他一切全部来自游戏本身，并且是**按轮、按物理步**读取的：`Grip` → `maximumTireGripForce`、`loadGripCurve`、轮胎自己的 `FrictionPresetAsphalt` / `FrictionPresetSand`（按路面自动选择）、`forceCoefficient`、`camberAngle`、电机扭矩与刹车扭矩。

**峰值抓地力与原版对齐**，所以切换模型改变的是**行为**，而不是车有多少抓地力。想改抓地力本身，用 `GripScale`。

**Tires 板块同时显示每轮实时遥测：**

```
PartWheelDirt2 g= 0.30  BCDE=(  7.0, 1.10, 0.83, 1.00)  k= 0.021/ 0.000 a=  -4.1/  -3.9 y=0.51 ...
```

`k` / `a` 显示为 *松弛后 / 运动学*；`BCDE` 是**当前实际生效**的摩擦预设——开上不同路面时它会变化，可以直接看到每种轮胎的曲线在切换。

**F9** 会把 30 秒内每个轮子的数据录到 `BepInEx/ScrewTweaks.tire-telemetry.csv`
（`t, wheel, body, tire, tireGrip, BCDE, kappa, alphaDeg, kappaRaw, alphaRawDeg, Fx, Fy, Fz, vx, omega, fwdMax, sideMax, radius, sigma, peak, camberDeg, camberFx`）。

### 悬挂物理

在 **F7 → Suspension** 里选择阻尼模型，选择会被记住。`Native` 完全不碰游戏自带的阻尼，也是默认值——所以装上这个插件、不选模型时，游戏行为不变。

弹簧是**刻意没动**的：把零件数据算过一遍之后发现它本来就是合理的（见 `docs/suspension-model-spec.md`）。游戏真正没有能力表达的是阻尼——它是 `系数 × |速度|`，**压缩和回弹用同一个系数**，而且没有卸压。

| 设置 | 默认 | 含义 |
|---|---|---|
| `Model/Selected` | `Native` | `Native` = 游戏自带的阻尼，完全不改 |
| `Damper/ReboundRatio` | `2.00` | 回弹系数相对压缩系数的倍数。游戏两个都是 `1.00` |
| `Damper/LowSpeedGain` | `1.60` | 拐点速度以下的阻尼倍率 |
| `Damper/KneeVelocity` | `0.10` | 卸压阀打开的悬挂速度 [m/s] |
| `Damper/BlowOffRatio` | `0.25` | 拐点之后的斜率，相对拐点之前斜率的比例 |
| `Damper/ReboundFloor` | `0.00` | 允许阻尼把车身**往下拉**多少，以该轮静态载荷的比例计 |

`LowSpeedGain 1.00` + `BlowOffRatio 1.00` + `ReboundRatio 1.00` 就是**完全复刻原版**，这是拿来 A/B 对比的开关。所有东西都是游戏自己算出的那个系数的倍数，所以质量、轮数、`damperforce` 属性的缩放任然是游戏原本那套。

**`Suspension` 板块同时显示每轮的实时悬挂状态：**

```
  wheel              comp     C Ns/m   dir  v m/s    damper N    game N   spring N
  PartWheelDirt2      52%      1248  bump   -0.31        412       387      2310
```

`damper N` 是模型算出来的，`game N` 是同一速度下游戏原本会算出的值——差值在开车过程中直接看得见。

**`ReboundFloor` 需要解释一下。** 游戏把悬挂合力钳在 0，所以回弹力一旦大于弹簧力就被截成 0，阻尼永远无法把车身往下拉。这防止了车被吸进地面，但也意味着**悬挂接近全伸张时回弹阻尼恰好失效**——而过波峰时正需要它。没有垂向模型时，阻尼是唯一能代替轮子惯性的东西，所以调高它就是拿回那部分权限的唯一办法。`0` 就是游戏原本的钳位；默认关闭，因为它改的是游戏的积分器，而不只是阻尼规律。

**不过选中垂向模型时它完全不生效。** 钳位的前提是刚性轮子没有惯性、阻尼把车身往下拉时后面没有任何东西；轮子一旦能悬空，这个下拉就是真实的，边界变成轮胎只能推不能拉的单侧力。保留钳位会把**每一次轮跳的整个回弹半边**都丢掉——这正是"阻尼拉满、小颠簸仍然弹两次"的原因。

#### 轮子的垂向自由度

另一个缺口更大。游戏的轮子**根本没有垂向自由度**：`SuspensionUpdate` 从射线读出地面，然后把悬挂长度直接赋成"让轮胎正好贴地"的值，于是轮胎是刚性的、轮子本身没有质量。这就是为什么路缘石、伸缩缝和落地都是纯冲量，也是为什么只有阻尼能吸收东西。

`TireVertical/Model = Linear` 给轮子一个垂向自由度：轮胎对地面变成弹簧加阻尼器，轮子以自身质量悬在悬挂上方。

**其余都没有可调项——轮胎的数值全部从你已经装上的零件推出来：**

- **非簧载质量** —— 轮子零件自身质量的 **2 倍**。游戏给的那个数只是轮子本身；真实的一个角还包括轮毂、刹车和半根摆臂，而**这个角相对于轮胎有多重，决定了颠簸会不会把轮子顶离地面**；
- **轮胎刚度** —— 悬挂轮速（`maxForce / maxLength`）的 6 倍。真实车是轮速的 5~10 倍，所以重型车配长行程会得到软的大轮胎、卡丁车得到硬的小轮胎，**没有任何针对具体车型的常数**；
- **轮胎阻尼** —— 临界阻尼的 0.2，落在实测轮胎垂向阻尼比（0.1~0.3）的中段；
- **子步数** —— 由跳动频率算出，所以不可能被调成不稳定的。

默认车上这大约是 40 kg 的一个角配 200 kN/m：静态挠度 13 mm、轮子跳动 11 Hz，都和真实车一致。面板会打印每个轮子实际得到的值。

| 设置 | 默认 | 含义 |
|---|---|---|
| `TireVertical/Model` | `Native` | `Native` = 游戏自带的刚性轮子。`Linear` = 弹簧+阻尼器轮胎 |

打开之后轮子会跟随路面、也会离地，尖锐载荷被轮胎吸收而不是直接传给车身。推理过程见 `docs/suspension-model-spec.md` §5.5——包括**为什么控制轮子跳动的是轮胎阻尼而不是阻尼器**：如果以后觉得跳动还是太明显，该动的就是这个数。

### ECU

两条互相独立的通道，每条既可以选**已注册的算法**，也可以选两个保留模式之一：

- **`Native`** —— 不动游戏自带的辅助。*（默认）*
- **`Off`** —— 关掉它，但不替代。
- 其他任何名字 —— 已注册算法的名字（内置：**`Progressive`**）。

| 设置 | 默认 | 含义 |
|---|---|---|
| `Channels/Abs` | `Native` | ABS 算法 |
| `Channels/Traction` | `Native` | 牵引力算法 |
| `Abs/TargetSlip` | `0.12` | 渐进式 ABS 维持的滑移。游戏沥青曲线峰值在 ~0.125 |
| `Abs/Gain` | `6.0` | 滑移超过目标后削减刹车的力度 |
| `Abs/Floor` | `0.10` | 打滑时保留的刹车比例下限 |
| `Traction/TargetSlip` | `0.12` | 渐进式牵引力控制维持的滑移 |
| `Traction/Gain` | `3.0` | 滑移超过目标后削减驱动扭矩的力度 |

选择任何算法时，都会**自动旁通该通道上游戏自带的 TCS/ABS**，避免两者互相打架。发 动机制动和脚刹走的是同一条刹车通道，所以滑行工况也由 ABS 覆盖。

### 转向

- **Instant Steering** —— 造车界面里的每个悬挂一个开关。键盘 / 十字键这类二值输入会**立即到位**，不再被 `FrontWheelsSteerer` 慢慢推过去——也就是和摇杆原本的直驱行为一致。
- **Steering Limit Relax** —— 在游戏自带的操控设置页里，位于 *Ignore Steer Angle Limit* 下方的一个滑条。把游戏随速度收紧的转向限制朝"无限制"方向混合。`0` = 原版。

### 其他插件

- **Auto Shift** —— `dev.dwecirn.screwtweaks.autoshift.cfg` 里的 `General/Enabled`，和 **F7 → Auto Shift** 里的勾选框是同一个设置。越过转速阈值后 0.2 秒就换挡（原版 1 秒），冷却和扭矩中断也更短；取消勾选会把游戏自己的数值写回去。
- **Engine Sound** —— 恢复原版选择器在混动车上丢掉的那一半引擎声音，并按瞬时扭矩把两者混合。
- **Power Factor** —— 按引擎类型设置的功率系数，随车的存档一起走。**全自动**：通过 Harmony 注入并持久化，不需要键位。

---

## 编写自己的算法

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

---

## 行为约定与坑

这些很容易做错，而且出问题时的症状很难判断，所以单独列出来。

**轮胎插槽负责的不只是力。**
`WheelController.FrictionUpdate` 还负责发布接触点速度、轮子 RPM，以及 `wheelHit` 里的滑移值——游戏的动力系统和 TCS 会读回它们。这些都由插槽宿主在调用你的模型**前后**完成，所以模型只需要填力、填滑移、积分轮速。

**要发布*运动学*滑移，不是你的内部滑移。**
`forwardFriction.slip` / `sideFriction.slip` 是游戏的反馈通道：`MechanicalOutputWheel` 自己的 ABS/TCS 会读它，ECU 的辅助也会读它。如果用的是带松弛长度的模型却把松弛值发布出去，**所有辅助都会晚一个松弛长度才反应过来**——轮子已经锁死了 ABS 才开始动作。发布轮子**运动学上**正在发生的事。

**轮胎身份是懒解析的，发生在第一个物理步。**
它来自 `WheelController.PartConfigurationWheel`，所以插件加载**之前**就已经生成的车，在重新生成之前不会有身份。面板里的 **Tires seen** 列表会显示已经捕获到什么。

**找不到算法时会回退到 `Native`，而不是"受管但空转"。**
如果配置里写的算法对应的插件已经不在了，该通道会退回游戏自带的辅助。把游戏的辅助旁通掉却没有人接管，会**静默地让车没有 ABS**。

**抓地力大小。**
`GripScale = 1` 时峰值与游戏**未做 clamp 之前**的数值一致。注意原版还会额外把**合力向量**硬压到 `loadCoefficient`，所以它的有效纵向峰值是 `~loadCoefficient`，而我们是 `|D| * loadCoefficient * forceCoefficient`（在 `forceCoefficient = 1.35` 时高约 25%）。这是用真实摩擦椭圆替换原版圆形 clamp 的必然结果，是有意为之，且距"完全对齐"只差一个开关。

**悬挂触底时阻尼不会被重算。**
`WheelController.SuspensionUpdate` 只在 `else if (hasHit)` 分支里计算 `damper.force`，所以压在限位块上时用的是上一帧的旧值，而它仍然被算进了合力。悬挂模块在触底时照常求值。这是选中非 `Native` 模型时，它唯一一处改动游戏原有行为的地方。

**本地参考资料不入库。**
`ScrewTweaks/reference/`（Project Chrono 的克隆）和 `ScrewTweaks/ScrewDrivers/`（反编译的游戏）都已被 gitignore。请保持现状。

---

## 设计说明

轮胎模型的推理过程和实测到的游戏数据写在 [`docs/tire-model-spec.md`](docs/tire-model-spec.md)；悬挂的实测数据、游戏缩放的数值推导和阻尼接入设计写在 [`docs/suspension-model-spec.md`](docs/suspension-model-spec.md)。要改哪个模块，就先读哪一份——它们记录了游戏实际提供了什么。

数值的来源：

- **直接用游戏提供的：** 峰值抓地力大小、滑移曲线形状（按轮胎、按路面）、滑移量的单位、外倾输入、扭矩输入。
- **套件新增的：** 外倾推力、外倾软化、松弛长度、自己的轮速积分、真正的合滑移摩擦椭圆。
- **刻意不动的：** 游戏自己的轮胎平衡，以及游戏已经暴露给玩家的控件。

最后一条已经否掉了两个想法：外置 brake bias（游戏本来就通过刹车盘的 `brakeforce` 属性提供每轮独立的刹车力度），以及"越野胎在沥青上更差"的几何推导惩罚。这两件事游戏本身都已经有机制了。

---

## 第三方

- 合滑移的 **ADAMS 摩擦椭圆**改编自 [Project Chrono](https://github.com/projectchrono/chrono) 的
  `ChPac02Tire::CalcFxyMz`（`src/chrono_vehicle/wheeled_vehicle/tire/ChPac02Tire.cpp`），BSD-3-Clause。
  项目**没有**编译或分发任何 Chrono 代码，只移植了公式。
- 轮胎插槽、面板注册表、辅助算法插槽均为本项目原创。

## 许可

BSD-3-Clause，见 [`LICENSE`](LICENSE)。

**随便用、随便改、随便再分发、随便商用**（包括做成闭源 mod）。只有两个条件：**保留版权声明**，以及**未经许可不得用作者名义背书或推广衍生作品**。

轮胎模型从 Project Chrono 移植了一条公式，而 Chrono 用的是同一个许可证，所以不存在许可证混用问题。
