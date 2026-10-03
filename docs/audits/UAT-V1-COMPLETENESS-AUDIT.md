# Audit de complétude fonctionnelle V1 — release gelée

Date : 9 septembre 2026. Audit documentaire et statique, sans modification du logiciel ni exécution d'UAT.

**V1 FUNCTIONAL COMPLETENESS: FAIL**  
**RELEASE UAT MAY CONTINUE: NO**

La release contient un socle financier substantiel, avec créations, paiements, affectations, reprises initiales, calculs et protections de stockage. Elle ne couvre cependant pas toutes les fonctionnalités des documents validés. Trois blocages concrets sont établis : confidentialité des lectures employé, absence d'accès Desktop aux sauvegardes/restaurations, absence de sauvegarde automatique avant migration. Les modifications et suppressions financières explicitement prévues sont également absentes. Le présent verdict concerne la complétude et l'autorisation de poursuivre la qualification de cette V1 ; il ne remplace pas les résultats positifs des tests existants.

Le rapport est remis au lead pour arbitrage. Aucun correctif, nouveau test, changement de schéma ou parcours manuel supplémentaire n'a été entrepris.

## 1. Référence, méthode et limites

| Élément | Constat |
|---|---|
| Dépôt | C:/Dev/logiciel-gestion-financiere |
| Branche | release/v1.0-rc |
| HEAD contrôlé | fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba |
| Gel | ACTIVE ; baseline inchangée |
| Baseline annoncée | 1090 PASS / 0 FAIL / 0 SKIP ; build 0 warning / 0 error ; verify-foundation PASS |
| Preuve antérieure disponible au même commit | Exécution des 1090 tests lors de l'audit RBAC, le 09/09/2026 ; ce n'est pas une nouvelle exécution dans cet audit |
| UAT antérieure | Phase 1 PASS, Phase 2 financière largement PASS, selon le contexte transmis |
| ASTRA-003 / 004 / 005 | CLOSED ; aucune réouverture de leurs correctifs de cohérence, stockage ou allocations |
| Travaux présents | Lecture des PDF, interfaces, services, DTO, DI, démarrage, ViewModels/XAML et tests permanents |
| Travaux non réalisés | Pas de lancement de l'application, de tests, de build, de migration ni d'ouverture de la base de release |

« Accessible Desktop » signifie qu'une route/vue, une commande WPF et son appel backend ont été retrouvés dans les sources gelées. Cela ne prétend pas remplacer un essai visuel de chaque parcours. Les vérifications précédentes de calculs et de permissions ne démontrent pas à elles seules la complétude UX.

La recherche d'une fonction absente a porté sur les interfaces Application, leurs implémentations Infrastructure, les enregistrements de services et les appels Desktop. Une entité EF, une valeur de permission ou un test de service isolé ne constitue pas un parcours utilisateur.

### Documents inspectés

Les pages citées sont les pages physiques des PDF ; les numéros imprimés correspondants permettent de les retrouver. Les abréviations suivantes sont utilisées dans toutes les matrices.

| Code | Document disponible et statut |
|---|---|
| FM | [Modélisation fonctionnelle complète V1.0 VALIDÉE](/C:/Dev/logiciel-gestion-financiere/doc/Modelisation_fonctionnelle_complete_V1.0_VALIDE_Logiciel_Gestion_Financiere.pdf), 17 pages |
| DATA | [Conception du modèle de données V1.0 VALIDÉE](/C:/Dev/logiciel-gestion-financiere/doc/Conception_Modele_Donnees_V1.0_VALIDE_Logiciel_Gestion_Financiere.pdf), 14 pages |
| UX | [Conception UX/UI V1.0 VALIDÉE](/C:/Dev/logiciel-gestion-financiere/doc/Conception_UX_UI_V1.0_VALIDE_Logiciel_Gestion_Financiere.pdf), 19 pages |
| ARCH | [Architecture technique V1.0 VALIDÉE](/C:/Dev/logiciel-gestion-financiere/doc/Architecture_Technique_V1.0_VALIDE_Logiciel_Gestion_Financiere.pdf), 19 pages |
| CDC-DRAFT | [Cahier des charges V0.1](/C:/Dev/logiciel-gestion-financiere/doc/Cahier_des_charges_fonctionnel_V0.1_Logiciel_Gestion_Financiere.pdf), document « à valider », utilisé uniquement comme contexte |

**Le Cahier_des_charges_fonctionnel_V1.0_VALIDE demandé n'est pas présent dans les PDF du dépôt.** Le V0.1 ne lui est pas substitué comme preuve validée. Cette limite interdit de certifier l'exhaustivité par rapport à un cahier validé non fourni ; elle n'annule pas les écarts explicites des quatre documents validés disponibles.

Priorité documentaire : les permissions personnalisées UX p.12–13 complètent la matrice plus restrictive FM p.11. Elles autorisent notamment Créer/Modifier sur les modules tiers et paiements. Elles ne suppriment pas explicitement la confidentialité des opérations d'autres employés ni les contraintes propriété/jour des trois modules financiers.

La politique de conservation d'une session existante après désactivation, acceptée par le lead dans la demande, est respectée. Aucune exigence explicite de révocation immédiate n'a été trouvée. Les 18 échecs de ce type dans l'ancien harness RBAC ne sont donc pas des blocages de cet audit. Les trois observations de dépendance aux lookups tiers ne sont pas non plus assimilées automatiquement à une violation des documents validés.

### Natures d'exigence et classifications

- **CONFIRMED REQUIRED V1** : exigence explicitement présente dans un document validé.
- **OPTIONAL / DEFERRED** : élément exclu, recommandé ou conditionnel, sans obligation de livraison V1 démontrée.
- **AMBIGUOUS** : périmètre ou arbitrage non tranché par les preuves disponibles.
- **IMPLEMENTATION DETAIL** : choix de réalisation qui ne prouve pas à lui seul un manque fonctionnel.

Chaque constat reçoit une seule classification finale dans les registres ci-dessous. Les références à son identifiant dans les autres tableaux ne créent pas d'autre classification. « VALIDATED BUT SAFE TO DEFER ONLY WITH LEAD DECISION » est une possibilité d'arbitrage, pas une dérogation déjà accordée.

## 2. Inventaire de la release réellement exposée

Autorisation : R = Responsable uniquement ; V/C/M = droits Voir/Créer/Modifier par module, Voir conditionnant les autres. « Tests oui » signifie tests permanents backend retrouvés, avec limites détaillées en section 12.

| Capacité | Backend | UI WPF | Autorisation | Tests | Utilisable dans Desktop gelé |
|---|---|---|---|---|---|
| Authentification | Login, hash, session, logout | Fenêtre Login | Comptes actifs à la connexion | Oui | Oui ; pas de révocation immédiate exigée |
| Premier bootstrap | Premier Responsable transactionnel, reprise d'initialisation | FirstRun puis ouverture | Bootstrap sans utilisateur ; ouverture R | Oui | Oui pour compte + ouverture ; entreprise absente (L10) |
| Utilisateurs | Liste, création Employé, activation, reset, permissions | Administration/Utilisateurs | R, contrôle backend actif, protection dernier R | Oui | Partiel : édition identité/profil absente (L09) |
| Permissions Employé | Sept modules V/C/M ; fonctions sensibles non délégables | Matrice et navigation conditionnelle | Oui pour capacités existantes | Oui | Partiel : lecture propriétaire violée (B01), trois M non réalisables |
| Clients | CRUD contacts, recherche/pagination, restriction tiers utilisé | Liste, création, édition, suppression | V/C/M ; suppression R | Oui | Partiel : détail financier/lecture seule et ID métier (L02, L13) |
| Fournisseurs | Miroir Clients | Idem | Idem | Oui | Même réserve |
| Ventes | Créer/lister, paiement initial atomique, anonyme/identifié | Liste + création | V/C ; lecture sans filtre créateur | Oui | Création oui ; détail, M, D incomplets/absents |
| Achats | Créer/lister, fournisseur, paiement initial atomique | Liste + création | V/C ; lecture sans filtre créateur | Oui | Création oui ; détail, M, D incomplets/absents |
| Dépenses | Créer/lister/détail, effet trésorerie immédiat | Liste + création | V/C ; lecture sans filtre créateur | Oui, dont règles incompatibles | Partiel : salaire/date/M/D/catégories/détail |
| Paiements clients | Créer/lister/détail, allocations multiples | Liste, détail, création, affectation avance | V/C et M pour affecter avance | Oui | Oui pour paiements affectés ; avance pure et D absents |
| Paiements fournisseurs | Symétrique | Symétrique | Symétrique | Oui | Même réserve |
| Avances | Solde non affecté + ApplyAsync explicite | Détail paiement → affecter avance | M du paiement | Oui | Oui après surplus ; saisie sans obligation absente |
| Créances initiales | Créer/lister, reste calculé, échéance | Administration/Créances initiales | R ; règlement par droits Paiements | Oui | Oui pour tout le cycle requis de reprise/règlement |
| Dettes initiales | Symétrique | Administration/Dettes initiales | Idem | Oui | Oui |
| Ouverture trésorerie | Création singleton persistée | Assistant + Trésorerie | R | Oui | Oui ; correction avant première opération absente |
| Transferts | Création, incidence opposée caisse/banque | Trésorerie → transfert | R | Oui | Oui pour création ; historique/détail/D absents |
| Trésorerie | Soldes courants calculés depuis sources | Cartes et agrégats | R | Oui | Oui pour soldes ; historique unifié absent |
| Dashboard | 11 indicateurs, flux période/stocks courants | Cartes, dates, actualiser | R | Oui | Partiel : graphiques, avances, raccourcis/presets manquants |
| Alertes d'échéance | Calcul J-5/retard/restes | Composant Dashboard | R via accès global | Oui | Calcul/affichage oui ; navigation/contextes absents |
| Administration | Comptes + reprises initiales | Trois onglets | R | Oui sur services | Partiel : paramètres, catégories, sauvegardes absents |
| Catégories dépenses | Cinq seeds et lookup actif | Sélecteur seulement | Lookup protégé ; pas de mutations exposées | Partiel via dépenses/schéma | Gestion impossible |
| Sauvegarde locale/externe | Service de snapshot valide | Aucun accès Desktop retrouvé | R backend | Oui | Backend-only |
| Restauration / sauvegarde de sécurité | Validation, pré-restauration, protection contre fichier invalide | Aucun accès Desktop retrouvé | R backend | Oui | Backend-only |
| Sauvegarde automatique | Aucun ordonnanceur/caller quotidien retrouvé | Sans objet | Pas de flux automatique | Pas de couverture d'un déclenchement Desktop | Non |
| Suppression définitive | Tiers inutilisés seulement ; pas de service D financier | Supprimer tiers seulement | Capacité sensible existe | Tests tiers ; pas de parcours D financier | Non pour opérations financières |
| Démarrage/migrations | Contrôle stockage avant/après migration, erreurs bloquantes | Démarrage et message d'erreur | Avant session / gate bootstrap | Oui | Partiel : pré-update absente ; livraison installateur non prouvée |
| Intégrité des données | Money entier, FK/contraintes, transactions, allocations et snapshots | Validations des formulaires + erreurs métier | Backend sur chemins existants | Oui, forte couverture | Oui sur fonctionnalités réalisées |
| Navigation WPF | Résolution des pages et refus de routes interdites | Dix routes, vues réellement enregistrées | Masquage + contrôle | Backend testé, WPF faible | Modules de base oui ; accueil Employé absent |

Preuves structurantes : [App.xaml.cs](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Desktop/App.xaml.cs), [ShellViewModel](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Desktop/ViewModels/ShellViewModel.cs), [AdministrationView](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Desktop/Views/AdministrationView.xaml:144), [interfaces Application](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Application), [services Infrastructure](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure).

## 3. Matrice CRUD : exigence et réalisation

Chaque cellule décrit l'exigence puis l'état réel. Une permission M isolée n'est pas utilisée comme preuve suffisante d'une fonction d'édition.

| Entité/module | Create | View/List | Modify | Delete | Source validée |
|---|---|---|---|---|---|
| Vente | Requis / présent | Requis / liste partielle, détail financier absent | Requis / absent M01 | Requis R / absent D01 | FM p.4 §3 ; UX p.6 §5, p.13 §12 ; DATA p.11 §12 |
| Achat | Requis / présent | Requis / liste partielle, détail absent | Requis / absent M02 | Requis R / absent D02 | FM p.6 §5 ; UX p.7 §6, p.13 §12 ; DATA p.11 |
| Dépense | Requis / présent avec écarts | Requis / liste présente, détail backend seul | Requis / absent M03 | Requis R / absent D03 | FM p.8 §7 ; UX p.8 §7, p.13 §12 ; DATA p.11 |
| Paiement client | Requis / présent sauf avance pure | Requis / liste et détail présents | Affecter avance requis / présent ; édition générale ambiguë A01 | Requis R / absent D04 | FM p.5 §4 ; DATA p.6 §5.4, p.11 §12, p.13 §16 ; UX p.10 §9 |
| Paiement fournisseur | Idem | Idem | Idem A01 | Requis R / absent D05 | FM p.7 §6 ; DATA p.7 §6.4, p.11, p.13 |
| Client | Requis / présent | Requis / contacts partiels, financier absent | Requis / présent | Requis si jamais utilisé / présent | FM p.10 §9 ; DATA p.4–5 §4, p.11 §12 ; UX p.9 §8 |
| Fournisseur | Idem | Idem | Idem | Idem | Mêmes sections |
| Créance initiale | Requis R / présent | Requis / présent | Correction directe non précisée A02 ; règlement présent | Portée non précisée A02 | FM p.12 §11 ; DATA p.9 §9 |
| Dette initiale | Idem | Idem | Idem | Idem | Idem |
| Ouverture caisse/banque | Requis singleton / présent | Requis / présent | Requis avant première opération / absent M04 | Pas de suppression autonome clairement prescrite A02 | FM p.12 §11.1 ; DATA p.8 §8.1 |
| Transfert caisse/banque | Requis / présent | Historique requis / agrégats seulement | Non explicitement détaillé A02 | Requis R / absent D06 | UX p.11 §10 ; DATA p.11 §12.1 |
| Catégorie dépense | Ajout R requis / absent M06 | Choix/liste requis / seeds + lookup | Gestion prévue ; utilisée doit rester inchangée / aucun flux | Inutilisée supprimable / absent M06 ; utilisée interdite | FM p.8 §7.1 ; DATA p.7 §7.1 et p.11 §12 ; UX p.8 §7, p.12 §11 |
| Utilisateur | Requis / premier R + création E présents | Requis / présent | Activation/reset/droits présents ; identité/profil absents L09 | Désactivation prévue ; suppression historique interdite / respectée | FM p.11 §10.1 ; UX p.12 §11.1 ; DATA p.13 §16.1 |
| Paramètres entreprise | Configuration requise / pas de flux | Écran requis / absent | Requis / absent L10 | Pas de CRUD Delete prescrit | UX p.12 §11.1 ; ARCH p.14 §13.1 |

## 4. Registre des écarts : exigence → preuve → classification unique

### Blocages de release confirmés

| ID | Exigence et source exacte | Implémentation / impact pratique | Nature | Classification |
|---|---|---|---|---|
| B01 | Employé : ne voir que ses propres ventes, achats, dépenses. FM p.11 §10.1 ; UX p.4 §3.3, données des autres employés masquées | Les trois lectures contrôlent V sans prédicat créateur. Un employé autorisé lit des opérations d'un autre utilisateur, y compris dépenses/salaires. Des tests exigent actuellement ce comportement opposé au document. Atteinte concrète à la confidentialité. | CONFIRMED REQUIRED V1 | RELEASE BLOCKER |
| B02 | Administration doit proposer sauvegarde locale, externe et restauration avec confirmation forte. UX p.12 §11.1 ; ARCH p.11 §10 et p.15 §14.3 | Services existants et testés, aucun appel/écran Desktop. Le Responsable ne peut ni obtenir sa sauvegarde ni réaliser le parcours de récupération depuis l'application livrée. | CONFIRMED REQUIRED V1 | RELEASE BLOCKER |
| B03 | Sauvegarde automatique avant migration ; échec laissant un point de récupération. ARCH p.14 §13.2 | Le démarrage appelle directement MigrateAsync après validation du stockage, sans sauvegarde pré-update. Une mise à jour de schéma n'a pas le filet de récupération exigé. Le blocage concerne le chemin de mise à jour ; aucune perte réelle n'est affirmée. | CONFIRMED REQUIRED V1 | RELEASE BLOCKER |

B01 est indépendant de la révocation des sessions. Preuves directes : [SaleService.GetPageAsync](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Sales/SaleService.cs:61), [ExpenseService](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Expenses/ExpenseService.cs:169), [test de lecture croisée ventes](/C:/Dev/logiciel-gestion-financiere/tests/FinancialManagement.Tests/SalesReadClientLookupTests.cs:59), [test achats](/C:/Dev/logiciel-gestion-financiere/tests/FinancialManagement.Tests/PurchaseReadSupplierLookupTests.cs:53), [test dépenses tous utilisateurs](/C:/Dev/logiciel-gestion-financiere/tests/FinancialManagement.Tests/ExpenseServiceTests.cs:301). L'absence du filtre n'est donc pas déduite d'une seule recherche textuelle.

B02 : [IDatabaseBackupService](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Application/Backup/IDatabaseBackupService.cs) et [DatabaseBackupService](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Backup/DatabaseBackupService.cs) existent ; aucune référence à leurs commandes dans le Desktop. B03 : [FinancialDatabaseStartup](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/FinancialDatabaseStartup.cs:9) valide, migre puis revalide ; [App.xaml.cs](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Desktop/App.xaml.cs) ne crée pas de sauvegarde avant cet appel.

### Fonctions confirmées à réaliser avant RC

| ID | Exigence validée | Réel : manque backend / UI | Nature | Classification |
|---|---|---|---|---|
| M01 | Modifier vente, propriété/jour Employé ; montant modifiable malgré paiements. FM p.11 §10.1 ; UX p.6 §5, p.13 §12 ; DATA p.11 §12.2 | Aucun Update service ni action WPF ; pas de recalcul/libération d'allocations d'une vente corrigée | CONFIRMED REQUIRED V1 | REQUIRED V1 — MUST FIX BEFORE RC |
| M02 | Modifier achat, mêmes règles. FM p.11 §10.1 ; UX p.7 §6, p.13 §12 ; DATA p.11 §12.2 | Aucun Update service ni action WPF | CONFIRMED REQUIRED V1 | REQUIRED V1 — MUST FIX BEFORE RC |
| M03 | Modifier dépense, propriété/jour Employé. FM p.11 §10.1 ; UX p.8 §7, p.13 §12 | Aucun Update service ni action WPF | CONFIRMED REQUIRED V1 | REQUIRED V1 — MUST FIX BEFORE RC |
| M04 | Corriger ouverture avant première opération réelle. FM p.12 §11.1 | Create refuse toute seconde ouverture ; aucune correction conditionnelle backend/UI | CONFIRMED REQUIRED V1 | REQUIRED V1 — MUST FIX BEFORE RC |
| M05 | Employé concerné obligatoire quand nécessaire, notamment salaire. FM p.8 §7 ; UX p.8 §7 | Nullable accepté pour Salaires ; UI ne rend pas le choix obligatoire ; test permanent valide même ce cas | CONFIRMED REQUIRED V1 | REQUIRED V1 — MUST FIX BEFORE RC |
| M06 | Responsable ajoute catégories ; gestion et suppression inutilisée ; catégorie utilisée conservée/inchangée. FM p.8 §7.1 ; DATA p.7 §7.1, p.11 §12 ; UX p.8 §7 et p.12 §11.1 | Seulement seeds et lookup ; pas de service ni écran de gestion. L'impossibilité de modifier toute catégorie ne réalise pas la gestion requise | CONFIRMED REQUIRED V1 | REQUIRED V1 — MUST FIX BEFORE RC |
| M07 | Paiement client entièrement en avance possible. DATA p.13 §16, cardinalité paiement→affectation 0..N explicitement expliquée | Create impose au moins une allocation ; UI refuse panier vide. Impossible d'encaisser l'avance seule avant toute obligation | CONFIRMED REQUIRED V1 | REQUIRED V1 — MUST FIX BEFORE RC |
| M08 | Avance fournisseur entièrement non affectée possible. DATA p.13 §16, 0..N ; FM p.7 §6.2 | Même refus backend/UI | CONFIRMED REQUIRED V1 | REQUIRED V1 — MUST FIX BEFORE RC |
| M09 | Sauvegarde automatique locale, au maximum une par jour. ARCH p.11 §10.1 | Pas de déclencheur quotidien dans le Desktop ni de flux automatique retrouvé ; service manuel seul | CONFIRMED REQUIRED V1 | REQUIRED V1 — MUST FIX BEFORE RC |

Preuves : [ISaleService](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Application/Sales/ISaleService.cs:5), [IPurchaseService](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Application/Purchases/IPurchaseService.cs:5), [IExpenseService](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Application/Expenses/IExpenseService.cs:5), [TreasuryService](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Treasury/TreasuryService.cs), [ExpenseService contrôle optionnel](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Expenses/ExpenseService.cs:76), [test salaire sans employé](/C:/Dev/logiciel-gestion-financiere/tests/FinancialManagement.Tests/ExpenseServiceTests.cs:231), [refus paiement sans allocation](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/CustomerPayments/CustomerPaymentService.cs:339), [refus fournisseur](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/SupplierPayments/SupplierPaymentService.cs:339), [préparation UI paiement](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Desktop/ViewModels/PaymentManagementViewModelBase.cs:1241).

La formulation « affecté à une ou plusieurs obligations » de FM p.5/p.7 décrit le paiement affecté. DATA p.13 précise explicitement le cas zéro affectation / avance entière ; elle empêche de considérer le refus systématique des paiements sans affectation comme une simple préférence. Si le lead souhaite exclure ce cas de la V1, il faut une décision de périmètre explicite.

### Suppressions financières — décision distincte par flux

Toutes les lignes ci-dessous sont de nature **CONFIRMED REQUIRED V1**. Source exacte : DATA p.11 §12 et §12.1 ; Responsable uniquement également FM p.16 INV-14 et UX p.13 §12.1. Aucun endpoint/service Delete ni commande WPF correspondants n'a été retrouvé. Les cascades EF/SQL ou la valeur PermanentDelete ne réalisent pas ces parcours.

| ID | Flux requis | Conséquence atomique exigée | Réalisation | Classification |
|---|---|---|---|---|
| D01 | Supprimer définitivement vente | Supprimer allocations vers vente ; conserver paiements ; montants libérés deviennent avances client | Backend/UI absents | REQUIRED V1 — MUST FIX BEFORE RC |
| D02 | Supprimer définitivement achat | Symétrique : avances fournisseur | Backend/UI absents | REQUIRED V1 — MUST FIX BEFORE RC |
| D03 | Supprimer définitivement dépense | Retirer opération ; recalcul résultat et trésorerie depuis sources | Backend/UI absents | REQUIRED V1 — MUST FIX BEFORE RC |
| D04 | Supprimer paiement client | Retirer paiement et ses allocations ensemble ; rouvrir restes ; retirer encaissement | Backend/UI absents | REQUIRED V1 — MUST FIX BEFORE RC |
| D05 | Supprimer paiement fournisseur | Symétrique : dette et décaissement | Backend/UI absents | REQUIRED V1 — MUST FIX BEFORE RC |
| D06 | Supprimer transfert | Retirer transfert ; recalculer caisse et banque ensemble, sans résultat | Backend/UI absents | REQUIRED V1 — MUST FIX BEFORE RC |

Les tests actuels de cohérence des allocations ne testent pas ces parcours inexistants. Aucune recommandation de suppression logique, journal comptable supplémentaire ou méthode de correction alternative n'est ajoutée à la spécification.

### Écarts validés pouvant être différés seulement par décision du lead

Ces écarts restent des exigences V1, pas des fonctionnalités inventées. Leur réalisation partielle ou un autre chemin existant permettent de proposer un arbitrage sans les présenter comme blocages immédiats de sécurité/récupération.

| ID | Exigence / source | Manque observé et solution actuelle limitée | Nature | Classification |
|---|---|---|---|---|
| L01 | Listes ventes/achats avec payé, reste, état ; fiche et historique paiements. UX p.6 §5 et p.7 §6 | DTO/listes basiques sans payé/reste/état ; pas de fiche transaction ni historique associé. Les lookups paiement montrent les obligations ouvertes, sans remplacer cette vue | CONFIRMED REQUIRED V1 | VALIDATED BUT SAFE TO DEFER ONLY WITH LEAD DECISION |
| L02 | Fiche tiers contacts + historique financier, soldes, avances et échéances pour R ; consultation autorisée pour E. FM p.10 §9.1 ; UX p.9 §8 | Contacts backend oui ; détail WPF chargé seulement lors d'Éditer, donc pas de vrai détail Voir seul. Pas de projection/historique financier ni solde dans liste tiers | CONFIRMED REQUIRED V1 | VALIDATED BUT SAFE TO DEFER ONLY WITH LEAD DECISION |
| L03 | Proposer explicitement l'avance disponible pendant nouvelle vente/achat. FM p.5 §4.2 et p.7 §6.2 ; UX p.6 §5, p.7 §6 | Pas d'avance disponible/choix dans ces créations ; création puis détail Paiement → affecter reste possible | CONFIRMED REQUIRED V1 | VALIDATED BUT SAFE TO DEFER ONLY WITH LEAD DECISION |
| L04 | Historique unifié trésorerie, impacts caisse/banque, origine et accès source. UX p.11 §10 | Agrégats et transferts créables ; pas de lignes chronologiques ni navigation aux pièces | CONFIRMED REQUIRED V1 | VALIDATED BUT SAFE TO DEFER ONLY WITH LEAD DECISION |
| L05 | Deux graphiques, activité et paiements, granularité selon période. FM p.14 §13.4 ; UX p.4 §3.2 et p.19 §18 | Ni séries backend ni graphiques WPF ; cartes seules | CONFIRMED REQUIRED V1 | VALIDATED BUT SAFE TO DEFER ONLY WITH LEAD DECISION |
| L06 | Affichage des stocks d'avances clients/fournisseurs. FM p.14 §13.2 ; UX p.4 §3.2 | Calculs d'avances existent dans paiements mais absents du résultat Dashboard et des cartes | CONFIRMED REQUIRED V1 | VALIDATED BUT SAFE TO DEFER ONLY WITH LEAD DECISION |
| L07 | Préréglages Aujourd'hui/Mois/Année/Tout/personnalisé et actions rapides Dashboard. FM p.14 §13.3 ; UX p.4 §3.2 | Dates manuelles et actualisation présentes ; pas des raccourcis prescrits | CONFIRMED REQUIRED V1 | VALIDATED BUT SAFE TO DEFER ONLY WITH LEAD DECISION |
| L08 | Accueil personnel Employé et ses actions/indicateurs du jour. UX p.4 §3.3, p.17 §16 HOME-E | Shell ouvre premier module autorisé ou aucun module ; pas d'accueil personnel | CONFIRMED REQUIRED V1 | VALIDATED BUT SAFE TO DEFER ONLY WITH LEAD DECISION |
| L09 | Modifier utilisateurs et changer profil. UX p.12 §11.1 | Création Employé, état, droits et reset seulement ; pas d'édition identité/profil dans interface ni WPF | CONFIRMED REQUIRED V1 | VALIDATED BUT SAFE TO DEFER ONLY WITH LEAD DECISION |
| L10 | Nom entreprise au premier démarrage ; paramètres entreprise/devise. ARCH p.14 §13.1 ; UX p.12 §11.1 | Entité ApplicationSettings seule ; bootstrap sans nom entreprise, pas de service/écran paramètres | CONFIRMED REQUIRED V1 | VALIDATED BUT SAFE TO DEFER ONLY WITH LEAD DECISION |
| L11 | Alertes présentes aussi dans pages concernées et ouvrant les opérations. FM p.13 §12 ; UX p.5 §4 | Calcul et listes Dashboard présents ; pas de commande d'ouverture ni affichage contextuel dans modules concernés | CONFIRMED REQUIRED V1 | VALIDATED BUT SAFE TO DEFER ONLY WITH LEAD DECISION |
| L12 | Date dépense aujourd'hui par défaut mais modifiable à la saisie. FM p.8 §7 ; DATA p.8 §7.2 | Commande sans date ; service impose date locale actuelle ; impossible de choisir autre date à la création | CONFIRMED REQUIRED V1 | VALIDATED BUT SAFE TO DEFER ONLY WITH LEAD DECISION |
| L13 | Identifiant métier tiers automatique et unique, distinct de clé technique. FM p.10 §9 ; DATA p.4 §3.1/§4.1, p.5 §4.2, p.11 §11.3 ; UX p.9 §8.1 | Identifier visible facultatif et saisi manuellement, pas d'auto-génération ni unicité obligatoire. Le backlog utilisateur ne doit pas bloquer la complétude : dérogation explicite à clarifier avec lead ; aucun préfixe CLI-/FOU- imposé par les documents | CONFIRMED REQUIRED V1 | VALIDATED BUT SAFE TO DEFER ONLY WITH LEAD DECISION |
| L14 | Filtres précis tiers/état paiement ventes-achats ; catégorie/moyen dépenses. UX p.6 §5, p.7 §6, p.8 §7 | Texte/période/pagination présents ; filtres structurés prescrits incomplets. Recherche texte catégorie ne remplace pas filtre moyen de paiement | CONFIRMED REQUIRED V1 | VALIDATED BUT SAFE TO DEFER ONLY WITH LEAD DECISION |
| L15 | Logs locaux datés dans Logs, événements démarrage/DB/backup/migration/crash. ARCH p.12 §11.1 | Trace.TraceError existe, mais aucun sink fichier daté/configuration Logs retrouvé dans le chemin livré | CONFIRMED REQUIRED V1 | VALIDATED BUT SAFE TO DEFER ONLY WITH LEAD DECISION |
| L16 | Consultation fiche dépense. UX p.17 §16, inventaire liste/fiche/formulaire | GetById backend existant sans action/vue de consultation raccordée ; liste disponible | CONFIRMED REQUIRED V1 | VALIDATED BUT SAFE TO DEFER ONLY WITH LEAD DECISION |

Preuves : [SaleListItem](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Application/Sales/SaleListItem.cs), [PurchaseListItem](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Application/Purchases/PurchaseListItem.cs), [édition tiers comme seul chargement détail](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Desktop/ViewModels/PartyManagementViewModelBase.cs:771), [IUserAdministrationService](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Application/Administration/IUserAdministrationService.cs), [FinancialDashboardResult](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Application/Dashboard/FinancialDashboardResult.cs), [CreateExpenseCommand](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Application/Expenses/CreateExpenseCommand.cs), [date imposée](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Expenses/ExpenseService.cs:101).

### Ambiguïtés et exclusions : ne pas inventer de CRUD

| ID | Question / constat | Nature et arbitrage | Classification |
|---|---|---|---|
| A01 | Modifier un paiement : champs, montant/date/moyen ou seulement affectations ? | AMBIGUOUS. UX p.13 donne M, DATA p.11 §12.2 parle de corrections avec paiements existants sans détailler un formulaire général de correction du paiement. ApplyAsync est réel. Édition générale absente, périmètre exact à décider ; ne pas étendre la règle propriétaire/jour des ventes-achats-dépenses aux paiements par supposition | AMBIGUOUS REQUIREMENT |
| A02 | Modifier/supprimer directement une reprise initiale ; supprimer ouverture ; modifier transfert | AMBIGUOUS. DATA p.9 définit reprises et allocations mais §12.1 ne décrit pas leur suppression ; le bit PermanentDelete n'autorise pas d'inventer toutes ces opérations. La correction d'ouverture avant activité est en revanche explicitement requise M04 ; suppression transfert explicitement requise D06 | AMBIGUOUS REQUIREMENT |
| A03 | Définition technique du « même jour » et champs modifiables | AMBIGUOUS. DATA p.4 §3.3 laisse date métier et/ou horodatage selon application. Propriété et jour sont obligatoires pour les trois modules ; fuseau/horloge et frontière exacte doivent être documentés lors d'un futur correctif, sans élargir la règle | AMBIGUOUS REQUIREMENT |
| A04 | Preuve livraison x64 self-contained / Inno Setup, installation propre et mise à jour conservant données | AMBIGUOUS quant à livraison réelle. ARCH p.13 §12, p.15 §14.3 l'exigent ; aucun script .iss/profil de publication/installateur vérifiable retrouvé dans le dépôt gelé. Un build vert ne prouve pas ce gate. Un artefact/support de livraison externe peut exister : demander sa preuve au lead, sans affirmer son inexistence partout | AMBIGUOUS REQUIREMENT |
| A05 | Copie d'une sauvegarde existante versus création d'un snapshot valide directement vers destination externe | AMBIGUOUS sur interaction exacte : ARCH p.11 §10.2 décrit copie d'un artefact valide. Service externe crée un snapshot cohérent empaqueté ; pas de copie brute de DB active. Valider le futur parcours écran, sans classer ce choix interne seul comme corruption | AMBIGUOUS REQUIREMENT |
| N01 | Révocation immédiate d'une session après désactivation | IMPLEMENTATION DETAIL / politique explicitement acceptée dans la demande. Aucun texte validé imposant révocation immédiate retrouvé | NOT A GAP |
| N02 | Dépendance aux droits Voir Clients/Fournisseurs pour utiliser leurs sélecteurs dans d'autres formulaires | IMPLEMENTATION DETAIL. Le harness précédent cherchait une parité stricte UI/backend ; les documents ne définissent pas explicitement cette autorisation de lookup transverse. Configurer les droits nécessaires permet le parcours ; ne pas créer une nouvelle exigence de sécurité à partir du seul harness | NOT A GAP |
| N03 | Initiales backend-only ou avance ultérieure sans UI | CONFIRMED REQUIRED V1, déjà présent : vues InitialObligation et commande ApplyAdvance raccordées | NOT A GAP |
| N04 | Préfixes CLI-/FOU- obligatoires | IMPLEMENTATION DETAIL. Aucun format précis prescrit ; seul l'identifiant métier automatique/unique est confirmé (L13) | NOT A GAP |
| O01 | 14 sauvegardes quotidiennes conservées | OPTIONAL / DEFERRED : ARCH p.11 §10.1 dit conservation recommandée. Ne pas assimiler le nombre exact à un gate V1 ; l'automatisation elle-même reste requise M09 | OPTIONAL / V1.1 |
| O02 | Ajustement dédié après démarrage réel | OPTIONAL / DEFERRED : FM p.12 §11.1 l'évoque pour une correction future si nécessaire. Ne pas inventer maintenant un nouveau module d'ajustements ; correction avant activité requise M04 | OPTIONAL / V1.1 |
| O03 | Multi-caisses/banques, rapprochement, clôture, notifications externes, auto-update Internet | OPTIONAL / DEFERRED ou explicitement hors V1 : FM p.9 §8, p.13 §12 ; ARCH p.14 §13.2. Leur absence n'est pas un manque de la release | OPTIONAL / V1.1 |

## 5. Adjudication explicite : Modify et règles Employé

**Sales Modify : MISSING et explicitement requis (M01).** Les sources ne se limitent pas à une enum : FM p.11 §10/10.1 décrit l'édition des propres opérations le jour même ; UX p.13 §12 nomme Ventes ; DATA p.11 §12.2 précise le recalcul avec paiements. ISaleService n'expose que GetPageAsync/CreateAsync et SalesViewModel n'a pas de commande d'édition.

**Purchases Modify : MISSING et explicitement requis (M02).** Mêmes preuves spécifiques à Achats : UX p.7 §6 et p.13 §12. IPurchaseService/GetPage/Create et le formulaire WPF réalisent uniquement la création.

**Expenses Modify : MISSING et explicitement requis (M03).** UX p.13 §12 cite expressément Dépenses avec propriété/jour. GetById d'une dépense ne permet pas sa modification ; aucun Update ni formulaire d'édition raccordé.

| Portée de règle propriétaire + jour pour Employé | État | Justification |
|---|---|---|
| Ventes | MISSING | Édition inexistante ; attribution à création présente, mais aucun chemin exécutant le contrôle complet à la modification |
| Achats | MISSING | Idem |
| Dépenses | MISSING | Idem |
| Clients/Fournisseurs | NOT REQUIRED pour cette contrainte particulière | UX p.13 définit la restriction tiers utilisé, pas une limite propriétaire/jour ; M contacts est implémenté |
| Paiements clients/fournisseurs | AMBIGUOUS pour édition générale ; pas d'extension automatique | UX impose surtout absence de suppression Employé ; M d'affectation avance existe |
| Ouverture/reprises/transferts | NOT REQUIRED pour un Employé | Flux Responsable ; ne pas inventer un cas d'édition Employé |
| Lecture des ventes/achats/dépenses propres | MISSING | Règle distincte, explicitement prescrite FM p.11 ; B01 |

L'impossibilité de modifier n'est pas une preuve que la règle d'autorisation d'édition est correctement réalisée. Le Responsable doit aussi disposer des éditions prévues ; la contrainte Employé ne doit pas lui être appliquée par défaut.

## 6. Opérations financières, reprises initiales et avances

### Ventes, achats, dépenses

| Règle | Couverture actuelle |
|---|---|
| Vente anonyme | Client nul accepté uniquement avec paiement intégral immédiat ; ni crédit ni avance anonyme ; backend + UI + tests |
| Vente client connu | Sélection client, montant positif, référence/date automatiques, observation/échéance facultatives ; paiement initial optionnel cash/bank ; atomique |
| Paiement partiel puis solde | Disponible via paiements séparés et allocations explicites ; encaissement affecte trésorerie, vente seule ne l'affecte pas |
| Achat | Fournisseur obligatoire, référence/date, montant, observation/échéance, paiement initial optionnel cash/bank ; dette résiduelle et surpaiement/avance |
| Autorisations de création | V/C du module pour création ; premier règlement intégré au workflow vente/achat ; pas d'élargissement inventé des permissions |
| Dépense | Catégorie existante active, montant positif, Espèces/Virement, observation facultative, employee choisi validé ; trésorerie et résultat immédiats |
| Écarts dépenses | Salaire sans employé accepté M05 ; date non choisissable L12 ; catégories M06 ; éditions/suppressions M03/D03 ; détail L16 |
| Écarts ventes/achats | M01/M02, D01/D02, détail/état L01, proposition d'avance L03, filtres L14 |

### Reprises initiales : deux parcours complets disponibles

Sources : FM p.12 §11 ; DATA p.9 §9 ; UX p.17 §16. [AdministrationView](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Desktop/Views/AdministrationView.xaml:144) héberge deux InitialObligationView. Les ViewModels concrets appellent les services clients/fournisseurs ; OnLoaded déclenche la lecture.

| Critère demandé | Créance initiale client et dette initiale fournisseur |
|---|---|
| Créer en UI | Oui, onglet Administration dédié, tiers + montant + reprise + échéance/observation |
| Lister en UI | Oui, montant initial, reste calculé, filtre tiers/pagination |
| Allouer paiement réel | Oui, cible reprise dans sélecteur d'obligations |
| Paiement partiel / solde complet | Oui, affectations et reste dérivé ; obligation soldée sort des cibles ouvertes |
| Allouer avance existante | Oui, même cible dans ApplyAdvance |
| Échéance | Facultative à création ; prise en compte par alertes |
| Dashboard | Stock receivable/payable actuel ; n'ajoute ni ventes ni achats de période |
| Trésorerie | Reprise seule sans effet ; règlement réel agit sur cash/bank ; consommation avance sans second mouvement |
| Autorisation | Création/lecture initiales R ; paiements et avances selon droits de leurs modules |
| Persistance | Entités/migrations/FK ; tests création, lecture, allocations, soldes et sauvegarde |

Les deux parcours sont symétriques : tous les critères ci-dessus s'appliquent aux deux reprises. **Aucun manque backend-only/UI-only pour le cycle création → règlement des reprises initiales.** L'absence de CRUD d'édition/suppression directe reste A02, faute de règle assez précise.

### Paiements et avances

Le parcours existant est : créer paiement avec une ou plusieurs affectations du même tiers → éventuel surplus non affecté → ouvrir détail paiement → action d'affectation de l'avance → choisir obligations → confirmer. [PaymentManagementViewModelBase](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Desktop/ViewModels/PaymentManagementViewModelBase.cs:1402), CustomerPaymentsViewModel et SupplierPaymentsViewModel appellent réellement les services d'avance.

- Allocations multiples, mélange opération courante/reprise du même tiers, paiements partiels/complets : présents, backend et UI.
- Refus mauvais tiers/cible invalide, surallocation et montants non positifs ; transactions et conflits d'allocations : couverts par backend/tests.
- Surpaiement non affecté conservé ; pas d'application automatique à une autre obligation : présent.
- Consommer une avance ne crée pas de nouveau paiement ni mouvement de trésorerie : présent.
- Avance entièrement seule avant obligation : refusée backend et UI, M07/M08.
- Proposition d'avance disponible dans création vente/achat : absente, L03 ; le parcours séparé d'affectation existe.
- View/Create existent ; Modify sert réellement à ApplyAdvance. Édition générale du paiement : A01. Delete requis mais absent : D04/D05.
- L'observation UAT « Aucune affectation » est conservée séparément (U06) ; elle n'autorise pas à conclure que les allocations ne sont pas persistées.

## 7. Sauvegarde, restauration, démarrage et intégrité

| Fonction | Backend | Desktop | Conclusion |
|---|---|---|---|
| Sauvegarde locale manuelle | Snapshot SQLite cohérent, artefact validé | Aucun écran/commande | B02 |
| Externe/USB | Production d'un artefact externe cohérent | Aucun sélecteur/action | B02 ; détail de copie A05 |
| Restauration | Lecture/compatibilité/intégrité et restauration protégées | Aucun choix de fichier ni confirmation forte | B02 |
| Pré-restauration automatique | Présente dans RestoreAsync | Accessible uniquement en appelant le service | Backend complet, parcours utilisateur impossible à ce stade |
| Fichier invalide / restauration échouée | Résultats d'erreur et protection des données ; tests dédiés | Présentation/confirmation de restauration non réalisée | Ne pas compter les tests backend comme UX complète |
| Préservation comptes/permissions/finances | Base restaurée inclut ces données ; service invalide la session | Aucun trajet Desktop K | Preuve backend, pas livraison de K |
| Sauvegarde locale automatique | Pas de déclencheur retrouvé | Non | M09 |
| Pré-migration | Validation avant migration mais pas sauvegarde | Démarrage migre directement | B03 |
| Erreur d'ouverture/stockage | Validation avant/après migration ; démarrage bloqué avec erreur | Oui | Pas de faux Dashboard vide après échec |
| Migrations/contraintes | Migrations versionnées, singleton ouverture, FK et intégrité financières | Au démarrage | Fonctionnel réalisé ; ne prouve pas gate d'installation A04 |
| Logs persistants datés | Trace sans dispositif fichier daté retrouvé | Pas de dispositif Logs retrouvé | L15 |

Aucune lecture ou manipulation de C:/ProgramData/FinancialManagement/Data/FinancialManagement.db n'a été nécessaire à cet audit. Les garanties de stockage et concurrence issues d'ASTRA-003/004/005 restent acquises dans leur périmètre ; elles ne créent pas les actions UX manquantes.

## 8. Trésorerie, Dashboard et échéances

Trésorerie conforme pour les calculs existants : ouverture singleton ; caisse et banque peuvent être négatives ; encaissements clients ajoutés selon moyen ; paiements fournisseurs et dépenses retranchés ; transferts ont deux impacts opposés et ne changent ni total trésorerie ni résultat. Les ventes/achats/reprises ne déplacent pas directement du cash. Création ouverture/transfert et lecture soldes sont WPF accessibles et Responsable uniquement. Restent correction avant activité M04, historique L04 et suppression transfert D06.

| Métrique / comportement Dashboard | Réalisation |
|---|---|
| Ventes, Achats, Dépenses | Flux sur période sélectionnée |
| Résultat | Ventes − Achats − Dépenses de période |
| Encaissements clients, paiements fournisseurs | Flux de paiements réels sur période, y compris part non affectée ; consommation d'avance sans double comptage |
| Créances clients, dettes fournisseurs | Stocks courants incluant reprises non soldées ; non tronqués par filtre de période |
| Caisse, Banque, Total trésorerie | Stocks courants ; ouverture et mouvements de toutes dates ; total = caisse + banque |
| Cohérence d'un résultat | Couverture snapshot cohérent, ne pas rouvrir ASTRA-003 |
| Avances disponibles totales | Absentes Dashboard, L06 |
| Deux graphiques | Absents, L05 |
| Périodes et actions rapides | Dates personnalisées présentes ; raccourcis validés absents L07 |
| Date fin initiale | Déjà localToday dans constructeur ; U07, observation non reproduite ici |

Alertes : échéance facultative ; au-delà de J-5, pas d'alerte proche ; J-5 à J-1 proches, jour J et retard distingués ; seulement reste positif, paiement partiel conserve alerte, solde complet la supprime. Reprises incluses. Calcul backend et affichage Dashboard sont présents, couverts par FinancialDueAlertServiceTests. Ouverture d'une pièce et présence dans pages concernées restent L11. Aucun besoin de notification externe n'est inventé.

## 9. Utilisateurs, tiers, navigation, recherche

Comptes : premier Responsable, création Employé, désactivation/réactivation, reset mot de passe, configuration des sept modules, interdiction des fonctions globales à Employé et préservation historique sont présents. Protection du dernier Responsable et revalidation de l'acteur sur administration existent. Éditer nom/identité et profil reste L09 ; paramètres entreprise L10. Pas d'exigence retrouvée de suppression physique d'utilisateur.

Clients/fournisseurs : création, noms non uniques autorisés, coordonnées facultatives, recherche nom/téléphone/société/Identifier, pagination, modification et suppression contrôlée sont présentes. Les références par opérations, paiements ou reprises interdisent suppression ; les FK protègent les dépendances. L'identifiant technique auto ne remplace pas IdentifiantMetier obligatoire/unique prévu par DATA : L13, sans imposer CLI-/FOU-. La fiche financière n'existe pas et la consultation complète des contacts n'est pas exposée indépendamment d'Éditer : L02.

Navigation : les dix routes sont Dashboard, Ventes, Achats, Dépenses, Clients, Fournisseurs, Paiements clients, Paiements fournisseurs, Trésorerie, Administration. Vues enregistrées dans App et résolues par Shell ; modules interdits masqués/refusés. Deux entrées de paiement au lieu d'une page à deux onglets UX p.10 §9 constituent une différence d'organisation, pas une fonction financière supplémentaire manquante. HOME-E, Paramètres, Catégories et Sauvegardes de l'inventaire UX p.17 restent absents (L08/L10/M06/B02).

| Module | Exigence de recherche/filtre validée | Réel |
|---|---|---|
| Clients/Fournisseurs | FM p.10 §9.2 ; UX p.9 §8 : retrouver tiers par coordonnées/ID | Recherche multi-champs et pagination présentes |
| Ventes/Achats | UX p.6 §5, p.7 §6 : référence/tiers, période, tiers, état règlement | Recherche texte/date/pagination présentes ; filtres structurés et états incomplets L01/L14 |
| Dépenses | UX p.8 §7 : période/catégorie/moyen | Texte/date/pagination ; manque catégorie/moyen structurés L14 |
| Paiements | UX p.10 §9 : sélection tiers, obligations et détail | Listes/lookup/filtrage tiers et allocations accessibles ; pas de manque de pagination démontré |
| Reprises initiales | FM p.12 §11 ; DATA p.9 : sélectionner tiers et positions | Listes et filtre tiers paginés ; aucun filtre additionnel non spécifié exigé |
| Alertes/trésorerie | UX p.5 §4 / p.11 §10 : accès contexte/source | L11/L04 ; pas de nouvelle obligation de pagination inventée |

L'affichage Page 0/0 est un point de présentation du backlog, pas un défaut de calcul ou un filtre manquant.

## 10. Parcours de release A–K

Statuts fondés sur les chemins de code et les tests antérieurs du backend, **sans nouvelle UAT manuelle**. COMPLETE signifie que le trajet décrit est réalisable ; cela ne certifie pas les variantes de détail/édition/suppression listées ailleurs.

| Parcours | Statut | Chemin disponible / limite |
|---|---|---|
| A. Premier démarrage → Responsable → ouverture | COMPLETE | Bootstrap puis login et ouverture ; reprise après bootstrap partiel. Configuration entreprise séparément manquante L10 |
| B. Client → vente → paiement partiel → créance → solde | COMPLETE | Créer Client/Vente ; paiement initial ou module Paiements ; reste dans obligations/Dashboard ; règlement final. Pas de fiche financière tiers complète L02 |
| C. Fournisseur → achat → paiement partiel → dette → solde | COMPLETE | Symétrique B, fournisseur obligatoire |
| D. Dépense → trésorerie/résultat | COMPLETE | Catégorie existante et cash/bank ; calculs raccordés. Variante salaire sans employé non conforme M05 ; ne pas confondre calcul complet et toutes validations complètes |
| E. Avance client | INCOMPLETE | Surplus et affectation ultérieure complets ; paiement entièrement en avance impossible M07 ; choix intégré à vente absent L03 |
| F. Avance fournisseur | INCOMPLETE | Symétrique E, M08/L03 |
| G. Reprise créance initiale → règlement | COMPLETE | Administration → créance ; paiement/avance partiel puis solde ; stocks/alertes dérivés |
| H. Reprise dette initiale → règlement | COMPLETE | Administration → dette ; symétrique G |
| I. Transfert interne | COMPLETE | Création dans Trésorerie, impacts opposés et total invariant ; historique/suppression sont variantes manquantes L04/D06 |
| J. Configurer permissions → usage restreint Employé | INCOMPLETE | Configuration et restriction des modules fonctionnent ; visibilité propriétaire violée B01 et éditions autorisées impossibles M01–03 |
| K. Sauvegarde → changer données → restaurer utilisateurs/finances | INCOMPLETE | Service testé mais pas de parcours Desktop B02 ; aucune manipulation de données entreprise réalisée pour le démontrer |

Tous ces parcours appartiennent au périmètre V1 ; aucun n'est classé NOT REQUIRED.

## 11. Réponses aux dix suspects imposés

| Suspect | Adjudication |
|---|---|
| A. Sales modification missing? | Oui, explicitement requise ; M01 |
| B. Purchases modification missing? | Oui, explicitement requise ; M02 |
| C. Expenses modification missing? | Oui, explicitement requise ; M03 |
| D. Permanent financial deletion missing? | Oui, six parcours distincts D01–D06 ; ne pas ajouter les reprises par déduction du bit |
| E. Backup/Restore UI complete? | Non ; backend-only, B02 |
| F. Initial receivable/payable UI complete? | Oui pour création/liste/règlement/dates/stocks/alertes ; N03, A02 uniquement pour CRUD direct non précisé |
| G. Payment advance allocation UI complete? | Oui pour affectation ultérieure, y compris reprises ; ne pas confondre avec avance entièrement non affectée à la création M07/M08 |
| H. Employee same-day/ownership modification implementable? | Pas actuellement : aucune édition des trois objets ; règle MISSING. Lecture propre également manquante B01 |
| I. Any validated module entirely missing? | Sous-écrans Catégories, Paramètres, Sauvegardes et accueil Employé absents ; le service Backup existe, donc pas « module backend entièrement absent » |
| J. Required Responsible-only action backend-only? | Sauvegarde locale/externe, Restore et sa sauvegarde de sécurité : oui. Les initiales sont raccordées ; suppressions/gestion catégories n'ont même pas de service public réalisant le flux |

## 12. Carte de couverture permanente

Racine des tests : [FinancialManagement.Tests](/C:/Dev/logiciel-gestion-financiere/tests/FinancialManagement.Tests). Les noms ci-dessous sont des fichiers existants. Le projet teste Domain/Application/Infrastructure, sans référence Desktop : couverture de services, pas test automatisé des parcours WPF. Le PASS global doit être lu avec les contradictions ci-dessous.

| Fonction V1 | Tests permanents pertinents | État de couverture |
|---|---|---|
| Bootstrap, premier R, initialisation incomplète/concurrence | ApplicationInitializationServiceTests | Implémenté + testé ; nom entreprise non implémenté |
| Login, actif/inactif, hash, logout | AuthenticationServiceTests, PasswordHasherTests | Implémenté + testé ; révocation immédiate non exigée |
| Comptes, reset, état, droits, dernier R | UserAdministrationServiceTests | Implémenté + testé sur API disponibles ; édition profil absente |
| Capacités V/C/M et fonctions R | AuthorizationServiceTests | Implémenté + testé ; ne prouve pas ownership des lectures/éditions |
| Clients et fournisseurs CRUD, recherche, pagination, restrictions suppression | ClientServiceTests, SupplierServiceTests | Implémenté + testé ; auto ID/historique UI absents |
| Création vente connue/anonyme, règlement initial/avance, atomicité | SaleServiceTests | Implémenté + testé |
| Création achat, paiement initial, dette/avance, atomicité | PurchaseServiceTests | Implémenté + testé |
| Lectures ventes/achats et lookups | SalesReadClientLookupTests, PurchaseReadSupplierLookupTests | Testé mais tests inter-utilisateurs contredisent B01 |
| Dépenses, catégorie/moyen/employé, trésorerie | ExpenseServiceTests | Testé mais salaire null et lecture tous utilisateurs contredisent M05/B01 ; date d'entrée/M/D absents |
| Paiements multiples, partiels, soldes, surplus, mauvais tiers, rollback | CustomerPaymentServiceTests, SupplierPaymentServiceTests | Implémenté + testé ; tests Missing_allocations_are_rejected contredisent le cas avance entière DATA §16 |
| Listes/détails paiements, lookup obligations | PaymentReadServiceTests, PaymentObligationLookupServiceTests | Implémenté + testé backend ; observation d'affichage U06 non couverte visuellement |
| Affectation avance partielle/complète, répartie, reprises, permissions/concurrence | AdvanceServiceTests | Implémenté + testé ; nouveau mouvement de trésorerie évité |
| Reprises, accès R, création sans activité artificielle | InitialObligationServiceTests | Implémenté + testé |
| Reprises listées avec original/reste et règlements | InitialCustomerReceivableReadServiceTests, InitialSupplierPayableReadServiceTests | Implémenté + testé ; vues retrouvées, pas de test Desktop permanent |
| Ouverture/transfert/négatifs/invariant total | TreasuryServiceTests, TreasuryOpeningInitializationTests | Implémenté + testé ; correction d'ouverture, Delete/historique absents |
| 11 KPI, flux/stocks/avances sans double comptage | FinancialDashboardServiceTests | Implémenté + testé ; graphiques et stocks avances affichés non implémentés |
| Snapshot cohérent Dashboard | FinancialDashboardSnapshotTests | Implémenté + testé ; ASTRA-003 reste CLOSED |
| Alertes J-5, aujourd'hui/retard, partiel/solde/reprises/avances | FinancialDueAlertServiceTests | Implémenté + testé ; navigation/contextes non implémentés |
| Backup/restore contenu, échec, sécurité, permissions, compatibilité | DatabaseBackupServiceTests, DatabaseBackupAuthorizationTests, DatabaseBackupSchemaTests, DatabaseBackupFinancialStorageTests, DatabaseBackupAllocationIntegrityTests | Backend implémenté + testé ; UI et orchestration quotidienne/pré-update absentes |
| Schéma/migrations/money/dépendances | FinancialManagementSchemaTests, FinancialManagementMigrationTests, FinancialManagementDbContextTests, DomainModelTests | Implémenté + testé ; n'atteste pas installateur A04 |
| Allocations, corruption et invariants financiers | AllocationIntegrityTests, FinancialStorageIntegrityTests, DaySixIntegrationTests | Implémenté + testé sur périmètre existant ; ASTRA-004/005 restent CLOSED |
| M ventes/achats/dépenses et D financiers | Aucun service de réalisation ; aucun test de ces parcours retrouvé | Required but unimplemented |
| Catégories CRUD, paramètres, édition profil, auto ID | Pas de couverture de ces parcours absents | Required but unimplemented |
| Navigation/vue/formulaire WPF | Sources inspectées ; UAT Phase 1/2 antérieure fournie | Implemented but weakly tested automatiquement ; preuves manuelles limitées aux observations antérieures, pas de nouveau PASS attribué ici |

Quatre familles empêchent de lire « 1090 PASS » comme une attestation de conformité totale : lectures inter-utilisateurs attendues par les tests, Salaires sans employé accepté, avances entièrement non affectées rejetées, fonctions requises sans code ni tests. Aucun test n'a été modifié pour masquer ces écarts.

## 13. Backlog UAT séparé, sans blocage fonctionnel ajouté

Ces observations sont enregistrées sans nouvelle reproduction visuelle et sans correction. Le préfixe CLI-/FOU- n'est pas ajouté aux exigences.

| ID | Observation UAT | Décision dans cet audit | Classification unique |
|---|---|---|---|
| U01 | Chevauchement disposition alertes Dashboard | Présentation ; calcul d'alertes distinct | UX POLISH |
| U02 | Actualisation automatique souhaitée | Boutons/chargements existent ; aucune obligation de rafraîchissement temps réel nouvellement imposée | UX POLISH |
| L13 (renvoi) | Identifiants Client/Fournisseur automatiques souhaités | Conserver dans backlog sans en faire un blocage ; exigence auto/unique néanmoins explicite dans DATA/FM/UX. Lead doit concilier backlog et texte validé ; classement unique déjà fixé en section 4 | Voir L13, pas de second constat |
| U03 | Page 0/0 en pagination vide | Présentation, pas absence de pagination | UX POLISH |
| U04 | Œil afficher/masquer mot de passe | Prévu UX p.4 §3.1 mais expressément maintenu hors blocages par la demande | UX POLISH |
| U05 | Doublon Fermer/Annuler de modale | Présentation | UX POLISH |
| U06 | Détail paiement « Aucune affectation » malgré affectation | Observation d'affichage à conserver ; service détail renvoie les allocations. Aucun défaut de persistance déduit de ce libellé, aucun parcours manuel refait | UX POLISH |
| U07 | Date fin Dashboard devrait être aujourd'hui local | Déjà affectée à localToday, DashboardViewModel lignes 58–60 ; pas de reproduction d'un état divergent ici | NOT A GAP |

Preuve U07 : [DashboardViewModel](/C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Desktop/ViewModels/DashboardViewModel.cs:58).

## 14. Décisions à prendre et portée du verdict

**Blocages confirmés : B01, B02, B03.** Leurs impacts respectifs sont l'exposition de données d'autres créateurs, l'impossibilité utilisateur de sauvegarder/restaurer, et le défaut de point de récupération avant migration.

**Exigences à réaliser avant RC : M01–M09 et D01–D06.** Elles concernent les trois éditions, la correction d'ouverture, la condition salaire, la gestion des catégories, les deux avances pures, l'automatisation locale des sauvegardes et six suppressions financières. Ce classement n'affirme pas que chaque absence détruit les données actuelles : il affirme que ces fonctions explicitement validées ne sont pas livrées.

**Dérogations possibles uniquement avec lead : L01–L16.** Les vues financières, proposition intégrée d'avance, historique trésorerie, graphiques, indicateurs d'avance, raccourcis, accueil Employé, édition comptes, paramètres, alertes contextuelles, date dépense, auto ID, filtres, logs et détail dépense ne doivent pas être silencieusement déclarés hors V1. La demande place déjà l'auto ID dans le backlog non bloquant ; la divergence documentaire est signalée sans le promouvoir en release blocker.

**Ambiguïtés à arbitrer : A01–A05**, plus fourniture du cahier V1 validé manquant. Aucun nouveau périmètre de CRUD ou de restriction propriétaire/jour n'est inventé. Pour A04, obtenir la preuve d'artefact et des gates de livraison est distinct d'exiger un nouveau système de déploiement.

**Optional / V1.1 : O01–O03 uniquement.** Aucun ajout de produit ou wishlist n'est proposé.

**V1 FUNCTIONAL COMPLETENESS: FAIL** : écarts positivement établis aux documents validés, même si le cahier validé manque. HOLD seul sous-estimerait ces absences confirmées.

**RELEASE UAT MAY CONTINUE: NO** : retour au lead pour correction autorisée ou décision formelle de périmètre avant poursuite de la qualification de release. Les phases UAT antérieures et les 1090 tests ne sont pas annulés ; ils ne couvrent pas les fonctionnalités absentes ni les exigences contradictoires relevées.

## 15. Sécurité et état Git final

- Production files modified: **NONE**.
- Test source modified: **NONE**.
- Release DB modified: **NO** ; ni ouverte ni utilisée dans cet audit.
- Migrations created/executed: **NONE**.
- Commit/push/merge/reset/rebase/clean/staging: **NONE**.
- Seul fichier créé par cet audit : **UAT-V1-COMPLETENESS-AUDIT.md**, local et non suivi.
- HEAD final : **fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba**.
- Branche finale : **release/v1.0-rc**.
- Fichiers suivis et index : propres ; aucun diff.

État final vérifié par git status --short :

    ?? ASTRA-003-REPORT.md
    ?? ASTRA-004-REPORT.md
    ?? ASTRA-005-REPORT.md
    ?? UAT-RBAC-AUDIT.md
    ?? UAT-V1-COMPLETENESS-AUDIT.md
    ?? UAT-V1-REPORT.md
    ?? doc/

Les autres rapports et doc/ étaient déjà non suivis avant cet audit.

**STOP — rapport produit ; aucun correctif ni nouvelle UAT manuelle entrepris.**

