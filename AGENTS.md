# Instructions pour Agents IA & Standards d'Ingénierie Logicielle (AGENTS.md)

Ce document consacre les standards de qualité logicielle, les protocoles d'outillage obligatoires et les règles non négociables pour le projet **Anima**. Tout agent intervenant sur cette base de code doit s'y conformer strictement.

---

## 1. Protocoles d'Initialisation Obligatoires (Dès le Premier Contact)

### 1.1. Dépôt Git Automatisé
- **Vérification immédiate** : Dès la prise en charge d'un projet, vérifier si le répertoire est un dépôt Git (`git rev-parse --is-inside-work-tree`).
- **Initialisation systématique** : Si aucun dépôt n'existe :
  1. Exécuter `git init`.
  2. Déployer un `.gitignore` officiel et complet pour .NET (`bin/`, `obj/`, `.vs/`, `.sonarqube/`, `TestResults/`, logs, caches).
  3. Effectuer un commit initial propre.
- **Cycle de commit** : Chaque lot fonctionnel, refactoring ou correction validé par les tests doit faire l'objet d'un commit Git atomique suivant la convention *Conventional Commits* (`feat:`, `fix:`, `refactor:`, `test:`, `style:`, `chore:`).

### 1.2. Intégration SonarQube Systématique
- **SonarQube n'est pas optionnel** : Il constitue le juge de paix de la qualité du code.
- **Provisioning** : Dès l'installation du harnais qualité, s'assurer que le projet est référencé sur l'instance locale (`http://localhost:9000`), avec la clé projet `Anima`.
- **Validation continue** : Le script `./verify.ps1` orchestre l'analyse SonarScanner. Aucune tâche n'est considérée comme achevée tant que le statut retourné n'est pas **QUALITY GATE STATUS: PASSED**.

---

## 2. Règles d'Or du "Code Humain" & Propreté Logicielle

### Règle 1 : Zéro masquage d'avertissement
- **Interdiction absolue** de recourir aux directives et attributs de masquage :
  - `#pragma warning disable`
  - `// NOSONAR`
  - `[SuppressMessage]`
  - L'opérateur null-forgiving `!` (dans le code applicatif et métier).
- **Principe fondamental** : Traiter systématiquement la cause racine par un modèle de typage rigoureux (`Nullable<T>`, clauses de garde explicites `ArgumentNullException.ThrowIfNull`, vérifications par pattern matching `is not null`).

### Règle 2 : Zéro bloc `catch` muet
- **Interdiction formelle** des blocs d'exception silencieux (`catch { }` ou `catch (Exception ex) { }` sans action).
- Ne capturer **que** les exceptions typées explicitement anticipées (ex: `IOException`, `UnauthorizedAccessException`).
- En cas de capture, toujours :
  1. Consigner l'anomalie via une journalisation structurée (`ILogger` ou `RaiseLog`), **ou**
  2. Documenter expressément dans le bloc la raison impérieuse pour laquelle l'exception est tolérée.

### Règle 3 : Zéro classe fourre-tout
- **Interdiction** de créer ou maintenir des classes, répertoires ou namespaces portant des suffixes ou préfixes génériques :
  - `*Helper*`
  - `*Utils*`
  - `*Manager*`
  - `*Common*`
- Chaque composant doit porter un nom reflétant fidèlement sa responsabilité métier unique :
  - `*Calculator` (calculs de métriques, algorithmes prédictifs)
  - `*Validator` (règles de conformité, validation de disques)
  - `*Parser` / `*Formatter` / `*Serializer` (transformation de formats, XML, texte)
  - `*Provider` / `*Resolver` (résolution d'identifiants, découverte système)
  - `*Advisor` (recommandations matérielles, heuristiques)
  - `*Dashboard` / `*Reporter` (rendu d'interface et affichage console)

### Règle 4 : Pas de découpage cosmétique
- Lorsqu'une méthode présente une complexité cyclomatique ou cognitive élevée, ne jamais la saucissonner artificiellement en fragments techniques vides de sens (`Part1`, `Part2`, `SubMethodA`).
- Extraire des abstractions métier cohérentes : un composant dédié, un Value Object, une stratégie polymorphique ou un pipeline fonctionnel clair.

### Règle 5 : Lisibilité humaine & Commentaires du "Pourquoi"
- **Clauses de garde** : Valider les préconditions dès le début des méthodes (`if (condition) return ...;`) afin de limiter l'arborescence d'indentation à un maximum de 2 niveaux.
- **Immuabilité par défaut** : Préférer les structures immuables (`readonly`, `record`, accesseurs `init`, collections en lecture seule `IReadOnlyList<T>`).
- **Commentaires utiles** :
  - Ne **JAMAIS** paraphraser le code (le code doit être expressif et auto-documenté).
  - Les commentaires doivent expliquer exclusivement le **POURQUOI** : décisions d'architecture non intuitives, contournements de bugs de l'OS/terminal, exigences de performance, invariants métier critiques.

### Règle 6 : Zéro marqueur lexical d'IA (Nommage sobre et factuel)
- **Interdiction formelle** d'utiliser des qualificatifs mélioratifs, vagues ou des tics de langage d'IA dans le nommage des méthodes, classes, interfaces, variables, commits et commentaires :
  - `*Robust*`
  - `*Resilient*`
  - `*Seamless*`
  - `*Smart*`
  - `*Advanced*`
  - `*Comprehensive*`
  - `*Ultimate*`
- **Principe fondamental** : Nommer par **l'intention technique factuelle** et non par une auto-évaluation subjective de la qualité du code :
  - ❌ `DeleteDirectoryRobust` ➔ ✔️ `ForceDeleteDirectory` ou `DeleteDirectoryWithRetries`
  - ❌ `RobustCopier` ➔ ✔️ `FileCopier`
  - ❌ `SeamlessRunner` ➔ ✔️ `SequentialTaskRunner`

### Règle 7 : Standard de nommage des tests (`NomDeMethode_Condition_ResultatAttendu`)
- **Nomenclature obligatoire** : Tout test unitaire ou d'intégration doit respecter le motif `MethodeCible_ScenarioOuCondition_ResultatAttendu` (ex. `Resolve_WhenUncPathHasDoubleBackslash_ReturnsNormalizedDiskId`, `CopyAsync_WhenTargetIsReadOnly_ThrowsUnauthorizedAccessException`).
- **Principe fondamental** : Un échec de test dans le rapport CI / SonarQube doit se lire comme une spécification métier défaillante sans obliger à inspecter le corps du test.

### Règle 8 : Typage fort & Refus de la Primitive Obsession
- **Interdiction** de faire circuler des types primitifs (`string`, `int`, `long`) pour manipuler des concepts métier porteurs de règles de validation, d'invariants ou de formats spécifiques (ex. identifiants de volume, chemins normalisés, empreintes de hachage).
- **Principe fondamental** : Encapsuler ces concepts dans des Value Objects immuables (`readonly record struct` ou `record class` scellé) validés dès leur instanciation.

### Règle 9 : Découplage de l'Infrastructure & Inversion de dépendance
- Le cœur métier et applicatif ne doit **jamais** dépendre directement de l'infrastructure concrète (appels directs à `Console.*`, accès bruts au disque sans abstraction, APIs natives de l'OS).
- Tout accès aux ressources externes ou périphériques doit être injecté via des contrats d'abstraction clairs (`ILogger`, `IFileSystem`, `IVolumeResolver`), garantissant la testabilité unitaire sans contournements complexes.

### Règle 10 : Asynchronisme propre & Annulation coopérative
- **Propagation du jeton** : Toute méthode asynchrone ou réalisant des opérations d'I/O doit systématiquement accepter un paramètre `CancellationToken cancellationToken = default` et le relayer à l'ensemble des méthodes descendantes.
- **Respect de l'annulation** : Ne jamais ignorer un jeton d'annulation ni avaler une `OperationCanceledException` sans la relancer, sauf terminaison gracieuse explicitement documentée.

### Règle 11 : Constructeurs primaires C# 12 pour l'injection
- **Syntaxe idiomatique** : Utiliser systématiquement la syntaxe des constructeurs primaires (`primary constructors`) pour déclarer et injecter les dépendances des classes et services (ex. `public sealed class VolumeSnapshotProvider(ILogger logger, IFileSystem fileSystem) : IVolumeSnapshotProvider`).
- **Élimination du boilerplate** : Ne pas déclarer de champs privés `private readonly` redondants lorsque le constructeur primaire suffit. Valider les préconditions si nécessaire dès l'initialisation.

### Règle 12 : Documentation XML rigoureuse des contrats publics
- **Contrats d'interface documentés** : Toute interface publique, méthode d'interface, énumération publique et propriété de contrat doit comporter une documentation XML (`///`) complète (`<summary>`, `<param>`, `<returns>`, `<exception>`).
- **Zéro bruit sur le code interne** : Ne pas polluer les méthodes privées, locales ou évidentes avec des commentaires XML mécaniques. Réserver la documentation XML aux points d'entrée et abstractions du système.

### Règle 13 : Couverture E2E & Tests IHM obligatoires avec Playwright (Projets Web)
- **Périmètre d'application** : Dès lors qu'un projet est de nature Web (Blazor, ASP.NET Core MVC / Razor Pages, ou UI Web servie par .NET), l'installation automatique de **Playwright** et la mise en place d'une suite de tests E2E dédiée sont obligatoires.
- **Exhaustivité du périmètre IHM couvert** :
  - **Affichage & Rendu structurel** : Vérification de la présence, de la visibilité et du bon agencement des conteneurs, tableaux, formulaires, barres de navigation et éléments visuels.
  - **Interactivité complète** : Déclenchement et validation du bon fonctionnement de 100 % des boutons, liens de navigation, champs de saisie, cases à cocher, menus déroulants et fenêtres modales.
  - **Intégrité et exactitude des données** : Assertion explicite sur l'exactitude des valeurs affichées à l'écran (textes calculés, montants, compteurs, labels dynamiques, messages de confirmation ou d'erreur).
- **Architecture Page Object Model (POM) obligatoire** :
  - Interdiction formelle de coder des sélecteurs CSS ou XPath bruts directement dans les méthodes de test.
  - Encapsuler chaque page ou composant réutilisable dans une classe Page Object dédiée (ex. `LoginPage`, `DashboardPage`, `NavbarComponent`) exposant des méthodes d'action métier et des accesseurs typés.
- **Nomenclature stricte des tests** : Respect obligatoire du motif de la Règle 7 (ex. `DashboardPage_WhenLoaded_DisplaysAccurateCounterValues`, `ExportButton_WhenClicked_TriggersDownload`).
- **Exécution automatisée** : Les tests Playwright doivent pouvoir s'exécuter de façon autonome en mode headless (sans interface graphique visible) lors du pipeline `./verify.ps1`.

### Règle 14 : Réutilisation systématique & Capitalisation dans les Frameworks partagés
- **Réflexe préalable d'inspection** : Avant d'implémenter un composant technique, un utilitaire, une opération d'I/O, du hachage, un algorithme ou du rendu terminal non spécifique au métier, inspecter le catalogue central des frameworks partagés (`D:\Dev\Framework\FRAMEWORKS_INDEX.md`).
- **Interdiction formelle de duplication** : Ne jamais réécrire localement une brique déjà présente dans un framework partagé. Référencer le projet concerné (`<ProjectReference>`).
- **Évolution rétrocompatible & Surcharges** : Si une fonctionnalité d'un framework doit être enrichie ou adaptée :
  1. Faire évoluer le composant directement dans le framework hôte via des surcharges de méthodes ou des paramètres optionnels.
  2. Préserver strictement les contrats existants pour ne provoquer aucun breaking change pour les autres projets consommateurs.
- **Création d'un nouveau Framework & Validation du nom** : Si une fonctionnalité générique ne correspond à aucun framework existant et nécessite d'en créer un nouveau, l'agent a l'interdiction de créer le projet unilatéralement. Il doit obligatoirement proposer 2 ou 3 suggestions de noms sobres et factuels (sans buzzwords d'IA) à l'utilisateur et attendre sa validation explicite avant d'initialiser le framework.
- **Mise à jour documentaire & Validation obligatoire** :
  1. Mettre à jour immédiatement le fichier `FRAMEWORK.md` du framework concerné dès qu'une capacité est ajoutée ou modifiée.
  2. Exécuter `./verify.ps1` à la racine du framework et s'assurer que la Quality Gate SonarQube est **PASSED** avant d'intégrer le composant dans l'application cliente.

---

## 3. Harnais d'Outillage Automatisé

Le projet s'appuie sur une suite automatisée de contrôles stricts déclarés au niveau solution :

1. **Analyseurs Roslyn globaux (`Directory.Build.props`)** :
   - `Roslynator.Analyzers` (bonnes pratiques et refactorings C# modernes).
   - `Meziantou.Analyzer` (détection des pièges d'APIs, sécurité et performances .NET).
   - `AsyncFixer` (détection des anti-patterns asynchrones et deadlocks).
   - `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` (le moindre avertissement bloque la génération).

2. **Style & Formatage (`.editorconfig`)** :
   - Encodage `UTF-8`, fins de ligne `CRLF`, indentation 4 espaces.
   - Espaces de noms en bloc de fichier (`file-scoped namespaces`).
   - Tri automatique des directives `using` (System en tête).
   - `dotnet format --verify-no-changes` exécuté systématiquement.

3. **Pipeline de validation unique (`verify.ps1`)** :
   Le script racine `./verify.ps1` exécute la chaîne complète et constitue l'unique condition d'acceptation d'un développement :
   - [1/6] Diagnostic & restauration des outils.
   - [2/6] Conformité du formatage (`dotnet format`).
   - [3/6] Initialisation SonarScanner.
   - [4/6] Compilation Release sans incrémentation avec avertissements traités en erreurs (`-warnaserror`).
   - [5/6] Exécution de 100 % des tests unitaires, d'architecture et E2E Playwright (mode headless) avec couverture OpenCover.
   - [6/6] Téléversement SonarQube et attente du Quality Gate (`PASSED`).
