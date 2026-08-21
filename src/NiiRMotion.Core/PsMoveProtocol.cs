namespace NiiRMotion.Core;

public enum PsMoveTransport
{
    Unknown,
    Usb,
    Bluetooth
}

public sealed record PsMoveDeviceDescriptor(
    string DevicePath,
    ushort VendorId,
    ushort ProductId,
    PsMoveTransport Transport,
    string Model)
{
    public const ushort SonyVendorId = 0x054C;
    public const ushort Zcm1ProductId = 0x03D5;

    public static bool TryCreate(
        string devicePath,
        ushort vendorId,
        ushort productId,
        out PsMoveDeviceDescriptor? descriptor)
    {
        descriptor = null;
        if (string.IsNullOrWhiteSpace(devicePath) || vendorId != SonyVendorId || productId != Zcm1ProductId)
            return false;

        descriptor = new(
            devicePath,
            vendorId,
            productId,
            InferTransport(devicePath),
            "PS Move CECH-ZCM1");
        return true;
    }

    private static PsMoveTransport InferTransport(string path)
    {
        if (path.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase)
            || path.Contains("BTHLEDEVICE", StringComparison.OrdinalIgnoreCase))
            return PsMoveTransport.Bluetooth;

        // Windows HID paths do not always expose their parent bus. An unknown
        // transport is safer than treating every VID/PID match as sensor-ready.
        return path.Contains("USB", StringComparison.OrdinalIgnoreCase)
            ? PsMoveTransport.Usb
            : PsMoveTransport.Unknown;
    }
}
