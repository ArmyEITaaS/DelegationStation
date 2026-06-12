using CorporateIdentifierSync;
using DelegationStationShared.Enums;
using Microsoft.Graph.Beta.Models;

namespace CorporateIdentifierSync.Tests.CorpIDUtilitiesTests;

public class CorpIDUtilitiesTests
{
    /// <summary>
    /// Verifies that GetCorpIDTypeForOS returns ManufacturerModelSerial for Windows devices.
    /// </summary>
    [Fact]
    public void GetCorpIDTypeForOS_Windows_ReturnsManufacturerModelSerial()
    {
        // Arrange & Act
        ImportedDeviceIdentityType result = CorpIDUtilities.GetCorpIDTypeForOS(DeviceOS.Windows);

        // Assert
        Assert.Equal(ImportedDeviceIdentityType.ManufacturerModelSerial, result);
    }

    /// <summary>
    /// Verifies that GetCorpIDTypeForOS returns ManufacturerModelSerial for devices with an unknown OS.
    /// </summary>
    [Fact]
    public void GetCorpIDTypeForOS_Unknown_ReturnsManufacturerModelSerial()
    {
        // Arrange & Act
        ImportedDeviceIdentityType result = CorpIDUtilities.GetCorpIDTypeForOS(DeviceOS.Unknown);

        // Assert
        Assert.Equal(ImportedDeviceIdentityType.ManufacturerModelSerial, result);
    }

    /// <summary>
    /// Verifies that GetCorpIDTypeForOS returns SerialNumber for macOS devices.
    /// </summary>
    [Fact]
    public void GetCorpIDTypeForOS_MacOS_ReturnsSerialNumber()
    {
        // Arrange & Act
        ImportedDeviceIdentityType result = CorpIDUtilities.GetCorpIDTypeForOS(DeviceOS.MacOS);

        // Assert
        Assert.Equal(ImportedDeviceIdentityType.SerialNumber, result);
    }

    /// <summary>
    /// Verifies that GetCorpIDTypeForOS returns SerialNumber for iOS devices.
    /// </summary>
    [Fact]
    public void GetCorpIDTypeForOS_iOS_ReturnsSerialNumber()
    {
        // Arrange & Act
        ImportedDeviceIdentityType result = CorpIDUtilities.GetCorpIDTypeForOS(DeviceOS.iOS);

        // Assert
        Assert.Equal(ImportedDeviceIdentityType.SerialNumber, result);
    }

    /// <summary>
    /// Verifies that GetCorpIDTypeForOS returns SerialNumber for Android devices.
    /// </summary>
    [Fact]
    public void GetCorpIDTypeForOS_Android_ReturnsSerialNumber()
    {
        // Arrange & Act
        ImportedDeviceIdentityType result = CorpIDUtilities.GetCorpIDTypeForOS(DeviceOS.Android);

        // Assert
        Assert.Equal(ImportedDeviceIdentityType.SerialNumber, result);
    }

    /// <summary>
    /// Verifies that GetCorpIDTypeForOS throws an ArgumentException when a null OS value is provided.
    /// </summary>
    [Fact]
    public void GetCorpIDTypeForOS_Null_ThrowsArgumentException()
    {
        // Arrange & Act
        Action act = () => CorpIDUtilities.GetCorpIDTypeForOS(null);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }

    /// <summary>
    /// Verifies that GetCorpIDTypeForOS throws an ArgumentException for an undefined DeviceOS enum value.
    /// </summary>
    [Fact]
    public void GetCorpIDTypeForOS_UndefinedEnumValue_ThrowsArgumentException()
    {
        // Arrange
        DeviceOS undefinedOs = (DeviceOS)999;

        // Act
        Action act = () => CorpIDUtilities.GetCorpIDTypeForOS(undefinedOs);

        // Assert
        Assert.Throws<ArgumentException>(act);
    }
}
