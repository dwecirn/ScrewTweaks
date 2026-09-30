# ScrewTweaks

[English](README.md) | **中文** | [日本語](README.ja.md)

一个面向 **Screw Drivers** 的 BepInEx 模组套件：用**游戏自己的零件数据**重建轮胎与悬挂的受力模型，并在其上加入四向可调阻尼、有垂向自由度的轮子，以及闭环的 ABS 与牵引力控制。

所有模型只读零件本来就带的那些数——`FrictionPreset` 的 B/C/D/E、`loadGripCurve`、`maximumTireGripForce`、`springforce`、`damperforce`、`SuspensionStiffness`。所以车的行为取决于它的零件怎么写，而街车与赛车的差别，仍然是游戏本来就打算让你感受到的那个差别。

---

## 它改变了什么

**轮胎力来自滑移曲线。** 针对轮胎自身的每路面 B/C/D/E 求值一条 **Pacejka-89 形式的 Magic Formula**，峰值由游戏的 `loadGripCurve` 缩放；轮子转速由模型自行积分，所以驱动与制动力矩作用在转动惯量上。

**组合滑移。** 一个 **ADAMS 摩擦椭圆**，即 Project Chrono 的 `ChPac02Tire` 所用的形式：纵向与横向需求共用一份由轮胎自身极限决定的预算，超过纵向峰值的轮子会让出横向力。

**瞬态滑移。** 两个滑移分量各有一条**松弛长度**，力在距离上积累；回写给游戏的是**运动学滑移**，动力总成与辅助系统读到的就是它。

**垂向自由度。** 一个**两质量四分之一车模型**：非簧载质量取自轮子零件，轮胎的垂向刚度与阻尼由悬挂自身的轮速推导，子步数按 **ω·dt 稳定判据**确定。

**四向阻尼。** 卸压速度两侧各有独立的压缩与回弹系数，分段线性、在拐点处连续，存在悬挂零件上，所以每辆车带着自己的设定。

**闭环辅助。** 以滑移率为目标，带增益与下限，每个轮子每个物理步各求值一次。

这些模型是可替换的插槽：`ITireModel`、`IDamperModel`、`ITireVerticalModel`、`IBrakeAid`/`IDriveAid` 与面板宿主都是公开 API，每一个的实例见 [docs/extending.zh-CN.md](docs/extending.zh-CN.md)。

两个物理模块的推理过程与实测到的游戏数据，分别在 [`docs/tire-model-spec.md`](docs/tire-model-spec.md) 与
[`docs/suspension-model-spec.md`](docs/suspension-model-spec.md)。


---
## 模块

每个插件互相独立，可以单独安装、开关、调参；唯一的例外是面板——其余模块都把标签页注册进 `ScrewTweaks.Panel.dll`，所以它必须存在。

| 模块 | 作用 | 说明 |
|---|---|---|
| `ScrewTweaks.Panel` | 共享的游戏内面板及其自身设置 | [面板](#面板) |
| `ScrewTweaks.Physics.Tires` | 轮胎受力模型：滑移曲线、组合滑移、松弛长度 | [轮胎物理](#轮胎物理) |
| `ScrewTweaks.Physics.Suspension` | 四向阻尼与轮子的垂向自由度 | [悬挂物理](#悬挂物理) |
| `ScrewTweaks.ECU` | ABS 与牵引力控制通道 | [ECU](#ecu) |
| `ScrewTweaks.Steering` | 瞬间转向、转向限制松弛 | [转向](#转向) |
| `ScrewTweaks.Views` | 不滞后的第三人称视角 | [视角](#视角) |
| `ScrewTweaks.AutoShift` | 更快的自动换挡 | [自动换挡](#自动换挡) |
| `ScrewTweaks.EngineSound` | 混合动力车的引擎声音 | [其他插件](#其他插件) |
| `ScrewTweaks.PowerFactor` | 按引擎类型的功率系数，随车存档 | [其他插件](#其他插件) |

按键：**F7** 开关面板，**F8** 把静态轮胎/轮子数据导出到 `BepInEx/ScrewTweaks.tire-dump.txt`，**F9** 录制 30 秒每轮轮胎遥测到 `BepInEx/ScrewTweaks.tire-telemetry.csv`。

## 安装

1. 如果还没装，先给 Screw Drivers 目录装好 **[BepInEx 5 (x64)](https://github.com/BepInEx/BepInEx/releases)**（`Screw Drivers.exe` 旁边要有 `BepInEx/` 和 `winhttp.dll`）。
2. 把所有 `ScrewTweaks.*.dll` 放进 `BepInEx/plugins/`。
3. 启动游戏。`BepInEx/LogOutput.log` 里应该每个插件各有一行日志。

> `ScrewTweaks.Panel.dll` 面板插件**必须安装**，所有插件都依赖它进行配置。
> 其余插件如无特别说明，都是可选的、互相独立的。

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

每个插件有各自的版本号：加载时会打印到 `BepInEx/LogOutput.log`，也可以在面板的 Settings 标签页里看到。
整个套件只用日期标记，GitHub 上的标签就是历次发布。

**本地参考资料不入库。** `ScrewTweaks/reference/`（Project Chrono 的克隆）与 `ScrewTweaks/ScrewDrivers/`（反编译的游戏）已被 gitignore。

## 面板

按 **F7** 打开一个浮动窗口，默认贴在**屏幕右侧、垂直居中**；拖标题栏移动。标签栏宽度按最长的标题自适应，剩下的宽度全部给内容区。每个功能插件注册一个带标题的板块：

- **ECU** —— ABS 与牵引力通道
- **Tires** —— 轮胎模型选择、调参、每轮实时遥测
- **Suspension** —— 阻尼与轮胎垂向模型选择、调参、每轮行程与受力实时显示
- **Auto Shift** —— 更快换挡时序的开关
- **Steering** —— 瞬间转向与转向限制松弛
- **Settings** —— 面板自身的设置，排在第一个：位置、语言，以及已加载的模块及各自版本

面板打开时鼠标可用、鼠标输入不再传给游戏，键盘输入照常。

### 面板设置

**Settings** 是面板自己的标签页，两项设置都保存在 `dev.dwecirn.screwtweaks.panel.cfg`：

| 设置 | 默认 | 含义 |
|---|---|---|
| `Panel/TabSide` | `Left` | 标签栏在窗口内的哪一侧。`Left` 或 `Right` |
| `Panel/Language` | `English` | 面板语言。`English`、`ChineseSimplified` 或 `Japanese` |

缩放手柄（点状）画在**能跟着鼠标走的那一个角**：通常右下角；但当窗口贴在屏幕右缘时改画在左下角，因为那时向右生长是不可能的。

---


所有数值都保存在 `BepInEx/config/dev.dwecirn.screwtweaks.<name>.cfg`。

## 轮胎物理

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

> **槽位负责的不只是力。** `WheelController.FrictionUpdate` 还会发布接触速度、轮子转速，以及动力总成与 TCS 回读的 `wheelHit` 滑移值。宿主会在你的模型前后把这些做完，所以模型只需要填力、滑移和轮子自转。
>
> **回写运动学滑移。** `forwardFriction.slip` / `sideFriction.slip` 是动力总成与辅助系统读取的反馈通道。回写松弛之后的值，会让每个辅助都晚一条松弛长度才反应过来——轮子先抱死，ABS 才响应。
>
> **轮胎识别是惰性解析的**，取自 `WheelController.PartConfigurationWheel`。插件加载前就已生成的车，要再生成一次才有识别信息；面板的 *Tires seen* 列表显示已经抓到的那些。
>
> **峰值力**在 `GripScale = 1` 时与原生模型钳位前的数值一致。原生模型随后会把合力向量钳到 `loadCoefficient`，所以它的有效纵向峰值是 `~loadCoefficient`，而这里能到 `|D| * loadCoefficient * forceCoefficient`（`forceCoefficient = 1.35` 时高约 25%）。这是摩擦椭圆应得的那一份。
>
> **仅 NWH 后端。** 旧的轮子路径一概不动。

## 悬挂物理

在 **F7 → Suspension** 里选择阻尼模型，选择会被记住。`Native` 完全不碰游戏自带的阻尼，也是默认值——所以装上这个插件、不选模型时，游戏行为不变。

弹簧是**刻意没动**的：把零件数据算过一遍之后发现它本来就是合理的（见 `docs/suspension-model-spec.md`）。游戏真正没有能力表达的是阻尼——它是 `系数 × |速度|`，**压缩和回弹用同一个系数**，而且完全没有卸压。

这个模型是**四向**的：压缩和回弹各自有独立的低速与高速系数，在同一个拐点速度处相接。

```
                 拐点以下   拐点以上
  压缩             1.60       0.40
  回弹             3.20       0.80
  拐点速度         0.10 m/s
```

**低速那个数**是曲线陡段（泄流）的斜率——它管的是慢速输入下的车身姿态，比如侧倾和俯仰。**高速那个数**是拐点之上的斜率，也就是卸压阀打开之后的段——路肩和落地看到的是它，数值越低越能把冲击吸收掉，而不是原样传给车身。

四个系数**随车存档，不随 mod**：它们是悬挂零件的属性，在造车界面里游戏自带的 *Spring Force* 和 *Damper Force* 下面编辑。每辆车各自保存，分享出去的车也带着自己的设置。

| 属性 | 默认 | 含义 |
|---|---|---|
| `damperbumplow` | 160% | 压缩、拐点以下。泄流段，管慢速输入下的车身姿态 |
| `damperbumphigh` | 40% | 压缩、拐点以上。路肩看到的是它，越低越能吸收 |
| `damperreboundlow` | 320% | 回弹、拐点以下 |
| `damperreboundhigh` | 80% | 回弹、拐点以上。落地后车身弹跳就调高它 |

它们是游戏为该轮算出的阻尼系数的**百分比**，所以同一个数在卡丁车和卡车上含义一致，而且四项都设成 100% 就是完全复刻原版。游戏的 *Damper Force* 没有被替代——它仍是基准，这四项是它的形状。

| 设置 | 默认 | 含义 |
|---|---|---|
| `Model/Selected` | `Native` | `Native` = 游戏自带的阻尼，完全不改 |
| `Damper/ReboundFloor` | `0.00` | 允许阻尼把车身**往下拉**多少，以该轮静态载荷的比例计。**选中垂向模型时完全不生效** |

**这四项只在选中阻尼模型时才会被读取。** `Native` 让游戏自带的阻尼全权负责、不读它们——但两种模式下都会随车保存，所以切换模型不会让任何一辆车丢掉调校。（只在需要时才注入，会让一辆分享出去的车"带不带调校"取决于发送方的 mod 配置，而那正是"数据放在车上"要避免的事情。）

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

> **触底的时候。** 游戏只在 `else if (hasHit)` 分支里计算 `damper.force`，所以压在限位块上时它沿用上一帧的值，一个陈旧的力就这样进了合力。本模块在那种情况下照常求值。选中非 `Native` 模型时，这是阻尼规律之外唯一一处改动游戏原有行为的地方。
>
> **范围。** 不含防倾杆与束角；这里打磨的是本来就有的弹簧与阻尼。

## ECU

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

> **缺失的算法会回落到 `Native`。** 如果配置里写的算法所属插件已经不在了，该通道会退回游戏自带的辅助。把游戏的辅助旁通掉、却没有任何东西接替，等于悄悄删掉了 ABS。

## 转向

两项设置各出现两次——游戏自带的操控设置页里各有一行（在 *Ignore Steer Angle Limit* 下方），**F7 → Steering** 面板里也各有一个控件。两边改的是同一个配置项，所以不会各说各话。

| 设置 | 默认 | 含义 |
|---|---|---|
| `Steering/InstantSteering` | `false` | 键盘 / 十字键这类二值输入**立即到位**，不再被逐渐推过去——也就是和摇杆原本的直驱行为一致 |
| `Steering/LimitRelax` | `0.00` | 把游戏随速度收紧的转向限制朝"无限制"方向混合。`0` = 原版，`1` = 等同游戏自带的 *Ignore Steer Angle Limit* |

两项都保存在 `dev.dwecirn.screwtweaks.steering.cfg`，改完会**立即作用到场上已有的车**，不用重新生成。

Instant Steering 原本是造车界面里每个悬挂的开关。现在它是全局设置，所以一次作用于所有车；用旧属性存过的车会在存档里留一条不再使用的属性，游戏会忽略它。

## 视角

游戏自带的第三人称视角都会把相机朝车身平滑过去，而那些平滑系数在相机 prefab 上，任何设置里都没有。这个模块把其中一个视角变成刚性的：相机直接拴在车身上，每帧读取车的位置和姿态，帧与帧之间不记住任何东西，所以地平线随车倾斜，和驾驶舱视角一样。

在 **F7 → 视角** 里打开。要 patch 游戏哪个视角，就在**游戏里**切到那个视角，然后开着面板点 **取用当前**——游戏给视角编号，设置里存的就是那个编号。

| 设置 | 默认 | 含义 |
|---|---|---|
| `Follow/Enabled` | `false` | 对 patch 的那个视角启用刚性跟随。和面板里的勾选框是同一个设置 |
| `Follow/Mode` | `10` | patch 游戏哪个视角 |
| `Follow/Pose` | `Rigid` | 姿态算法，通过 `ViewPoses.Register` 注册 |
| `Follow/Distance` | `3.0` | 相机在车后多远，以车身自身尺寸半径为倍率 |
| `Follow/Height` | `1.4` | 相机在车身上方多高，同样是尺寸半径的倍率 |
| `Follow/AimHeight` | `0.3` | 相机瞄准车体坐标系里的哪个高度，同样是尺寸半径的倍率 |

只改那一个视角，其它视角保持游戏原本的平滑。距离和高度都以车身自身尺寸为倍率，所以卡丁车和卡车用同一套数值都合适。

## 自动换挡

`dev.dwecirn.screwtweaks.autoshift.cfg` 里的 `General/Enabled`，和 **F7 → Auto Shift** 里的勾选框是同一个设置。越过转速阈值后 0.2 秒就换挡（原版 1 秒），冷却和扭矩中断也更短；取消勾选会把游戏自己的数值写回去。

## 其他插件

- **Engine Sound** —— 恢复原版选择器在混动车上丢掉的那一半引擎声音，并按瞬时扭矩把两者混合。
- **Power Factor** —— 按引擎类型设置的功率系数，随车的存档一起走。**全自动**：通过 Harmony 注入并持久化，不需要键位。

## 第三方

- 合滑移的 **ADAMS 摩擦椭圆**改编自 [Project Chrono](https://github.com/projectchrono/chrono) 的
  `ChPac02Tire::CalcFxyMz`（`src/chrono_vehicle/wheeled_vehicle/tire/ChPac02Tire.cpp`），BSD-3-Clause。
  项目**没有**编译或分发任何 Chrono 代码，只移植了公式。
- 轮胎插槽、面板注册表、辅助算法插槽均为本项目原创。
