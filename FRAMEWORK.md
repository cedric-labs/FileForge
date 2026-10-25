<p align="right">
  <b>English</b> | <a href="FRAMEWORK.fr.md">Français</a>
</p>

# FileForge — Framework Specification & AI Documentation

This document is the technical manifest intended for AI agents and software engineers. It outlines all capabilities, architectural components, and public contracts exposed by **FileForge**.

---

## 🎯 Scope & Responsibility

FileForge is a shared Windows system engineering library dedicated to **high-performance streaming I/O**, **directory synchronization**, **physical and network volume resolution**, and **retro console dashboards**.

---

## 🧱 Components & Detailed Capabilities

### 1. `FileForge.IO` — Streaming Transfers, Integrity & Catalogs
- **`FileCopier`** (static):
  - `CopyFile(source, destination, bufferSize, displayProgress, verifyHash, cancellationToken)`: Direct stream-based file copy with cooperative cancellation and optional SHA-256 integrity verification.
  - `CompareFileHashes(file1, file2)`: Cryptographic SHA-256 integrity check between two files.
  - `ComputeFileHash(filePath, algorithm)` / `ComputeFileHashAsync(...)`: Synchronous and asynchronous hexadecimal file hashing (SHA-256 by default).
  - `ComputeHash(stream, algorithm)` / `ComputeHashAsync(...)`: Hexadecimal hash generation from a `Stream`.
  - `VerifyFileHash(filePath, expectedHashHex, algorithm)`: File integrity verification against an expected hexadecimal digest.
  - `ReadFileHeader(filePath, byteCount)` / `ReadFileHeaderAsync(...)`: Non-blocking read of the first $N$ bytes for magic number header detection.
  - `MatchesFileHeader(filePath, expectedHeader)`: Validates whether a file begins with an expected binary header sequence.
  - `CalculateOptimalBufferSize(fileSize)`: Dynamic buffer size calculation (from 64 KB up to 16 MB) proportional to file size.
  - `CopyMetadata(source, destination, includeAcl)`: Preserves creation, modification, and access timestamps along with file attributes.
  - `CopyAcl(source, destination)`: Replicates Windows NTFS Access Control Lists (ACLs).
- **`SyncCatalogRepository`** (static):
  - `LoadCatalog(catalogFilePath)`: Loads the binary backup catalog into a `Dictionary<string, string>`.
  - `SaveCatalog(catalog, catalogFilePath)`: Atomically persists the backup index catalog.
  - `MigrateLegacyBackupNames(destinationRoot)`: Renames legacy backup folders to `Backup_YYYYMMDD_HHMM`.
  - `BootstrapInitialCatalog(sourcePath, destinationPath)`: Initializes a retroactive catalog from existing backup targets.
- **`FileOperationProgress`**: File operation progress model (percentage, bytes transferred, completion state).
- **`PersistentDelimitedFileWriter`**:
  - `InitializeAsync(cancellationToken)`: Initializes a delimited file (CSV, TSV) in non-blocking shared mode (`FileShare.ReadWrite`), writes headers on creation, or positions the pointer at the end of stream.
  - `ReadExistingLinesAsync(cancellationToken)`: Asynchronously reads existing non-header lines to hydrate deduplication caches on startup.
  - `AppendLineAsync(line, cancellationToken)`: Thread-safe streaming line appender with `AutoFlush` and `SemaphoreSlim` synchronization.
  - `EscapeField(value, delimiter)`: RFC 4180-compliant field escaping (handling commas, line breaks, and escaped double quotes).
- **`ProtectedFileStore`**:
  - `ReadSecret()` / `WriteSecret(secret)` / `ClearSecret()`: Secure local secret storage on encrypted files (DPAPI `ProtectedData` scoped to `CurrentUser` on Windows, AES-256 with prefixed IV as fallback).
  - `EncryptAesWithEntropy(plainBytes, entropy)` / `DecryptAesWithEntropy(cipherData, entropy)`: Symmetric AES-256 encryption primitives bound to application-specific entropy.

### 2. `FileForge.Sync` — Directory Synchronization Engine
- **`DirectorySynchronizer`** (static):
  - `CloneDirectory(source, destination, options, cancellationToken)`: Exact 1:1 directory mirroring with target orphan pruning.
  - `IncrementalBackup(source, destination, options, cancellationToken)`: Incremental backup producing dated `Backup_...` folders based on the state catalog.
  - `ParallelEnumerateFiles(directory, maxParallelism, cancellationToken)`: High-speed parallel discovery of files and subdirectories.
  - `ForceDeleteDirectory(path)`: Robust recursive deletion automatically clearing ReadOnly attributes on protected files and folders beforehand.
- **`DirectorySyncOptions` & `DirectoryCloneOptions`**:
  - Independent parallelism configuration for source, destination, and transfer workers.
  - Exclusion paths and pattern filters (`ExcludedPaths`, `ExcludedFilters`, `IncludedFilters`).

### 3. `FileForge.Storage` — Physical Disks & Hardware Heuristics
- **`VolumeIdentityResolver`** (static):
  - `GetDiskId(path)`: Extracts the Windows volume serial number (`VOL:XXXXXXXX`) or normalized network root (`UNC:\\server\share`).
  - `AreMatchingDiskIds(diskId1, diskId2)`: Case- and slash-insensitive volume identifier comparison.
  - `NormalizeDiskId(diskId)`: Normalizes prefix representations across platforms.
- **`HardwarePerformanceAdvisor`** (static):
  - `IsSsdDrive(path)`: Detects whether the underlying drive is an SSD or a rotational magnetic HDD.
  - `CalculateOptimalParallelism(source, dest)`: Calculates optimal worker thread count based on storage media (SSD vs. HDD) and available CPU cores.
  - `GetAvailableMemoryInGB()`: Queries physical available system RAM.

### 4. `FileForge.Paths` — Path Management & Normalization
- **`PathNormalizer`** (static):
  - `NormalizeLongPath(path)`: Prefixes long paths with `\\?\` or `\\?\UNC\` to bypass the Windows 260-character `MAX_PATH` limit.
  - `RemoveLongPathPrefix(path)`: Strips technical prefixes for user-facing displays and logs.

### 5. `FileForge.Terminal` — Retro Console Dashboards
- **`ConsoleDashboard`**:
  - Displays a flicker-free double-border terminal frame (Norton Commander-inspired) refreshed in real-time.
  - Supported phases: *Scanning*, *UpToDate*, *Copying*, *Completed*.
  - Text progress bars: `[██████████░░░░░░░░░░]`.
  - Real-time statistics: instant transfer throughput, pending file counts, elapsed / estimated remaining time.

---

## 📋 Maintenance Protocol for AI Agents

All enhancements to FileForge must comply with:
1. **Backward Compatibility:** Preserve existing public signatures. Introduce overloads or optional parameters.
2. **Code Cleanliness:** Zero warnings (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`), zero pragmas/suppressions, systematic `CancellationToken` on all I/O methods.
3. **Testing Standards:** Test naming convention `TargetMethod_Scenario_ExpectedResult`.
4. **Documentation:** Update both `FRAMEWORK.md` and `FRAMEWORK.fr.md` immediately upon introducing or modifying capabilities.
5. **Validation:** Full pass of `./verify.ps1` (SonarQube Quality Gate: PASSED).
