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
- **Steering** —— 瞬间转向与转向限制松弛
- **Settings** —— 面板自身的设置，排在第一个：位置、语言，以及已加载的模块及各自版本

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

两项设置各出现两次——游戏自带的操控设置页里各有一行（在 *Ignore Steer Angle Limit* 下方），**F7 → Steering** 面板里也各有一个控件。两边改的是同一个配置项，所以不会各说各话。

| 设置 | 默认 | 含义 |
|---|---|---|
| `Steering/InstantSteering` | `false` | 键盘 / 十字键这类二值输入**立即到位**，不再被逐渐推过去——也就是和摇杆原本的直驱行为一致 |
| `Steering/LimitRelax` | `0.00` | 把游戏随速度收紧的转向限制朝"无限制"方向混合。`0` = 原版，`1` = 等同游戏自带的 *Ignore Steer Angle Limit* |

两项都保存在 `dev.dwecirn.screwtweaks.steering.cfg`，改完会**立即作用到场上已有的车**，不用重新生成。

Instant Steering 原本是造车界面里每个悬挂的开关。现在它是全局设置，所以一次作用于所有车；用旧属性存过的车会在存档里留一条不再使用的属性，游戏会忽略它。

### 其他插件

- **Auto Shift** —— `dev.dwecirn.screwtweaks.autoshift.cfg` 里的 `General/Enabled`，和 **F7 → Auto Shift** 里的勾选框是同一个设置。越过转速阈值后 0.2 秒就换挡（原版 1 秒），冷却和扭矩中断也更短；取消勾选会把游戏自己的数值写回去。
- **Engine Sound** —— 恢复原版选择器在混动车上丢掉的那一半引擎声音，并按瞬时扭矩把两者混合。
- **Power Factor** —— 按引擎类型设置的功率系数，随车的存档一起走。**全自动**：通过 Harmony 注入并持久化，不需要键位。
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

**只覆盖 NWH 轮子后端。** 旧的轮子路径一概不动。

**不含防倾杆与束角。** 悬挂部分打磨的是本来就有的弹簧与阻尼。

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
