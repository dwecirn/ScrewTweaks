# 独自アルゴリズムの書き方

Part of [ScrewTweaks](../README.md).

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

