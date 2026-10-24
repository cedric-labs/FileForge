# FileForge

High-performance .NET system engineering library for thread-safe streaming I/O, RFC 4180 delimited file writing, volume resolution, hardware performance detection, and directory synchronization.

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Framework](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)

---

## Key Capabilities

### 1. High-Performance Streaming I/O & Cryptography (FileForge.IO)
* **FileCopier**: Stream-based direct copy engine supporting cooperative cancellation (CancellationToken), dynamic buffer size calculation (64 KB to 16 MB), metadata/ACL preservation, and cryptographic integrity verification.
* **PersistentDelimitedFileWriter**: Thread-safe delimited file writer (CSV, TSV) with non-blocking shared access (FileShare.ReadWrite), dynamic deduplication cache rehydration, and RFC 4180 compliance (automatic escaping of commas, quotes, and line breaks).
* **ProtectedFileStore**: Secure local secret storage using Windows DPAPI (ProtectedData under CurrentUser) with AES-256 fallback and application-specific entropy.
* **Cryptographic Hashing**: Synchronous and asynchronous SHA-256 / SHA-512 hashing for disk files, memory streams, and magic byte file header inspections.

### 2. Physical & Network Storage Resolution (FileForge.Storage)
* **VolumeIdentityResolver**: Extracts and normalizes Windows physical volume serial numbers (VOL:XXXXXXXX) and network root shares (UNC:\\server\share), enabling reliable disk identification across drive letter changes.
* **HardwarePerformanceAdvisor**: Queries disk hardware characteristics (SSD vs. HDD magnetic media), calculates optimal copy thread parallelism based on CPU core counts and storage bus throughput, and evaluates available physical RAM.

### 3. Directory Synchronization Engine (FileForge.Sync)
* **DirectorySynchronizer**: Exact 1:1 directory mirroring with orphan deletion, incremental catalog-driven backups, and high-speed parallel file discovery.

---

## Installation

Add the package via the .NET CLI:

`powershell
dotnet add package FileForge.Core --version 1.0.2
`

---

## Code Example

`csharp
using FileForge.Core.IO;

// Thread-safe RFC 4180 CSV writer with non-blocking shared access
await using var writer = new PersistentDelimitedFileWriter("audit_log.csv", delimiter: ",");
await writer.InitializeAsync();

// Append record safely across concurrent threads
await writer.AppendLineAsync(["2026-10-24T12:00:00Z", "10.10.1.5", "443", "nginx", "TCP"]);
`

---

## License

This project is licensed under the [MIT License](LICENSE).