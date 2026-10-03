SYNC SUCCESSFUL: YES

SYNCHRONIZED BASELINE: `1ddc377a5d2162536cd73ac056d55490847fae11`

ASTRA-004 BRANCH: `fix/astra-004-allocation-integrity`

Rapport de travail pour revue lead. Aucun commit, indexage ou push. Validation finale terminée ; détails ci-dessous.

Le dépôt était sur `integration`, propre, à `59b5926222c28a09bedbc3d53d0481cc4975b11d`. Le `git pull --ff-only origin integration` autorisé a synchronisé HEAD et origin/integration avec la baseline exacte. La branche de correction a ensuite été créée depuis cette baseline. Les vues Sales, Purchases, Expenses, Clients, Suppliers et Payments, les APIs Payment Read/Lookup, le backend Initialization/Admin, FirstRunWindow, InitializationWindow et AdministrationView sont présents. ASTRA-003 et ASTRA-005 restent OPEN selon le mandat lead ; aucun registre ASTRA distinct n'a été trouvé dans le dépôt et aucune correction de ces deux items n'est incluse.

La baseline contenait déjà des bornes correctes pour les nouvelles allocations, un rejet des doublons et des transactions qui réservent le writer SQLite avant de lire les soldes. La faille reproduite est différente : un paiement existant de 600, réparti en deux allocations de 400 sur deux obligations de 1000, pouvait contribuer à une obligation que Create/Apply continuait à traiter comme saine. L'agrégat du paiement contributeur n'était pas vérifié. Les huit combinaisons client/fournisseur, document/obligation initiale et Create/Apply acceptent l'écriture avec les quatre services originaux de la baseline. Les huit tests de régression échouent donc volontairement sur ces services avec `Success = True`, puis les services corrigés ont été rétablis. Des sommes SQL et additions checked pouvaient également lever une exception de dépassement avant les gardes de lecture. Enfin, Restore pouvait importer des agrégats impossibles malgré des contrôles structurels valides.

| Writer de production | Allocation créée ou importée | Protection auditée / résultat |
|---|---|---|
| CustomerPaymentService.CreateAsync | CustomerPaymentAllocation vers Sale ou InitialCustomerReceivable | Positivité, XOR, existence, propriétaire, doublons rejetés, somme de commande bornée au paiement, soldes vérifiés dans la transaction ; nouveau contrôle des paiements contributeurs |
| CustomerAdvanceService.ApplyAsync | CustomerPaymentAllocation sur paiement existant | Même validation de cible ; source, avance disponible, totalité des allocations de la source et obligations déjà financées contrôlées |
| SaleService.CreateAsync | Un CustomerPayment et une allocation vers la nouvelle Sale | `min(total vente, paiement initial)` ; paiement entier conservé, surplus en avance dérivée ; vente anonyme exige paiement exact ; transaction commune |
| SupplierPaymentService.CreateAsync | SupplierPaymentAllocation vers Purchase ou InitialSupplierPayable | Symétrique de Customer Create |
| SupplierAdvanceService.ApplyAsync | SupplierPaymentAllocation sur paiement existant | Symétrique de Customer Advance |
| PurchaseService.CreateAsync | Un SupplierPayment et une allocation vers la nouvelle Purchase | Même règle canonique de paiement initial, surplus en avance, transaction commune |
| DatabaseBackupService.RestoreAsync | Remplacement de la base complète, donc import des deux familles | Nouveau contrôle de tous les agrégats après migration de staging et avant remplacement ; même contrôle en validation après remplacement ; rejet `InvalidBackup` avant swap pour les fichiers incohérents |

La recherche a couvert les constructeurs, Add/AddRange, navigations Allocations, DbSet/Set, SaveChanges, modifications et suppressions, SQL direct, ainsi que l'initialisation et la restauration. Les deux InitialObligationService créent uniquement l'obligation ; leurs allocations passent par Payment/Advance. ApplicationInitializationService n'écrit pas d'allocation. Aucun writer de production ne modifie le montant d'un paiement ou d'une obligation existante ni ne réaffecte/supprime une allocation. Les écritures directes DbContext dans les tests sont des fixtures, dont des données volontairement corrompues ; les tests de parcours appellent les six services ci-dessus. Les backups exportent un instantané sans modifier la base active ; leur création reste possible pour préserver des données historiques corrompues.

Les huit entités et leurs huit configurations EF ont été lues, puis les migrations effectives ont été vérifiées. Les invariants persistés sont les suivants.

| Invariant | SQLite / schéma existant |
|---|---|
| Paiements, ventes, achats et obligations initiales strictement positifs | CHECK sur AmountMinor, TotalAmountMinor ou InitialAmountMinor |
| Chaque allocation strictement positive | CHECK `AmountMinor > 0` des deux tables ; zéro interdit, règle inchangée |
| Une et une seule cible | CHECK XOR document / obligation initiale |
| Existence du paiement et de la cible | FK, avec foreign_keys activé dans l'intercepteur de connexion |
| Suppression du paiement | CASCADE vers ses allocations |
| Suppression de la cible, partie ou utilisateur référencé | RESTRICT |
| Références métier | Index uniques sur les références des paiements et obligations |
| Index d'allocation | Index non uniques sur paiement, document et obligation initiale ; aucun index unique paiement/cible |
| Agrégat des allocations borné au paiement ou à la cible | Aucun CHECK ou trigger de somme interligne ; garantie des parcours applicatifs transactionnels et du contrôle Restore |
| Propriétaire de la cible égal à celui du paiement | Contrôle des commandes dans les services ; pas de contrainte FK composite de propriété |

La migration 002 rend ClientId de CustomerPayment nullable pour les paiements de vente anonyme. Elle ne change aucune contrainte d'allocation. Les CHECK locaux ne peuvent pas exprimer les sommes de plusieurs lignes. Aucune prétention de protection contre une écriture SQL arbitraire contournant les services n'est faite : les fixtures de corruption démontrent précisément cette frontière. Aucune migration n'est nécessaire pour protéger les writers de production identifiés.

`AllocationIntegrity` lit les allocations concernées, valide chaque ligne et borne séparément les totaux de cible et les totaux de paiement. Pour chaque paiement contributeur, toutes ses allocations sont lues, même celles vers d'autres obligations. Apply inclut aussi les anciennes cibles de son paiement source. Un contrôle de borne avant addition et une addition checked empêchent le dépassement de long sans calcul flottant. Le résultat nul est traduit en DataIntegrityViolation avant toute nouvelle entité suivie ou écriture. Les gardes canoniques de reste dû utilisent ensuite les totaux validés. Les gardes Payment Read et Lookup sont conservées et renforcées ; les sommes risquant de déborder sont remplacées par les mêmes additions bornées. Aucune réparation ni écriture de lecture.

Les commandes, interfaces et objets de résultat conservent leurs signatures. Seul DataIntegrityViolation est ajouté à la fin des quatre enums de failure des writers, sans renumérotation des anciennes valeurs. Les valeurs calculées dans les projections de lecture restent des données temporaires, jamais des balances persistées.

La transaction EF commence avant la lecture des montants source/cible pour les six writers. Create client/fournisseur commence maintenant aussi avant la vérification d'existence de la partie. La validation pure des entrées ne dépendant pas de la base reste avant la transaction. Sale/Purchase calculent leur paiement initial à partir de nouveaux objets sans solde existant ; leur première insertion, celle du paiement et celle de l'allocation partagent la même transaction. Les SaveChanges intermédiaires ne constituent pas des commits autonomes.

Le package réellement installé Microsoft.Data.Sqlite.Core 10.0.11 indique le commit `e2f47b0110ed922f21a1522da67279133ce28f32`. Dans ce code, l'appel sans niveau explicite utilise Unspecified, active le mode non différé, promeut le niveau à Serializable et émet BEGIN IMMEDIATE. Le test de contention démarre une transaction EF sans aucune lecture/écriture, puis observe que le writer concurrent est déjà bloqué. Sources exactes : [SqliteConnection](https://raw.githubusercontent.com/dotnet/dotnet/e2f47b0110ed922f21a1522da67279133ce28f32/src/efcore/src/Microsoft.Data.Sqlite.Core/SqliteConnection.cs), [SqliteTransaction](https://raw.githubusercontent.com/dotnet/dotnet/e2f47b0110ed922f21a1522da67279133ce28f32/src/efcore/src/Microsoft.Data.Sqlite.Core/SqliteTransaction.cs).

Un seul writer obtient ce verrou ; le suivant attend sa libération avant de lire la disponibilité. Il voit alors les allocations validées du premier et ne peut pas réutiliser leur disponibilité. En cas de timeout il ne commet rien. Il n'y a donc pas de fenêtre de lecture obsolète entre validation et insertion dans ces writers. Aucun mutex limité au processus, retry d'insertion seule ou changement de transaction n'a été ajouté. La faiblesse antérieure de concurrence était l'absence de preuves suffisantes pour toutes les cibles, pas une transaction DEFERRED avérée.

L'intercepteur conserve WAL, synchronous FULL, foreign_keys ON et busy_timeout 5000 ms. Microsoft.Data.Sqlite conserve son timeout de commande par défaut de 30 s et utilise DefaultTimeout pour BeginTransaction. BUSY/LOCKED sont réessayés par le provider jusqu'au timeout. Les erreurs techniques demeurent distinctes des échecs métier et DataIntegrityViolation : leur propagation infrastructure existante est conservée, et le WPF existant les journalise puis affiche un message générique sans texte SQLite/EF. Aucun catch global ne transforme BUSY en corruption. Le test force un DefaultTimeout de 1 s, vérifie le code SQLite 5/6 et l'absence de mutation, puis vérifie qu'une nouvelle tentative explicite réussit après libération. [Documentation des erreurs et délais Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors).

SQLite SUM sur des entiers peut lever une erreur de dépassement avant qu'un contrôle applicatif examine le résultat. Les chemins modifiés évitent ce SUM ; les entrées positives sont additionnées uniquement lorsqu'elles tiennent dans la borne long du paiement/de la cible. Les tests couvrent exactement long.MaxValue et long.MaxValue + 1 réparti sur plusieurs lignes. [Documentation SQLite SUM](https://www.sqlite.org/lang_aggfunc.html).

Le contrôle est ciblé sur les données participant à l'opération, et non un balayage complet de chaque base à chaque création de vente. Une corruption historique sans lien avec l'opération n'est ni réparée ni déclarée saine. Restore contrôle toute la base importée. Le coût ajouté est proportionnel aux allocations des paiements et obligations concernés ; Restore matérialise les allocations de la base. Aucun benchmark de très gros volumes n'a été réalisé.

Les 66 points demandés sont consignés ci-dessous. Les inventaires de fichiers et résultats détaillés suivent.

| N° | Point | Résultat |
|---|---|---|
| 1 | Baseline | Hash exact vérifié ; baseline annoncée par lead : 857 PASS ; pas de réexécution complète séparée du code original, reproduction ciblée effectuée |
| 2 | Branche | fix/astra-004-allocation-integrity |
| 3 | Tous les writers Customer | CustomerPayment.Create, CustomerAdvance.Apply, Sale.Create ; Restore import complet |
| 4 | Tous les writers Supplier | SupplierPayment.Create, SupplierAdvance.Apply, Purchase.Create ; Restore import complet |
| 5 | Paiement avant fix | Commande bornée ; source Advance bornée ; paiement contributeur à une cible non contrôlé globalement |
| 6 | Cible avant fix | Reste dû vérifié dans la transaction ; corruption détectée par exception brute ; overflow SQL non contrôlé |
| 7 | Concurrence avant fix | BEGIN IMMEDIATE déjà effectif ; aucune course de surallocation démontrée ; couverture initiale incomplète |
| 8 | Cause racine | Dimensions paiement/cible vérifiées localement, contributions historiques et import Restore non validés ensemble ; overflow avant les gardes |
| 9 | Fichiers créés | AllocationIntegrity.cs, AllocationIntegrityTests.cs, DatabaseBackupAllocationIntegrityTests.cs, ce rapport |
| 10 | Fichiers modifiés | 14 ; inventaire exact ci-dessous |
| 11 | Schéma nécessaire | NON |
| 12 | Migration nécessaire | NON |
| 13 | Invariant paiement après fix | 0 <= somme <= montant ; commande bornée + toutes allocations des paiements contributeurs contrôlées |
| 14 | Invariant cible après fix | 0 <= somme <= obligation originale ; validation canonique du reste dû après agrégat validé |
| 15 | Doublons | Rejet explicite DuplicateAllocationTarget avant insertion, pour les quatre cibles ; aucun changement d'acceptation |
| 16 | Customer Create | Protégé dans transaction, corruption contrôlée avant mutation |
| 17 | Supplier Create | Protection symétrique |
| 18 | Customer Advance | Source, cibles nouvelles et déjà financées, paiements contributeurs, disponibilité vérifiés |
| 19 | Supplier Advance | Protection symétrique |
| 20 | Sale initial payment | Règle min canonique conservée, avance dérivée, transaction commune, aucun fichier modifié |
| 21 | Purchase initial payment | Protection symétrique, aucun fichier modifié |
| 22 | InitialCustomerReceivable | Création positive sans allocation ; financement par Payment/Advance protégés |
| 23 | InitialSupplierPayable | Protection symétrique |
| 24 | Transactions | Validation financière + insertion + commit dans transaction EF existante ; contrôle Restore staging avant swap |
| 25 | Concurrence | 40 nouveaux cas avec deux providers/connexions et synchronisation des débuts de transaction ; pairs compatibles acceptées, pairs incompatibles bornées |
| 26 | Busy SQLite | Comportement technique existant conservé et testé ; aucune conversion en DataIntegrityViolation ; message UI générique existant |
| 27 | Overflow | Bornes avant addition, checked, uniquement long ; cas exact et au-delà de long.MaxValue |
| 28 | Corruption existante | DataIntegrityViolation sans mutation sur données concernées ; InvalidBackup pour import incohérent ; aucun outil de réparation |
| 29 | Réparation silencieuse absente | OUI ; aucune introduction de Math.Max/Abs/Clamp, troncature ou allocation automatique |
| 30 | Tests paiement Customer | Bornes positives, zéro/négatif, exact/partiel/excès, source Advance, overflow, absence de mutation ; tests existants + nouveaux |
| 31 | Tests paiement Supplier | Symétriques |
| 32 | Tests cible Customer | Sale et receivable : bornes, doublons, paiements multiples, corruption/overflow ; tests existants + nouveaux |
| 33 | Tests cible Supplier | Purchase et payable : symétriques |
| 34 | Tests concurrence Customer | 20 nouveaux cas : Create/Create, Apply/Apply, mixte, source partagée/cibles distinctes, sources distinctes/cible partagée ; document et initial ; compatible/incompatible |
| 35 | Tests concurrence Supplier | 20 nouveaux cas symétriques |
| 36 | Payment Read régression | PASS : 22 Customer + 18 Supplier, plus nouveaux cas overflow/zéro |
| 37 | Obligation Lookup régression | PASS : 19 Customer + 19 Supplier, plus nouveaux cas de corruption |
| 38 | Payment Write régression | PASS : 44 Customer + 42 Supplier ; attentes de corruption contrôlée mises à jour |
| 39 | Advance régression | PASS : 63 tests existants ; cas nouveaux dans AllocationIntegrityTests |
| 40 | Sales/Purchases régression | PASS : 21 Sale + 18 Purchase |
| 41 | Initial Obligation régression | PASS : 26 workflows + 14 Customer Read + 14 Supplier Read ; Initialization également vert |
| 42 | Build WPF Payment | PASS, Desktop compilé sans modification |
| 43 | Total solution | 991 PASS / 0 FAIL / 0 SKIP, soit +134 tests |
| 44 | Build warnings/errors | 0 warning / 0 error |
| 45 | verify-foundation | PASS, y compris sa réexécution des 991 tests |
| 46 | Vulnérabilités | Aucune vulnérabilité connue signalée par le contrôle Infrastructure, transitives incluses |
| 47 | Pending model changes | Aucun changement de modèle en attente |
| 48 | Desktop modifié | NON |
| 49 | Domain modifié | NON |
| 50 | DbContext modifié | NON ; classe, configuration et registration inchangées |
| 51 | Migration/schéma modifiés | NON ; packages et configurations EF inchangés |
| 52 | git diff --check | PASS |
| 53 | Critère 1 : tous writers identifiés | PROUVÉ : six writers de lignes et Restore |
| 54 | Critère 2 : invariant paiement | PROUVÉ sur tous les writers de production identifiés |
| 55 | Critère 3 : invariant cible | PROUVÉ sur les quatre cibles |
| 56 | Critère 4 : doublons | PROUVÉ : rejet canonique et tests permanents |
| 57 | Critère 5 : concurrence | PROUVÉ : source du provider + tests SQLite réels avec contention |
| 58 | Critère 6 : atomicité | PROUVÉ : retour avant mutation pour intégrité, snapshots identiques, rollback transactionnel et tests existants d'échec DB, import rejeté avant swap |
| 59 | Critère 7 : lectures fail closed | PROUVÉ : gardes conservées, régressions vertes et nouveaux tests de dépassement/zéro |
| 60 | Critère 8 : aucune réparation | PROUVÉ : audit statique, snapshots, sauvegarde source inchangée |
| 61 | Critère 9 : toute régression verte | PROUVÉ : 991/0/0 dans la suite puis dans foundation |
| 62 | Critère 10 : aucune dépendance schéma non revue | PROUVÉ : aucun changement schéma/migration/package |
| 63 | ASTRA-004 STATUS | CLOSED — conclusion technique soumise à revue lead, non commitée |
| 64 | Blocage restant | Aucun blocage identifié sur le périmètre ASTRA-004 ; revue lead avant commit |
| 65 | Git status | 14 fichiers modifiés, 4 nouveaux fichiers non indexés ; détail ci-dessous ; aucun commit/push |
| 66 | ASTRA-004 READY FOR LEAD REVIEW | YES |

Les tests de doublons conservent la règle de rejet stricte : ils ne fusionnent pas les entrées dupliquées. Les paiements initiaux continuent à employer le Math.Min existant, qui conserve le paiement entier et expose le surplus ; aucune occurrence nouvelle de Math.Max, Math.Abs, Clamp, double, float, unchecked ou FIFO n'a été trouvée dans le code ajouté. Aucun solde mutable, TreasuryMovement, migration ou réparation automatique n'a été introduit.

| État | Fichier |
|---|---|
| M | [src/FinancialManagement.Application/CustomerAdvances/ApplyCustomerAdvanceFailureReason.cs](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Application/CustomerAdvances/ApplyCustomerAdvanceFailureReason.cs) |
| M | [src/FinancialManagement.Application/CustomerPayments/CreateCustomerPaymentFailureReason.cs](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Application/CustomerPayments/CreateCustomerPaymentFailureReason.cs) |
| M | [src/FinancialManagement.Application/SupplierAdvances/ApplySupplierAdvanceFailureReason.cs](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Application/SupplierAdvances/ApplySupplierAdvanceFailureReason.cs) |
| M | [src/FinancialManagement.Application/SupplierPayments/CreateSupplierPaymentFailureReason.cs](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Application/SupplierPayments/CreateSupplierPaymentFailureReason.cs) |
| M | [src/FinancialManagement.Infrastructure/Backup/DatabaseBackupService.cs](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Backup/DatabaseBackupService.cs) |
| M | [src/FinancialManagement.Infrastructure/CustomerAdvances/CustomerAdvanceService.cs](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/CustomerAdvances/CustomerAdvanceService.cs) |
| M | [src/FinancialManagement.Infrastructure/CustomerPayments/CustomerPaymentObligationLookupService.cs](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/CustomerPayments/CustomerPaymentObligationLookupService.cs) |
| M | [src/FinancialManagement.Infrastructure/CustomerPayments/CustomerPaymentService.cs](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/CustomerPayments/CustomerPaymentService.cs) |
| M | [src/FinancialManagement.Infrastructure/SupplierAdvances/SupplierAdvanceService.cs](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/SupplierAdvances/SupplierAdvanceService.cs) |
| M | [src/FinancialManagement.Infrastructure/SupplierPayments/SupplierPaymentObligationLookupService.cs](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/SupplierPayments/SupplierPaymentObligationLookupService.cs) |
| M | [src/FinancialManagement.Infrastructure/SupplierPayments/SupplierPaymentService.cs](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/SupplierPayments/SupplierPaymentService.cs) |
| M | [tests/FinancialManagement.Tests/AdvanceServiceTests.cs](C:/Dev/logiciel-gestion-financiere/tests/FinancialManagement.Tests/AdvanceServiceTests.cs) |
| M | [tests/FinancialManagement.Tests/CustomerPaymentServiceTests.cs](C:/Dev/logiciel-gestion-financiere/tests/FinancialManagement.Tests/CustomerPaymentServiceTests.cs) |
| M | [tests/FinancialManagement.Tests/SupplierPaymentServiceTests.cs](C:/Dev/logiciel-gestion-financiere/tests/FinancialManagement.Tests/SupplierPaymentServiceTests.cs) |
| ?? | [ASTRA-004-REPORT.md](C:/Dev/logiciel-gestion-financiere/ASTRA-004-REPORT.md) |
| ?? | [src/FinancialManagement.Infrastructure/AllocationIntegrity.cs](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/AllocationIntegrity.cs) |
| ?? | [tests/FinancialManagement.Tests/AllocationIntegrityTests.cs](C:/Dev/logiciel-gestion-financiere/tests/FinancialManagement.Tests/AllocationIntegrityTests.cs) |
| ?? | [tests/FinancialManagement.Tests/DatabaseBackupAllocationIntegrityTests.cs](C:/Dev/logiciel-gestion-financiere/tests/FinancialManagement.Tests/DatabaseBackupAllocationIntegrityTests.cs) |


Les commandes finales ont été exécutées séquentiellement : dotnet restore, dotnet build FinancialManagement.sln, dotnet test FinancialManagement.sln (avec sortie TRX), scripts\verify-foundation.cmd, git diff --check. Le script foundation réexécute build/tests puis vérifie migrations, modèle et vulnérabilités ; cette répétition est celle explicitement demandée.

La validation finale est verte : 991 PASS, 0 FAIL, 0 SKIP ; build WPF/solution à 0 avertissement et 0 erreur ; foundation PASS ; aucune vulnérabilité connue signalée et aucun changement de modèle en attente. Les 134 nouveaux cas sont 122 AllocationIntegrityTests (dont 40 de concurrence et 2 de contention/timeout) et 12 cas Restore. La première exécution complète avait révélé un timeout du harnais de concurrence sous charge ; les writers de test utilisent maintenant des workers dédiés et sont attendus avant destruction de la fixture. La suite complète et foundation ont ensuite passé avec ce harnais.

| Classe de régression | PASS |
|---|---|
| AdvanceServiceTests | 63 |
| AllocationIntegrityTests | 122 |
| ApplicationInitializationServiceTests | 20 |
| CustomerPaymentObligationLookupServiceTests | 19 |
| CustomerPaymentReadServiceTests | 22 |
| CustomerPaymentServiceTests | 44 |
| DatabaseBackupServiceTests | 156 |
| InitialCustomerReceivableReadServiceTests | 14 |
| InitialObligationServiceTests | 26 |
| InitialSupplierPayableReadServiceTests | 14 |
| PurchaseServiceTests | 18 |
| SaleServiceTests | 21 |
| SupplierPaymentObligationLookupServiceTests | 19 |
| SupplierPaymentReadServiceTests | 18 |
| SupplierPaymentServiceTests | 42 |
| TreasuryOpeningInitializationTests | 1 |


Preuves locales : [reproduction sur baseline](C:/Users/HP/AppData/Local/Temp/astra004-validation/baseline-reproduction.log), [restore packages](C:/Users/HP/AppData/Local/Temp/astra004-validation/restore.log), [build](C:/Users/HP/AppData/Local/Temp/astra004-validation/build.log), [tests complets](C:/Users/HP/AppData/Local/Temp/astra004-validation/test.log), [TRX](C:/Users/HP/AppData/Local/Temp/astra004-validation/astra004-full.trx), [foundation](C:/Users/HP/AppData/Local/Temp/astra004-validation/foundation.log).

La liste EF affiche les trois migrations existantes comme Pending parce que FinancialManagementDesignTimeDbContextFactory utilise une base SQLite :memory: vide. Cela ne correspond pas à un changement de modèle en attente et aucune migration nouvelle n'a été ajoutée ou appliquée à une base utilisateur.
