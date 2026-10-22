# FileForge — Spécification & Documentation IA du Framework

Ce document est le manifeste technique destiné à l'agent IA et aux développeurs. Il récapitule l'ensemble des capacités, composants et contrats publics exposés par **FileForge**.

---

## 🎯 Périmètre & Responsabilité

FileForge est la bibliothèque partagée d'ingénierie système pour Windows dédiée aux **opérations d'E/S haute performance**, à la **synchronisation de répertoires**, à la **résolution de volumes physiques/réseau** et au **rendu de tableaux de bord en mode console**.

---

## 🧱 Composants & Capacités Détaillées

### 1. `FileForge.IO` — Transfert & Catalogue
- **`FileCopier`** (statique) :
  - `CopyFile(source, destination, bufferSize, displayProgress, verifyHash, cancellationToken)` : Copie par flux direct avec gestion coopérative de l'annulation et vérification optionnelle SHA-256.
  - `CompareFileHashes(file1, file2)` : Vérification d'intégrité par hachage cryptographique SHA-256 entre deux fichiers.
  - `ComputeFileHash(filePath, algorithm)` / `ComputeFileHashAsync(...)` : Calcul synchrone et asynchrone de l'empreinte hexadécimale d'un fichier physique (SHA-256 par défaut).
  - `ComputeHash(stream, algorithm)` / `ComputeHashAsync(...)` : Calcul de l'empreinte hexadécimale à partir d'un flux source `Stream`.
  - `VerifyFileHash(filePath, expectedHashHex, algorithm)` : Vérification d'intégrité d'un fichier par rapport à une empreinte hexadécimale attendue.
  - `ReadFileHeader(filePath, byteCount)` / `ReadFileHeaderAsync(...)` : Lecture non bloquante des $N$ premiers octets d'un fichier pour détection d'en-tête et signatures magiques.
  - `MatchesFileHeader(filePath, expectedHeader)` : Validation de la présence d'une signature binaire au début d'un fichier physique.
  - `CalculateOptimalBufferSize(fileSize)` : Calcul dynamique de la taille de buffer (de 64 Ko à 16 Mo) selon le volume du fichier.
  - `CopyMetadata(source, destination, includeAcl)` : Préservation des dates de création, modification, accès et attributs.
  - `CopyAcl(source, destination)` : Réplication des listes de contrôle d'accès de sécurité Windows.
- **`SyncCatalogRepository`** (statique) :
  - `LoadCatalog(catalogFilePath)` : Charge l'index binaire des fichiers sauvegardés en `Dictionary<string, string>`.
  - `SaveCatalog(catalog, catalogFilePath)` : Enregistre de façon atomique le catalogue de sauvegarde.
  - `MigrateLegacyBackupNames(destinationRoot)` : Renomme les dossiers d'anciennes versions en `Backup_YYYYMMDD_HHMM`.
  - `BootstrapInitialCatalog(sourcePath, destinationPath)` : Initialise un index rétroactif à partir d'une sauvegarde existante.
- **`FileOperationProgress`** : Modèle de progression d'opération de fichier (pourcentage, octets transférés, état d'achèvement).
- **`PersistentDelimitedFileWriter`** :
  - `InitializeAsync(cancellationToken)` : Initialise un fichier délimité (CSV, TSV) en mode partagé non-bloquant (`FileShare.ReadWrite`), écrit la ligne d'en-tête si le fichier est nouvellement créé, ou positionne le curseur en fin de flux.
  - `ReadExistingLinesAsync(cancellationToken)` : Lit de manière asynchrone l'ensemble des enregistrements préexistants (hors en-tête) pour hydrater les registres de dédoublonnage au démarrage.
  - `AppendLineAsync(line, cancellationToken)` : Ajoute une ligne au fil de l'eau avec `AutoFlush` et synchronisation thread-safe (`SemaphoreSlim`).
  - `EscapeField(value, delimiter)` : Échappement conforme RFC 4180 (prise en charge des virgules, retours à la ligne et guillemets doublés).
- **`ProtectedFileStore`** :
  - `ReadSecret()` / `WriteSecret(secret)` / `ClearSecret()` : Persistance locale sécurisée de secrets sur fichier chiffré (DPAPI `ProtectedData` en portée `CurrentUser` sous Windows, AES-256 avec IV préfixé en repli).
  - `EncryptAesWithEntropy(plainBytes, entropy)` / `DecryptAesWithEntropy(cipherData, entropy)` : Primitives de chiffrement symétrique AES-256 liées à une entropie applicative.

### 2. `FileForge.Sync` — Moteur de Synchronisation
- **`DirectorySynchronizer`** (statique) :
  - `CloneDirectory(source, destination, options, cancellationToken)` : Synchronisation miroir exacte 1:1 avec suppression des fichiers orphelins cibles.
  - `IncrementalBackup(source, destination, options, cancellationToken)` : Sauvegarde incrémentale créant un dossier daté `Backup_...` basé sur le catalogue.
  - `ParallelEnumerateFiles(directory, maxParallelism, cancellationToken)` : Découverte parallèle haute vitesse des fichiers et sous-répertoires.
  - `ForceDeleteDirectory(path)` : Suppression récursive robuste supprimant préalablement les attributs ReadOnly sur les fichiers et dossiers protégés.
- **`DirectorySyncOptions` & `DirectoryCloneOptions`** :
  - Gestion du parallélisme distinct pour la source, la destination et les transferts.
  - Chemins et masques exclus (`ExcludedPaths`, `ExcludedFilters`, `IncludedFilters`).

### 3. `FileForge.Storage` — Disques & Recommandations Matérielles
- **`VolumeIdentityResolver`** (statique) :
  - `GetDiskId(path)` : Extrait le numéro de série de volume Windows (`VOL:XXXXXXXX`) ou la racine réseau normalisée (`UNC:\\serveur\partage`).
  - `AreMatchingDiskIds(diskId1, diskId2)` : Comparaison insensible aux variations de casse ou de barres obliques.
  - `NormalizeDiskId(diskId)` : Harmonisation des formats de préfixes.
- **`HardwarePerformanceAdvisor`** (statique) :
  - `IsSsdDrive(path)` : Détecte si le lecteur sous-jacent est un SSD ou un disque magnétique traditionnel.
  - `CalculateOptimalParallelism(source, dest)` : Calcule le nombre idéal de threads de copie selon le type de stockage (SSD vs HDD) et le nombre de cœurs CPU.
  - `GetAvailableMemoryInGB()` : Interroge la mémoire vive physique disponible.

### 4. `FileForge.Paths` — Gestion des Chemins
- **`PathNormalizer`** (statique) :
  - `NormalizeLongPath(path)` : Préfixe les chemins longs avec `\\?\` ou `\\?\UNC\` pour dépasser la limite Windows MAX_PATH (260 caractères).
  - `RemoveLongPathPrefix(path)` : Supprime les préfixes techniques pour l'affichage utilisateur.

### 5. `FileForge.Terminal` — Rendu Console Rétro
- **`ConsoleDashboard`** :
  - Affiche un cadre double bordure (style Norton Commander) rafraîchi en temps réel sans scintillement.
  - Phases supportées : *Scanning* (inventaire), *UpToDate* (déjà à jour), *Copying* (transfert), *Completed* (terminé).
  - Barre de progression textuelle `[██████████░░░░░░░░░░]`.
  - Statistiques en temps réel : débit instantané, fichiers restants, temps écoulé / restant estimé.

---

## 📋 Protocole de Maintenance par l'Agent IA

Toute modification de FileForge doit respecter :
1. **Rétrocompatibilité :** Conserver toutes les méthodes publiques existantes. Utiliser des surcharges ou des paramètres optionnels.
2. **Qualité :** Zéro warning (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`), zéro pragma/suppress, `CancellationToken` systématique sur les I/O.
3. **Tests :** Nommage `MethodeCible_Scenario_ResultatAttendu`.
4. **Mise à jour :** Compléter immédiatement ce fichier `FRAMEWORK.md` en cas d'ajout d'une nouvelle capacité.
5. **Validation :** Valider par `./verify.ps1` (Quality Gate: PASSED).
