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
    private readonly FileSystem _fileSystem = new();
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
            await service.DisconnectAsync();
            bool disconnectedInTime = disconnectedEvent.Wait(TimeSpan.FromSeconds(5));

            Assert.True(disconnectedInTime, "The 'Disconnected' event was not fired within the timeout.");
            Assert.False(service.IsConnected, "Service should be disconnected after Disconnect.");
            Assert.True(disconnectedFired, "Disconnected event flag should be true.");
        }
        finally
        {
            if (service.IsConnected)
            {
                await service.DisconnectAsync();
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
            await service.DiscoverStructureAsync();
            await UpdateAllVariableTypesAsync(service); // Simulate loading configuration
            await service.ReadAllVariablesAsync();

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

            AssertVariable("DataBlocksInstance.FunctionBlock_InstDB.Static.NestedStruct.NestedBool", true);
            AssertVariable("DataBlocksInstance.FunctionBlock_InstDB.Static.AnotherNestedStruct.ANestedStructInsideANestedStruct.AVeryNestedBool", true);
        }
        finally
        {
            await service!.DisconnectAsync();
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
            await service.DiscoverStructureAsync();
            await UpdateAllVariableTypesAsync(service);
            await service.ReadAllVariablesAsync();

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
            await service.ReadAllVariablesAsync();

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
            await service!.DisconnectAsync();
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
            await service.DiscoverStructureAsync();
            await UpdateAllVariableTypesAsync(service);
            await service.ReadAllVariablesAsync();

            var testVar = service.GetVariable(varPath);
            Assert.NotNull(testVar);
            originalValue = testVar.Value;

            // Act
            const ushort newValue = 1234;
            bool success = await service.WriteVariableAsync(varPath, newValue);
            Assert.True(success, "WriteVariableAsync should return true on success.");

            await service.ReadAllVariablesAsync();
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
            await service!.DisconnectAsync();
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
            await service.DiscoverStructureAsync();
            await UpdateAllVariableTypesAsync(service);

            // Act
            bool success = await service.WriteVariableAsync(varPath, "this-is-not-an-int");

            // Assert
            Assert.False(success, "Writing a value with an incompatible type should fail and return false.");
        }
        finally
        {
            await service!.DisconnectAsync();
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
            await service1.DiscoverStructureAsync();
            await UpdateAllVariableTypesAsync(service1);
            var test = service1.GetVariable("DataBlocksInstance.FunctionBlock_InstDB.Static.NestedStruct.NestedBool");
            var test2 = service1.GetVariable("DataBlocksInstance.FunctionBlock_InstDB.Static.NestedStruct");
            await service1.SaveStructureAsync(tempFile);
            await service1.DisconnectAsync();

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
            await service2.ReadAllVariablesAsync();

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
            await service1!.DisconnectAsync();
            await service2!.DisconnectAsync();
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
    private static async Task UpdateAllVariableTypesAsync(S7Service service)
    {
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestBool", S7DataType.BOOL);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestByte", S7DataType.BYTE);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestChar", S7DataType.CHAR);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestWChar", S7DataType.WCHAR);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestInt", S7DataType.INT);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestSInt", S7DataType.SINT);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestDInt", S7DataType.DINT);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestLInt", S7DataType.LINT);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestUInt", S7DataType.UINT);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestUSInt", S7DataType.USINT);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestUDInt", S7DataType.UDINT);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestULInt", S7DataType.ULINT);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestReal", S7DataType.REAL);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestLReal", S7DataType.LREAL);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestDWord", S7DataType.DWORD);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestLWord", S7DataType.LWORD);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestString", S7DataType.STRING);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestDate", S7DataType.DATE);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestTime", S7DataType.TIME);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestTimeOfDay", S7DataType.TIME_OF_DAY);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestS5Time", S7DataType.S5TIME);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestDateAndTime", S7DataType.DATE_AND_TIME);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestLTime", S7DataType.LTIME);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestLTimeOfDay", S7DataType.LTIME_OF_DAY);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestDTL", S7DataType.DTL);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestLDT", S7DataType.LDT);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestStruct", S7DataType.STRUCT);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestStruct.TestStructBool", S7DataType.BOOL);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestStruct.TestStructInt", S7DataType.INT);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestStruct.TestDateAndTime", S7DataType.DATE_AND_TIME);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestCharArray", S7DataType.ARRAY_OF_CHAR);
        await UpdateTypeAsync(service, "DataBlocksGlobal.Datablock.TestDateAndTimeArray", S7DataType.ARRAY_OF_DATE_AND_TIME);

        await UpdateTypeAsync(service, "Inputs.TestInput", S7DataType.BOOL);
        await UpdateTypeAsync(service, "Outputs.TestOutput", S7DataType.BOOL);
        await UpdateTypeAsync(service, "Memory.TestVar", S7DataType.BOOL);

        await UpdateTypeAsync(service, "DataBlocksInstance.FunctionBlock_InstDB.Inputs.Function_InputBool", S7DataType.BOOL);
        await UpdateTypeAsync(service, "DataBlocksInstance.FunctionBlock_InstDB.Outputs.Function_OutputInt", S7DataType.INT);
        await UpdateTypeAsync(service, "DataBlocksInstance.FunctionBlock_InstDB.Static.NestedStruct", S7DataType.STRUCT);
        await UpdateTypeAsync(service, "DataBlocksInstance.FunctionBlock_InstDB.Static.NestedStruct.NestedBool", S7DataType.BOOL);
        await UpdateTypeAsync(service, "DataBlocksInstance.FunctionBlock_InstDB.Static.AnotherNestedStruct", S7DataType.STRUCT);
        await UpdateTypeAsync(service, "DataBlocksInstance.FunctionBlock_InstDB.Static.AnotherNestedStruct.ANestedStructInsideANestedStruct", S7DataType.STRUCT);
        await UpdateTypeAsync(service, "DataBlocksInstance.FunctionBlock_InstDB.Static.AnotherNestedStruct.ANestedStructInsideANestedStruct.AVeryNestedBool", S7DataType.BOOL);
    }

    private static async Task UpdateTypeAsync(S7Service service, string path, S7DataType type)
    {
        if (service.GetVariable(path) is not null)
        {
            var success = await service.UpdateVariableTypeAsync(path, type);
            Assert.True(success, $"Failed to update type for {path}");
        }
    }

    #endregion Helper - Type Update
}