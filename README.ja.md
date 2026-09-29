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

> `ScrewTweaks.Panel.dll` は**必須**です。これはパネルのホストで、各機能プラグインは自分のセクションをここに登録します。それ以外のプラグインはすべて任意で、互いに独立しています。

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

### バージョン

各プラグインは**独自のバージョン**を持ち、定義は一か所だけ：そのプラグインの `.csproj` にある `<Version>` です。`[BepInPlugin]` のバージョン文字列と DLL のメタデータはそこから生成されるため、他に同期する場所はありません。

**リリースは「バンドル」であり、単一のバージョン番号ではありません：**

- プラグインのバージョンは**それ自体が変わったときだけ**動きます。変わっていないプラグインが同じバージョンに留まるのが正しい挙動です；
- **git タグは日付**（`2026.09.29`）で、識別するのは「このバンドル」であり、どのコンポーネントのバージョンとも誤解されません；
- **バージョンが実際に効くのは `BepInDependency`** です。あるプラグインが別のプラグインの最低バージョンを要求でき、満たさない場合は BepInEx が**明確なエラーで読み込みを拒否**します。実行時に落ちることはありません。

面倒な部分——**前回のタグ以降にどのプラグインが変わったか**——は `tools/release.ps1` が担当します：

```powershell
pwsh tools/release.ps1              # 変更点と各プラグインの現在のバージョン
pwsh tools/release.ps1 -Package     # 同上、さらにビルドして zip にまとめる
```

git 履歴を見ずに全バージョンだけ知りたい場合：

```powershell
dotnet build ScrewTweaks.sln --no-restore -m:1 -t:ListVersions
```

---

## プラグインとキー

| アセンブリ | 役割 | キー |
|---|---|---|
| `ScrewTweaks.Panel` | 共有 F7 パネルホスト。機能自体は持ちません | **F7** |
| `ScrewTweaks.Physics.Tires` | 差し替え可能なタイヤモデルスロット（ゲーム内で選択） | **F9**（テレメトリ記録） |
| `ScrewTweaks.Physics.Suspension` | 差し替え可能なダンパーモデルスロット（ゲーム内で選択） | — |
| `ScrewTweaks.ECU` | ABS / トラクションコントロール | — |
| `ScrewTweaks.Steering` | Instant Steering + Steering Limit Relax | — |
| `ScrewTweaks.AutoShift` | より速いオートシフト（F7 → Auto Shift） | — |
| `ScrewTweaks.EngineSound` | ハイブリッド車のエンジン音（両方のエンジンが聞こえる） | — |
| `ScrewTweaks.PowerFactor` | エンジン種別ごとのパワーファクター。車の保存データに乗る（全自動） | — |

その他：**F8** で静的なタイヤ／ホイールデータを `BepInEx/ScrewTweaks.tire-dump.txt` に出力します。

---

## ゲーム内パネル

**F7** でフローティングウィンドウが開きます。既定では**画面右端・垂直中央**に表示され、タイトルバーで移動できます。タブ列は最も長い見出しに合わせて幅を自動で決め、残りはすべて内容側に回ります。各機能プラグインがタイトル付きのセクションを登録します：

- **ECU** — ABS とトラクションのチャンネル
- **Tires** — タイヤモデルの選択、調整、ホイールごとのライブテレメトリ
- **Suspension** — ダンパーとタイヤ上下モデルの選択、調整、ホイールごとのストロークと力のライブ表示
- **Auto Shift** — より速いシフトタイミングのオン／オフ
- **Settings** — パネル自身の設定。常に最後にあります

パネルを開くとカーソルが解放されます。セクションはプラグインの読み込み順に並びます。

### パネルの設定

**Settings** はパネル自身のタブで、どちらの設定も `dev.dwecirn.screwtweaks.panel.cfg` に保存されます：

| 設定 | 既定 | 意味 |
|---|---|---|
| `Panel/TabSide` | `Left` | タブ列をウィンドウのどちら側に置くか。`Left` か `Right` |
| `Panel/Language` | `English` | パネルの言語。`English`、`ChineseSimplified`、`Japanese` |

サイズ変更用のドット状グリップは、**マウスに追従できる側の角**に描かれます。通常は右下ですが、ウィンドウが画面右端に接している間は左下になります——その位置では右へ広げられないためです。

言語はパネル自身だけでなく**すべてのセクション**に効きます。訳を用意していないプラグインのセクションは英語のままになるので、切り替えても中途半端な混在にはなりません。アルゴリズム名とモデル名は**翻訳しません**。設定ファイルに保存される文字列そのものであり、`EcuAids.Register` / `TireModels.Register` / `DamperModels.Register` に渡す名前でもあるからです。

README と `.cfg` 内のコメントは言語に関係なく英語のままです。

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

### サスペンション物理

**F7 → Suspension** でダンパーモデルを選べます。選択は記憶されます。`Native` はゲーム標準のダンパーに一切触れず、これが既定値です——つまりこのプラグインを入れてモデルを選ばない限り、挙動は変わりません。

スプリングは**意図的に触っていません**。パーツデータを計算し直したところ、既に妥当でした（`docs/suspension-model-spec.md`）。ゲームが表現できないのはダンパーのほうで、`係数 × |速度|`、**圧縮と伸張で同じ係数**、そしてブローオフがありません。

| 設定 | 既定 | 意味 |
|---|---|---|
| `Model/Selected` | `Native` | `Native` = ゲーム標準のダンパーをそのまま使う |
| `Damper/ReboundRatio` | `2.00` | 伸張係数を圧縮係数の何倍にするか。ゲームは両方 `1.00` |
| `Damper/LowSpeedGain` | `1.60` | ニー速度以下の減衰倍率 |
| `Damper/KneeVelocity` | `0.10` | シムスタックが開くサスペンション速度 [m/s] |
| `Damper/BlowOffRatio` | `0.25` | ニーより上の傾き（下の傾きに対する比率） |
| `Damper/ReboundFloor` | `0.00` | ダンパーがボディを**下へ引ける**量。そのホイールの静的荷重に対する比率 |

`LowSpeedGain 1.00` + `BlowOffRatio 1.00` + `ReboundRatio 1.00` で**ゲームと完全に同じ**になります。A/B 比較用のスイッチです。すべてゲームが算出した係数の倍数なので、質量・ホイール数・`damperforce` のスケーリングはゲーム本来のものが保たれます。

**`Suspension` セクションにはホイールごとのライブ状態も出ます：**

```
  wheel              comp     C Ns/m   dir  v m/s    damper N    game N   spring N
  PartWheelDirt2      52%      1248  bump   -0.31        412       387      2310
```

`damper N` がモデルの出した値、`game N` が同じ速度でゲーム標準のダンパーが出す値です。差が走行中に見えます。

**`ReboundFloor` には説明が要ります。** ゲームはサスペンションの合力を 0 でクランプするため、伸張力がスプリングの力を超えた分は切り捨てられ、ダンパーがボディを下へ引くことは決してありません。これが車が地面に吸い込まれるのを防いでいますが、同時に**サスペンションがほぼ伸び切ったところで伸張減衰が効かなくなる**ということでもあり、波峰を越えるときにまさに必要な場面です。このエンジンではホイールに上下自由度がないため、ホイールの慣性を代弁できるのはダンパーだけです。`0` がゲームのクランプそのもの。既定で切ってあるのは、これがダンパーの法則だけでなくゲームの積分器を変えるからです。

#### ホイールの上下自由度

もう一つの缺口はもっと大きい。ゲームのホイールには**上下の自由度がまったくありません**。`SuspensionUpdate` はレイキャストから地面を読み、タイヤがちょうど接地するようにサスペンション長を代入します。その結果タイヤは剛体になり、ホイール自身の質量も存在しません。縁石・目地・着地が純粋な衝撃になるのはこのためで、何かを吸収できるのがダンパーだけなのも同じ理由です。

`TireVertical/Model = Linear` はホイールに上下の自由度を与えます。タイヤは地面に対してばねとダンパーになり、ホイールはその上のサスペンションに吊られます。

**それ以外に設定項目はありません。タイヤの数値は、すでに付けているパーツから導出されます：**

- **非簧荷（ばね下）質量** — ホイールパーツ自身の質量。ゲームはこれまで回転慣性にしか使っていませんでした。
- **タイヤ剛性** — サスペンションのホイールレート（`maxForce / maxLength`）の 6 倍。実車はホイールレートの 5〜10 倍なので、重くてストロークの長い車は柔らかい大きなタイヤ、ゴーカートは硬い小さなタイヤになり、**車種固有の定数はどこにもありません**。
- **タイヤ減衰** — 臨界減衰の 0.2。実測されるタイヤ上下の減衰比（0.1〜0.3）の中ほどです。
- **サブステップ数** — ホップの速さから計算するので、不安定にはできません。

既定の車では 20 kg のホイールで約 200 kN/m、静的たわみ 12 mm、ホップ 17 Hz となり、どちらも実車のコーナーに近い値です。パネルには各ホイールが実際にどうなったかが出ます。

| 設定 | 既定 | 意味 |
|---|---|---|
| `TireVertical/Model` | `Native` | `Native` = ゲーム標準の剛体ホイール。`Linear` = ばね＋ダンパーのタイヤ |

オンにするとホイールが路面に追従し、離れることもできます。鋭い荷重はシャシーではなくタイヤが吸収します。根拠は `docs/suspension-model-spec.md` §5.5 にあります。**ホイールホップを抑えるのはダンパーではなくタイヤの減衰**だという点もそこで説明しています——ホップがまだ目立つと感じたら、まずそこを疑ってください。

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

- **Auto Shift** — `dev.dwecirn.screwtweaks.autoshift.cfg` の `General/Enabled`。**F7 → Auto Shift** のチェックボックスと同じ設定です。RPM しきい値を越えてから 0.2 秒でシフトし（バニラは 1 秒）、クールダウンとトルクカットも短くなります。チェックを外すとゲーム本来の値に戻ります。
- **Engine Sound** — 標準のセレクタがハイブリッド車で落としてしまう側のエンジン音を復活させ、瞬時トルクで両者をミックスします。
- **Power Factor** — エンジン種別ごとのパワーファクターで、車の保存データと一緒に移動します。**全自動**：Harmony パッチで注入・永続化するため、キーは不要です。

---

## 独自アルゴリズムの書き方

このスイートは拡張されることを前提に組まれています。実際に使える拡張ポイントは 4 つ、すべて公開 API です。

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
using ScrewTweaks.Panel;

private void Start() => PanelHost.Register("My Section", DrawSection);

private void DrawSection()
{
    GUILayout.Label("hello");
    if (GUILayout.Button("do a thing")) { /* ... */ }
}
```

`Start()` から `PanelHost.Register(title, draw)` を呼びます。描画コールバックはスクロールビュー内で毎フレーム呼ばれるので `GUILayout` を使ってください。同じタイトルで再登録すると、以前のコールバックが置き換わります。

セクションのローカライズは、スイート本体と同じ表を使います。文字列は**英語の原文をキー**にするので、訳が無ければ読みやすい英語に戻り、1 文字列ずつ訳していけます：

```csharp
using ScrewTweaks.Panel;

// Start() の中で、PanelHost.Register の隣に：
Loc.Add(PanelLanguage.Japanese, ("hello", "こんにちは"));
Loc.Add(PanelLanguage.ChineseSimplified, ("hello", "你好"));

// タブのタイトル自体にも 1 つ足せば、タブもローカライズされます
Loc.Add(PanelLanguage.Japanese, ("My Section", "マイセクション"));
```

描画コールバック内で `Loc.T("hello")` が訳を返し、`Loc.Tf` はフォーマット文字列を埋めます。自分の文字列以外には触れません。

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

### 3. ダンパーまたはタイヤのモデル

ここには同じ接縫の上に 2 つのスロットがあります。`IDamperModel` がダンパーの力の法則、`ITireVerticalModel` がホイールに上下自由度を与えたときのタイヤの上下力です。

`IDamperModel` を実装して登録すると、**Suspension** のドロップダウンにすぐ現れます。

```csharp
using ScrewTweaks.Physics.Suspension;
using UnityEngine;

public sealed class MyDamper : IDamperModel
{
    public string Name => "MyDamper";
    public string Description => "例：力が速度の平方根に比例する。";

    // 抵抗力の大きさ [N] を返します。負にしてはいけません。
    // 向きはあなたの仕事ではありません：宿主が符号（圧縮は上、伸張は下）と接触法線を扱います。
    public float Evaluate(in DamperState s)
    {
        // s.Compressing - 圧縮中は true
        // s.Velocity    - |m/s|、常に非負
        // s.GameCoefficient - このホイールについてゲームが算出した係数 C [N*s/m]。車重・ホイール数・
        //                     パーツの damperforce から作られます。これを基準にすれば、モデルは
        //                     ゲームの他の部分と同じスケーリングを保てます。
        // s.Travel、s.CompressionPercent、s.SpringForce、s.Wheel - 必要ならどうぞ。
        float c = s.GameCoefficient * (s.Compressing ? 1f : 2f);   // 分離は自分で
        return c * Mathf.Sqrt(s.Velocity);
    }
}

// Start() の中で：
DamperModels.Register(new MyDamper());
```

接地しているホイールごとに物理ステップ 1 回、ゲームのヒット判定とジオメトリの**後**、力が実際に加わる**前**に呼ばれます。したがってタイヤ荷重・シャシーへの力・サスペンションの音はすべてあなたの数値を見ます。係数が `0` の場所（戦車の履帯ホイール）では、それを基準にしたモデルは何もしません。

`ITireVerticalModel` はもう半分です。`TireVertical/Model` が `Native` でないときだけ呼ばれ、しかも呼ばれる頻度は物理ステップごとではなく**サブステップ**ごとです。

```csharp
using ScrewTweaks.Physics.Suspension;
using UnityEngine;

public sealed class MyTyre : ITireVerticalModel
{
    public string Name => "MyTyre";
    public string Description => "例：踏み込むほど硬くなるタイヤ。";

    // 地面がホイールを押し上げる力 [N] を返します。負にしてはいけません。
    public float Evaluate(in TireVerticalState s)
    {
        // s.Deflection         - タイヤが地面に沈んでいる量 [m]。浮いているときは負
        // s.DeflectionRate     - ホイールと地面の相対速度 [m/s]
        // s.UnsprungMass       - ホイール自身の質量 [kg]
        // s.ReferenceStiffness - 宿主がこのホイール用に算出した線形剛性。基準として使いやすい
        float d = Mathf.Max(s.Deflection, 0f);
        return s.ReferenceStiffness * d * (1f + d * 20f);   // 漸進的
    }
}

// Start() の中で：
TireVerticalModels.Register(new MyTyre());
```

### 4. ABS / トラクションアルゴリズム

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

**サスペンションが底付きしている間、ダンパーは再評価されません。**
`WheelController.SuspensionUpdate` は `damper.force` を `else if (hasHit)` 分岐でしか計算しないため、バンプストップに乗っている間は前ステップの値が使い回され、それが合力に混ざります。サスペンションのモジュールは底付き中も評価します。`Native` 以外のモデルを選んでいるときに、ダンパーの法則以外でゲームの挙動が変わる唯一の箇所です。

**ローカルの参考資料はコミットしません。**
`ScrewTweaks/reference/`（Project Chrono のクローン）と `ScrewTweaks/ScrewDrivers/`（逆コンパイルしたゲーム）は gitignore されています。そのままにしてください。

---

## 設計メモ

タイヤモデルの根拠と実測したゲームデータは [`docs/tire-model-spec.md`](docs/tire-model-spec.md) に、サスペンションの実測データ・ゲームのスケーリングの数値・ダンパーの割り込み設計は [`docs/suspension-model-spec.md`](docs/suspension-model-spec.md) にあります。どちらのモジュールを変えるにしても、まずその資料を読んでください——ゲームが実際に何を提供しているかが記録されています。

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
