using Microsoft.Extensions.Logging;
using Opc.Ua;
using S7UaLib.Events;
using S7UaLib.S7.Types;
using S7UaLib.Services;
using System.Collections;
using System.IO.Abstractions;

namespace S7UaLib.IntegrationTests.Services;

[Trait("Category", "Integration")]
public class S7ServiceIntegrationTests
{
    private const string _serverUrl = "opc.tcp://172.168.0.1:4840";

    private readonly ApplicationConfiguration _appConfig;
    private readonly Action<IList, IList> _validateResponse;
    private readonly IFileSystem _fileSystem = new FileSystem();
    private readonly ILoggerFactory? _loggerFactory = null;

    public S7ServiceIntegrationTests()
    {
        _appConfig = new ApplicationConfiguration
        {
            ApplicationName = "Integration Test S7Service",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier(),
                TrustedPeerCertificates = new CertificateTrustList(),
                AutoAcceptUntrustedCertificates = true
            },
            ClientConfiguration = new ClientConfiguration(),
            TransportQuotas = new TransportQuotas { OperationTimeout = 15000 }
        };

        _validateResponse = ClientBase.ValidateResponse;
    }

    #region Helper Methods

    private async Task<S7Service> CreateAndConnectServiceAsync()
    {
        var service = new S7Service(_appConfig, _validateResponse, _loggerFactory)
        {
            AcceptUntrustedCertificates = true
        };

        try
        {
            await service.ConnectAsync(_serverUrl, useSecurity: false);
        }
        catch (ServiceResultException ex)
        {
            Assert.Fail($"Failed to connect to the server at '{_serverUrl}'. Ensure the server is running. Error: {ex.Message}");
        }

        Assert.True(service.IsConnected, "Service-Setup failed, could not connect to server.");
        return service;
    }

    #endregion Helper Methods

    #region Connection Tests

    [Fact]
    public async Task ConnectAndDisconnect_Successfully()
    {
        // Arrange
        var service = new S7Service(_appConfig, _validateResponse, _loggerFactory)
        {
            AcceptUntrustedCertificates = true
        };

        bool connectedFired = false;
        bool disconnectedFired = false;

        var connectedEvent = new ManualResetEventSlim();
        var disconnectedEvent = new ManualResetEventSlim();

        service.Connected += (s, e) => { connectedFired = true; connectedEvent.Set(); };
        service.Disconnected += (s, e) => { disconnectedFired = true; disconnectedEvent.Set(); };

        // Act & Assert: Connect
        try
        {
            await service.ConnectAsync(_serverUrl, useSecurity: false);
            bool connectedInTime = connectedEvent.Wait(TimeSpan.FromSeconds(10));

            Assert.True(connectedInTime, "The 'Connected' event was not fired within the timeout.");
            Assert.True(service.IsConnected, "Service should be connected after ConnectAsync.");
            Assert.True(connectedFired, "Connected event flag should be true.");

            // Act & Assert: Disconnect
            service.Disconnect();
            bool disconnectedInTime = disconnectedEvent.Wait(TimeSpan.FromSeconds(5));

            Assert.True(disconnectedInTime, "The 'Disconnected' event was not fired within the timeout.");
            Assert.False(service.IsConnected, "Service should be disconnected after Disconnect.");
            Assert.True(disconnectedFired, "Disconnected event flag should be true.");
        }
        finally
        {
            if (service.IsConnected)
            {
                service.Disconnect();
            }
        }
    }

    #endregion Connection Tests

    #region Full Workflow Tests

    [Fact]
    public async Task DiscoverStructure_And_ReadAllVariables_PopulatesStoreCorrectly()
    {
        S7Service? service = null;
        try
        {
            // Arrange
            service = await CreateAndConnectServiceAsync();

            // Act
            service.DiscoverStructure();
            UpdateAllVariableTypes(service); // Simulate loading configuration
            service.ReadAllVariables();

            // Assert
            AssertVariable("DataBlocksGlobal.Datablock.TestBool", true);
            AssertVariable("DataBlocksGlobal.Datablock.TestInt", (short)9);
            var realVar = service.GetVariable("DataBlocksGlobal.Datablock.TestReal");
            Assert.NotNull(realVar);
            Assert.Equal(8.2f, (float)realVar.Value!, 5);

            AssertVariable("Inputs.TestInput", false);
            AssertVariable("Outputs.TestOutput", false);
            AssertVariable("Memory.TestVar", false);

            AssertVariable("DataBlocksInstance.FunctionBlock_InstDB.Inputs.Function_InputBool", false);
            AssertVariable("DataBlocksInstance.FunctionBlock_InstDB.Outputs.Function_OutputInt", (short)0);

            var nestedStructMember = service.GetVariable("DataBlocksInstance.FunctionBlock_InstDB.Static.NestedStruct.NestedBool");
            Assert.NotNull(nestedStructMember);
            Assert.Equal(true, nestedStructMember.Value);
        }
        finally
        {
            service?.Disconnect();
        }

        void AssertVariable(string path, object? expected)
        {
            var variable = service.GetVariable(path);
            Assert.NotNull(variable);
            Assert.Equal(StatusCodes.Good, variable.StatusCode);
            Assert.Equal(expected, variable.Value);
        }
    }

    [Fact]
    public async Task ReadAllVariables_FiresVariableChangedEvent_OnValueChange()
    {
        S7Service? service = null;
        const string varPath = "DataBlocksGlobal.Datablock.TestInt";
        object? originalValue = null;

        try
        {
            // Arrange
            service = await CreateAndConnectServiceAsync();
            service.DiscoverStructure();
            UpdateAllVariableTypes(service);
            service.ReadAllVariables();

            var testVar = service.GetVariable(varPath);
            Assert.NotNull(testVar);
            originalValue = testVar.Value!;

            const short newValue = 555;
            Assert.True(await service.WriteVariableAsync(varPath, newValue), "Pre-condition failed: Could not write new value.");

            int eventCount = 0;
            VariableValueChangedEventArgs? eventArgs = null;
            service.VariableValueChanged += (s, e) =>
            {
                if (e.NewVariable.FullPath == varPath)
                {
                    eventCount++;
                    eventArgs = e;
                }
            };

            // Act
            service.ReadAllVariables();

            // Assert
            Assert.Equal(1, eventCount);
            Assert.NotNull(eventArgs);
            Assert.Equal(originalValue, eventArgs.OldVariable.Value);
            Assert.Equal(newValue, eventArgs.NewVariable.Value);
        }
        finally
        {
            if (service?.IsConnected == true && originalValue is not null)
            {
                await service.WriteVariableAsync(varPath, originalValue);
            }
            service?.Disconnect();
        }
    }

    [Fact]
    public async Task WriteVariableAsync_And_ReadBack_Succeeds()
    {
        S7Service? service = null;
        const string varPath = "DataBlocksGlobal.Datablock.TestUInt";
        object? originalValue = null;

        try
        {
            // Arrange
            service = await CreateAndConnectServiceAsync();
            service.DiscoverStructure();
            UpdateAllVariableTypes(service);
            service.ReadAllVariables();

            var testVar = service.GetVariable(varPath);
            Assert.NotNull(testVar);
            originalValue = testVar.Value;

            // Act
            const ushort newValue = 1234;
            bool success = await service.WriteVariableAsync(varPath, newValue);
            Assert.True(success, "WriteVariableAsync should return true on success.");

            service.ReadAllVariables();
            var updatedVar = service.GetVariable(varPath);

            // Assert
            Assert.NotNull(updatedVar);
            Assert.Equal(newValue, updatedVar.Value);
        }
        finally
        {
            if (service?.IsConnected == true && originalValue is not null)
            {
                await service.WriteVariableAsync(varPath, originalValue);
            }
            service?.Disconnect();
        }
    }

    [Fact]
    public async Task WriteVariableAsync_WithWrongType_ReturnsFalse()
    {
        S7Service? service = null;
        const string varPath = "DataBlocksGlobal.Datablock.TestInt";

        try
        {
            // Arrange
            service = await CreateAndConnectServiceAsync();
            service.DiscoverStructure();
            UpdateAllVariableTypes(service);

            // Act
            bool success = await service.WriteVariableAsync(varPath, "this-is-not-an-int");

            // Assert
            Assert.False(success, "Writing a value with an incompatible type should fail and return false.");
        }
        finally
        {
            service?.Disconnect();
        }
    }

    [Fact]
    public async Task SaveAndLoadStructure_Succeeds()
    {
        S7Service? service1 = null;
        S7Service? service2 = null;

        var tempFile = _fileSystem.Path.Combine(_fileSystem.Path.GetTempPath(), _fileSystem.Path.GetRandomFileName());

        try
        {
            // Arrange: Connect, discover, and save
            service1 = await CreateAndConnectServiceAsync();
            service1.DiscoverStructure();
            UpdateAllVariableTypes(service1);
            var test = service1.GetVariable("DataBlocksInstance.FunctionBlock_InstDB.Static.NestedStruct.NestedBool");
            var test2 = service1.GetVariable("DataBlocksInstance.FunctionBlock_InstDB.Static.NestedStruct");
            await service1.SaveStructureAsync(tempFile);
            service1.Disconnect();

            // Act: Create new service and load
            service2 = new S7Service(_appConfig, _validateResponse, _loggerFactory);
            await service2.LoadStructureAsync(tempFile);

            // Assert: Structure is loaded before connection
            var preConnectVar = service2.GetVariable("DataBlocksGlobal.Datablock.TestString");
            Assert.NotNull(preConnectVar);
            Assert.Equal(S7DataType.STRING, preConnectVar.S7Type);
            Assert.Null(preConnectVar.Value); // Value is not read yet

            var preConnectNestedStructVar = service2.GetVariable("DataBlocksInstance.FunctionBlock_InstDB.Static.NestedStruct.NestedBool");
            Assert.NotNull(preConnectNestedStructVar);
            Assert.Equal(S7DataType.BOOL, preConnectNestedStructVar.S7Type);
            Assert.Null(preConnectNestedStructVar.Value);

            // Act: Connect and read values
            await service2.ConnectAsync(_serverUrl, useSecurity: false);
            service2.ReadAllVariables();

            // Assert: Values are now populated
            var postConnectVar = service2.GetVariable("DataBlocksGlobal.Datablock.TestString");
            Assert.NotNull(postConnectVar);
            Assert.Equal("Hallo", postConnectVar.Value);

            var postConnectNestedStructVar = service2.GetVariable("DataBlocksInstance.FunctionBlock_InstDB.Static.NestedStruct.NestedBool");
            Assert.NotNull(postConnectNestedStructVar);
            Assert.Equal(true, postConnectNestedStructVar.Value);
        }
        finally
        {
            service1?.Disconnect();
            service2?.Disconnect();
            if (_fileSystem.File.Exists(tempFile))
            {
                _fileSystem.File.Delete(tempFile);
            }
        }
    }

    #endregion Full Workflow Tests

    #region Helper - Type Update

    // This helper simulates loading a configuration where S7 data types are known,
    // as this information is not available on the OPC UA server itself.
    private static void UpdateAllVariableTypes(S7Service service)
    {
        UpdateType(service, "DataBlocksGlobal.Datablock.TestBool", S7DataType.BOOL);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestByte", S7DataType.BYTE);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestChar", S7DataType.CHAR);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestWChar", S7DataType.WCHAR);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestInt", S7DataType.INT);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestSInt", S7DataType.SINT);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestDInt", S7DataType.DINT);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestLInt", S7DataType.LINT);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestUInt", S7DataType.UINT);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestUSInt", S7DataType.USINT);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestUDInt", S7DataType.UDINT);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestULInt", S7DataType.ULINT);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestReal", S7DataType.REAL);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestLReal", S7DataType.LREAL);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestDWord", S7DataType.DWORD);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestLWord", S7DataType.LWORD);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestString", S7DataType.STRING);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestDate", S7DataType.DATE);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestTime", S7DataType.TIME);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestTimeOfDay", S7DataType.TIME_OF_DAY);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestS5Time", S7DataType.S5TIME);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestDateAndTime", S7DataType.DATE_AND_TIME);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestLTime", S7DataType.LTIME);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestLTimeOfDay", S7DataType.LTIME_OF_DAY);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestDTL", S7DataType.DTL);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestLDT", S7DataType.LDT);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestStruct", S7DataType.STRUCT);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestStruct.TestStructBool", S7DataType.BOOL);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestStruct.TestStructInt", S7DataType.INT);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestStruct.TestDateAndTime", S7DataType.DATE_AND_TIME);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestCharArray", S7DataType.ARRAY_OF_CHAR);
        UpdateType(service, "DataBlocksGlobal.Datablock.TestDateAndTimeArray", S7DataType.ARRAY_OF_DATE_AND_TIME);

        UpdateType(service, "Inputs.TestInput", S7DataType.BOOL);
        UpdateType(service, "Outputs.TestOutput", S7DataType.BOOL);
        UpdateType(service, "Memory.TestVar", S7DataType.BOOL);

        UpdateType(service, "DataBlocksInstance.FunctionBlock_InstDB.Inputs.Function_InputBool", S7DataType.BOOL);
        UpdateType(service, "DataBlocksInstance.FunctionBlock_InstDB.Outputs.Function_OutputInt", S7DataType.INT);
        UpdateType(service, "DataBlocksInstance.FunctionBlock_InstDB.Static.NestedStruct", S7DataType.STRUCT);
        UpdateType(service, "DataBlocksInstance.FunctionBlock_InstDB.Static.NestedStruct.NestedBool", S7DataType.BOOL);
    }

    private static void UpdateType(S7Service service, string path, S7DataType type)
    {
        if (service.GetVariable(path) is not null)
        {
            var success = service.UpdateVariableType(path, type);
            Assert.True(success, $"Failed to update type for {path}");
        }
    }

    #endregion Helper - Type Update
}