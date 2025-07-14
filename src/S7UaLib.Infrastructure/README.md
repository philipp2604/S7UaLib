# S7UaLib.Infrastructure ⚙️
The internal engine room for the S7UaLib ecosystem. This library provides the concrete implementations for the abstractions defined in `S7UaLib.Core`. It brings the data models and contracts to life with a robust OPC UA client, a rich set of data type converters, an in-memory data store, and serialization logic.

This package contains the "how" – the logic for connecting, browsing, reading, writing, and converting data. It is a required dependency for the high-level `S7UaLib` package and is **not intended for direct use by end-users**.

[![.NET 8 (LTS) Build & Test](https://github.com/philipp2604/S7UaLib/actions/workflows/dotnet-8-build-and-test.yml/badge.svg)](https://github.com/philipp2604/S7UaLib/actions/workflows/dotnet-8-build-and-test.yml)
[![.NET 9 (Latest) Build & Test](https://github.com/philipp2604/S7UaLib/actions/workflows/dotnet-9-build-and-test.yml/badge.svg)](https://github.com/philipp2604/S7UaLib/actions/workflows/dotnet-9-build-and-test.yml)
[![Language](https://img.shields.io/badge/language-C%23-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![NuGet Version](https://img.shields.io/nuget/v/philipp2604.S7UaLib.Infrastructure.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/philipp2604.S7UaLib.Infrastructure/)
[![License](https://img.shields.io/badge/License-GPL_v2-blue.svg)](https://www.gnu.org/licenses/old-licenses/gpl-2.0.en.html)
[![GitHub issues](https://img.shields.io/github/issues/philipp2604/S7UaLib)](https://github.com/philipp2604/S7UaLib/issues)

## ✨ Key Features

-   **🔌 Robust OPC UA Client (`S7UaClient`)**: The heart of the library, providing a concrete implementation for all communication with the PLC. It manages sessions, handles automatic reconnection, and exposes low-level methods for browsing, reading, writing, and creating subscriptions.
-   **🔄 Comprehensive S7 Data Type Conversion**: A rich collection of type converters that seamlessly handle complex S7-specific formats.
    -   BCD-based types like `DATE_AND_TIME` and legacy `S5TIME`.
    -   Modern nanosecond-precision types like `DTL` and `LTIME`.
    -   Specialized types like `COUNTER` (BCD) and `DATE`.
    -   Meta-converters for handling `ARRAY`s of complex types.
-   **💾 Efficient In-Memory Data Store (`S7DataStore`)**: A thread-safe, in-memory cache for holding the entire discovered PLC structure. It enables fast, path-based variable lookups and uses immutable data patterns for updating variable state.
-   **📄 Advanced JSON Serialization (`S7StructureSerializer`)**: A purpose-built `System.Text.Json` configuration for saving and loading the complete, discovered PLC structure. It correctly handles the polymorphic nature of the data models (`IS7Variable`, `IS7StructureElement`, etc.).

## 🚀 Getting Started

### Installation

This package is a dependency for other `S7UaLib` libraries and will typically be installed automatically. However, you can install it manually using the .NET CLI:

```bash
dotnet add package philipp2604.S7UaLib.Infrastructure
```

## 🎯 What This Library Is (and Isn't)

-   **✔️ IT IS:** The concrete implementation layer. It contains the working code that communicates with the PLC and manages data.
-   **❌ IT IS NOT:** The public-facing API. Its components are complex and require careful coordination. For a simple, unified interface, you should use the main `S7UaLib` package, which orchestrates these components for you.

## 🤝 Contributing

Contributions are welcome! Please refer to the main [S7UaLib repository](https://github.com/philipp2604/S7UaLib) for guidelines on reporting issues or submitting pull requests.

## ⚖️ License

This project is licensed under the **GNU General Public License v2.0 (GPL-2.0)**.

This is a direct consequence of the dependency on the official `OPCFoundation.NetStandard.Opc.Ua` packages, which are licensed under GPL 2.0. Any project using S7UaLib.Infrastructure must therefore also comply with the terms of the GPL 2.0 license, which generally means that if you distribute your application, you must also make the source code available.

Please review the [license terms](https://www.gnu.org/licenses/old-licenses/gpl-2.0.en.html) carefully before integrating this library into your project.