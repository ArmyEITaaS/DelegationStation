using DelegationStationShared.Enums;
using Microsoft.Graph.Beta.Models;

namespace CorporateIdentifierSync
{
    internal static class CorpIDUtilities
    {
        public static ImportedDeviceIdentityType GetCorpIDTypeForOS(DeviceOS? os)
        {
            switch (os)
            {
                case DeviceOS.Windows:
                case DeviceOS.Unknown:
                    //treat Unknown as Windows for Corporate Identifier purposes
                    return ImportedDeviceIdentityType.ManufacturerModelSerial;
                case DeviceOS.MacOS:
                case DeviceOS.iOS:
                case DeviceOS.Android:
                    return ImportedDeviceIdentityType.SerialNumber;
                default:
                    // We are in trouble if we get here....
                    throw new ArgumentException($"Unsupported OS type: {os}");
            }
        }

    }
}
