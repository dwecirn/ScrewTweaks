# ScrewTweaks

[English](README.md) | [中文](README.zh-CN.md) | **日本語**

**Screw Drivers** 用の BepInEx モッドスイートです。シムレーシングの方向で、このゲームの**ドライビング体験を改善する**ことを目的としています —— リアルなタイヤのスリップ挙動、その上に成り立つドライバー支援、そしていくつかの快適化です。

ひとつの固定的な解決策を配るのではなく、**差し替え可能なスロット**を中心に組まれています。タイヤモデル、ABS / トラクションアルゴリズム、パネルはいずれも公開された拡張ポイントで、同梱されている実装は「たまたま同梱されているもの」にすぎません。他の開発者が、本プロジェクトのコードに触れずに自分のアルゴリズムへ差し替えられます。[独自アルゴリズムの書き方](#独自アルゴリズムの書き方)を参照してください。

スイートは独立したプラグインに分かれているので、個別に導入・有効化・調整できます。設定はすべて共有のゲーム内パネルと、普通の `.cfg` ファイルから行います。

---

## 目次

- [インストール](#インストール)
- [ソースからのビルド](#ソースからのビルド)
- [プラグインとキー](#プラグインとキー)
- [ゲーム内パネル](#ゲーム内パネル)
- [設定リファレンス](#設定リファレンス)
  - [タイヤ物理](#タイヤ物理)
  - [ECU](#ecu)
  - [ステアリング](#ステアリング)
  - [その他のプラグイン](#その他のプラグイン)
- [独自アルゴリズムの書き方](#独自アルゴリズムの書き方)
  - [1. パネルセクション](#1-パネルセクション)
  - [2. タイヤモデル](#2-タイヤモデル)
  - [3. ABS / トラクションアルゴリズム](#3-abs--トラクションアルゴリズム)
- [挙動の約束と落とし穴](#挙動の約束と落とし穴)
- [設計メモ](#設計メモ)
- [サードパーティ](#サードパーティ)

---

## インストール

1. まだなら **BepInEx 5 (x64)** を Screw Drivers フォルダに導入します（`Screw Drivers.exe` の隣に `BepInEx/` と `winhttp.dll` が必要）。
2. すべての `ScrewTweaks.*.dll` を `BepInEx/plugins/` に入れます。
3. ゲームを起動します。`BepInEx/LogOutput.log` にプラグインごとに 1 行ログが出ます。

> `ScrewTweaks.UI.dll` は**必須**です。これはパネルのホストで、各機能プラグインは自分のセクションをここに登録します。それ以外のプラグインはすべて任意で、互いに独立しています。

---

## ソースからのビルド

.NET SDK とゲーム本体のコピーが必要です（ビルド時にゲームフォルダから `Assembly-CSharp.dll` と `0Harmony.dll` を参照します）。ゲームのパスは次の順で解決されます：

1. `-p:GameDir="C:\path\to\Screw Drivers"`
2. 環境変数 `SCREWDRIVERS_DIR`
3. リポジトリ直下の `.env`（`SCREWDRIVERS_DIR=...`、`.env.example` 参照）

```powershell
dotnet build ScrewTweaks.sln --no-restore -m:1
```

ビルドが成功すると、各プラグインは自動的に `BepInEx/plugins` にコピーされます。

> 現在の NuGet 構成では単体の `dotnet restore` がハングすることがあります。一度だけ復元したあとは `--no-restore` を使い続けるのが確実です。`-m:1` は一部環境での並列ビルド問題を避けるためです。

キー割り当ては `keybinds.props` のコンパイル時デフォルトで、各プラグインの `.cfg` から実行時に上書きできます。

---

## プラグインとキー

| アセンブリ | 役割 | キー |
|---|---|---|
| `ScrewTweaks.UI` | 共有 F7 パネルホスト。機能自体は持ちません | **F7** |
| `ScrewTweaks.Physics.Tires` | 差し替え可能なタイヤモデルスロット（ゲーム内で選択） | **F9**（テレメトリ記録） |
| `ScrewTweaks.ECU` | ABS / トラクションコントロール | — |
| `ScrewTweaks.Steering` | Instant Steering + Steering Limit Relax | — |
| `ScrewTweaks.AutoShift` | より速いオートシフト調整 | **N** |
| `ScrewTweaks.EngineSound` | ハイブリッド車のエンジン音（両方のエンジンが聞こえる） | — |
| `ScrewTweaks.PowerFactor` | エンジン種別ごとのパワーファクター。車の保存データに乗る（全自動） | — |

その他：**F8** で静的なタイヤ／ホイールデータを `BepInEx/ScrewTweaks.tire-dump.txt` に出力します。

---

## ゲーム内パネル

**F7** でタブ式ウィンドウが開きます。各機能プラグインがタイトル付きのセクションを登録します：

- **ECU** — ABS とトラクションのチャンネル
- **Tires** — タイヤモデルの選択、調整、ホイールごとのライブテレメトリ

パネルを開くとカーソルが解放されます。セクションはプラグインの読み込み順に並びます。

---

## 設定リファレンス

すべての値は `BepInEx/config/dev.dwecirn.screwtweaks.<name>.cfg` に保存されます。

### タイヤ物理

**F7 → Tires** でモデルを選択します。選択は記憶され、次回起動時も維持されます。

| 設定 | 既定値 | 意味 |
|---|---|---|
| `Model/Selected` | `Native` | `Native` = ゲーム標準の摩擦をそのまま使用 |
| `Pacejka/GripScale` | `1.00` | ピークグリップの倍率。**1.0 = ゲームと完全に同じ** |
| `Pacejka/CombinedSlip` | `1.00` | `0` = 前後を独立、`1` = 完全な摩擦楕円 |
| `Pacejka/RelaxationLength` | `0.30` | タイヤがスリップを築くのに要する距離 [m]（半径 0.30 m のホイール基準）。ホイール半径に比例してスケール。`0` = 無効 |
| `Pacejka/CamberThrust` | `0.015` | キャンバースラスト。キャンバー 1 度あたりの荷重比 |
| `Pacejka/CamberDynamics` | `1.00` | キャンバーがタイヤを柔らかくする度合い（ピークが後ろへ、横ピークが低下）。`0` = スラストのみ |
| `Pacejka/GeometryShapeCoupling` | `0.00` | 接地面（半径 × 幅）がピーク位置を動かす強さ。**既定ではオフ** |

モデルが必要とする残りの入力はすべてゲーム側から、**ホイールごと・物理ステップごと**に読み取ります：`Grip` → `maximumTireGripForce`、`loadGripCurve`、タイヤ自身の `FrictionPresetAsphalt` / `FrictionPresetSand`（路面に応じて選択）、`forceCoefficient`、`camberAngle`、モーター／ブレーキトルク。

**ピークグリップは標準モデルに合わせてある**ため、モデルを切り替えても変わるのは**挙動**であり、車が持つグリップ量ではありません。グリップ量自体を変えたい場合は `GripScale` を使います。

**Tires セクションにはホイールごとのライブテレメトリも表示されます：**

```
PartWheelDirt2 g= 0.30  BCDE=(  7.0, 1.10, 0.83, 1.00)  k= 0.021/ 0.000 a=  -4.1/  -3.9 y=0.51 ...
```

`k` / `a` は *緩和後 / 運動学的* の順に表示されます。`BCDE` は**現在実際に使われている**摩擦プリセットで、別の路面に乗ると変化するため、タイヤごとの曲線が切り替わる様子を直接確認できます。

**F9** で 30 秒間の全ホイールのデータを `BepInEx/ScrewTweaks.tire-telemetry.csv` に記録します（`t, wheel, body, tire, tireGrip, BCDE, kappa, alphaDeg, kappaRaw, alphaRawDeg, Fx, Fy, Fz, vx, omega, fwdMax, sideMax, radius, sigma, peak, camberDeg, camberFx`）。

### ECU

互いに独立した 2 つのチャンネル。それぞれ**登録済みアルゴリズム**か、2 つの予約モードのいずれかを選べます：

- **`Native`** — ゲーム標準のアシストに触れない。*（既定）*
- **`Off`** — 無効化するが、代替は入れない。
- それ以外の名前 — 登録済みアルゴリズムの名前（組み込み：**`Progressive`**）。

| 設定 | 既定値 | 意味 |
|---|---|---|
| `Channels/Abs` | `Native` | ABS アルゴリズム |
| `Channels/Traction` | `Native` | トラクションアルゴリズム |
| `Abs/TargetSlip` | `0.12` | プログレッシブ ABS が維持するスリップ。ゲームのアスファルト曲線のピークは約 0.125 |
| `Abs/Gain` | `6.0` | 目標を超えたスリップに対してブレーキを絞る強さ |
| `Abs/Floor` | `0.10` | 滑っている間に残すブレーキの下限割合 |
| `Traction/TargetSlip` | `0.12` | プログレッシブ・トラクションが維持するスリップ |
| `Traction/Gain` | `3.0` | 目標を超えたスリップに対して駆動トルクを絞る強さ |

アルゴリズムを選ぶと、そのチャンネルで**ゲーム標準の TCS/ABS は自動的に無効化**され、互いに干渉しません。エンジンブレーキはフットブレーキと同じブレーキチャンネルを通るため、惰性走行時の挙動も ABS が担当します。

### ステアリング

- **Instant Steering** — 車の製作画面でサスペンションごとのオン／オフプロパティ。キーボード／十字キーのような二値入力でも、`FrontWheelsSteerer` に徐々に動かされず**即座に**目標角度へ届きます。つまりアナログスティック本来のダイレクトドライブと同じ挙動です。
- **Steering Limit Relax** — ゲーム標準の操作設定ページ、*Ignore Steer Angle Limit* の下にあるスライダー。速度に応じて厳しくなるステアリング制限を「制限なし」側へ混ぜます。`0` = バニラ。

### その他のプラグイン

- **Auto Shift (N)** — より速いオートシフト調整を適用します。
- **Engine Sound** — 標準のセレクタがハイブリッド車で落としてしまう側のエンジン音を復活させ、瞬時トルクで両者をミックスします。
- **Power Factor** — エンジン種別ごとのパワーファクターで、車の保存データと一緒に移動します。**全自動**：Harmony パッチで注入・永続化するため、キーは不要です。

---

## 独自アルゴリズムの書き方

このスイートは拡張されることを前提に組まれています。実際に使える拡張ポイントは 3 つ、すべて公開 API です。

拡張するプラグインの DLL を自分のプロジェクトから参照し、**ローカルにコピーしない**設定にします（BepInEx が既に提供しているため）：

```xml
<Reference Include="ScrewTweaks.ECU">
  <HintPath>$(GameDir)\BepInEx\plugins\ScrewTweaks.ECU.dll</HintPath>
  <Private>false</Private>
</Reference>
```

読み込み順を正しくするため、依存も宣言します：

```csharp
[BepInDependency("dev.dwecirn.screwtweaks.ecu")]
```

### 1. パネルセクション

```csharp
using ScrewTweaks.UI;

private void Start() => Panel.Register("My Section", DrawSection);

private void DrawSection()
{
    GUILayout.Label("hello");
    if (GUILayout.Button("do a thing")) { /* ... */ }
}
```

`Start()` から `Panel.Register(title, draw)` を呼びます。描画コールバックはスクロールビュー内で毎フレーム呼ばれるので `GUILayout` を使ってください。同じタイトルで再登録すると、以前のコールバックが置き換わります。

### 2. タイヤモデル

`ITireModel` を実装して登録します。**すぐに** **Tires** のドロップダウンに現れます。

```csharp
using ScrewTweaks.Physics.Tires;

public sealed class MyTire : ITireModel
{
    public string Name => "MyTire";
    public string Description => "What it does.";

    // 力の算出とホイール回転の積分まで行ったなら true を返す。
    // false を返すと、そのステップはゲーム標準の摩擦に任せる。
    public bool Apply(WheelController wheel, float dt)
    {
        // 速度はあらかじめ埋められています：
        float vx = wheel.forwardFriction.speed;
        float vy = wheel.sideFriction.speed;

        // ... 自分のモデルで fx, fy を計算 ...

        wheel.forwardFriction.force = fx;
        wheel.sideFriction.force = fy;
        wheel.forwardFriction.slip = slipRatio;   // 運動学的スリップ。「落とし穴」参照
        wheel.sideFriction.slip = slipAngle;
        // さらに wheel.wheel.angularVelocity を自分で積分する
        return true;
    }
}

// Start() 内：
TireModels.Register(new MyTire());
```

### 3. ABS / トラクションアルゴリズム

`IBrakeAid` または `IDriveAid` を実装して登録します。**ECU** のドロップダウンに自動で現れ、設定には**名前で**保存されます。

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

        // ctx.Controller は NWH のホイールそのもの：荷重、半径、角速度、モーター／ブレーキトルク、
        // 現在有効な摩擦プリセット、最新のスリップ値。NWH 以外のバックエンドでは null。
        if (ctx.Controller == null) return desiredBrake;

        float peakSlip = 0.125f;                     // ゲームのアスファルト曲線のピーク位置
        return Mathf.Abs(ctx.ForwardSlip) > peakSlip ? 0f : desiredBrake;
    }
}

// IDriveAid の場合：EcuAids.Register(new MyTraction());
EcuAids.Register(new MyAbs());
```

`IBrakeAid` / `IDriveAid` はホイールごと・物理ステップごとに 1 回呼ばれます。**ゲームが自身のトルクを計算した後、ホイールに渡す前**なので、変更後の値を返すだけで十分です。そのチャンネルのゲーム標準アシストは自動的に無効化されます。

---

## 挙動の約束と落とし穴

間違えやすく、しかも症状から原因が分かりにくいものをまとめます。

**タイヤスロットは「力」だけを担っているわけではない。**
`WheelController.FrictionUpdate` は接触点の速度、ホイール RPM、`wheelHit` のスリップ値も公開しており、ゲームの駆動系と TCS がそれらを読み返します。これらはスロットホストがモデルの**前後**で処理するので、モデルは力・スリップ・ホイール回転だけを埋めれば済みます。

**公開するのは*運動学的*スリップであり、内部スリップではない。**
`forwardFriction.slip` / `sideFriction.slip` はゲームのフィードバックチャンネルです。`MechanicalOutputWheel` 自身の ABS/TCS が読み、ECU のアシストも読みます。緩和長を持つモデルで緩和後の値を公開すると、**すべてのアシストが緩和長ひとつ分だけ遅れて反応**し、ホイールがロックしてから ABS が動き出します。ホイールが**運動学的に**何をしているかを公開してください。

**タイヤの識別情報は最初の物理ステップで遅延解決される。**
`WheelController.PartConfigurationWheel` から取得するため、プラグイン読み込み**前**に生成済みの車は、再生成するまで識別情報を持ちません。パネルの **Tires seen** リストで、何が取得できているか確認できます。

**存在しないアルゴリズムは `Native` にフォールバックする。「管理下だが空」にはならない。**
設定に書かれたアルゴリズムのプラグインが既に無い場合、そのチャンネルはゲーム標準のアシストに戻ります。ゲームのアシストを無効化したまま何も引き継がないと、**気付かないうちに ABS が無い車**になってしまいます。

**グリップ量について。**
`GripScale = 1` のとき、ピークはゲームの**クランプ前**の数値と一致します。なお標準モデルはさらに**合力ベクトル**を `loadCoefficient` にクランプするため、その実効的な前後ピークは `~loadCoefficient` となり、こちらは `|D| * loadCoefficient * forceCoefficient`（`forceCoefficient = 1.35` で約 25% 高い）になります。これは標準の円形クランプを本物の摩擦楕円に置き換えた結果であり、意図的なもので、完全一致まであとスイッチひとつです。

**ローカルの参考資料はコミットしません。**
`ScrewTweaks/reference/`（Project Chrono のクローン）と `ScrewTweaks/ScrewDrivers/`（逆コンパイルしたゲーム）は gitignore されています。そのままにしてください。

---

## 設計メモ

タイヤモデルの根拠、実測したゲームデータ、延期されたサスペンション計画は [`docs/tire-model-spec.md`](docs/tire-model-spec.md) にあります。タイヤモデルを変更する予定なら、まず §15–§19 を読んでください——ゲームが実際に何を提供しているかが記録されています。

数値の出どころ：

- **ゲームからそのまま取る：** ピークグリップ量、スリップ曲線の形状（タイヤごと・路面ごと）、スリップの単位、キャンバー入力、トルク入力。
- **本スイートが追加する：** キャンバースラスト、キャンバーによる軟化、緩和長、独自のホイール回転積分、本物の合スリップ摩擦楕円。
- **意図的に触らない：** ゲーム自身のタイヤバランス、およびゲームが既にプレイヤーへ公開している操作系。

最後の項目で既に 2 つの案が不要になりました：外部ブレーキバイアス（ゲームは既にブレーキパーツの `brakeforce` プロパティでホイールごとのブレーキ力を提供しています）と、「オフロードタイヤはアスファルトで不利」という幾何由来のペナルティです。どちらもゲーム側に既存の仕組みがありました。

---

## サードパーティ

- 合スリップの **ADAMS 摩擦楕円**は [Project Chrono](https://github.com/projectchrono/chrono) の
  `ChPac02Tire::CalcFxyMz`（`src/chrono_vehicle/wheeled_vehicle/tire/ChPac02Tire.cpp`）、BSD-3-Clause を基にしています。
  Chrono のコードはビルドも同梱もしておらず、数式のみを移植しています。
- タイヤスロット、パネルレジストリ、アシストスロットは本プロジェクトのオリジナルです。

## ライセンス

BSD-3-Clause。[`LICENSE`](LICENSE) を参照してください。

**自由に使う・改変する・再配布する・商用利用する**（クローズドソースの mod に組み込むことも可）。条件は二つだけ：**著作権表示を保持すること**、そして**許可なく作者の名前を派生物の推奨や宣伝に使わないこと**です。

タイヤモデルは Project Chrono から数式を一つ移植しており、Chrono も同じライセンスなので、ライセンスの混在はありません。
