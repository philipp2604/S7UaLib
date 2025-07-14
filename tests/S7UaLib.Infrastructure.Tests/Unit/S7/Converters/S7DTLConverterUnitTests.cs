using Opc.Ua;
using S7UaLib.Infrastructure.S7.Converters;
using System.Buffers.Binary;
using System.Xml;

namespace S7UaLib.Infrastructure.Tests.Unit.S7.Converters;

[Trait("Category", "Unit")]
public class S7DTLConverterUnitTests
{
    private const string _s7DtlTypeId = "nsu=http://www.siemens.com/simatic-s7-opcua;s=TE_DTL";

    private static S7DTLConverter CreateSut()
    {
        return new S7DTLConverter();
    }

    #region Property Tests

    [Fact]
    public void TargetType_ReturnsDateTimeType()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.Equal(typeof(DateTime), sut.TargetType);
    }

    #endregion Property Tests

    #region ConvertFromOpc Tests

    [Fact]
    public void ConvertFromOpc_WhenValueIsNull_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.ConvertFromOpc(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ConvertFromOpc_WhenValueIsNotExtensionObject_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        const string incompatibleValue = "not an ExtensionObject";

        // Act
        var result = sut.ConvertFromOpc(incompatibleValue);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ConvertFromOpc_WhenTypeIdIsIncorrect_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        var eo = new ExtensionObject(new ExpandedNodeId("ns=2;i=123"), new byte[12]);

        // Act
        var result = sut.ConvertFromOpc(eo);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ConvertFromOpc_WhenBodyIsNotByteArray_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        var doc = new XmlDocument();
        var xmlElement = doc.CreateElement("root");
        var eo = new ExtensionObject(new ExpandedNodeId(_s7DtlTypeId), xmlElement);

        // Act
        var result = sut.ConvertFromOpc(eo);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ConvertFromOpc_WhenByteArrayIsWrongLength_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        var eo = new ExtensionObject(new ExpandedNodeId(_s7DtlTypeId), new byte[11]);

        // Act
        var result = sut.ConvertFromOpc(eo);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ConvertFromOpc_WhenDateComponentIsInvalid_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        var dtlBytes = new byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(dtlBytes.AsSpan(0, 2), 2024);
        dtlBytes[2] = 13; // Invalid month
        var eo = new ExtensionObject(new ExpandedNodeId(_s7DtlTypeId), dtlBytes);

        // Act
        var result = sut.ConvertFromOpc(eo);

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [MemberData(nameof(GetDtlToDateTimeTestData))]
    public void ConvertFromOpc_WhenValueIsCorrect_ReturnsCorrectDateTime(byte[] dtlBytes, DateTime expectedDateTime)
    {
        // Arrange
        var sut = CreateSut();
        var eo = new ExtensionObject(new ExpandedNodeId(_s7DtlTypeId), dtlBytes);

        // Act
        var result = sut.ConvertFromOpc(eo);

        // Assert
        var actualDateTime = Assert.IsType<DateTime>(result);
        Assert.Equal(expectedDateTime, actualDateTime);
    }

    #endregion ConvertFromOpc Tests

    #region ConvertToOpc Tests

    [Fact]
    public void ConvertToOpc_WhenValueIsNull_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.ConvertToOpc(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ConvertToOpc_WhenValueIsNotDateTime_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        var incompatibleValue = new object();

        // Act
        var result = sut.ConvertToOpc(incompatibleValue);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ConvertToOpc_WithYearOutsideRange_ReturnsValueAndLogsWarning()
    {
        // Arrange
        var sut = CreateSut();
        var dateTime = new DateTime(1969, 1, 1);

        // Act
        var result = sut.ConvertToOpc(dateTime);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<ExtensionObject>(result);
    }

    [Theory]
    [MemberData(nameof(GetDtlToDateTimeTestData))]
    public void ConvertToOpc_WhenValueIsDateTime_ReturnsCorrectExtensionObject(byte[] expectedDtlBytes, DateTime dateTime)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.ConvertToOpc(dateTime);

        // Assert
        var eo = Assert.IsType<ExtensionObject>(result);
        Assert.Equal(_s7DtlTypeId, eo.TypeId.ToString());
        var actualBytes = Assert.IsType<byte[]>(eo.Body);
        Assert.Equal(expectedDtlBytes, actualBytes);
    }

    #endregion ConvertToOpc Tests

    public static TheoryData<byte[], DateTime> GetDtlToDateTimeTestData()
    {
        // Test case 1: 2024-07-11 10:20:30.1234567, DayOfWeek=Thursday(4) -> S7 Day=5
        var dt1 = new DateTime(2024, 7, 11, 10, 20, 30).AddTicks(1234567);
        var bytes1 = new byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes1.AsSpan(0, 2), 2024);
        bytes1[2] = 7;
        bytes1[3] = 11;
        bytes1[4] = 5; // Thursday
        bytes1[5] = 10;
        bytes1[6] = 20;
        bytes1[7] = 30;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes1.AsSpan(8, 4), 123456700);

        // Test case 2: 1999-12-31 23:59:59.0, DayOfWeek=Friday(5) -> S7 Day=6
        var dt2 = new DateTime(1999, 12, 31, 23, 59, 59);
        var bytes2 = new byte[12];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes2.AsSpan(0, 2), 1999);
        bytes2[2] = 12;
        bytes2[3] = 31;
        bytes2[4] = 6; // Friday
        bytes2[5] = 23;
        bytes2[6] = 59;
        bytes2[7] = 59;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes2.AsSpan(8, 4), 0);

        return new TheoryData<byte[], DateTime>
        {
            { bytes1, dt1 },
            { bytes2, dt2 }
        };
    }
}