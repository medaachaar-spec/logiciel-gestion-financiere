# DASHBOARD V2 — STARTUP FAILURE DIAGNOSIS

Date : 2026-09-09  
Repository : `C:\Users\INPT\Dev\logiciel-gestion-financiere`

## 1. Exact Windows user

`MED-AMINE\INPT`  
SID : `S-1-5-21-1929670590-1094964854-1901593636-1022`

## 2. Branch / hash / tree

- Branch : `feature/dashboard-v2`
- HEAD : `fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba`
- `git diff --check` : PASS, aucune erreur
- Modifications Dashboard V2 préexistantes conservées, non indexées
- Aucun `git add`, commit, push, merge, reset, rebase ou clean exécuté

## 3. Exact underlying exception

Exception SQLite reproduite sur une copie jetable avec les mêmes restrictions sur les fichiers DB/WAL/SHM :

`Microsoft.Data.Sqlite.SqliteException: SQLite Error 8: 'attempt to write a readonly database'.`

Cause Windows sous-jacente confirmée sans écrire :

`System.UnauthorizedAccessException: Access to the path 'C:\ProgramData\FinancialManagement\Data\FinancialManagement.db' is denied.`

Cette seconde exception est levée dès qu'un handle `FileAccess.Write` est demandé sur le fichier existant. Aucun octet n'a été écrit.

## 4. Failing code path

Chemin exact :

1. `App.OnStartup`
2. `ApplicationPaths` résout `C:\ProgramData\FinancialManagement\Data\FinancialManagement.db`
3. `BuildServiceProvider` réussit
4. `MigrateDatabaseAsync`
5. résolution de `FinancialManagementDbContext`
6. `FinancialDatabaseStartup.InitializeAsync`
7. `OpenConnectionAsync` : PASS
8. `FinancialStorageValidator.ValidateAsync(..., allowEmpty: true)` : PASS
9. `Database.MigrateAsync` tente l'acquisition/écriture du verrou de migration SQLite
10. échec SQLite code 8, base en lecture seule pour `INPT`
11. le premier `catch` de `App.OnStartup` affiche `Impossible d'initialiser les données de l'application.` puis ferme l'application

Même avec zéro migration en attente, EF Core doit écrire dans le mécanisme de verrouillage de migration. La table `__EFMigrationsLock` existe déjà, mais ses opérations de verrouillage exigent un accès en modification.

## 5. Failing file/path/resource

Objets refusant la modification sous `INPT` :

- `C:\ProgramData\FinancialManagement\Data\FinancialManagement.db`
- `C:\ProgramData\FinancialManagement\Data\FinancialManagement.db-wal`
- `C:\ProgramData\FinancialManagement\Data\FinancialManagement.db-shm`

Les trois fichiers appartiennent à `MED-AMINE\HP`. `MED-AMINE\HP`, SYSTEM et Administrateurs ont le contrôle total ; `BUILTIN\Utilisateurs`, dont `INPT`, n'a que `ReadAndExecute` sur les fichiers existants.

Les sauvegardes et le fichier `.daily-backup-20260909.guard` présentent le même profil : propriétaire `HP`, fichiers existants en lecture/exécution seulement pour `INPT` via le groupe Utilisateurs.

## 6. Was Dashboard instantiated before failure?

**NO.**

Le Dashboard est enregistré via `Lazy<DashboardViewModel>`. Il n'est résolu qu'à l'initialisation du Shell, après le démarrage base de données, le bootstrap éventuel et la connexion. L'échec se produit dans le premier bloc de démarrage base de données, avant `ShowFirstRunAsync`, `ShowLogin` et `ShowShellAsync`.

Classification temporelle : **B — during database startup**.

Il ne se produit pas pendant la création du service Dashboard, du `DashboardViewModel`, le chargement de LiveCharts/SkiaSharp ou le rendu WPF.

## 7. ACL findings

### `C:\ProgramData\FinancialManagement`

- SYSTEM : Full Control, hérité
- Administrateurs : Full Control, hérité
- `MED-AMINE\INPT` : Full Control sur ce dossier uniquement, sans `(OI)(CI)` ; ce droit ne descend donc pas vers `Data`, `Backups` ni leurs fichiers
- Utilisateurs : lecture/exécution, plus droits de création sur les dossiers

### `Data` et `Backups`

- Propriétaire : `MED-AMINE\HP`
- `HP` : Full Control
- Utilisateurs : lecture/exécution et création de nouveaux fichiers/sous-dossiers
- `INPT` peut lire les dossiers et créer un nouvel objet, mais ne peut pas modifier les fichiers existants créés par `HP`

### Fichiers existants

- DB, WAL, SHM, sauvegardes et guard : Utilisateurs = `ReadAndExecute` seulement
- Test d'ouverture en écriture de la DB : `UnauthorizedAccessException`
- Ouverture SQLite en lecture : PASS
- Lecture de `__EFMigrationsHistory` : PASS, 4 lignes
- Validation de stockage financier exacte : PASS
- Migrations en attente : 0
- DB réelle inchangée après tous les diagnostics : taille et `LastWriteTimeUtc` identiques

Conclusion ACL : le droit manquant est **Modify** sur les fichiers SQLite existants et leurs sidecars, ainsi que l'héritage de ce droit pour les futurs fichiers de `Data`. `Backups` a le même défaut pour les opérations ultérieures de sauvegarde/guard, même si le bloc de démarrage observé échoue d'abord sur `Data`.

## 8. LiveCharts / SkiaSharp findings

- `LiveChartsCore.SkiaSharpView.WPF.dll` est présent dans la sortie Desktop.
- `SkiaSharp.dll`, `SkiaSharp.Views.WPF.dll` et les bibliothèques natives `libSkiaSharp.dll` x64/x86/arm64 sont présentes.
- L'exécutable Desktop et toutes les dépendances ont été produits par le build validé.
- Aucun type Dashboard n'est instancié et aucune surface LiveCharts n'est rendue avant l'échec.
- Le message affiché provient exclusivement du bloc d'initialisation des données, pas du bloc général de démarrage/UI.

Conclusion : aucune indication de défaillance LiveCharts/SkiaSharp ; ces composants ne sont pas sur le chemin fautif.

## 9. TFM findings

- TFM : `net10.0-windows10.0.19041`
- Machine : Windows 10 64 bits
- Runtime installé : `Microsoft.WindowsDesktop.App 10.0.10`
- Build précédent : PASS, 0 warning, 0 error

Conclusion : le TFM Windows 10 est satisfait et n'est pas la cause.

## 10. Database startup findings

Diagnostic en lecture seule contre ProgramData :

- connexion SQLite : PASS
- lecture du schéma : PASS
- migrations appliquées : 4
- migrations en attente : 0
- `FinancialStorageValidator` : `true`
- table `__EFMigrationsLock` : présente
- accès modification fichier DB : DENIED

Reproduction sûre dans `%TEMP%` :

- copie jetable avec droits normaux + appel exact `FinancialDatabaseStartup.InitializeAsync` : PASS
- copie jetable dont DB/WAL/SHM sont en lecture seule + même appel : SQLite Error 8
- fichiers jetables déclarés inchangés après le scénario en lecture seule

Répertoire de diagnostic jetable laissé hors dépôt :

`C:\Users\INPT\AppData\Local\Temp\DashboardStartupDiagnosis-64de419a8e694b8d95f6195d4a1750bd`

Aucune restauration, migration manuelle ou modification financière n'a été effectuée sur ProgramData.

## 11. Root cause classification

**ENVIRONMENT / ACL**

La base et ses sidecars ont été créés/manipulés par `HP` avec des ACL de fichier non modifiables par `INPT`. Le Dashboard V2 n'est pas construit avant l'échec et n'a pas modifié le chemin commun de démarrage base de données.

## 12. Source modified

**NO.**

Aucun fichier source Dashboard ou partagé n'a été modifié pendant ce diagnostic. Seul ce rapport non suivi a été ajouté. Les fichiers temporaires de diagnostic sont hors dépôt.

## 13. Exact files modified by this diagnosis

- `DASHBOARD-V2-STARTUP-DIAGNOSIS.md` — nouveau rapport local/non suivi uniquement

## 14. Safe remediation required

À exécuter par le lead dans un terminal élevé, après validation. **Ces commandes n'ont pas été exécutées.**

Minimum nécessaire pour démarrer et utiliser la base existante sous `INPT` :

```powershell
icacls "C:\ProgramData\FinancialManagement\Data" /grant "MED-AMINE\INPT:(OI)(CI)(M)" /T
```

Correction ciblée recommandée pour éviter le même problème lors des sauvegardes/guards existants :

```powershell
icacls "C:\ProgramData\FinancialManagement\Backups" /grant "MED-AMINE\INPT:(OI)(CI)(M)" /T
```

Portée : uniquement les deux sous-dossiers applicatifs. Droits : Modify, pas Full Control. Aucun droit `Everyone`, aucune prise de possession et aucune modification de `C:\ProgramData` au sens large.

Après application, vérifier :

```powershell
icacls "C:\ProgramData\FinancialManagement\Data\FinancialManagement.db"
icacls "C:\ProgramData\FinancialManagement\Data\FinancialManagement.db-wal"
icacls "C:\ProgramData\FinancialManagement\Data\FinancialManagement.db-shm"
```

Puis relancer normalement l'application, sans mode Administrateur permanent.

## 15. ProgramData DB modified

**NO.**

Les contrôles ont été limités à l'ouverture/lecture, aux métadonnées et aux ACL. Taille et date de dernière écriture de la DB ont été contrôlées avant/après et sont restées identiques.

## 16. Final result

**ENVIRONMENT FIX NEEDED**
