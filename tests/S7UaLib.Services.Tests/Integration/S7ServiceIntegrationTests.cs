using Microsoft.Extensions.Logging;
using Opc.Ua.Configuration;
using S7UaLib.Core.Enums;
using S7UaLib.Core.Events;
using S7UaLib.Core.Ua;
using S7UaLib.Infrastructure.Ua.Client;
using S7UaLib.Services.S7;
using S7UaLib.TestHelpers;
using System.Collections;
using System.IO.Abstractions;
using System.Security.Cryptography.X509Certificates;

namespace S7UaLib.Services.Tests.Integration;

[Trait("Category", "Integration")]
public class S7ServiceIntegrationTests : IDisposable
{
    private const string _serverUrl = "opc.tcp://172.168.0.1:4840";

    private readonly Action<IList, IList> _validateResponse = Opc.Ua.ClientBase.ValidateResponse;
    private readonly FileSystem _fileSystem = new();
    private readonly ILoggerFactory? _loggerFactory = null;
    private const string _appName = "S7UaLib Integration Tests";
    private const string _appUri = "urn:localhost:UA:S7UaLib:IntegrationTests";
    private const string _productUri = "uri:philipp2604:S7UaLib:IntegrationTests";
    private readonly SecurityConfiguration _securityConfig = new(new SecurityConfigurationStores()) { AutoAcceptUntrustedCertificates = true };
    private readonly UserIdentity _userIdentity = new();

    private readonly TempDirectory _tempDir = new();
    private readonly List<S7Service> _servicesToDispose = [];

    #region Helper Methods / Classes

    private class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
            catch (IOException) { }
        }
    }

    private SecurityConfiguration CreateTestSecurityConfig()
    {
        var certStores = new SecurityConfigurationStores
        {
            AppRoot = Path.Combine(_tempDir.Path, "pki"),
            TrustedRoot = Path.Combine(_tempDir.Path, "pki", "trusted"),
            IssuerRoot = Path.Combine(_tempDir.Path, "pki", "issuer"),
            RejectedRoot = Path.Combine(_tempDir.Path, "pki", "rejected"),
            SubjectName = $"CN={_appName}"
        };

        Directory.CreateDirectory(certStores.AppRoot);
        return new SecurityConfiguration(certStores) { AutoAcceptUntrustedCertificates = true };
    }

    private async Task<S7Service> CreateAndConnectServiceAsync()
    {
        var service = new S7Service(_userIdentity, _validateResponse, _loggerFactory);
        _servicesToDispose.Add(service);

        var securityConfig = CreateTestSecurityConfig();
        await service.ConfigureAsync(_appName, _appUri, _productUri, securityConfig);

        try
        {
            await service.ConnectAsync(_serverUrl, useSecurity: true);
        }
        catch (Opc.Ua.ServiceResultException ex)
        {
            Assert.Fail($"Failed to connect to the server at '{_serverUrl}'. Ensure the server is running. Error: {ex.Message}");
        }

        Assert.True(service.IsConnected, "Service-Setup failed, could not connect to server.");
        return service;
    }

    public void Dispose()
    {
        foreach (var service in _servicesToDispose)
        {
            service.Dispose();
        }
        _tempDir.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion Helper Methods / Classes

    #region Configuration Workflow Tests

    [Fact]
    public async Task SaveAndLoadConfiguration_Succeeds()
    {
        // Arrange
        var configFilePath = Path.Combine(_tempDir.Path, "S7Service.Config.xml");
        var securityConfig = CreateTestSecurityConfig();

        // 1. Create and configure a service with specific, non-default values.
        var saveService = new S7Service(_userIdentity, _validateResponse, _loggerFactory);
        _servicesToDispose.Add(saveService);

        await saveService.ConfigureAsync(
            "SaveLoadTestApp",
            "urn:saveload",
            "urn:saveload:prod",
            securityConfig,
            new ClientConfiguration { SessionTimeout = 98765 });

        // 2. Save the configuration to a file.
        saveService.SaveConfiguration(configFilePath);
        Assert.True(_fileSystem.File.Exists(configFilePath), "Configuration file was not created.");

        // 3. Create a new service instance with a *different* initial configuration.
        var loadService = new S7Service(_userIdentity, _validateResponse, _loggerFactory);
        _servicesToDispose.Add(loadService);
        await loadService.ConfigureAsync("InitialApp", "urn:initial", "urn:initial:prod", CreateTestSecurityConfig());

        // Act: Load the previously saved configuration into the new service.
        await loadService.LoadConfigurationAsync(configFilePath);

        // Assert: Verify that the configuration of the loading client was updated.
        // We need to use reflection to inspect the private internal state.
        var internalClient = PrivateFieldHelpers.GetPrivateField(loadService, "_client") as IS7UaClient;
        var appInst = PrivateFieldHelpers.GetPrivateField(internalClient!, "_appInst") as ApplicationInstance;
        Assert.NotNull(appInst);

        var loadedConfig = appInst.ApplicationConfiguration;
        Assert.Equal("SaveLoadTestApp", loadedConfig.ApplicationName);
        Assert.Equal("urn:saveload", loadedConfig.ApplicationUri);
        Assert.Equal(98765, loadedConfig.ClientConfiguration.DefaultSessionTimeout);
    }

    #endregion Configuration Workflow Tests

    #region Connection Tests

    [Fact]
    public async Task ConnectAndDisconnect_Successfully()
    {
        // Arrange
        var service = new S7Service(_userIdentity, _validateResponse, _loggerFactory);
        _securityConfig.SecurityConfigurationStores = new SecurityConfigurationStores(_appName, "certs", "certs", "certs", "certs");
        _securityConfig.AutoAcceptUntrustedCertificates = true;
        await service.ConfigureAsync(_appName, _appUri, _productUri, _securityConfig);

        bool connectedFired = false;
        bool disconnectedFired = false;

        var connectedEvent = new ManualResetEventSlim();
        var disconnectedEvent = new ManualResetEventSlim();

        service.Connected += (s, e) => { connectedFired = true; connectedEvent.Set(); };
        service.Disconnected += (s, e) => { disconnectedFired = true; disconnectedEvent.Set(); };

        // Act & Assert: Connect
        try
        {
            await service.ConnectAsync(_serverUrl, useSecurity: true);
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
            Assert.Equal(StatusCode.Good, variable.StatusCode);
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
            service2 = new S7Service(_userIdentity, _validateResponse, _loggerFactory);
            await service2.ConfigureAsync(_appName, _appUri, _productUri, _securityConfig);
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
            await service2.ConnectAsync(_serverUrl, useSecurity: true);
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

    #region Subscription Workflow Tests

    [Fact]
    public async Task SubscribeToVariableAsync_ReceivesNotification_OnValueChange()
    {
        S7Service? service = null;
        const string varPath = "DataBlocksGlobal.Datablock.AnotherTestInt";
        object? originalValue = null;

        try
        {
            // Arrange
            // 1. Create service, connect, and discover the structure
            service = await CreateAndConnectServiceAsync();
            await service.DiscoverStructureAsync();
            // Necessary to set the S7DataType for the variable, so Write/Read work correctly
            await service.UpdateVariableTypeAsync(varPath, S7DataType.INT);
            await service.ReadAllVariablesAsync();

            var testVar = service.GetVariable(varPath);
            Assert.NotNull(testVar);
            originalValue = testVar.Value;
            Assert.NotNull(originalValue);

            // 2. Set up event handling for the notification
            var notificationReceivedEvent = new ManualResetEventSlim();
            VariableValueChangedEventArgs? receivedArgs = null;

            service.VariableValueChanged += (sender, args) =>
            {
                // We are only interested in notifications for our test item
                if (args.NewVariable.FullPath == varPath)
                {
                    receivedArgs = args;
                    notificationReceivedEvent.Set();
                }
            };

            // 3. Subscribe to the variable
            Assert.True(await service.SubscribeToVariableAsync(varPath), "Subscribing to the variable failed.");

            var subscribedVar = service.GetVariable(varPath);
            Assert.True(subscribedVar?.IsSubscribed, "Variable should be marked as 'IsSubscribed' after subscribing.");

            // Act
            // 4. Change the value on the server to trigger a notification
            const short newValue = 888;
            Assert.NotEqual(newValue, (short)originalValue); // Ensure the value actually changes
            Assert.True(await service.WriteVariableAsync(varPath, newValue), "Writing the new value failed.");

            // 5. Wait for the notification
            bool eventWasSet = notificationReceivedEvent.Wait(TimeSpan.FromSeconds(10));

            // Assert
            Assert.True(eventWasSet, "Did not receive VariableValueChanged event within the timeout.");
            Assert.NotNull(receivedArgs);

            // Validate the event arguments
            Assert.Equal(originalValue, receivedArgs.OldVariable.Value);
            Assert.Equal(newValue, receivedArgs.NewVariable.Value);
            Assert.Equal(varPath, receivedArgs.NewVariable.FullPath);

            // The value in the DataStore should also be updated
            var finalVar = service.GetVariable(varPath);
            Assert.NotNull(finalVar);
            Assert.Equal(newValue, finalVar.Value);

            // 6. Test Unsubscribe
            Assert.True(await service.UnsubscribeFromVariableAsync(varPath), "Unsubscribing from the variable failed.");
            var unsubscribedVar = service.GetVariable(varPath);
            Assert.False(unsubscribedVar?.IsSubscribed, "Variable should no longer be marked as 'IsSubscribed' after unsubscribing.");
        }
        catch (Opc.Ua.ServiceResultException ex)
        {
            Assert.Fail($"Test failed due to a server communication error. Ensure the server is running. Error: {ex.Message}");
        }
        finally
        {
            // Cleanup: Restore the original value
            if (service?.IsConnected == true && originalValue is not null)
            {
                await service.WriteVariableAsync(varPath, originalValue);
            }
            await service!.DisconnectAsync();
            service.Dispose();
        }
    }

    #endregion Subscription Workflow Tests

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