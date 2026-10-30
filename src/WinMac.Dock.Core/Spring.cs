namespace WinMac.Dock;

/// <summary>Resorte amortiguado integrado con subpasos fijos (estable con dt variable). Valores aproximados al «tacto» de macOS [H].</summary>
public struct Spring
{
    public double Value, Velocity, Target;
    public double Stiffness, Damping;

    public Spring(double value, double target, double stiffness = 380, double dampingRatio = 0.82)
    {
        Value = value; Target = target; Velocity = 0; Stiffness = stiffness;
        Damping = 2 * Math.Sqrt(stiffness) * dampingRatio;
    }

    public bool IsSettled => Math.Abs(Value - Target) < 0.0005 && Math.Abs(Velocity) < 0.005;

    public void Step(double dt)
    {
        dt = Math.Min(dt, 1.0 / 20);
        const double sub = 1.0 / 240;
        int n = Math.Max(1, (int)Math.Ceiling(dt / sub));
        double h = dt / n;
        for (int i = 0; i < n; i++)
        {
            double a = -Stiffness * (Value - Target) - Damping * Velocity;
            Velocity += a * h;
            Value += Velocity * h;
        }
        if (IsSettled) { Value = Target; Velocity = 0; }
    }
}
