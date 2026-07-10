namespace AndromedaFleetCommand.Core.Model;

public readonly record struct Vector2D(double X, double Y)
{
    public static readonly Vector2D Zero = new(0, 0);

    public double LengthSquared => X * X + Y * Y;
    public double Length => Math.Sqrt(LengthSquared);
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);

    public Vector2D Normalized => Length < 1e-9 ? Zero : this / Length;

    public Vector2D Limit(double maximum) =>
        Length <= maximum || Length < 1e-9 ? this : this * (maximum / Length);

    public double DistanceTo(Vector2D other) => (this - other).Length;
    public double Angle => Math.Atan2(Y, X);

    public static Vector2D FromAngle(double radians) => new(Math.Cos(radians), Math.Sin(radians));
    public static Vector2D operator +(Vector2D left, Vector2D right) => new(left.X + right.X, left.Y + right.Y);
    public static Vector2D operator -(Vector2D left, Vector2D right) => new(left.X - right.X, left.Y - right.Y);
    public static Vector2D operator *(Vector2D value, double scalar) => new(value.X * scalar, value.Y * scalar);
    public static Vector2D operator /(Vector2D value, double scalar) => new(value.X / scalar, value.Y / scalar);
}
