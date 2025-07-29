# S7UaLib.Core 🧱
The foundational library for the S7UaLib ecosystem. This package provides the core data models, interfaces, and enumerations used for representing a Siemens S7 PLC's structure via OPC UA. It is the shared kernel that enables all other `S7UaLib` packages to work together seamlessly.

This library defines *what* a variable, a data block, or a PLC memory area is, using modern, immutable C# records and interfaces. It is a required dependency for other S7UaLib packages and is typically not used directly by end-users.

[![.NET 8 (LTS) Build & Test](https://github.com/philipp2604/S7UaLib/actions/workflows/dotnet-8-build-and-test.yml/badge.svg)](https://github.com/philipp2604/S7UaLib/actions/workflows/dotnet-8-build-and-test.yml)
[![.NET 9 (Latest) Build & Test](https://github.com/philipp2604/S7UaLib/actions/workflows/dotnet-9-build-and-test.yml/badge.svg)](https://github.com/philipp2604/S7UaLib/actions/workflows/dotnet-9-build-and-test.yml)  
[![Language](https://img.shields.io/badge/language-C%23-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![License](https://img.shields.io/badge/License-GPL_v2-blue.svg)](https://www.gnu.org/licenses/old-licenses/gpl-2.0.en.html)
[![GitHub issues](https://img.shields.io/github/issues/philipp2604/S7UaLib)](https://github.com/philipp2604/S7UaLib/issues)  
[![NuGet Version](https://img.shields.io/nuget/v/philipp2604.S7UaLib.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/philipp2604.S7UaLib/)
[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/philipp2604/S7UaLib)

## ✨ Key Features

-   **🏗️ Immutable Data Models**: All PLC structural elements (`S7Variable`, `S7DataBlockGlobal`, etc.) are implemented as C# `record` types, ensuring thread safety and predictable behavior.
-   **🏛️ Comprehensive PLC Structure Contracts**: Defines a rich set of interfaces (`IS7Variable`, `IS7DataBlockInstance`, `IS7StructureElement`) that accurately model the entire S7 OPC UA server layout, including:
    -   Global Data Blocks (`DB`) and Instance Data Blocks (`iDB`)
    -   Inputs (`I`), Outputs (`Q`), Memory (`M`), Timers (`T`), and Counters (`C`)
    -   Nested structures and complex types (UDTs)
-   **📋 Rich S7 Data Type Enumeration**: Provides the `S7DataType` enum, which covers a wide range of Siemens-specific types, from `BOOL` and `INT` to `DTL` and `S5TIME`.
-   **🔔 Core Event Definitions**: Includes the essential `VariableValueChangedEventArgs` class, providing a standardized way to communicate data changes throughout the ecosystem.
-   **🔄 Type Converter Abstraction**: Defines the `IS7TypeConverter` interface, establishing a contract for converting between raw OPC UA values and user-friendly .NET types.
-   **⚙️ Simplified OPC UA Configuration Models**: Contains user-friendly classes like `ApplicationConfiguration` to abstract away some of the complexities of OPC UA setup.

## 🚀 Getting Started

### Installation

This package is a dependency for other `S7UaLib` libraries and will typically be installed automatically. However, you can install it manually using the .NET CLI:

```bash
dotnet add package philipp2604.S7UaLib.Core
```

## 🎯 What This Library Is (and Isn't)

-   **✔️ IT IS:** A library of contracts and data models. It defines the "shared language" for the S7UaLib ecosystem.
-   **❌ IT IS NOT:** An active client. This library **does not** contain logic to connect to a PLC, browse the server, read/write values, or manage subscriptions. For that functionality, you need a full implementation package like `S7UaLib`.

## 🤝 Contributing

Contributions are welcome! Please refer to the main [S7UaLib repository](https://github.com/philipp2604/S7UaLib) for guidelines on reporting issues or submitting pull requests.

## ⚖️ License

This project is licensed under the **GNU General Public License v2.0 (GPL-2.0)**.

This is a direct consequence of the dependency on the official `OPCFoundation.NetStandard.Opc.Ua` packages, which are licensed under GPL 2.0. Any project using S7UaLib.Core must therefore also comply with the terms of the GPL 2.0 license, which generally means that if you distribute your application, you must also make the source code available.

Please review the [license terms](https://www.gnu.org/licenses/old-licenses/gpl-2.0.en.html) carefully before integrating this library into your project.