using System.Collections.Generic;
using Watt.Models;

namespace Watt.UI
{
    /// <summary>Deterministic sample data used by --snapshot and --demo so the panel can be
    /// designed and reviewed on machines without Bluetooth radios.</summary>
    public static class DemoData
    {
        public static IReadOnlyList<PeripheralDevice> Three()
        {
            return new[]
            {
                new PeripheralDevice
                {
                    Id = "demo:keyboard", Name = "MX Keys", Kind = DeviceKind.Keyboard,
                    BatteryPercent = 86, SourceId = "demo"
                },
                new PeripheralDevice
                {
                    Id = "demo:mouse", Name = "MX Master 3S", Kind = DeviceKind.Mouse,
                    BatteryPercent = 64, SourceId = "demo"
                },
                new PeripheralDevice
                {
                    Id = "demo:headphone", Name = "WH-1000XM5", Kind = DeviceKind.Headphone,
                    BatteryPercent = 42, IsCharging = true, SourceId = "demo"
                }
            };
        }

        public static IReadOnlyList<PeripheralDevice> Two()
        {
            var all = Three();
            return new[] { all[1], all[2] };
        }

        public static IReadOnlyList<PeripheralDevice> OneLow()
        {
            return new[]
            {
                new PeripheralDevice
                {
                    Id = "demo:mouse", Name = "MX Anywhere 3", Kind = DeviceKind.Mouse,
                    BatteryPercent = 9, SourceId = "demo"
                }
            };
        }

        public static IReadOnlyList<PeripheralDevice> Empty()
        {
            return new PeripheralDevice[0];
        }
    }
}

