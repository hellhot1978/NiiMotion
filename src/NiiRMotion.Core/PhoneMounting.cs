using System.Numerics;

namespace NiiRMotion.Core;

public static class PhoneMounting
{
    public const string Id = "landscape_screen_inward_top_left";
    public const string TurkishDescription = "Ekran göğse dönük · yatay · üst kenar sola";

    // Device +X points body-up and device +Y points body-left.
    // NiiMotion body frame is right, up, screen-normal.
    public static Vector3 ToBodyFrame(Vector3 deviceVector) =>
        new(-deviceVector.Y, deviceVector.X, deviceVector.Z);

    public static PhoneBodyMotion ToBodyFrame(PhoneImuSample sample) => new(
        ToBodyFrame(sample.AccelerationMps2),
        ToBodyFrame(sample.AngularVelocityRadps));
}

public readonly record struct PhoneBodyMotion(Vector3 AccelerationMps2, Vector3 AngularVelocityRadps)
{
    public double VerticalTurnRadps => AngularVelocityRadps.Y;
}
