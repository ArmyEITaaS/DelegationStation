using CorporateIdentifierSync;
using DelegationStationShared.Enums;
using Microsoft.Graph.Beta.Models;

namespace CorporateIdentifierSync.Tests.CorpIDUtilitiesTests;

public class CorpIDUtilitiesTests
{
    /// <summary>
    /// Verifies that GetCorpIDTypeForOS returns the expected ImportedDeviceIdentityType for a given OS.
    /// </summary>
    [Theory]
    [InlineData(DeviceOS.Windows, ImportedDeviceIdentityType.ManufacturerModelSerial)]
    [InlineData(DeviceOS.Unknown, ImportedDeviceIdentityType.ManufacturerModelSerial)]
    [InlineData(DeviceOS.MacOS,   ImportedDeviceIdentityType.SerialNumber)]
    [InlineData(DeviceOS.iOS,     ImportedDeviceIdentityType.SerialNumber)]
    [InlineData(DeviceOS.Android, ImportedDeviceIdentityType.SerialNumber)]
    public void GetCorpIDTypeForOS_ReturnsExpectedIdentityType(DeviceOS os, ImportedDeviceIdentityType expected)
    {
        // Act
        ImportedDeviceIdentityType result = CorpIDUtilities.GetCorpIDTypeForOS(os);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that GetCorpIDTypeForOS throws an ArgumentException for null or undefined OS values.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData((DeviceOS)999)]
    public void GetCorpIDTypeForOS_InvalidInput_ThrowsArgumentException(DeviceOS? os)
    {
        // Act
        Action act = () => CorpIDUtilities.GetCorpIDTypeForOS(os);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }
}
