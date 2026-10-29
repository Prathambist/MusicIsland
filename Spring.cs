namespace MusicIsland
{
    /// <summary>
    /// Damped harmonic spring, parameterised the same way as SwiftUI's
    /// spring(response:dampingFraction:). Retargeting keeps the current
    /// velocity, so interrupted animations stay continuous like iOS.
    /// </summary>
    public sealed class Spring
    {
        double stiffness;
        double damping;

        public double Value { get; set; }
        public double Velocity { get; set; }
        public double Target { get; set; }

        public Spring(double value, double response = 0.5, double dampingFraction = 0.8)
        {
            Value = value;
            Target = value;
            Configure(response, dampingFraction);
        }

        public void Configure(double response, double dampingFraction)
        {
            double omega = 2 * Math.PI / response;
            stiffness = omega * omega;
            damping = 2 * dampingFraction * omega;
        }

        public bool IsSettled =>
            Math.Abs(Value - Target) < 0.01 && Math.Abs(Velocity) < 0.01;

        public void Step(double dt)
        {
            if (IsSettled)
            {
                Value = Target;
                Velocity = 0;
                return;
            }

            // Fixed sub-steps keep the integration stable on slow frames
            const double maxStep = 1.0 / 240.0;

            while (dt > 0)
            {
                double h = Math.Min(dt, maxStep);
                double force = -stiffness * (Value - Target) - damping * Velocity;
                Velocity += force * h;
                Value += Velocity * h;
                dt -= h;
            }
        }

        public void Snap(double value)
        {
            Value = value;
            Target = value;
            Velocity = 0;
        }
    }
}
