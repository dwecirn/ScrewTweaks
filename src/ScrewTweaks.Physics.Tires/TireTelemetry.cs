#nullable enable

using NWH.WheelController3D;
using UnityEngine;

namespace ScrewTweaks.Physics.Tires
{
    internal struct WheelSample
    {
        public string Name;
        public float Kappa;
        public float AlphaDeg;
        public float KappaRaw;
        public float AlphaRawDeg;
        public float Fx;
        public float Fy;
        public float Fz;
        public float FxMax;
        public float FyMax;
        public float Vx;
        public float Omega;
        public float Radius;
        public float Sigma;
        public float PeakSlipScale;
        public float CamberDeg;
        public float CamberThrustForce;
        public bool HasIdentity;
        public PartType TireType;
        public float TireGrip;
        public float BcdeB;
        public float BcdeC;
        public float BcdeD;
        public float BcdeE;
        public float SlipXk;   // slip-vector share used by the combined-slip model
        public float SlipYs;
    }

    /// <summary>
    /// Last computed sample per wheel, for the diagnostic panel. Purely observational.
    /// </summary>
    internal static class TireTelemetry
    {
        private const int Capacity = 12;

        private static readonly WheelController?[] Wheels = new WheelController?[Capacity];
        private static readonly WheelSample[] Samples = new WheelSample[Capacity];
        private static int _count;

        internal static int Count => _count;

        internal static void Report(WheelController wc, in WheelSample sample)
        {
            TireRecorder.Sample(wc, sample);

            for (int i = 0; i < _count; i++)
            {
                if (Wheels[i] == wc)
                {
                    Samples[i] = sample;
                    return;
                }
            }

            if (_count < Capacity)
            {
                Wheels[_count] = wc;
                Samples[_count] = sample;
                _count++;
            }
        }

        internal static bool TryGet(int index, out WheelController? wc, out WheelSample sample)
        {
            wc = null;
            sample = default;
            if (index < 0 || index >= _count) return false;
            wc = Wheels[index];
            sample = Samples[index];
            return true;
        }
    }
}
