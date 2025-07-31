using S7UaLib.Core.Enums;
using S7UaLib.Core.S7.Converters;
using S7UaLib.Core.S7.Structure;
using S7UaLib.Core.Ua;
using S7UaLib.Core.Ua.Configuration;
using S7UaLib.Infrastructure.Events;
using S7UaLib.Infrastructure.Ua.Client;
using S7UaLib.TestHelpers;
using System.Collections;

namespace S7UaLib.Infrastructure.Tests.Integration.Ua.Client;

/// <summary>
/// Test record representing the PLC UDT ""DT_\"typeMyCustomUDT\""" with members: OneBool, OneInt, OneString
/// </summary>
/// <param name="OneBool">Boolean member</param>
/// <param name="OneInt">S7-Integer member</param>
/// <param name="OneString">String member</param>
public record MyCustomUdt(bool OneBool, short OneInt, string OneString);

/// <summary>
/// Custom converter for MyCustomUdt that converts between PLC UDT structure members and the C# record
/// </summary>
public class MyCustomUdtConverter : UdtConverterBase<MyCustomUdt>
{
    public MyCustomUdtConverter() : base("DT_\"typeMyCustomUDT\"")
    {
    }

    public override MyCustomUdt ConvertFromUdtMembers(IReadOnlyList<IS7Variable> structMembers)
    {
        var oneBool = GetMemberValue<bool>(FindMember(structMembers, "OneBool"));
        var oneInt = GetMemberValue<short>(FindMember(structMembers, "OneInt"));
        var oneString = GetMemberValue<string>(FindMember(structMembers, "OneString"), "");

        return new MyCustomUdt(oneBool, oneInt, oneString);
    }

    public override IReadOnlyList<IS7Variable> ConvertToUdtMembers(MyCustomUdt udtInstance, IReadOnlyList<IS7Variable> structMemberTemplate)
    {
        var updatedMembers = new List<IS7Variable>();
        foreach (var member in structMemberTemplate)
        {
            var updatedValue = member.DisplayName switch
            {
                "OneBool" => udtInstance.OneBool,
                "OneInt" => udtInstance.OneInt,
                "OneString" => udtInstance.OneString,
                _ => member.Value
            };

            if (member is S7Variable s7Member)
            {
                updatedMembers.Add(s7Member with { Value = updatedValue });
            }
        }
        return [.. updatedMembers.Cast<IS7Variable>()];
    }
}

[Trait("Category", "Integration")]
public class S7UaClientIntegrationTests : IDisposable
{
    private const string _serverUrl = "opc.tcp://172.168.0.1:4840";

    private readonly Action<IList, IList> _validateResponse;
    private readonly UserIdentity _userIdentity;
    private const int _maxSessions = 5;
    private const string _appName = "S7UaLib Integration Tests";
    private const string _appUri = "urn:localhost:UA:S7UaLib:IntegrationTests";
    private const string _productUri = "uri:philipp2604:S7UaLib:IntegrationTests";

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

        var saveClient = new S7UaClient(_userIdentity, _maxSessions);
        _clientsToDispose.Add(saveClient);

        var appConfig = CreateTestAppConfig();
        appConfig.ClientConfiguration.SessionTimeout = 88888;

        await saveClient.ConfigureAsync(appConfig);

        saveClient.SaveConfiguration(configFilePath);
        Assert.True(File.Exists(configFilePath));

        var loadClient = new S7UaClient(_userIdentity, _maxSessions);
        _clientsToDispose.Add(loadClient);
        await loadClient.ConfigureAsync(appConfig);

        // Act
        await loadClient.LoadConfigurationAsync(configFilePath);

        // Assert
        var mainClient = PrivateFieldHelpers.GetPrivateField(loadClient, "_mainClient") as S7UaMainClient;
        Assert.NotNull(mainClient);
        var appInst = PrivateFieldHelpers.GetPrivateField(mainClient, "_appInst") as Opc.Ua.Configuration.ApplicationInstance;
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
        var client = new S7UaClient(_userIdentity, _maxSessions);
        _clientsToDispose.Add(client);

        var appConfig = CreateTestAppConfig();
        await client.ConfigureAsync(appConfig);

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

    private ApplicationConfiguration CreateTestAppConfig()
    {
        return new ApplicationConfiguration
        {
            ApplicationName = _appName,
            ApplicationUri = _appUri,
            ProductUri = _productUri,
            SecurityConfiguration = CreateTestSecurityConfig(),
            ClientConfiguration = new ClientConfiguration { SessionTimeout = 60000 },
            TransportQuotas = new TransportQuotas { OperationTimeout = 60000 },
        };
    }

    private SecurityConfiguration CreateTestSecurityConfig()
    {
        var certStores = new Core.Ua.Configuration.SecurityConfigurationStores
        {
            AppRoot = Path.Combine(_tempDir.Path, "pki", "app"),
            TrustedRoot = Path.Combine(_tempDir.Path, "pki", "trusted"),
            IssuerRoot = Path.Combine(_tempDir.Path, "pki", "issuer"),
            RejectedRoot = Path.Combine(_tempDir.Path, "pki", "rejected"),
            SubjectName = $"CN={_appName}"
        };
        Directory.CreateDirectory(certStores.AppRoot);
        return new Core.Ua.Configuration.SecurityConfiguration(certStores) { AutoAcceptUntrustedCertificates = true, SkipDomainValidation = new() { Skip = true }, RejectSHA1SignedCertificates = new SHA1Validation() { Reject = false } };
    }

    private async Task<S7UaClient> CreateAndConnectClientAsync()
    {
        var client = new S7UaClient(_userIdentity, _maxSessions);
        var appConfig = CreateTestAppConfig();
        await client.ConfigureAsync(appConfig);

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
            var populatedInputs = (S7Inputs?)await client.DiscoverNodeAsync((S7Inputs)inputsShell);
            Assert.NotNull(populatedInputs);

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
    public async Task DiscoverElement_WithRealInstanceDb_ReturnsFullyPopulatedDbWithMappedVariables()
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
            var discoveredElement = (S7DataBlockInstance?)await client.DiscoverNodeAsync(dbShell);

            // Assert
            Assert.NotNull(discoveredElement);
            var populatedDb = discoveredElement;
            Assert.NotNull(populatedDb.Inputs);
            Assert.NotNull(populatedDb.Outputs);
            Assert.NotNull(populatedDb.InOuts);
            Assert.NotNull(populatedDb.Static);
            Assert.NotEmpty(populatedDb.Inputs.Variables);
            Assert.NotEmpty(populatedDb.Outputs.Variables);
            Assert.NotEmpty(populatedDb.InOuts.Variables);
            Assert.NotEmpty(populatedDb.Static.Variables);

            var inpBool = populatedDb.Inputs.Variables.FirstOrDefault(v => v.DisplayName == "Function_InputBool");
            Assert.NotNull(inpBool);
            Assert.Equal(S7DataType.BOOL, inpBool.S7Type);

            var inpReal = populatedDb.Inputs.Variables.FirstOrDefault(v => v.DisplayName == "Function_InputReal");
            Assert.NotNull(inpReal);
            Assert.Equal(S7DataType.REAL, inpReal.S7Type);

            var outBool = populatedDb.Outputs.Variables.FirstOrDefault(v => v.DisplayName == "Function_OutputBool");
            Assert.NotNull(outBool);
            Assert.Equal(S7DataType.BOOL, outBool.S7Type);

            var outInt = populatedDb.Outputs.Variables.FirstOrDefault(v => v.DisplayName == "Function_OutputInt");
            Assert.NotNull(outInt);
            Assert.Equal(S7DataType.INT, outInt.S7Type);

            var inOutWord = populatedDb.InOuts.Variables.FirstOrDefault(v => v.DisplayName == "Function_InOutWord");
            Assert.NotNull(inOutWord);
            Assert.Equal(S7DataType.WORD, inOutWord.S7Type);

            var nestedStruct = populatedDb.Static.Variables.FirstOrDefault(v => v.DisplayName == "NestedStruct");
            Assert.NotNull(nestedStruct);
            Assert.Equal(S7DataType.UDT, nestedStruct.S7Type);
            Assert.NotEmpty(nestedStruct.StructMembers);

            var nestedStructNestedBool = nestedStruct.StructMembers.FirstOrDefault(m => m.DisplayName == "NestedBool");
            Assert.NotNull(nestedStructNestedBool);
            Assert.Equal(S7DataType.BOOL, nestedStructNestedBool.S7Type);

            var anotherNestedStruct = populatedDb.Static.Variables.FirstOrDefault(v => v.DisplayName == "AnotherNestedStruct");
            Assert.NotNull(anotherNestedStruct);
            Assert.Equal(S7DataType.UDT, anotherNestedStruct.S7Type);
            Assert.NotEmpty(anotherNestedStruct.StructMembers);

            var aNestedStructInsideANestedStruct = anotherNestedStruct.StructMembers.FirstOrDefault(m => m.DisplayName == "ANestedStructInsideANestedStruct");
            Assert.NotNull(aNestedStructInsideANestedStruct);
            Assert.Equal(S7DataType.UDT, aNestedStructInsideANestedStruct.S7Type);
            Assert.NotEmpty(aNestedStructInsideANestedStruct.StructMembers);

            var aVeryNestedBool = aNestedStructInsideANestedStruct.StructMembers.FirstOrDefault(m => m.DisplayName == "AVeryNestedBool");
            Assert.NotNull(aVeryNestedBool);
            Assert.Equal(S7DataType.BOOL, aVeryNestedBool.S7Type);
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
            var populatedInputs = (S7Inputs?)await client.DiscoverNodeAsync((S7Inputs)inputsShell);
            Assert.NotNull(populatedInputs);
            var populatedOutputs = (S7Outputs?)await client.DiscoverNodeAsync((S7Outputs)outputsShell);
            Assert.NotNull(populatedOutputs);
            var populatedMemory = (S7Memory?)await client.DiscoverNodeAsync((S7Memory)memoryShell);
            Assert.NotNull(populatedMemory);
            var correctlyTypedInputs = populatedInputs.Variables.Cast<S7Variable>().Select(v => v with { S7Type = S7DataType.BOOL }).ToList();
            var correctlyTypedOutputs = populatedOutputs.Variables.Cast<S7Variable>().Select(v => v with { S7Type = S7DataType.BOOL }).ToList();
            var correctlyTypedMemory = populatedMemory.Variables.Cast<S7Variable>().Select(v => v with { S7Type = S7DataType.BOOL }).ToList();
            var inputsToRead = populatedInputs with { Variables = correctlyTypedInputs };
            var outputsToRead = populatedOutputs with { Variables = correctlyTypedOutputs };
            var memoryToRead = populatedMemory with { Variables = correctlyTypedMemory };
            var inputsWithValues = await client.ReadNodeValuesAsync(inputsToRead, "Inputs");
            var outputsWithValues = await client.ReadNodeValuesAsync(outputsToRead, "Outputs");
            var memoryWithValues = await client.ReadNodeValuesAsync(memoryToRead, "Memory");

            // Assert
            var inputVar = inputsWithValues.Variables.First(v => v.DisplayName == "TestInput");
            Assert.Equal(StatusCode.Good, inputVar.StatusCode);
            Assert.Equal(S7DataType.BOOL, inputVar.S7Type);
            Assert.False((bool)inputVar.Value!);

            var outputVar = outputsWithValues.Variables.First(v => v.DisplayName == "TestOutput");
            Assert.Equal(StatusCode.Good, outputVar.StatusCode);
            Assert.Equal(S7DataType.BOOL, outputVar.S7Type);
            Assert.False((bool)outputVar.Value!);

            var memoryVar = memoryWithValues.Variables.First(v => v.DisplayName == "TestVar");
            Assert.Equal(StatusCode.Good, memoryVar.StatusCode);
            Assert.Equal(S7DataType.BOOL, memoryVar.S7Type);
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
            var dbWithVars = (S7DataBlockGlobal?)await client.DiscoverNodeAsync(dbShell);
            Assert.NotNull(dbWithVars?.Variables);

            // Act
            var dbWithValues = await client.ReadNodeValuesAsync(dbWithVars, "DataBlocksGlobal");

            // Assert
            void AssertVar(string name, object? expected, S7DataType s7Type)
            {
                var variable = dbWithValues.Variables.FirstOrDefault(v => v.DisplayName == name);
                Assert.NotNull(variable);
                Assert.Equal(StatusCode.Good, variable.StatusCode);
                Assert.Equal(s7Type, variable.S7Type);

                if (expected != null)
                {
                    Assert.Equal(expected, variable.Value);
                }
            }
            AssertVar("TestBool", true, S7DataType.BOOL);
            AssertVar("TestInt", (short)9, S7DataType.INT);
            AssertVar("TestReal", (float)8.2, S7DataType.REAL);
            AssertVar("TestString", "Hallo", S7DataType.STRING);
            AssertVar("TestByte", (byte)3, S7DataType.BYTE);
            AssertVar("TestChar", 'C', S7DataType.CHAR);
            AssertVar("TestDInt", 12, S7DataType.DINT);
            AssertVar("TestDWord", (uint)0x31, S7DataType.DWORD);
            AssertVar("TestDate", new DateTime(2025, 3, 10), S7DataType.DATE);
            AssertVar("TestDateAndTime", new DateTime(2025, 10, 12, 8, 9, 31, 212), S7DataType.DATE_AND_TIME);
            AssertVar("TestLDT", new DateTime(2008, 10, 25, 8, 12, 34, 567), S7DataType.LDT);
            AssertVar("TestLInt", 1500000L, S7DataType.LINT);
            AssertVar("TestLReal", (double)12.13123, S7DataType.LREAL);
            AssertVar("TestLTime", TimeSpan.FromMilliseconds(200), S7DataType.LTIME);
            AssertVar("TestLTimeOfDay", new TimeSpan(12, 11, 31), S7DataType.LTIME_OF_DAY);
            AssertVar("TestLWord", (ulong)0x22, S7DataType.LWORD);
            AssertVar("TestS5Time", TimeSpan.FromSeconds(60), S7DataType.S5TIME);
            AssertVar("TestSInt", (sbyte)-103, S7DataType.SINT);
            AssertVar("TestTime", TimeSpan.FromSeconds(40), S7DataType.TIME);
            AssertVar("TestTimeOfDay", new TimeSpan(8, 12, 22), S7DataType.TIME_OF_DAY);
            AssertVar("TestUDInt", (uint)234134, S7DataType.UDINT);
            AssertVar("TestUInt", (ushort)32421, S7DataType.UINT);
            AssertVar("TestULInt", (ulong)891841, S7DataType.ULINT);
            AssertVar("TestUSInt", (byte)222, S7DataType.USINT);
            AssertVar("TestWChar", 'e', S7DataType.WCHAR);
            AssertVar("TestDTL", new DateTime(2008, 12, 16, 20, 30, 20, 250).AddTicks(1110 + 3), S7DataType.DTL);

            var structVar = dbWithValues.Variables.First(v => v.DisplayName == "TestStruct");
            Assert.NotNull(structVar);
            Assert.Equal(StatusCode.Good, structVar.StatusCode);
            Assert.Equal(S7DataType.UDT, structVar.S7Type);
            Assert.NotNull(structVar.StructMembers);
            Assert.NotEmpty(structVar.StructMembers);
            Assert.Collection(structVar.StructMembers,
                m =>
                {
                    Assert.Equal("TestStructBool", m.DisplayName);
                    Assert.Equal(StatusCode.Good, m.StatusCode);
                    Assert.Equal(S7DataType.BOOL, m.S7Type);
                    Assert.Equal(true, m.Value);
                },
                m =>
                {
                    Assert.Equal("TestStructInt", m.DisplayName);
                    Assert.Equal(StatusCode.Good, m.StatusCode);
                    Assert.Equal(S7DataType.INT, m.S7Type);
                    Assert.Equal((short)12341, m.Value);
                },
                m =>
                {
                    Assert.Equal("TestDateAndTime", m.DisplayName);
                    Assert.Equal(StatusCode.Good, m.StatusCode);
                    Assert.Equal(S7DataType.DATE_AND_TIME, m.S7Type);
                    Assert.Equal(new DateTime(1990, 1, 1), m.Value);
                });

            AssertVar("TestCharArray", new List<char> { 'h', 'u', 'H', 'U' }, S7DataType.ARRAY_OF_CHAR);
            AssertVar("TestDateAndTimeArray",
                new List<DateTime>
                { new(2025, 10, 12, 8, 9, 31, 212),
                    new(2024, 10, 12, 8, 9, 31, 212),
                    new(2023, 10, 12, 8, 9, 31, 212),
                    new(2022, 10, 12, 8, 9, 31, 212) },
                S7DataType.ARRAY_OF_DATE_AND_TIME);
        }
        finally
        {
            await client!.DisconnectAsync();
        }
    }

    #region Write Tests

    [Fact]
    public async Task WriteAndReadBack_NestedStructMember_Succeeds()
    {
        S7UaClient? client = null;
        S7Variable? testVar = null;
        object? originalValue = null;

        try
        {
            // Arrange
            client = await CreateAndConnectClientAsync();
            var dbShell = (await client.GetAllGlobalDataBlocksAsync()).First(db => db.DisplayName == "Datablock");
            var dbWithVars = (S7DataBlockGlobal?)await client.DiscoverNodeAsync(dbShell);
            Assert.NotNull(dbWithVars);

            // Inline logic to prepare the specific variable structure for reading
            var structVar = dbWithVars.Variables.First(v => v.DisplayName == "TestStruct") as S7Variable;
            Assert.NotNull(structVar);
            Assert.Equal(S7DataType.UDT, structVar.S7Type);
            Assert.Collection(structVar.StructMembers,
                m =>
                {
                    Assert.Equal("TestStructBool", m.DisplayName);
                    Assert.Equal(S7DataType.BOOL, m.S7Type);
                },
                m =>
                {
                    Assert.Equal("TestStructInt", m.DisplayName);
                    Assert.Equal(S7DataType.INT, m.S7Type);
                },
                m =>
                {
                    Assert.Equal("TestDateAndTime", m.DisplayName);
                    Assert.Equal(S7DataType.DATE_AND_TIME, m.S7Type);
                });

            // Act 1: Read the initial value
            var dbWithOriginalValues = await client.ReadNodeValuesAsync(dbWithVars);
            testVar = dbWithOriginalValues.Variables
                .First(v => v.DisplayName == "TestStruct").StructMembers
                .First(m => m.DisplayName == "TestStructInt") as S7Variable;

            Assert.NotNull(testVar);
            originalValue = testVar.Value;
            Assert.NotNull(originalValue);

            // Act 2: Write a new value and read it back
            short newValue = (short)((short)originalValue + 1);
            Assert.True(await client.WriteVariableAsync(testVar, newValue));

            var dbWithNewValues = await client.ReadNodeValuesAsync(dbWithVars);
            var updatedVar = dbWithNewValues.Variables
                .First(v => v.DisplayName == "TestStruct").StructMembers
                .First(m => m.DisplayName == "TestStructInt");

            // Assert
            Assert.Equal(newValue, updatedVar.Value);
        }
        finally
        {
            // Cleanup
            if (client?.IsConnected == true && testVar != null && originalValue != null)
            {
                await client.WriteVariableAsync(testVar, originalValue);
            }
            await client!.DisconnectAsync();
        }
    }

    [Fact]
    public async Task WriteAndReadBack_ArrayValue_Succeeds()
    {
        S7UaClient? client = null;
        S7Variable? testVar = null;
        object? originalValue = null;

        try
        {
            // Arrange
            client = await CreateAndConnectClientAsync();
            var dbShell = (await client.GetAllGlobalDataBlocksAsync()).First(db => db.DisplayName == "Datablock");
            var dbWithVars = (S7DataBlockGlobal?)await client.DiscoverNodeAsync(dbShell);
            Assert.NotNull(dbWithVars);

            // Act 1: Read initial value
            var dbWithOriginalValues = await client.ReadNodeValuesAsync(dbWithVars);
            testVar = dbWithOriginalValues.Variables.First(v => v.DisplayName == "TestCharArray") as S7Variable;
            Assert.NotNull(testVar);
            Assert.Equal(S7DataType.ARRAY_OF_CHAR, testVar.S7Type);
            originalValue = testVar.Value;
            Assert.NotNull(originalValue);

            // Act 2: Write new array value and read back
            var newValue = new List<char> { 'X', 'Y', 'Z', '!' };
            Assert.True(await client.WriteVariableAsync(testVar, newValue));

            var dbWithNewValues = (S7DataBlockGlobal)await client.ReadNodeValuesAsync(dbWithOriginalValues);
            var updatedVar = dbWithNewValues.Variables.First(v => v.DisplayName == "TestCharArray");

            // Assert
            Assert.Equal(newValue, updatedVar.Value);
        }
        finally
        {
            // Cleanup
            if (client?.IsConnected == true && testVar != null && originalValue != null)
            {
                await client.WriteVariableAsync(testVar, originalValue);
            }
            await client!.DisconnectAsync();
        }
    }

    [Fact]
    public async Task WriteAndReadBack_UdtArrayValue_Succeeds()
    {
        S7UaClient? client = null;
        S7Variable? testVar = null;
        object? originalValue = null;

        try
        {
            // Arrange
            client = await CreateAndConnectClientAsync();

            // Register the custom UDT converter
            client.RegisterUdtConverter(new MyCustomUdtConverter());

            var dbShell = (await client.GetAllGlobalDataBlocksAsync()).First(db => db.DisplayName == "MyGlobalDb");
            var dbWithVars = (S7DataBlockGlobal?)await client.DiscoverNodeAsync(dbShell);
            Assert.NotNull(dbWithVars);

            // Find the UDT array variable
            testVar = dbWithVars.Variables.FirstOrDefault(v =>
                v.DisplayName == "myUDTArray" &&
                v.S7Type == S7DataType.ARRAY_OF_UDT) as S7Variable;

            Assert.NotNull(testVar);
            Assert.Equal(S7DataType.ARRAY_OF_UDT, testVar.S7Type);
            Assert.Equal("DT_\"typeMyCustomUDT\"", testVar.UdtTypeName);

            // Act 1: Read initial value with custom converter
            var dbWithOriginalValues = await client.ReadNodeValuesAsync(dbWithVars);
            testVar = dbWithOriginalValues.Variables.First(v => v.NodeId == testVar.NodeId) as S7Variable;
            Assert.NotNull(testVar);

            Assert.IsType<System.Collections.IList>(testVar.Value, exactMatch: false);
            originalValue = testVar.Value;
            Assert.NotNull(originalValue);

            // Convert to strongly typed list for inspection
            var originalList = originalValue as System.Collections.IList;
            Assert.NotNull(originalList);
            Assert.True(originalList.Count > 0, "Array should have at least one element");

            Assert.Collection<MyCustomUdt>(originalList.Cast<MyCustomUdt>(),
                item =>
                {
                    Assert.IsType<MyCustomUdt>(item);
                    Assert.IsType<bool>(item.OneBool);
                    Assert.True(item.OneBool);
                    Assert.IsType<short>(item.OneInt);
                    Assert.Equal(1, item.OneInt);
                    Assert.IsType<string>(item.OneString);
                    Assert.Equal("Test1", item.OneString);
                },
                item =>
                {
                    Assert.IsType<MyCustomUdt>(item);
                    Assert.IsType<bool>(item.OneBool);
                    Assert.False(item.OneBool);
                    Assert.IsType<short>(item.OneInt);
                    Assert.Equal(2, item.OneInt);
                    Assert.IsType<string>(item.OneString);
                    Assert.Equal("Test2", item.OneString);
                },
                item =>
                {
                    Assert.IsType<MyCustomUdt>(item);
                    Assert.IsType<bool>(item.OneBool);
                    Assert.True(item.OneBool);
                    Assert.IsType<short>(item.OneInt);
                    Assert.Equal(3, item.OneInt);
                    Assert.IsType<string>(item.OneString);
                    Assert.Equal("Test3", item.OneString);
                });

            // Act 2: Write new UDT array values
            var newUdtArray = new List<MyCustomUdt>
            {
                new(OneBool: false, OneInt: 100, OneString: "First"),
                new(OneBool: true, OneInt: 200, OneString: "Second"),
                new(OneBool: false, OneInt: 300, OneString: "Third")
            };

            //bool writeSuccess = await client.WriteVariableAsync(originalArrayVar, newUdtArray);
            bool writeSuccess = await client.WriteVariableAsync(testVar, newUdtArray);

            Assert.True(writeSuccess, "Writing the new UDT array values failed.");
            // Act 3: Read back the modified values
            var dbWithNewValues = await client.ReadNodeValuesAsync(dbWithVars);
            var modifiedArrayVar = dbWithNewValues.Variables.First(v => v.NodeId == testVar.NodeId) as S7Variable;
            Assert.NotNull(modifiedArrayVar);

            // Verify the read value is the expected modified UDT array
            Assert.IsType<System.Collections.IList>(modifiedArrayVar.Value, exactMatch: false);
            var modifiedList = modifiedArrayVar.Value as System.Collections.IList;
            Assert.NotNull(modifiedList);
            Assert.Equal(newUdtArray.Count, modifiedList.Count);

            Assert.Collection<MyCustomUdt>(modifiedList.Cast<MyCustomUdt>(),
                item =>
                {
                    Assert.IsType<MyCustomUdt>(item);
                    Assert.IsType<bool>(item.OneBool);
                    Assert.False(item.OneBool);
                    Assert.IsType<short>(item.OneInt);
                    Assert.Equal(100, item.OneInt);
                    Assert.IsType<string>(item.OneString);
                    Assert.Equal("First", item.OneString);
                },
                item =>
                {
                    Assert.IsType<MyCustomUdt>(item);
                    Assert.IsType<bool>(item.OneBool);
                    Assert.True(item.OneBool);
                    Assert.IsType<short>(item.OneInt);
                    Assert.Equal(200, item.OneInt);
                    Assert.IsType<string>(item.OneString);
                    Assert.Equal("Second", item.OneString);
                },
                item =>
                {
                    Assert.IsType<MyCustomUdt>(item);
                    Assert.IsType<bool>(item.OneBool);
                    Assert.False(item.OneBool);
                    Assert.IsType<short>(item.OneInt);
                    Assert.Equal(300, item.OneInt);
                    Assert.IsType<string>(item.OneString);
                    Assert.Equal("Third", item.OneString);
                });

            // Verify struct members are also properly populated
            Assert.NotEmpty(modifiedArrayVar.StructMembers);

            // Check that array members have proper array indices
            var membersWithIndex0 = modifiedArrayVar.StructMembers.Where(m =>
                m.FullPath?.Contains("[0]") == true).ToList();
            var membersWithIndex1 = modifiedArrayVar.StructMembers.Where(m =>
                m.FullPath?.Contains("[1]") == true).ToList();
            var membersWithIndex2 = modifiedArrayVar.StructMembers.Where(m =>
                m.FullPath?.Contains("[2]") == true).ToList();

            Assert.NotEmpty(membersWithIndex0);
            Assert.NotEmpty(membersWithIndex1);
            Assert.NotEmpty(membersWithIndex2);
        }
        finally
        {
            // Cleanup: Restore original values
            if (client?.IsConnected == true && testVar != null && originalValue != null)
            {
                await client.WriteVariableAsync(testVar, originalValue);
            }
            await client!.DisconnectAsync();
        }
    }

    [Fact]
    public async Task WriteVariableAsync_WithIncompatibleType_ReturnsFalse()
    {
        S7UaClient? client = null;
        try
        {
            // Arrange
            client = await CreateAndConnectClientAsync();
            var dbShell = (await client.GetAllGlobalDataBlocksAsync()).First(db => db.DisplayName == "Datablock");
            var dbWithVars = (S7DataBlockGlobal?)await client.DiscoverNodeAsync(dbShell);
            Assert.NotNull(dbWithVars);
            var testVar = dbWithVars.Variables.First(v => v.DisplayName == "TestInt") as S7Variable;
            Assert.NotNull(testVar);
            Assert.Equal(S7DataType.INT, testVar.S7Type);

            // Act: This should fail because the server expects a short (Int), not a string.
            bool success = await client.WriteVariableAsync(testVar, "this is not a number");

            // Assert
            Assert.False(success);
        }
        finally
        {
            await client!.DisconnectAsync();
        }
    }

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
            var dbWithVars = (S7DataBlockGlobal?)await client.DiscoverNodeAsync(dbShell);
            Assert.NotNull(dbWithVars);
            testVar = dbWithVars.Variables.FirstOrDefault(v => v.DisplayName == "AnotherTestInt") as S7Variable;
            Assert.NotNull(testVar);
            Assert.Equal(S7DataType.INT, testVar.S7Type);

            // Act & Assert
            var dbWithOriginalValues = await client.ReadNodeValuesAsync(dbWithVars with { Variables = [testVar] });
            originalValue = dbWithOriginalValues.Variables[0].Value;
            Assert.NotNull(originalValue);

            const short newValue = 999;
            bool writeSuccess = await client.WriteVariableAsync(testVar, newValue);
            Assert.True(writeSuccess, "Writing the new value failed.");

            // Act & Assert
            var dbWithNewValues = await client.ReadNodeValuesAsync(dbWithVars with { Variables = [testVar] });
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
    public async Task SubscribeToVariableAsync_ReceivesNotification_AndUnsubscribes()
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
            var dbWithVars = (S7DataBlockGlobal?)await client.DiscoverNodeAsync(dbShell);
            Assert.NotNull(dbWithVars);
            testVar = dbWithVars.Variables.FirstOrDefault(v => v.DisplayName == "AnotherTestDInt") as S7Variable;
            Assert.NotNull(testVar);
            Assert.Equal(S7DataType.DINT, testVar.S7Type);
            testVar = testVar with { SamplingInterval = 100 };

            var dbWithOriginalValue = await client.ReadNodeValuesAsync(dbWithVars with { Variables = [testVar] });
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

            // Act 1: Subscribe and trigger notification
            Assert.True(await client.CreateSubscriptionAsync(publishingInterval: 200));
            Assert.True(await client.SubscribeToVariableAsync(testVar));

            const int newValue = -2222;
            Assert.True(await client.WriteVariableAsync(testVar, newValue));

            // Assert 1: Notification is received and correct
            Assert.True(notificationReceivedEvent.Wait(TimeSpan.FromSeconds(10)), "No notification received.");
            Assert.NotNull(receivedArgs);
            var notificationValue = receivedArgs.Notification.Value;
            Assert.Equal(newValue, (int)notificationValue.Value!);

            // Act 2: Unsubscribe
            Assert.True(await client.UnsubscribeFromVariableAsync(testVar));

            // Assert 2: No more notifications are received
            notificationReceivedEvent.Reset();
            await client.WriteVariableAsync(testVar, (int)newValue + 1);
            Assert.False(notificationReceivedEvent.Wait(TimeSpan.FromSeconds(2)), "A notification was received after unsubscribing.");
        }
        finally
        {
            // Cleanup
            if (client?.IsConnected == true && testVar != null && originalValue != null)
            {
                await client.WriteVariableAsync(testVar, originalValue);
            }
            await client!.DisconnectAsync();
        }
    }

    #endregion Subscription Tests

    #region Custom UDT Converter Tests

    [Fact]
    public async Task WriteAndReadBack_CustomUdtConverter_Succeeds()
    {
        S7UaClient? client = null;
        S7Variable? testUdtVar = null;
        MyCustomUdt? originalValue = null;

        try
        {
            // Arrange
            client = await CreateAndConnectClientAsync();

            // Register the custom UDT converter
            client.RegisterUdtConverter(new MyCustomUdtConverter());

            var globalDbs = await client.GetAllGlobalDataBlocksAsync();
            var dbShell = globalDbs.FirstOrDefault(db => db.DisplayName == "MyGlobalDb");
            Assert.NotNull(dbShell);

            var dbWithVars = (S7DataBlockGlobal?)await client.DiscoverNodeAsync(dbShell);
            Assert.NotNull(dbWithVars);

            // Find the UDT variable - assuming it exists in the PLC as "MyCustomUdtInstance"
            testUdtVar = dbWithVars.Variables.FirstOrDefault(v =>
                v.S7Type == S7DataType.UDT &&
                !string.IsNullOrWhiteSpace(v.UdtTypeName) &&
                v.UdtTypeName.Contains("typeMyCustomUdt", StringComparison.OrdinalIgnoreCase)) as S7Variable;

            Assert.NotNull(testUdtVar);
            Assert.Equal(S7DataType.UDT, testUdtVar.S7Type);
            Assert.Equal("DT_\"typeMyCustomUDT\"", testUdtVar.UdtTypeName);

            // Verify the UDT structure has the expected members
            Assert.Collection(testUdtVar.StructMembers,
                m =>
                {
                    Assert.Equal("OneBool", m.DisplayName);
                    Assert.Equal(S7DataType.BOOL, m.S7Type);
                },
                m =>
                {
                    Assert.Equal("OneInt", m.DisplayName);
                    Assert.Equal(S7DataType.INT, m.S7Type);
                },
                m =>
                {
                    Assert.Equal("OneString", m.DisplayName);
                    Assert.Equal(S7DataType.STRING, m.S7Type);
                });

            // Act 1: Read original values using custom converter
            var dbWithOriginalValues = await client.ReadNodeValuesAsync(dbWithVars);
            var originalUdtVar = dbWithOriginalValues.Variables.First(v => v.NodeId == testUdtVar.NodeId) as S7Variable;
            Assert.NotNull(originalUdtVar);

            // The Value should now be a MyCustomUdt object thanks to our custom converter
            Assert.IsType<MyCustomUdt>(originalUdtVar.Value);
            originalValue = (MyCustomUdt)originalUdtVar.Value!;

            // Verify original values are properly converted
            Assert.NotNull(originalValue);
            Assert.IsType<bool>(originalValue.OneBool);
            Assert.IsType<short>(originalValue.OneInt);
            Assert.IsType<string>(originalValue.OneString);

            // Act 2: Write new values using custom UDT object
            var newUdtValue = new MyCustomUdt(
                OneBool: !originalValue.OneBool,
                OneInt: (short)(originalValue.OneInt + 100),
                OneString: originalValue.OneString + "_Modified"
            );

            bool writeSuccess = await client.WriteVariableAsync(originalUdtVar, newUdtValue);
            Assert.True(writeSuccess, "Writing the new UDT value failed.");

            // Act 3: Read back the modified values
            var dbWithNewValues = await client.ReadNodeValuesAsync(dbWithVars);
            var modifiedUdtVar = dbWithNewValues.Variables.First(v => v.NodeId == testUdtVar.NodeId) as S7Variable;
            Assert.NotNull(modifiedUdtVar);

            // Verify the read value is the expected modified UDT object
            Assert.IsType<MyCustomUdt>(modifiedUdtVar.Value);
            var readBackValue = (MyCustomUdt)modifiedUdtVar.Value!;

            Assert.Equal(newUdtValue.OneBool, readBackValue.OneBool);
            Assert.Equal(newUdtValue.OneInt, readBackValue.OneInt);
            Assert.Equal(newUdtValue.OneString, readBackValue.OneString);

            // Act 4: Verify struct members are also properly populated
            Assert.NotEmpty(modifiedUdtVar.StructMembers);
            var boolMember = modifiedUdtVar.StructMembers.First(m => m.DisplayName == "OneBool");
            var intMember = modifiedUdtVar.StructMembers.First(m => m.DisplayName == "OneInt");
            var stringMember = modifiedUdtVar.StructMembers.First(m => m.DisplayName == "OneString");

            Assert.Equal(newUdtValue.OneBool, boolMember.Value);
            Assert.Equal(newUdtValue.OneInt, intMember.Value);
            Assert.Equal(newUdtValue.OneString, stringMember.Value);
        }
        finally
        {
            // Cleanup: Restore original values
            if (client?.IsConnected == true && testUdtVar != null && originalValue != null)
            {
                await client.WriteVariableAsync(testUdtVar, originalValue);
            }
            await client!.DisconnectAsync();
        }
    }

    #endregion Custom UDT Converter Tests
}