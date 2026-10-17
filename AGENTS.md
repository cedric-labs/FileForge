# Instructions pour Agents IA & Standards d'Ingénierie Logicielle (AGENTS.md)

Ce document consacre les standards de qualité logicielle, les protocoles d'outillage obligatoires et les règles non négociables pour le projet **FileForge**. Tout agent intervenant sur cette base de code doit s'y conformer strictement.

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
- **Provisioning** : Dès l'installation du harnais qualité, s'assurer que le projet est référencé sur l'instance locale (`http://localhost:9000`), avec la clé projet `FileForge`.
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
   - [5/6] Exécution de 100 % des tests unitaires et d'architecture avec couverture OpenCover.
   - [6/6] Téléversement SonarQube et attente du Quality Gate (`PASSED`).
