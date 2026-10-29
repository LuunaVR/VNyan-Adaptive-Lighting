using System;

namespace VNyanAdaptiveLighting.Lighting
{
    /// <summary>Frame-rate independent exponential smoothing with separate rise/fall time constants.</summary>
    public sealed class LightingSmoother
    {
        public float Value { get; private set; }
        public bool Initialized { get; private set; }

        public void Reset(float value)
        {
            Value = value;
            Initialized = true;
        }

        /// <param name="riseTime">Time constant (seconds) when moving up. 0 = instant.</param>
        /// <param name="fallTime">Time constant (seconds) when moving down. 0 = instant.</param>
        public float Update(float target, float dt, float riseTime, float fallTime)
        {
            if (!Initialized) { Reset(target); return Value; }
            if (dt <= 0f) return Value;

            float tau = target > Value ? riseTime : fallTime;
            float alpha = tau <= 1e-4f ? 1f : 1f - (float)Math.Exp(-dt / tau);
            Value += (target - Value) * alpha;
            return Value;
        }

        /// <summary>Same smoothing for a scalar that has no direction preference (e.g. tint channels).</summary>
        public static float Step(float current, float target, float dt, float tau)
        {
            if (dt <= 0f) return current;
            float alpha = tau <= 1e-4f ? 1f : 1f - (float)Math.Exp(-dt / tau);
            return current + (target - current) * alpha;
        }
    }
}
