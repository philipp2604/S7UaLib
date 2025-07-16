using S7UaLib.Core.Enums;
using S7UaLib.Core.S7.Structure;
using S7UaLib.Core.Ua;
using S7UaLib.Infrastructure.Events;
using S7UaLib.Infrastructure.Ua.Client;
using S7UaLib.TestHelpers;
using System.Collections;

namespace S7UaLib.Infrastructure.Tests.Integration.Ua.Client;

[Trait("Category", "Integration")]
public class S7UaClientIntegrationTests : IDisposable
{
    private const string _serverUrl = "opc.tcp://172.168.0.1:4840";

    private readonly Action<IList, IList> _validateResponse;
    private readonly UserIdentity _userIdentity;
    private const string _appName = "S7UaLib Integration Tests";
    private const string _appUri = "urn:localhost:UA:S7UaLib:IntegrationTests";
    private const string _productUri = "uri:philipp2604:S7UaLib:IntegrationTests";
    private readonly SecurityConfiguration _securityConfiguration = new(new SecurityConfigurationStores()) { AutoAcceptUntrustedCertificates = true };

    private readonly TempDirectory _tempDir;
    private readonly List<S7UaClient> _clientsToDispose = [];

    public S7UaClientIntegrationTests()
    {
        _validateResponse = Opc.Ua.ClientBase.ValidateResponse;
        _userIdentity = new UserIdentity();
        _tempDir = new TempDirectory();
    }

    #region Configuration Tests

    [Fact]
    public async Task SaveAndLoadConfiguration_Succeeds()
    {
        // Arrange
        var configFilePath = Path.Combine(_tempDir.Path, "S7UaClient.Config.xml");
        var securityConfig = CreateTestSecurityConfig();

        var saveClient = new S7UaClient(null, _validateResponse);
        _clientsToDispose.Add(saveClient);

        await saveClient.ConfigureAsync(
            _appName,
            _appUri,
            _productUri,
            securityConfig,
            new ClientConfiguration { SessionTimeout = 88888 });

        saveClient.SaveConfiguration(configFilePath);
        Assert.True(File.Exists(configFilePath));

        var loadClient = new S7UaClient(null, _validateResponse);
        _clientsToDispose.Add(loadClient);
        await loadClient.ConfigureAsync(_appName, _appUri, _productUri, securityConfig);

        // Act
        await loadClient.LoadConfigurationAsync(configFilePath);

        // Assert
        var appInst = PrivateFieldHelpers.GetPrivateField(loadClient, "_appInst") as Opc.Ua.Configuration.ApplicationInstance;
        Assert.NotNull(appInst);

        var loadedConfig = appInst.ApplicationConfiguration;
        Assert.Equal(_appName, loadedConfig.ApplicationName);
        Assert.Equal(_productUri, loadedConfig.ProductUri);
        Assert.Equal(88888, loadedConfig.ClientConfiguration.DefaultSessionTimeout);
    }

    #endregion Configuration Tests

    #region Connection Tests

    [Fact]
    public async Task ConnectAndDisconnect_Successfully()
    {
        // Arrange
        var client = new S7UaClient(_userIdentity, _validateResponse);
        _clientsToDispose.Add(client);

        var securityConfig = CreateTestSecurityConfig();
        await client.ConfigureAsync(_appName, _appUri, _productUri, securityConfig);

        bool connectedFired = false;
        bool disconnectedFired = false;

        var connectedEvent = new ManualResetEventSlim();
        var disconnectedEvent = new ManualResetEventSlim();

        client.Connected += (s, e) => { connectedFired = true; connectedEvent.Set(); };
        client.Disconnected += (s, e) => { disconnectedFired = true; disconnectedEvent.Set(); };

        // Act & Assert: Connect
        try
        {
            await client.ConnectAsync(_serverUrl, useSecurity: false);
            bool connectedInTime = connectedEvent.Wait(TimeSpan.FromSeconds(10));
            Assert.True(connectedInTime, "The 'Connected' event was not fired within the timeout.");
            Assert.True(client.IsConnected, "Client should be connected after ConnectAsync.");
            Assert.True(connectedFired, "Connected event flag should be true.");

            // Act & Assert: Disconnect
            await client.DisconnectAsync();
            bool disconnectedInTime = disconnectedEvent.Wait(TimeSpan.FromSeconds(5));
            Assert.True(disconnectedInTime, "The 'Disconnected' event was not fired within the timeout.");
            Assert.False(client.IsConnected, "Client should be disconnected after Disconnect.");
            Assert.True(disconnectedFired, "Disconnected event flag should be true.");
        }
        catch (Opc.Ua.ServiceResultException ex)
        {
            Assert.Fail($"Connection to the server at '{_serverUrl}' failed. Ensure the server is running. Error: {ex.Message}");
        }
    }

    #endregion Connection Tests

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

    public void Dispose()
    {
        foreach (var client in _clientsToDispose)
        {
            client.Dispose();
        }
        _tempDir.Dispose();
        GC.SuppressFinalize(this);
    }

    private SecurityConfiguration CreateTestSecurityConfig()
    {
        var certStores = new Core.Ua.SecurityConfigurationStores
        {
            AppRoot = Path.Combine(_tempDir.Path, "pki"),
            TrustedRoot = Path.Combine(_tempDir.Path, "pki", "trusted"),
            IssuerRoot = Path.Combine(_tempDir.Path, "pki", "issuer"),
            RejectedRoot = Path.Combine(_tempDir.Path, "pki", "rejected"),
            SubjectName = $"CN={_appName}"
        };
        Directory.CreateDirectory(certStores.AppRoot);
        return new Core.Ua.SecurityConfiguration(certStores) { AutoAcceptUntrustedCertificates = true };
    }

    private async Task<S7UaClient> CreateAndConnectClientAsync()
    {
        var client = new S7UaClient(_userIdentity, _validateResponse, null);
        await client.ConfigureAsync(_appName, _appUri, _productUri, _securityConfiguration);

        try
        {
            await client.ConnectAsync(_serverUrl, useSecurity: false);
        }
        catch (Opc.Ua.ServiceResultException ex)
        {
            Assert.Fail($"Failed to connect to the server at '{_serverUrl}'. Ensure the server is running. Error: {ex.Message}");
        }

        Assert.True(client.IsConnected, "Client-Setup failed, could not connect to server..");
        return client;
    }

    #endregion Helper Methods / Classes

    #region Structure Discovery and Browsing Tests

    [Fact]
    public async Task GetAllInstanceDataBlocks_ReturnsDataFromRealServer()
    {
        S7UaClient? client = null;
        try
        {
            // Arrange
            client = await CreateAndConnectClientAsync();

            // Act
            var instanceDbs = await client.GetAllInstanceDataBlocksAsync();

            // Assert
            Assert.NotNull(instanceDbs);
            Assert.True(instanceDbs.Count > 0, "It was expected to find at least one instance data block.");
            Assert.Contains(instanceDbs, db => db.DisplayName == "FunctionBlock_InstDB");
        }
        finally
        {
            await client!.DisconnectAsync();
        }
    }

    [Fact]
    public async Task GetInputs_And_DiscoverVariables_ReturnsPopulatedElement()
    {
        S7UaClient? client = null;
        try
        {
            // Arrange
            client = await CreateAndConnectClientAsync();

            // Act
            var inputsShell = await client.GetInputsAsync();

            // Assert
            Assert.NotNull(inputsShell);
            Assert.False(inputsShell.Variables?.Any(), "Variables list should be empty before discovery.");

            // Act
            var populatedInputs = await client.DiscoverVariablesOfElementAsync((S7Inputs)inputsShell);

            // Assert
            Assert.NotNull(populatedInputs);
            Assert.NotNull(populatedInputs.Variables);
            Assert.True(populatedInputs.Variables.Count > 0, "At least one variable was expected in global Inputs.");
            Assert.Contains(populatedInputs.Variables, v => v.DisplayName == "TestInput");
        }
        finally
        {
            await client!.DisconnectAsync();
        }
    }

    [Fact]
    public async Task DiscoverElement_WithRealInstanceDb_ReturnsFullyPopulatedDb()
    {
        S7UaClient? client = null;
        try
        {
            // Arrange
            client = await CreateAndConnectClientAsync();
            var instanceDbs = await client.GetAllInstanceDataBlocksAsync();
            var dbShell = instanceDbs.FirstOrDefault(db => db.DisplayName == "FunctionBlock_InstDB");
            Assert.NotNull(dbShell);
            Assert.Null(dbShell.Outputs);
            Assert.Null(dbShell.Inputs);

            // Act
            var discoveredElement = await client.DiscoverElementAsync(dbShell);

            // Assert
            Assert.NotNull(discoveredElement);
            Assert.IsType<S7DataBlockInstance>(discoveredElement);
            var populatedDb = (S7DataBlockInstance)discoveredElement;
            Assert.NotNull(populatedDb.Outputs);
            Assert.NotNull(populatedDb.Inputs);
            Assert.NotEmpty(populatedDb.Outputs.Variables);
            Assert.NotEmpty(populatedDb.Inputs.Variables);
            Assert.Contains(populatedDb.Inputs.Variables, v => v.DisplayName == "Function_InputBool");
        }
        finally
        {
            await client!.DisconnectAsync();
        }
    }

    #endregion Structure Discovery and Browsing Tests

    #region Reading and Writing Tests

    [Fact]
    public async Task ReadValues_For_IO_And_Memory_ReturnsCorrectValues()
    {
        S7UaClient? client = null;
        try
        {
            // Arrange
            client = await CreateAndConnectClientAsync();
            var inputsShell = await client.GetInputsAsync();
            var outputsShell = await client.GetOutputsAsync();
            var memoryShell = await client.GetMemoryAsync();
            Assert.NotNull(inputsShell);
            Assert.NotNull(outputsShell);
            Assert.NotNull(memoryShell);

            // Act
            var populatedInputs = await client.DiscoverVariablesOfElementAsync((S7Inputs)inputsShell);
            var populatedOutputs = await client.DiscoverVariablesOfElementAsync((S7Outputs)outputsShell);
            var populatedMemory = await client.DiscoverVariablesOfElementAsync((S7Memory)memoryShell);
            var correctlyTypedInputs = populatedInputs.Variables.Cast<S7Variable>().Select(v => v with { S7Type = S7DataType.BOOL }).ToList();
            var correctlyTypedOutputs = populatedOutputs.Variables.Cast<S7Variable>().Select(v => v with { S7Type = S7DataType.BOOL }).ToList();
            var correctlyTypedMemory = populatedMemory.Variables.Cast<S7Variable>().Select(v => v with { S7Type = S7DataType.BOOL }).ToList();
            var inputsToRead = populatedInputs with { Variables = correctlyTypedInputs };
            var outputsToRead = populatedOutputs with { Variables = correctlyTypedOutputs };
            var memoryToRead = populatedMemory with { Variables = correctlyTypedMemory };
            var inputsWithValues = await client.ReadValuesOfElementAsync(inputsToRead, "Inputs");
            var outputsWithValues = await client.ReadValuesOfElementAsync(outputsToRead, "Outputs");
            var memoryWithValues = await client.ReadValuesOfElementAsync(memoryToRead, "Memory");

            // Assert
            var inputVar = inputsWithValues.Variables.First(v => v.DisplayName == "TestInput");
            Assert.Equal(StatusCode.Good, inputVar.StatusCode);
            Assert.False((bool)inputVar.Value!);

            var outputVar = outputsWithValues.Variables.First(v => v.DisplayName == "TestOutput");
            Assert.Equal(StatusCode.Good, outputVar.StatusCode);
            Assert.False((bool)outputVar.Value!);

            var memoryVar = memoryWithValues.Variables.First(v => v.DisplayName == "TestVar");
            Assert.Equal(StatusCode.Good, memoryVar.StatusCode);
            Assert.False((bool)memoryVar.Value!);
        }
        finally
        {
            await client!.DisconnectAsync();
        }
    }

    [Fact]
    public async Task ReadValues_For_GlobalDataBlock_ReturnsCorrectlyConvertedValues()
    {
        S7UaClient? client = null;
        try
        {
            // Arrange
            client = await CreateAndConnectClientAsync();
            var globalDbs = await client.GetAllGlobalDataBlocksAsync();
            var dbShell = globalDbs.FirstOrDefault(db => db.DisplayName == "Datablock");
            Assert.NotNull(dbShell);

            // Act
            var dbWithVars = await client.DiscoverVariablesOfElementAsync(dbShell);
            Assert.NotNull(dbWithVars?.Variables);
            var correctlyTypedVars = dbWithVars.Variables.Cast<S7Variable>().Select(variable => variable with
            {
                S7Type = variable.DisplayName switch
                {
                    "TestBool" => S7DataType.BOOL,
                    "TestByte" => S7DataType.BYTE,
                    "TestChar" => S7DataType.CHAR,
                    "TestWChar" => S7DataType.WCHAR,
                    "TestInt" => S7DataType.INT,
                    "TestSInt" => S7DataType.SINT,
                    "TestDInt" => S7DataType.DINT,
                    "TestLInt" => S7DataType.LINT,
                    "TestUInt" => S7DataType.UINT,
                    "TestUSInt" => S7DataType.USINT,
                    "TestUDInt" => S7DataType.UDINT,
                    "TestULInt" => S7DataType.ULINT,
                    "TestReal" => S7DataType.REAL,
                    "TestLReal" => S7DataType.LREAL,
                    "TestDWord" => S7DataType.DWORD,
                    "TestLWord" => S7DataType.LWORD,
                    "TestString" => S7DataType.STRING,
                    "TestDate" => S7DataType.DATE,
                    "TestTime" => S7DataType.TIME,
                    "TestTimeOfDay" => S7DataType.TIME_OF_DAY,
                    "TestS5Time" => S7DataType.S5TIME,
                    "TestDateAndTime" => S7DataType.DATE_AND_TIME,
                    "TestLTime" => S7DataType.LTIME,
                    "TestLTimeOfDay" => S7DataType.LTIME_OF_DAY,
                    "TestDTL" => S7DataType.DTL,
                    "TestLDT" => S7DataType.LDT,
                    "TestStruct" => S7DataType.STRUCT,
                    "TestCharArray" => S7DataType.ARRAY_OF_CHAR,
                    "TestDateAndTimeArray" => S7DataType.ARRAY_OF_DATE_AND_TIME,
                    _ => S7DataType.UNKNOWN
                }
            }).ToList();
            int structIndex = correctlyTypedVars.FindIndex(v => v.DisplayName == "TestStruct");
            if (structIndex != -1)
            {
                var hollowStruct = correctlyTypedVars[structIndex];
                var typedStructMembers = new List<S7Variable>
                {
                    new() { DisplayName = "TestStructBool", S7Type = S7DataType.BOOL },
                    new() { DisplayName = "TestStructInt", S7Type = S7DataType.INT },
                    new() { DisplayName = "TestDateAndTime", S7Type = S7DataType.DATE_AND_TIME }
                };
                correctlyTypedVars[structIndex] = hollowStruct with { StructMembers = typedStructMembers };
            }
            var dbToRead = dbWithVars with { Variables = correctlyTypedVars };

            // Act
            var dbWithValues = await client.ReadValuesOfElementAsync(dbToRead, "DataBlocksGlobal");

            // Assert
            void AssertVar(string name, object? expected)
            {
                var variable = dbWithValues.Variables.FirstOrDefault(v => v.DisplayName == name);
                Assert.NotNull(variable);
                Assert.Equal(StatusCode.Good, variable.StatusCode);
                Assert.Equal(expected, variable.Value);
            }
            AssertVar("TestBool", true); AssertVar("TestInt", (short)9); Assert.Equal(8.2f, (float)dbWithValues.Variables.First(v => v.DisplayName == "TestReal").Value!, 5);
            AssertVar("TestString", "Hallo"); AssertVar("TestByte", (byte)3); AssertVar("TestChar", 'C'); AssertVar("TestDInt", 12);
            AssertVar("TestDWord", (uint)0x31); AssertVar("TestDate", new DateTime(2025, 3, 10));
            AssertVar("TestDateAndTime", new DateTime(2025, 10, 12, 8, 9, 31, 212)); AssertVar("TestLDT", new DateTime(2008, 10, 25, 8, 12, 34, 567));
            AssertVar("TestLInt", 1500000L); Assert.Equal(12.13123, (double)dbWithValues.Variables.First(v => v.DisplayName == "TestLReal").Value!, 5);
            AssertVar("TestLTime", TimeSpan.FromMilliseconds(200)); AssertVar("TestLTimeOfDay", new TimeSpan(12, 11, 31));
            AssertVar("TestLWord", (ulong)0x22); AssertVar("TestS5Time", TimeSpan.FromSeconds(60)); AssertVar("TestSInt", (sbyte)-103);
            AssertVar("TestTime", TimeSpan.FromSeconds(40)); AssertVar("TestTimeOfDay", new TimeSpan(8, 12, 22));
            AssertVar("TestUDInt", (uint)234134); AssertVar("TestUInt", (ushort)32421); AssertVar("TestULInt", (ulong)891841);
            AssertVar("TestUSInt", (byte)222); AssertVar("TestWChar", 'e');
            var dtlVar = dbWithValues.Variables.First(v => v.DisplayName == "TestDTL"); Assert.Equal(StatusCode.Good, dtlVar.StatusCode);
            var expectedDtl = new DateTime(2008, 12, 16, 20, 30, 20, 250).AddTicks(1110 + 3); Assert.Equal(expectedDtl, dtlVar.Value);
            var structVar = dbWithValues.Variables.First(v => v.DisplayName == "TestStruct"); Assert.NotNull(structVar); Assert.Equal(S7DataType.STRUCT, structVar.S7Type);
            Assert.Equal(StatusCode.Good, structVar.StatusCode); Assert.NotNull(structVar.StructMembers); Assert.Equal(3, structVar.StructMembers.Count);
            var structBool = structVar.StructMembers.First(m => m.DisplayName == "TestStructBool"); Assert.Equal(true, structBool.Value);
            var structInt = structVar.StructMembers.First(m => m.DisplayName == "TestStructInt"); Assert.Equal((short)12341, structInt.Value);
            var structDT = structVar.StructMembers.First(m => m.DisplayName == "TestDateAndTime"); Assert.Equal(new DateTime(1990, 1, 1), structDT.Value);
            var charArrayVar = dbWithValues.Variables.First(v => v.DisplayName == "TestCharArray"); Assert.Equal(StatusCode.Good, charArrayVar.StatusCode);
            Assert.NotNull(charArrayVar.Value); Assert.IsType<List<char>>(charArrayVar.Value); Assert.Equal(['h', 'u', 'H', 'U'], (List<char>)charArrayVar.Value);
            var dtArrayVar = dbWithValues.Variables.First(v => v.DisplayName == "TestDateAndTimeArray"); Assert.Equal(StatusCode.Good, dtArrayVar.StatusCode);
            Assert.NotNull(dtArrayVar.Value); Assert.IsType<List<DateTime>>(dtArrayVar.Value);
            var expectedDateTimes = new List<DateTime> { new(2025, 10, 12, 8, 9, 31, 212), new(2024, 10, 12, 8, 9, 31, 212), new(2023, 10, 12, 8, 9, 31, 212), new(2022, 10, 12, 8, 9, 31, 212) };
            Assert.Equal(expectedDateTimes, (List<DateTime>)dtArrayVar.Value);
        }
        finally
        {
            await client!.DisconnectAsync();
        }
    }

    #region Write Tests

    [Fact]
    public async Task WriteAndReadBack_Variable_Succeeds()
    {
        S7UaClient? client = null;
        S7Variable? testVar = null;
        object? originalValue = null;

        try
        {
            // Arrange
            client = await CreateAndConnectClientAsync();
            var globalDbs = await client.GetAllGlobalDataBlocksAsync();
            var dbShell = globalDbs.FirstOrDefault(db => db.DisplayName == "Datablock");
            Assert.NotNull(dbShell);
            var dbWithVars = await client.DiscoverVariablesOfElementAsync(dbShell);
            testVar = dbWithVars.Variables.FirstOrDefault(v => v.DisplayName == "AnotherTestInt") as S7Variable;
            Assert.NotNull(testVar);
            testVar = testVar with { S7Type = S7DataType.INT };

            // Act & Assert
            var dbWithOriginalValues = await client.ReadValuesOfElementAsync(dbWithVars with { Variables = [testVar] });
            originalValue = dbWithOriginalValues.Variables[0].Value;
            Assert.NotNull(originalValue);

            const short newValue = 999;
            bool writeSuccess = await client.WriteVariableAsync(testVar, newValue);
            Assert.True(writeSuccess, "Writing the new value failed.");

            // Act & Assert
            var dbWithNewValues = await client.ReadValuesOfElementAsync(dbWithVars with { Variables = [testVar] });
            Assert.Equal(newValue, dbWithNewValues.Variables[0].Value);
        }
        finally
        {
            // 4. Restore Original Value
            if (client is not null && testVar is not null && originalValue is not null && client.IsConnected)
            {
                await client.WriteVariableAsync(testVar, originalValue);
            }
            await client!.DisconnectAsync();
        }
    }

    #endregion Write Tests

    #endregion Reading and Writing Tests

    #region Subscription Tests

    [Fact]
    public async Task SubscribeToVariableAsync_ReceivesNotification_OnValueChange()
    {
        S7UaClient? client = null;
        S7Variable? testVar = null;
        object? originalValue = null;

        try
        {
            // Arrange
            client = await CreateAndConnectClientAsync();
            var globalDbs = await client.GetAllGlobalDataBlocksAsync();
            var dbShell = globalDbs.FirstOrDefault(db => db.DisplayName == "Datablock");
            Assert.NotNull(dbShell);
            var dbWithVars = await client.DiscoverVariablesOfElementAsync(dbShell);
            testVar = dbWithVars.Variables.FirstOrDefault(v => v.DisplayName == "AnotherTestDInt") as S7Variable;
            Assert.NotNull(testVar);
            testVar = testVar with { S7Type = S7DataType.SINT, SamplingInterval = 100 };

            var dbWithOriginalValue = await client.ReadValuesOfElementAsync(dbWithVars with { Variables = [testVar] });
            originalValue = dbWithOriginalValue.Variables[0].Value;
            Assert.NotNull(originalValue);

            var notificationReceivedEvent = new ManualResetEventSlim();
            MonitoredItemChangedEventArgs? receivedArgs = null;

            client.MonitoredItemChanged += (sender, args) =>
            {
                if (args.MonitoredItem.StartNodeId.ToString() == testVar.NodeId)
                {
                    receivedArgs = args;
                    notificationReceivedEvent.Set();
                }
            };

            Assert.True(await client.CreateSubscriptionAsync(publishingInterval: 200));
            Assert.True(await client.SubscribeToVariableAsync(testVar));

            // Act
            const int newValue = -22;
            Assert.NotEqual(newValue, originalValue);
            Assert.True(await client.WriteVariableAsync(testVar, newValue), "Error while writing new value to variable.");

            bool eventWasSet = notificationReceivedEvent.Wait(TimeSpan.FromSeconds(10));

            // Assert
            Assert.True(eventWasSet, "No Notification received before timeout.");
            Assert.NotNull(receivedArgs);

            var notificationValue = receivedArgs.Notification.Value;
            Assert.NotNull(notificationValue);
            Assert.Equal(Opc.Ua.StatusCodes.Good, notificationValue.StatusCode);
            Assert.Equal(newValue, notificationValue.Value!);

            Assert.True(await client.UnsubscribeFromVariableAsync(testVar));
        }
        catch (Opc.Ua.ServiceResultException ex)
        {
            Assert.Fail($"Test failed due to a server communication error. Ensure the server is running. Error: {ex.Message}");
        }
        finally
        {
            if (client?.IsConnected == true && testVar?.NodeId is not null && originalValue is not null)
            {
                await client.WriteVariableAsync(testVar.NodeId, originalValue, testVar.S7Type);
            }
            await client!.DisconnectAsync();
            client.Dispose();
        }
    }

    #endregion Subscription Tests
}