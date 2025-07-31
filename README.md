# S7UaLib 🏭
A modern, high-level .NET library designed to simplify communication with Siemens S7 PLCs (like S7-1200/1500) via their integrated OPC UA servers. It abstracts the complexities of the OPC UA protocol, providing an intuitive, object-oriented way to browse the PLC structure, read/write variables, and handle S7-specific data types.

[![.NET 8 (LTS) Build & Test](https://github.com/philipp2604/S7UaLib/actions/workflows/dotnet-8-build-and-test.yml/badge.svg)](https://github.com/philipp2604/S7UaLib/actions/workflows/dotnet-8-build-and-test.yml)
[![.NET 9 (Latest) Build & Test](https://github.com/philipp2604/S7UaLib/actions/workflows/dotnet-9-build-and-test.yml/badge.svg)](https://github.com/philipp2604/S7UaLib/actions/workflows/dotnet-9-build-and-test.yml)  
[![Language](https://img.shields.io/badge/language-C%23-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![License](https://img.shields.io/badge/License-GPL_v2-blue.svg)](https://www.gnu.org/licenses/old-licenses/gpl-2.0.en.html)
[![GitHub issues](https://img.shields.io/github/issues/philipp2604/S7UaLib)](https://github.com/philipp2604/S7UaLib/issues)  
[![NuGet Version](https://img.shields.io/nuget/v/philipp2604.S7UaLib.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/philipp2604.S7UaLib/)
[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/philipp2604/S7UaLib)

## ✨ Key Features

- **High-Level API**: Interact with your PLC through the simple and powerful `S7Service`.
- **🔌 Effortless Connection Management**: Handles connecting, disconnecting, and automatic reconnection with configurable keep-alive and backoff strategies.
- **⚡️ High-Performance Session Pooling**: Utilizes a pool of OPC UA sessions for stateless operations (read, write, browse), dramatically reducing overhead and increasing throughput for high-frequency tasks.
- **🌳 Full Structure Discovery**: Automatically browses and maps the entire S7 OPC UA server structure, including:
  - Global Data Blocks (`DB`)
  - Instance Data Blocks (`iDB`), including nested structures
  - Inputs (`I`), Outputs (`Q`), and Memory (`M`)
  - Timers (`T`) and Counters (`C`)
- **✍️ Manual Registration**: Manually define variables and data blocks that are not browsable on the OPC UA server, enabling access to any tag.
- **🔄 Automatic S7 Data Type Conversion**: Seamlessly converts complex S7 data types to and from standard .NET types. No more manual byte-wrangling!
  - `DATE_AND_TIME` ↔ `System.DateTime`
  - `DTL` ↔ `System.DateTime` (with nanosecond precision)
  - `TIME`, `LTIME`, `S5TIME` ↔ `System.TimeSpan`
  - `TIME_OF_DAY`, `LTIME_OF_DAY` ↔ `System.TimeSpan`
  - `CHAR`, `WCHAR` ↔ `System.Char`
  - And all corresponding `ARRAY` types.
- **💾 Structure Persistence**: Save your discovered PLC structure to a JSON file and load it on startup to bypass the time-consuming discovery process.
- **⚡️ Type-Safe & Path-Based Access**: Read and write variables using their full symbolic path (e.g., `"DataBlocksGlobal.MyDb.MySetting"`).
- **🔔 Event-Driven Value Changes**: React to data changes in your application through two powerful mechanisms:
  - **Polling:** Use `ReadAllVariablesAsync()` to get a snapshot and trigger `VariableValueChanged` for any changes since the last read.
  - **Subscriptions:** Use `SubscribeToVariableAsync()` to receive real-time updates from the PLC, which also trigger the `VariableValueChanged` event.
- **🚀 Async & Thread-Safe**: Fully asynchronous API (`async`/`await`) for all network operations. A central `S7Service` instance is thread-safe and can be shared across your application, managing a persistent connection for subscriptions and a high-performance session pool for other tasks.
- **🏗️ Modern & Immutable**: Built with modern C# features, using immutable records for data structures to ensure thread safety and predictability.

## 🏛️ Architecture

The library is designed with a clean, modular architecture, split into several key projects. This separation of concerns makes the library more maintainable and easier to understand.

-   **`S7UaLib.Core`**: The foundational library. It defines all shared interfaces, enumerations, and data models (`IS7Variable`, `S7DataType`, etc.). It's the "vocabulary" of the ecosystem.
-   **`S7UaLib.Infrastructure`**: The implementation engine. This library contains the concrete logic for communicating via OPC UA, converting data types, and caching the PLC structure. It's the internal "machinery".
-   **`S7UaLib` (S7UaLib.Services)**: The high-level public API. It exposes the simple `S7Service`, which orchestrates the underlying components to provide the easy-to-use functionality you see in the features list.

The `S7UaClient` within the Infrastructure layer is intelligently composed of two parts: a main client that maintains a persistent, stateful connection for handling subscriptions and connection status, and a high-performance session pool for all other stateless operations like browsing, reading, and writing. This design ensures both robust event handling and high-throughput communication.

As an end-user, you only need to install the main `philipp2604.S7UaLib` NuGet package. The others will be included automatically as dependencies.

## 🚀 Getting Started

### Installation

S7UaLib is available on NuGet. You can install it using the .NET CLI:

```bash
dotnet add package philipp2604.S7UaLib
```
Or via the NuGet Package Manager in Visual Studio.

### Quick Start

Here's a simple example demonstrating the main workflow: connect, discover, subscribe to changes, and write a value.

```csharp
using S7UaLib.Core.Events;
using S7UaLib.Core.S7.Converters;
using S7UaLib.Core.S7.Structure;
using S7UaLib.Core.Ua;
using S7UaLib.Core.Ua.Configuration;
using S7UaLib.Services.S7;

// --- Configuration ---

const string serverUrl = "opc.tcp://172.168.0.1:4840";
const string configFile = "my_plc_structure.json";
const string myIntVarPath = "DataBlocksGlobal.Datablock.TestInt";
const string myStringVarPath = "DataBlocksGlobal.Datablock.TestString";

const string appName = "S7UaLib Example";
const string appUri = "urn:localhost:UA:S7UaLib:Example";
const string productUri = "uri:philipp2604:S7UaLib:Example";

// Use an empty constructor for anonymous user, or new UserIdentity("user", "pass")
var userIdentity = new UserIdentity();

// 1. Initialize S7Service
var service = new S7Service(userIdentity);

// 2. Configure Service / Client
var appConfig = new ApplicationConfiguration
{
    ApplicationName = appName,
    ApplicationUri = appUri,
    ProductUri = productUri,
    SecurityConfiguration = new SecurityConfiguration(new SecurityConfigurationStores())
    {
        AutoAcceptUntrustedCertificates = true,
        SkipDomainValidation = new() { Skip = true },
        RejectSHA1SignedCertificates = new() { Reject = false }
    }
};
await service.ConfigureAsync(appConfig);

// Optional: Subscribe to value changes
service.VariableValueChanged += OnVariableValueChanged;

try
{
    // Optional: Register custom UDT converter
    service.RegisterUdtConverter(new MyCustomUdtConverter());

    // 3. Connect to the PLC
    Console.WriteLine($"Connecting to {serverUrl}...");
    await service.ConnectAsync(serverUrl, useSecurity: false);
    Console.WriteLine("Connected!");

    // 4. Load structure from file or discover it
    if (File.Exists(configFile))
    {
        Console.WriteLine("Loading structure from file...");
        await service.LoadStructureAsync(configFile);
    }
    else
    {
        Console.WriteLine("Discovering PLC structure...");
        await service.DiscoverStructureAsync();
        Console.WriteLine("Saving structure for next time...");
        await service.SaveStructureAsync(configFile);
    }

    // 5. Read all variables to get the initial state
    Console.WriteLine("\nReading all variable values...");
    await service.ReadAllVariablesAsync();

    // 6. Subscribe to real-time changes for a specific variable
    Console.WriteLine($"Subscribing to changes for '{myIntVarPath}'...");
    await service.SubscribeToVariableAsync(myIntVarPath);

    Console.WriteLine("\nLibrary is now listening for changes. Try changing the value in the PLC.");
    Console.WriteLine("Or press Enter to write a value from here and trigger a change...");
    Console.ReadLine();

    // 7. Write a new value to the variable
    if (service.GetVariable(myIntVarPath)?.Value is short intVal)
    {
        intVal = (short)(intVal + 1);
        Console.WriteLine($"Writing '{intVal}' to '{myIntVarPath}'...");
        if (await service.WriteVariableAsync(myIntVarPath, intVal))
        {
            Console.WriteLine($"Write to {myIntVarPath} successful! A new value change event should have been triggered.");
        }
    }

    // Optional: Write a value using the custom UDT
    var myCustomUdtS7Var = service.GetVariable("DataBlocksGlobal.MyGlobalDb.myUDTInstance");
    if(myCustomUdtS7Var != null && myCustomUdtS7Var.Value is MyCustomUdt myCustomUdt)
    {
        myCustomUdt = myCustomUdt with { OneInt = 42, OneBool = true, OneString = "Updated from S7UaLib" };
        await service.WriteVariableAsync(myCustomUdtS7Var.FullPath!, myCustomUdt);
    }

    Console.WriteLine($"Press Enter to write a value to {myStringVarPath} from here...");
    Console.ReadLine();

    // 8. Write a string value
    string newValue = $"Hello from S7UaLib at {DateTime.Now:T}";
    Console.WriteLine($"Writing '{newValue}' to '{myStringVarPath}'...");
    bool success = await service.WriteVariableAsync(myStringVarPath, newValue);

    if (success)
    {
        Console.WriteLine("Write successful! A new value change event should have been triggered if subscribed.");
    }

    Console.WriteLine("\nPress Enter to disconnect.");
    Console.ReadLine();
}
catch (Exception ex)
{
    Console.WriteLine($"An error occurred: {ex.Message}");
}
finally
{
    if (service.IsConnected)
    {
        Console.WriteLine("Disconnecting...");
        await service.DisconnectAsync();
    }
    service.VariableValueChanged -= OnVariableValueChanged;
    service.Dispose();
}

void OnVariableValueChanged(object? sender, VariableValueChangedEventArgs e)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("\n--- Value Change Detected ---");
    Console.WriteLine($"  Path:      {e.NewVariable.FullPath}");
    Console.WriteLine($"  Old Value: {e.OldVariable.Value ?? "null"}");
    Console.WriteLine($"  New Value: {e.NewVariable.Value ?? "null"}");
    Console.ResetColor();
}

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
```

## 📖 Documentation
- **[Manual](./MANUAL.md)**: A small manual on how to use this library.
- **[IS7Service Reference](./src/S7UaLib.Services/S7/IS7Service.cs)**: The `IS7Service` interface is the primary entry point and contract for all top-level operations.
- **[Integration Tests](./tests/S7UaLib.Services.Tests/Integration/S7ServiceIntegrationTests.cs)**: These tests showcase real-world usage patterns against a live S7-1500 PLC and serve as excellent, practical examples.

## 🤝 Contributing

Contributions are welcome! Whether it's bug reports, feature requests, or pull requests, your help is appreciated.

1.  **Fork** the repository.
2.  Create a new **branch** for your feature or bug fix.
3.  Make your changes.
4.  Add or update **unit/integration tests** to cover your changes.
5.  Submit a **Pull Request** with a clear description of your changes.

Please open an issue first to discuss any major changes.

## ⚖️ License

This project is licensed under the **GNU General Public License v2.0 (GPL-2.0)**.

This is a direct consequence of the dependency on the official `OPCFoundation.NetStandard.Opc.Ua` packages, which are licensed under GPL 2.0. Any project using S7UaLib must therefore also comply with the terms of the GPL 2.0 license, which generally means that if you distribute your application, you must also make the source code available.

Please review the [license terms](https://www.gnu.org/licenses/old-licenses/gpl-2.0.en.html) carefully before integrating this library into your project.