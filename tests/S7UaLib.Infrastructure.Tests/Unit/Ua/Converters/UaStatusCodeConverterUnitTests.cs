using S7UaLib.Core.Enums;
using S7UaLib.Infrastructure.Ua.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.Infrastructure.Tests.Unit.Ua.Converters;

[Trait("Category", "Unit")]
public class UaStatusCodeConverterUnitTests
{
    #region Convert Tests

    [Theory]
    [InlineData(Opc.Ua.StatusCodes.Good, StatusCode.Good)]
    [InlineData(Opc.Ua.StatusCodes.GoodLocalOverride, StatusCode.Good)]
    [InlineData(Opc.Ua.StatusCodes.BadNodeIdUnknown, StatusCode.Bad)]
    [InlineData(Opc.Ua.StatusCodes.BadTypeMismatch, StatusCode.Bad)]
    [InlineData(Opc.Ua.StatusCodes.UncertainDataSubNormal, StatusCode.Uncertain)]
    [InlineData(Opc.Ua.StatusCodes.UncertainLastUsableValue, StatusCode.Uncertain)]
    public void Convert_WithVariousUaStatusCodes_ReturnsCorrectStatusCode(uint uaStatusCodeValue, StatusCode expectedStatusCode)
    {
        // Arrange
        var uaStatusCode = new Opc.Ua.StatusCode(uaStatusCodeValue);

        // Act
        var result = UaStatusCodeConverter.Convert(uaStatusCode);

        // Assert
        Assert.Equal(expectedStatusCode, result);
    }

    #endregion Convert Tests
}