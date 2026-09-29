#nullable enable

using System;
using System.IO;
using System.Text;
using BepInEx;
using HarmonyLib;
using NWH.VehiclePhysics2.GroundDetection;
using NWH.WheelController3D;
using SappUnityUtils.ScriptableObjects;
using ScrewTweaks.ECU.Generated;
using UnityEngine;

namespace ScrewTweaks.ECU
{
    /// TEMPORARY diagnostic: dumps the game's tire data on F8.
    /// Press once in the menu (assets) and once with a car spawned (live wheel values).
    internal static class TireDataDump
    {
        internal static void Update()
        {
            if (!Input.GetKeyDown(KeyBinds.Dump)) return;
            try
            {
                Dump();
            }
            catch (Exception e)
            {
                Debug.LogError("[ScrewTweaks] tire dump failed: " + e);
            }
        }

        private static string F(float v) => v.ToString("0.####");
        private static string V(Vector4 v) => $"({F(v.x)},{F(v.y)},{F(v.z)},{F(v.w)})";

        private static string Curve(AnimationCurve? c)
        {
            if (c == null) return "null";
            var sb = new StringBuilder();
            foreach (var k in c.keys) sb.Append('(').Append(F(k.time)).Append(':').Append(F(k.value)).Append(')');
            return sb.ToString();
        }

        private static void Dump()
        {
            var sb = new StringBuilder();
            sb.Append("=== ScrewTweaks tire data dump ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).AppendLine(" ===");

            sb.AppendLine();
            sb.AppendLine("--- Wheel parts ---");
            sb.AppendLine("PartType\tGrip\tRadius\tWidth\tPresetIdx\tAsphaltBCDE\tSandBCDE\tFwdForce\tFwdSlip\tSideForce\tSideSlip");
            foreach (PartType pt in Enum.GetValues(typeof(PartType)))
            {
                try
                {
                    var p = Part.MakePart(pt);
                    if (p == null) continue;
                    if (!p.IsWheel && p.WheelRadius <= 0f && p.FrictionPresetAsphalt == null) continue;

                    var ff = p.FrictionForward?.friction;
                    var sf = p.FrictionSideways?.friction;

                    sb.Append(pt).Append('\t')
                      .Append(F(p.Grip)).Append('\t')
                      .Append(F(p.WheelRadius)).Append('\t')
                      .Append(F(p.WheelWidth)).Append('\t')
                      .Append(p.TireFrictionPresetIndex).Append('\t')
                      .Append(p.FrictionPresetAsphalt != null ? V(p.FrictionPresetAsphalt.BCDE) : "null").Append('\t')
                      .Append(p.FrictionPresetSand != null ? V(p.FrictionPresetSand.BCDE) : "null").Append('\t')
                      .Append(ff != null ? F(ff.forceCoefficient) : "-").Append('\t')
                      .Append(ff != null ? F(ff.slipCoefficient) : "-").Append('\t')
                      .Append(sf != null ? F(sf.forceCoefficient) : "-").Append('\t')
                      .Append(sf != null ? F(sf.slipCoefficient) : "-")
                      .AppendLine();
                }
                catch { }
            }

            sb.AppendLine();
            sb.AppendLine("--- FrictionPreset assets ---");
            sb.AppendLine("Name\tBCDE\tCurve");
            try
            {
                foreach (var fp in ScriptableObjectsProvider.LoadAssetsAll<FrictionPreset>())
                {
                    if (fp == null) continue;
                    sb.Append(fp.name).Append('\t').Append(V(fp.BCDE)).Append('\t').Append(Curve(fp.Curve)).AppendLine();
                }
            }
            catch (Exception e) { sb.AppendLine("LoadAssetsAll<FrictionPreset> failed: " + e.Message); }

            sb.AppendLine();
            sb.AppendLine("--- SurfacePreset assets ---");
            sb.AppendLine("Name\tFrictionPreset\tBCDE");
            try
            {
                foreach (var sp in ScriptableObjectsProvider.LoadAssetsAll<SurfacePreset>())
                {
                    if (sp == null) continue;
                    sb.Append(sp.name).Append('\t')
                      .Append(sp.frictionPreset != null ? sp.frictionPreset.name : "null").Append('\t')
                      .Append(sp.frictionPreset != null ? V(sp.frictionPreset.BCDE) : "null").AppendLine();
                }
            }
            catch (Exception e) { sb.AppendLine("LoadAssetsAll<SurfacePreset> failed: " + e.Message); }

            sb.AppendLine();
            sb.AppendLine("--- Active WheelControllers (spawn a car first for real values) ---");
            sb.AppendLine("GO\tMaxGripForce\tMaxLoad\tLoadGripCurve\tGripFactorByWeight\tFwdForce\tFwdSlip\tSideForce\tSideSlip\tRadius\tMass\tInertia\tActiveBCDE");
            foreach (var wc in UnityEngine.Object.FindObjectsByType<WheelController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                try
                {
                    if (wc == null) continue;

                    AnimationCurve? gripCurve = null;
                    var setter = wc.GetComponentInParent<WheelPropertiesSetter>();
                    if (setter != null)
                        gripCurve = AccessTools.Field(typeof(WheelPropertiesSetter), "wheelGripFactorByWeight")?.GetValue(setter) as AnimationCurve;

                    sb.Append(wc.gameObject.name).Append('\t')
                      .Append(F(wc.maximumTireGripForce)).Append('\t')
                      .Append(F(wc.maximumTireLoad)).Append('\t')
                      .Append(Curve(wc.loadGripCurve)).Append('\t')
                      .Append(Curve(gripCurve)).Append('\t')
                      .Append(F(wc.forwardFriction.forceCoefficient)).Append('\t')
                      .Append(F(wc.forwardFriction.slipCoefficient)).Append('\t')
                      .Append(F(wc.sideFriction.forceCoefficient)).Append('\t')
                      .Append(F(wc.sideFriction.slipCoefficient)).Append('\t')
                      .Append(F(wc.wheel.radius)).Append('\t')
                      .Append(F(wc.wheel.mass)).Append('\t')
                      .Append(F(wc.wheel.inertia)).Append('\t')
                      .Append(wc.activeFrictionPreset != null ? V(wc.activeFrictionPreset.BCDE) : "null")
                      .AppendLine();
                }
                catch { }
            }

            string path = Path.Combine(Paths.BepInExRootPath, "ScrewTweaks.tire-dump.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[ScrewTweaks] tire data dumped to {path}");
        }
    }
}
