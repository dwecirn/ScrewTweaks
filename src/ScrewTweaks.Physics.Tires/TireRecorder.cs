#nullable enable

using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using NWH.WheelController3D;
using UnityEngine;

namespace ScrewTweaks.Physics.Tires
{
    /// <summary>
    /// Records per-wheel tyre state to a CSV so it can be analysed offline instead of read off the
    /// in-game panel while driving. Toggle with the Telemetry key; auto-stops after
    /// <see cref="MaxSeconds"/> and writes BepInEx/ScrewTweaks.tire-telemetry.csv.
    /// </summary>
    internal static class TireRecorder
    {
        private const float MaxSeconds = 30f;

        private static readonly StringBuilder Buffer = new StringBuilder();
        private static bool _recording;
        private static float _startTime;

        internal static ManualLogSource? Log;

        internal static bool Recording => _recording;

        internal static void Toggle()
        {
            if (_recording)
                Stop();
            else
                Start();
        }

        internal static void Start()
        {
            Buffer.Clear();
            Buffer.AppendLine("t,wheel,body,tire,tireGrip,kappa,alphaDeg,kappaRaw,alphaRawDeg,Fx,Fy,Fz,vx,omega,fwdMax,sideMax,radius,sigma,peak,camberDeg,camberFx");
            _startTime = Time.realtimeSinceStartup;
            _recording = true;
            Log?.LogInfo($"Tire telemetry recording for {MaxSeconds:F0}s...");
        }

        internal static void Update()
        {
            if (_recording && Time.realtimeSinceStartup - _startTime > MaxSeconds)
                Stop();
        }

        internal static void Stop()
        {
            if (!_recording) return;
            _recording = false;

            try
            {
                string path = Path.Combine(Paths.BepInExRootPath, "ScrewTweaks.tire-telemetry.csv");
                File.WriteAllText(path, Buffer.ToString());
                Log?.LogInfo($"Tire telemetry written to {path}");
            }
            catch (System.Exception e)
            {
                Log?.LogWarning($"Could not write tire telemetry: {e.Message}");
            }

            Buffer.Clear();
        }

        internal static void Sample(WheelController wc, in WheelSample s)
        {
            if (!_recording) return;

            string body = wc.ActiveRigidbody != null ? wc.ActiveRigidbody.gameObject.name : "-";

            Buffer.Append((Time.realtimeSinceStartup - _startTime).ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.Name).Append(',')
                .Append(body).Append(',')
                .Append(s.HasIdentity ? s.TireType.ToString() : "-").Append(',')
                .Append(s.TireGrip.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.Kappa.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.AlphaDeg.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.KappaRaw.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.AlphaRawDeg.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.Fx.ToString("F0", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.Fy.ToString("F0", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.Fz.ToString("F0", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.Vx.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.Omega.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.FxMax.ToString("F0", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.FyMax.ToString("F0", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.Radius.ToString("F4", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.Sigma.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.PeakSlipScale.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.CamberDeg.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                .Append(s.CamberThrustForce.ToString("F0", CultureInfo.InvariantCulture))
                .AppendLine();
        }
    }
}
