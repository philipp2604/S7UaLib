# S7UaLib 🏭
A modern, high-level .NET library designed to simplify communication with Siemens S7 PLCs (like S7-1200/1500) via their integrated OPC UA servers. It abstracts the complexities of the OPC UA protocol, providing an intuitive, object-oriented way to browse the PLC structure, read/write variables, and handle S7-specific data types.

[![.NET 8 (LTS) Build & Test](https://github.com/philipp2604/S7UaLib/actions/workflows/dotnet-8-build-and-test.yml/badge.svg)](https://github.com/philipp2604/S7UaLib/actions/workflows/dotnet-8-build-and-test.yml)
[![.NET 9 (Latest) Build & Test](https://github.com/philipp2604/S7UaLib/actions/workflows/dotnet-9-build-and-test.yml/badge.svg)](https://github.com/philipp2604/S7UaLib/actions/workflows/dotnet-9-build-and-test.yml)
[![Language](https://img.shields.io/badge/language-C%23-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![NuGet Version](https://img.shields.io/nuget/v/philipp2604.S7UaLib.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/philipp2604.S7UaLib/)
[![License](https://img.shields.io/badge/License-GPL_v2-blue.svg)](https://www.gnu.org/licenses/old-licenses/gpl-2.0.en.html)
[![GitHub issues](https://img.shields.io/github/issues/philipp2604/S7UaLib)](https://github.com/philipp2604/S7UaLib/issues)

## ✨ Key Features

- **High-Level API**: Interact with your PLC through the simple and powerful `S7Service`.
- **🔌 Effortless Connection Management**: Handles connecting, disconnecting, and automatic reconnection with configurable keep-alive and backoff strategies.
- **🌳 Full Structure Discovery**: Automatically browses and maps the entire S7 OPC UA server structure, including:
  - Global Data Blocks (`DB`)
  - Instance Data Blocks (`iDB`), including nested structures
  - Inputs (`I`), Outputs (`Q`), and Memory (`M`)
  - Timers (`T`) and Counters (`C`)
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
- **🚀 Async & Thread-Safe**: Fully asynchronous API (`async`/`await`) for all network operations ensures your application remains responsive. Built from the ground up to be thread-safe, allowing you to reliably use a single `S7Service` instance across multiple concurrent tasks.
- **🏗️ Modern & Immutable**: Built with modern C# features, using immutable records for data structures to ensure thread safety and predictability.

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
using Microsoft.Extensions.Logging;
using Opc.Ua;
using S7UaLib.Events;
using S7UaLib.Services;
using System.Collections;

// --- Configuration ---
const string serverUrl = "opc.tcp://192.168.0.1:4840";
const string configFile = "my_plc_structure.json";
const string myIntVarPath = "DataBlocksGlobal.Datablock.TestInt";
const string myStringVarPath = "DataBlocksGlobal.Datablock.TestString";

// 1. Configure the OPC UA Application
var appConfig = new ApplicationConfiguration
{
    ApplicationName = "S7UaLib QuickStart",
    ApplicationType = ApplicationType.Client,
    SecurityConfiguration = new SecurityConfiguration { AutoAcceptUntrustedCertificates = true },
    ClientConfiguration = new ClientConfiguration(),
    TransportQuotas = new TransportQuotas { OperationTimeout = 15000 }
};

// 2. Initialize the S7Service
// The second parameter is the standard OPC UA response validation action.
var service = new S7Service(appConfig, ClientBase.ValidateResponse);

// Optional: Subscribe to value changes
service.VariableValueChanged += OnVariableValueChanged;

try
{
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
    
    // After discovery/loading, it's often necessary to set the specific S7 data types
    // for variables, as this info isn't always exposed by the server.
    // This is typically done once and saved in the config file.
    await service.UpdateVariableTypeAsync(myIntVarPath, S7DataType.INT);
    await service.UpdateVariableTypeAsync(myStringVarPath, S7DataType.STRING);

    // 5. Read all variables to get the initial state
    Console.WriteLine("\nReading all variable values...");
    await service.ReadAllVariablesAsync();

    // 6. Subscribe to real-time changes for a specific variable
    Console.WriteLine($"Subscribing to changes for '{myIntVarPath}'...");
    await service.SubscribeToVariableAsync(myIntVarPath);
    
    Console.WriteLine("\nLibrary is now listening for changes. Try changing the value in the PLC.");
    Console.WriteLine("Or press Enter to write a value from here and trigger a change...");
    Console.ReadLine();

    // 7. Write a new value to a different variable
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
}

// Event handler for value changes (from polling or subscriptions)
void OnVariableValueChanged(object? sender, VariableValueChangedEventArgs e)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("\n--- Value Change Detected ---");
    Console.WriteLine($"  Path:      {e.NewVariable.FullPath}");
    Console.WriteLine($"  Old Value: {e.OldVariable.Value ?? "null"}");
    Console.WriteLine($"  New Value: {e.NewVariable.Value ?? "null"}");
    Console.ResetColor();
}
```

## 📖 Documentation
- **[Manual](./MANUAL.md)**: A small manual on how to use this library.
- **[IS7Service Reference](./S7UaLib/Services/IS7Service.cs)**: The `IS7Service` interface is the primary entry point and contract for all top-level operations.
- **[Example Project](./S7UaLib.Example/Program.cs)**: A runnable console application demonstrating library usage in more detail.
- **[Integration Tests](./S7UaLib.IntegrationTests/Services/S7ServiceIntegrationTests.cs)**: These tests showcase real-world usage patterns against a live S7-1500 PLC and serve as excellent, practical examples.

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