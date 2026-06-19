namespace TangledeepAccess.Audio {
    /// <summary>
    /// First-order allpass fractional-delay filter. Realizes a sub-sample delay of
    /// <c>d</c> samples (the part an integer sample shift can't express) while leaving the
    /// magnitude response flat — it only shifts phase, so a tone passes through at full
    /// amplitude, just later.
    ///
    /// Transfer function <c>H(z) = (a + z⁻¹) / (1 + a·z⁻¹)</c> with
    /// <c>a = (1 − d) / (1 + d)</c>; its phase delay at low frequency is <c>d</c> samples.
    /// The pole sits at <c>z = −a</c>, so accuracy and decay are best near <c>d = 1</c> and
    /// worst as <c>d → 0</c> (pole → unit circle, slow ringing). Callers keep <c>d</c> in
    /// <c>[0.5, 1.5)</c> so <c>|a| ≤ 1/3</c> and the filter is well-behaved.
    ///
    /// One instance carries the state for one mono stream; feed samples in time order.
    /// </summary>
    public sealed class AllpassFractionalDelay {
        private readonly double _a;
        private double _xPrev;
        private double _yPrev;

        /// <param name="fractionalDelay">Desired delay in samples; intended range [0.5, 1.5).</param>
        public AllpassFractionalDelay(double fractionalDelay) {
            _a = (1.0 - fractionalDelay) / (1.0 + fractionalDelay);
        }

        /// <summary>Push one input sample and get the delayed output. <c>y[n] = a·x[n] + x[n−1] − a·y[n−1]</c>.</summary>
        public float Process(float x) {
            double y = _a * x + _xPrev - _a * _yPrev;
            _xPrev = x;
            _yPrev = y;
            return (float)y;
        }
    }
}
