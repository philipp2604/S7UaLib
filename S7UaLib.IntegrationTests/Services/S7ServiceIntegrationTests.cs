using Opc.Ua;
using S7UaLib.Services;
using System.Collections;
using S7UaLib.S7.Structure.Contracts;
using S7UaLib.S7.Types;
using S7UaLib.S7.Structure;
using S7UaLib.Client;

namespace S7UaLib.IntegrationTests.Services;

[Trait("Category", "Integration")]
public class S7ServiceIntegrationTests
{
    private const string _serverUrl = "opc.tcp://172.168.0.1:4840";

    private readonly ApplicationConfiguration _appConfig;
    private readonly Action<IList, IList> _validateResponse;

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

    private async Task<(S7Service, S7UaClient)> CreateAndConnectServiceAsync()
    {
        // For integration tests, we need access to the underlying client to perform the connection
        var client = new S7UaClient(_appConfig, _validateResponse)
        {
            AcceptUntrustedCertificates = true
        };
        var service = new S7Service(client, new S7UaLib.DataStore.S7DataStore());

        try
        {
            await client.ConnectAsync(_serverUrl, useSecurity: false);
        }
        catch (ServiceResultException ex)
        {
            Assert.Fail($"Failed to connect to the server at '{_serverUrl}'. Ensure the server is running. Error: {ex.Message}");
        }

        Assert.True(client.IsConnected, "Client-Setup failed, could not connect to server.");
        return (service, client);
    }

    #endregion

    #region Full Workflow Tests

    [Fact]
    public async Task DiscoverStructure_And_ReadAllVariables_PopulatesStoreCorrectly()
    {
        (S7Service? service, S7UaClient? client) = (null, null);
        try
        {
            // Arrange
            (service, client) = await CreateAndConnectServiceAsync();

            // Act: Discover the entire structure
            service.DiscoverStructure();

            // Manually set S7DataTypes for accurate value conversion, as this info is not on the server.
            // This simulates loading a configuration or setting types programmatically.
            UpdateAllVariableTypes(service);

            // Act: Read all variables
            service.ReadAllVariables();

            // Assert: Check if specific, known variables are present and have correct values.
            // This combines discovery and reading into one comprehensive test.

            // Assert Global Datablock variables
            AssertVariable("DataBlocksGlobal.Datablock.TestBool", true);
            AssertVariable("DataBlocksGlobal.Datablock.TestInt", (short)9);
            var realVar = service.GetVariable("DataBlocksGlobal.Datablock.TestReal");
            Assert.NotNull(realVar);
            Assert.Equal(8.2f, (float)realVar.Value!, 5);

            // Assert IO/Memory variables
            AssertVariable("Inputs.TestInput", false);
            AssertVariable("Outputs.TestOutput", false);
            AssertVariable("Memory.TestVar", false);

            // Assert Instance Datablock variables
            AssertVariable("DataBlocksInstance.FunctionBlock_InstDB.Inputs.Function_InputBool", false);
            AssertVariable("DataBlocksInstance.FunctionBlock_InstDB.Outputs.Function_OutputInt", (short)0);

            // Assert a nested struct inside an instance DB
            var nestedStruct = service.GetVariable("DataBlocksInstance.FunctionBlock_InstDB.Static.NestedStruct");
            Assert.NotNull(nestedStruct);
            Assert.Equal(S7DataType.STRUCT, nestedStruct.S7Type);
            Assert.True(nestedStruct.StructMembers.Count > 0);

            var nestedStructMember = service.GetVariable("DataBlocksInstance.FunctionBlock_InstDB.Static.NestedStruct.NestedBool");
            Assert.NotNull(nestedStructMember);
            Assert.Equal(true, nestedStructMember.Value);
        }
        finally
        {
            client?.Disconnect();
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
        (S7Service? service, S7UaClient? client) = (null, null);
        var originalValue = new object();
        IS7Variable? testVar = null;

        try
        {
            // Arrange
            (service, client) = await CreateAndConnectServiceAsync();
            service.DiscoverStructure();
            UpdateAllVariableTypes(service);
            service.ReadAllVariables();

            testVar = service.GetVariable("DataBlocksGlobal.Datablock.TestInt");
            Assert.NotNull(testVar);
            originalValue = testVar.Value!;

            const short newValue = 555;
            await client.WriteVariableAsync((S7Variable)testVar, newValue);

            int eventCount = 0;
            IS7Variable? oldVarState = null;
            IS7Variable? newVarState = null;
            service.VariableValueChanged += (s, e) =>
            {
                if (e.NewVariable.FullPath == testVar.FullPath)
                {
                    eventCount++;
                    oldVarState = e.OldVariable;
                    newVarState = e.NewVariable;
                }
            };

            // Act
            service.ReadAllVariables();

            // Assert
            Assert.Equal(1, eventCount);
            Assert.NotNull(oldVarState);
            Assert.NotNull(newVarState);
            Assert.Equal(originalValue, oldVarState.Value);
            Assert.Equal(newValue, newVarState.Value);
        }
        finally
        {
            // Restore Original Value
            if (client is not null && testVar is not null && client.IsConnected)
            {
                await client.WriteVariableAsync((S7Variable)testVar, originalValue);
            }
            client?.Disconnect();
        }
    }


    [Fact]
    public async Task WriteVariableAsync_And_ReadBack_Succeeds()
    {
        (S7Service? service, S7UaClient? client) = (null, null);
        const string varPath = "DataBlocksGlobal.Datablock.AnotherTestUInt";
        object? originalValue = null;

        try
        {
            // Arrange
            (service, client) = await CreateAndConnectServiceAsync();
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

            // Re-read all variables to update the store
            service.ReadAllVariables();
            var updatedVar = service.GetVariable(varPath);

            // Assert
            Assert.NotNull(updatedVar);
            Assert.Equal(newValue, updatedVar.Value);
        }
        finally
        {
            // Restore
            if (service is not null && client is not null && client.IsConnected && originalValue is not null)
            {
                await service.WriteVariableAsync(varPath, originalValue);
            }
            client?.Disconnect();
        }
    }

    [Fact]
    public async Task WriteVariableAsync_WithWrongType_ReturnsFalse()
    {
        (S7Service? service, S7UaClient? client) = (null, null);
        const string varPath = "DataBlocksGlobal.Datablock.AnotherTestBool"; // Expects short

        try
        {
            // Arrange
            (service, client) = await CreateAndConnectServiceAsync();
            service.DiscoverStructure();
            UpdateAllVariableTypes(service);

            // Act: Try to write a string to an INT variable
            bool success = await service.WriteVariableAsync(varPath, "this-is-not-an-int");

            // Assert
            Assert.False(success, "Writing a value with an incompatible type should fail.");
        }
        finally
        {
            client?.Disconnect();
        }
    }


    #endregion

    #region Helper - Type Update

    // This helper simulates loading a configuration where S7 data types are known.
    // In a real application, this information would come from a file or a configuration database.
    private void UpdateAllVariableTypes(S7Service service)
    {
        // Global Datablock
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
        UpdateType(service, "DataBlocksGlobal.Datablock.AnotherTestInt", S7DataType.INT);
        UpdateType(service, "DataBlocksGlobal.Datablock.AnothertTestUInt", S7DataType.UINT);

        // I/O/M
        UpdateType(service, "Inputs.TestInput", S7DataType.BOOL);
        UpdateType(service, "Outputs.TestOutput", S7DataType.BOOL);
        UpdateType(service, "Memory.TestVar", S7DataType.BOOL);

        // Instance DB
        UpdateType(service, "DataBlocksInstance.FunctionBlock_InstDB.Inputs.Function_InputBool", S7DataType.BOOL);
        UpdateType(service, "DataBlocksInstance.FunctionBlock_InstDB.Outputs.Function_OutputInt", S7DataType.INT);
        UpdateType(service, "DataBlocksInstance.FunctionBlock_InstDB.Static.NestedStruct", S7DataType.STRUCT);
        UpdateType(service, "DataBlocksInstance.FunctionBlock_InstDB.Static.NestedStruct.NestedBool", S7DataType.BOOL);
    }

    private void UpdateType(S7Service service, string path, S7DataType type)
    {
        // This is a placeholder for the future "UpdateVariableType" method.
        // For now, we manually manipulate the store for the test's purpose.
        if (service.GetVariable(path) is S7Variable variable)
        {
            var success = service.UpdateVariableType(path, type);
            Assert.True(success, $"Failed to update type for {path}");
        }
    }

    #endregion
}