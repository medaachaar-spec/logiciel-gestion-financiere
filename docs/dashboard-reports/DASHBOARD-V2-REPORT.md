# DASHBOARD V2 — RAPPORT D'IMPLÉMENTATION

Date de validation : 2026-09-09 (Africa/Casablanca)

## 1. Starting hash

`fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba`

La référence distante `origin/release/v1.0-rc` a été récupérée en lecture seule et confirmée exactement sur ce hash. Le commit était absent du clone local au démarrage.

## 2. Branch

`feature/dashboard-v2`, créée directement depuis le hash gelé. La branche `release/v1.0-rc` n'a pas été modifiée.

## 3. Files changed

Modifiés :

- `src/FinancialManagement.Application/Dashboard/FinancialDashboardQuery.cs`
- `src/FinancialManagement.Application/Dashboard/FinancialDashboardResult.cs`
- `src/FinancialManagement.Desktop/FinancialManagement.Desktop.csproj`
- `src/FinancialManagement.Desktop/ViewModels/DashboardMetricViewModel.cs`
- `src/FinancialManagement.Desktop/ViewModels/DashboardViewModel.cs`
- `src/FinancialManagement.Desktop/ViewModels/DueAlertsViewModel.cs`
- `src/FinancialManagement.Desktop/ViewModels/ShellViewModel.cs`
- `src/FinancialManagement.Desktop/Views/DashboardView.xaml`
- `src/FinancialManagement.Desktop/Views/DueAlertsView.xaml`
- `src/FinancialManagement.Infrastructure/Dashboard/FinancialDashboardService.cs`
- `tests/FinancialManagement.Tests/FinancialDashboardServiceTests.cs`

Ajoutés et non suivis :

- `src/FinancialManagement.Application/Dashboard/FinancialDashboardComparison.cs`
- `src/FinancialManagement.Application/Dashboard/FinancialDashboardDailyPoint.cs`
- `src/FinancialManagement.Application/Dashboard/FinancialDashboardPartyBalance.cs`
- `src/FinancialManagement.Desktop/ViewModels/DashboardPartyBalanceViewModel.cs`
- `src/FinancialManagement.Desktop/ViewModels/DashboardPeriodOption.cs`
- `DASHBOARD-V2-REPORT.md`

## 4. Current Dashboard architecture found

Le Dashboard existant utilisait un `DashboardViewModel`, une vue WPF dédiée et un `IFinancialDashboardService`. Le service lisait tous les indicateurs au sein d'une transaction SQLite différée unique, garantissant un snapshot WAL cohérent (ASTRA-003), et déléguait les soldes de trésorerie au service métier existant. La présentation se limitait à des cartes numériques et à deux sélecteurs de dates. Les alertes d'échéance étaient un composant séparé avec deux tableaux larges.

## 5. Chart library decision

Choix : `LiveChartsCore.SkiaSharpView.WPF` version épinglée `2.0.5`.

Motifs : bibliothèque WPF native maintenue, rendu moderne via SkiaSharp, aucune dépendance WebView/runtime web, fonctionnement hors ligne après publication, support WPF et compatibilité NuGet calculée pour `net10.0-windows`. Le TFM Desktop est devenu `net10.0-windows10.0.19041` afin d'indiquer explicitement Windows 10 et de suivre la recommandation LiveCharts/SkiaSharp. La dépendance et toute la configuration de séries restent limitées au projet Desktop et au Dashboard.

Références d'évaluation :

- https://www.nuget.org/packages/LiveChartsCore.SkiaSharpView.WPF/2.0.5
- https://livecharts.dev/docs/wpf/2.0.0/Overview.Installation

Audit NuGet Desktop : aucun package vulnérable connu, dépendances transitives incluses.

## 6. New/extended read models

- `FinancialDashboardQuery` accepte une période de comparaison complète et optionnelle.
- `FinancialDashboardResult` expose les totaux de comparaison, cinq séries quotidiennes fusionnées, les tops d'encours et le nombre de tiers concernés.
- `FinancialDashboardDailyPoint` porte les cinq agrégats réels pour une date.
- `FinancialDashboardPartyBalance` porte un encours actuel agrégé par tiers.
- `FinancialDashboardComparison` produit un pourcentage descriptif nullable et neutre quand le dénominateur vaut zéro.

## 7. Queries added

Dans le snapshot commun :

- cinq requêtes SQL `GROUP BY` date pour la période sélectionnée (ventes, achats, dépenses, encaissements clients, paiements fournisseurs) ;
- trois requêtes SQL `GROUP BY` date pour la comparaison (ventes, achats, dépenses) ;
- aucune lecture intégrale supplémentaire pour les graphiques ;
- les tops sont dérivés pendant les lectures d'obligations déjà nécessaires au calcul et au contrôle anti-surallocation ;
- aucun N+1, aucune écriture, aucun nouveau stockage de solde.

Pour les appels historiques sans comparaison, le nombre de lectures du snapshot testé reste inchangé à 26.

## 8. Financial semantics preserved

- Résultat = Ventes - Achats - Dépenses.
- Ventes, achats, dépenses, encaissements et paiements restent des flux filtrés par période.
- Créances, dettes, caisse, banque et trésorerie totale restent des positions actuelles indépendantes de la période.
- Trésorerie totale provient toujours du service de trésorerie existant et respecte Caisse + Banque.
- Les transferts internes ne touchent ni le résultat ni la trésorerie totale.
- Une affectation ultérieure d'avance ne crée aucun nouvel encaissement/paiement.
- Les contrôles de surallocation par obligation restent actifs.
- Aucun seuil qualitatif arbitraire n'a été ajouté.

## 9. Selected-month behavior

À l'ouverture, la période est le mois local courant complet (du premier au dernier jour), avec le mois précédent en comparaison. Le `TimeProvider` et la date locale métier sont relus lors de chaque retour au Dashboard. Si le mois local a changé et que l'utilisateur suivait le mois courant, la sélection est reconstruite : aucune ancienne `EndDate` ne peut rester bloquée sur la veille. Le sélecteur couvre dix années passées et douze mois futurs ; le bouton `Ce mois` revient immédiatement au mois métier courant.

## 10. Comparison behavior

La comparaison est sélectionnable indépendamment et se recale par défaut sur le mois précédent à chaque changement de période principale. Les quatre KPI affichent un delta descriptif. Si la valeur de comparaison vaut zéro, l'UI affiche `—`, jamais NaN ou Infinity. Les valeurs négatives du résultat utilisent la valeur absolue du dénominateur pour conserver une direction descriptive intuitive. Aucune comparaison n'est affichée pour les stocks actuels.

## 11. KPI implementation

Quatre cartes principales : VENTES, ACHATS, DÉPENSES, RÉSULTAT. Chaque carte présente un pictogramme cohérent, un montant fortement hiérarchisé, un delta et la légende `vs <mois>`. Le format monétaire existant `MoneyFormatter` est réutilisé ; aucune devise n'est codée en dur.

## 12. Activity chart

Graphique LiveCharts lissé à trois séries (Ventes, Achats, Dépenses), axe X quotidien, axe Y monétaire sans distorsion, remplissage très léger sur la série principale et infobulle contenant date + montant, le nom de la série étant fourni par la légende/infobulle LiveCharts. Les jours sans opération sont explicitement remplis avec zéro à partir des faits persistés.

## 13. Collections/payments chart

Second graphique quotidien à deux séries (Encaissements clients, Paiements fournisseurs), avec même sémantique d'axes et d'infobulles. Les consommations d'avances ne sont pas dupliquées parce que la série agrège exclusivement les paiements persistés d'origine.

## 14. Receivables panel

Le panneau affiche l'encours total actuel, le nombre de clients dont l'encours est strictement positif, les cinq plus gros encours agrégés et une action vers les encaissements clients. Les ventes anonymes restent incluses dans le total financier, mais ne sont naturellement pas présentées comme un client nommé.

## 15. Payables panel

Le panneau fournisseur est symétrique : total actuel, nombre de fournisseurs concernés, cinq plus gros encours et action vers les paiements fournisseurs.

## 16. Alert redesign

Les calculs existants sont inchangés. La vue présente trois compteurs compacts (retard, aujourd'hui, à venir), puis deux listes équilibrées. Chaque ligne utilise trois zones adaptatives (tiers/référence/date, montant, statut) au lieu des cinq colonnes à largeurs minimales responsables du chevauchement.

## 17. Responsive behavior

Le contenu reste dans un `ScrollViewer` vertical sans défilement horizontal. KPI et positions utilisent des grilles uniformes ; graphiques et panneaux opérationnels utilisent des colonnes étoilées symétriques et un espacement réduit. Les textes longs sont tronqués avec info-bulle ou retournés à la ligne selon leur rôle. La fenêtre minimale existante 1024×640 est respectée.

## 18. Empty states

Une base vide affiche des cartes à zéro, des surfaces de graphique calmes avec un message explicite, des listes d'encours vides et une carte d'échéances sans alerte. Aucune donnée de démonstration, valeur aléatoire ou tendance fabriquée n'est produite.

## 19. Tests added/modified

Ajouts ciblés dans `FinancialDashboardServiceTests` :

- période sélectionnée et mois de comparaison de longueurs différentes ;
- séries quotidiennes Ventes/Achats/Dépenses/Encaissements/Paiements ;
- jours vides normalisés à zéro ;
- mois entièrement vide ;
- dénominateur de comparaison nul ;
- comparaison avec résultat négatif ;
- tops clients/fournisseurs, agrégation et limite de cinq ;
- maintien des totaux de stocks.

Les tests permanents existants couvrent toujours : indépendance période/stocks, invariant de trésorerie, transferts internes, absence de double comptage des avances, corruption par surallocation et cohérence du snapshot.

## 20. Test totals

- Baseline historique : 1090.
- Nouveau total : 1096 (+6 cas exécutés, dont une théorie à quatre variantes).
- Tests ciblés service Dashboard : PASS — 27/27, SKIP 0.
- Tests ciblés snapshot ASTRA-003 : PASS — 19/19, SKIP 0, dont 50 bases WAL indépendantes.
- Première suite complète : 1095 PASS, 1 FAIL intermittent hors périmètre (`UserAdministrationServiceTests.Concurrent_employee_creations_with_same_username_return_controlled_results`), 0 SKIP. Rejeu isolé immédiat : PASS 1/1.
- Suite complète finale via `verify-foundation` : PASS — 1096/1096, FAIL 0, SKIP 0, durée 6 min 12 s.

## 21. Build result

PASS — `dotnet build FinancialManagement.sln`

- warnings : 0
- errors : 0

## 22. verify-foundation result

PASS — les 7 étapes ont réussi : tool restore, restore, build, tests, liste des migrations, détection de changement du modèle EF et audit de vulnérabilités Infrastructure.

Audit additionnel Desktop avec transitifs : PASS, aucun package vulnérable connu.

## 23. Migrations before/after

Avant : 001, 002, 003, 004.

Après : 001, 002, 003, 004.

`dotnet ef migrations has-pending-model-changes` : `No changes have been made to the model since the last migration.`

Aucune migration ni modification de schéma.

## 24. Git status

Branche : `feature/dashboard-v2`.

Le HEAD reste le hash de départ `fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba`. Tous les changements sont locaux, non indexés ; aucun `git add`, commit, push, merge, rebase, reset ou clean n'a été exécuté. Le rapport est volontairement non suivi.

## 25. Known limitations

- Aucun screenshot natif n'a pu être capturé : la session de contrôle Windows ne proposait aucune application native, uniquement le navigateur intégré.
- Les raccourcis `Aujourd'hui` et `Cette année` n'ont pas été ajoutés afin de conserver le workflow mensuel validé et des axes quotidiens lisibles ; `Ce mois` est disponible.
- Les boutons `Voir les créances/dettes` ouvrent les écrans opérationnels d'encaissements/paiements existants, faute de page dédiée aux encours consolidés.

## 26. Precise visual description

Le haut de page présente `TABLEAU DE BORD`, une phrase d'aide et un bouton d'actualisation iconographique. Une carte de filtre blanche arrondie regroupe deux sélecteurs mois/année, `Appliquer` en bleu et `Ce mois` en action secondaire. Viennent quatre grandes cartes KPI aérées, puis cinq cartes plus compactes pour les positions actuelles avec pictogrammes bleu sobre. Deux cartes graphiques de même largeur affichent des courbes fines et lissées, une grille gris clair, une légende inférieure et des aplats à très faible opacité. Deux panneaux d'encours symétriques montrent ensuite les cinq principaux tiers. La carte finale d'échéances affiche trois pastilles de synthèse puis deux listes compactes ; le rouge est réservé aux retards, l'ambre à aujourd'hui et le bleu aux échéances futures. Les fonds, bordures, rayons, typographie et interactions réutilisent la palette existante de l'application.

## 27. DASHBOARD V2 RESULT

**PASS**

---

# FINAL UI/UX POLISH

Date de validation technique : 2026-09-09 (Africa/Casablanca)

## 1. Fichiers modifiés par ce polish

- `src/FinancialManagement.Desktop/ViewModels/DashboardMetricViewModel.cs`
- `src/FinancialManagement.Desktop/ViewModels/DashboardViewModel.cs`
- `src/FinancialManagement.Desktop/ViewModels/ReportComparisonRowViewModel.cs`
- `src/FinancialManagement.Desktop/ViewModels/ReportsViewModel.cs`
- `src/FinancialManagement.Desktop/Views/DashboardView.xaml`
- `src/FinancialManagement.Desktop/Views/ReportsView.xaml`
- `DASHBOARD-V2-REPORT.md` (rapport non suivi)

Aucun service financier, fichier d'autorisation, modèle EF, migration ou écran hors périmètre n'a été modifié pendant cette passe.

## 2. Refonte des onglets Rapports

Le `TabControl` natif est conservé pour son comportement clavier et sa gestion du focus, mais reçoit un template scoped moderne : groupe segmenté arrondi, fond gris très léger, espacement interne régulier, survol discret et état actif bleu avec texte blanc. Les trois sections restent exactement Synthèse, Comparaison et Évolution.

## 3. Cadre extérieur

Le cadre natif page-dans-page du TabControl a été supprimé. La page utilise désormais un layout ouvert : en-tête, navigation segmentée, puis cartes uniquement autour des filtres, KPI, graphiques et matrice qui constituent de vrais blocs fonctionnels.

## 4. Espacement et densité

Les marges verticales sont normalisées à 10–14 px entre sections, les filtres utilisent une carte légère sans ombre, les KPI gardent un padding cohérent de 16 px et les graphiques une hauteur minimale réduite à 286 px. Les contrôles de filtre restent à 36 px, avec cibles confortables et alignement stable.

## 5. Libellés Comparaison

- `Période A` devient `Mois analysé`.
- `Comparer à` devient `Mois de comparaison`.
- Le bouton reste `Comparer`.
- Les en-têtes de matrice reprennent les mêmes termes métier.

## 6. Matrice de comparaison

La grille est maintenant une matrice analytique compacte : aucun cadre lourd, fond transparent, en-tête gris clair, séparateurs horizontaux fins, lignes de 46 px, typographie sobre, indicateur semi-gras et valeurs monétaires alignées à droite. La hauteur minimale vide de 330 px a été retirée ; les six lignes déterminent naturellement la hauteur du composant. La grille reste strictement en lecture seule.

## 7. Style sémantique Résultat

Une propriété de présentation reçoit uniquement le montant mathématique de Résultat :

- positif : vert existant + graisse Bold ;
- négatif : rouge existant + graisse Bold ;
- zéro : couleur neutre existante + graisse standard du KPI.

La règle s'applique au KPI Dashboard, à Synthèse, aux totaux annuels Évolution et aux deux montants Résultat de la matrice Comparaison. La variation en pourcentage et les autres indicateurs, notamment Achats et Dépenses, restent neutres.

## 8. Synthèse

Le filtre est plus cohérent et libellé `Période analysée`. Les six KPI partagent le même template, les cartes de graphiques sont alignées, les marges sont resserrées sans modifier les presets ni les données.

## 9. Évolution

Le filtre est libellé `Année analysée`, avec espacement régulier avant `Afficher`. Les six totaux annuels réutilisent le template Synthèse et les deux graphiques mensuels sont alignés à hauteur identique. L'agrégation janvier–décembre reste inchangée.

## 10. États vides

Les quatre graphiques Rapports distinguent désormais l'absence complète de valeurs réelles :

- activité vide : `Aucune activité sur cette période` ou `Aucune activité pour cette année` ;
- flux vides : `Aucun flux sur cette période` ou `Aucun flux pour cette année`.

Si au moins une série contient une valeur non nulle, le graphique réel reste affiché normalement. Aucune donnée n'est inventée.

## 11. CTA Créances Dashboard

`Voir les créances` devient une action texte alignée en bas à droite, avec chevron moderne, couleur d'accent, fond transparent, séparateur supérieur très léger et survol bleu pâle. La commande et la destination existantes sont inchangées.

## 12. CTA Dettes Dashboard

`Voir les dettes` reçoit exactement le même traitement secondaire, avec alignement symétrique et navigation inchangée.

## 13. Validation responsive

Validation structurelle WPF : compilation XAML réussie, aucun défilement horizontal ajouté, filtres en `WrapPanel`, zones de contenu en colonnes étoilées, matrice compatible avec la largeur utile minimale et défilement vertical conservé par onglet. La validation visuelle réelle à 1366×768 et à la taille minimale reste explicitement à effectuer par le lead.

## 14. Build

**PASS** — `dotnet build FinancialManagement.sln` avec sortie temporaire pour ne pas fermer l'instance Desktop éventuellement ouverte :

- avertissements : 0 ;
- erreurs : 0 ;
- durée : 29,81 s.

Compilation Desktop XAML isolée supplémentaire : PASS, 0 avertissement, 0 erreur, 16,77 s.

## 15. Tests ciblés

- Dashboard : PASS 31/31, FAIL 0, SKIP 0.
- Rapports : PASS 6/6, FAIL 0, SKIP 0.
- Autorisation : PASS 15/15, FAIL 0, SKIP 0.
- Smoke WPF navigation : PASS 6/6.
- Smoke états de signe Résultat : PASS 8/8.

## 16. Suite complète

**PASS 1107/1107**, FAIL 0, SKIP 0, durée 3 min 53 s. Le baseline permanent reste inchangé.

## 17. Migrations

Inchangées : 001, 002, 003 et 004 uniquement. Aucune migration 005.

## 18. Schéma

Inchangé. Aucun modèle EF ou snapshot de modèle modifié, aucune base réelle ouverte ou écrite pendant ce polish.

## 19. Git status

Tous les travaux Dashboard V2.1 + Reports V1 restent locaux, non indexés et non committés sur `feature/dashboard-v2`. `git diff --check` : PASS. Aucun add, commit, push, merge, reset, rebase ou clean.

## 20. MANUAL VISUAL REVIEW REQUIRED

**YES** — Dashboard, Rapports > Synthèse, Comparaison et Évolution doivent être inspectés manuellement à 1366×768. Aucune acceptation visuelle finale n'est revendiquée.

Commande normale :

```powershell
dotnet run --project src/FinancialManagement.Desktop/FinancialManagement.Desktop.csproj
```

## 21. FINAL TECHNICAL RESULT

**PASS**

---

# DASHBOARD V2.1 + REPORTS V1

Date de validation technique : 2026-09-09 (Africa/Casablanca)

## 1. Starting hash / branch

- Hash gelé et HEAD inchangé : `fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba`.
- Branche : `feature/dashboard-v2`.
- Le travail Dashboard V2 existant a été conservé et refactoré. Aucun reset, clean, commit, push, merge, rebase ou `git add`.

## 2. Dashboard V2 réutilisé

Le service snapshot, les formules financières, LiveCharts, les cartes d'encours, les tops tiers, la trésorerie déléguée et le composant d'échéances V2 sont réutilisés. Rapports appelle le même moteur de lecture agrégée via une interface dédiée ; aucune formule financière concurrente n'a été créée.

## 3. Comparaison retirée du Dashboard

Le Dashboard ne contient plus `Comparer avec`, sélecteur de mois comparatif, libellé `vs <mois>` ni pourcentage sur les KPI. La logique de comparaison nullable est préservée et déplacée fonctionnellement dans Rapports > Comparaison.

## 4. Presets Dashboard finaux

Les quatre et seuls presets sont :

- Aujourd'hui ;
- Ce mois — valeur par défaut ;
- Cette année ;
- Personnalisé.

Confirmation explicite : **aucun preset hebdomadaire / « Cette semaine » n'existe**.

## 5. Comportement personnalisé

Les champs Date début / Date fin sont masqués pour les trois presets et visibles uniquement pour Personnalisé. Les deux dates sont obligatoires et la plage inversée est refusée. Une plage personnalisée reste inchangée lors des retours au Dashboard pendant la session ; seuls un changement de preset ou une nouvelle application la remplace.

## 6. Granularité des graphiques

- Aujourd'hui : un point journalier réel et soigné, sans données horaires inventées.
- Ce mois : points quotidiens, jours sans faits à zéro.
- Cette année : 12 points mensuels, janvier à décembre.
- Personnalisé : quotidien jusqu'à **62 jours inclus**, mensuel au-delà.

Les mêmes règles alimentent `Évolution de l'activité` et `Encaissements et paiements`. Les regroupements mensuels sont exécutés en SQL/EF avant matérialisation ; aucune histoire annuelle complète n'est chargée en mémoire pour être agrégée.

## 7. Densité du premier écran

Le filtre est devenu une barre segmentée compacte ; les dates personnalisées ne réservent aucun espace hors mode Personnalisé. Les marges, hauteurs KPI et cartes graphiques ont été réduites avec retenue. L'ordre est désormais : en-tête, filtre, quatre KPI, graphiques, positions actuelles, panneaux détaillés, échéances. Les graphiques commencent donc avant les cinq cartes de position et nettement plus haut dans un viewport 1366×768.

## 8. Correction d'initialisation de période

L'ancien couple d'objets mois/année affichés a été supprimé du Dashboard. Un unique `FinancialPeriodSelection` résout, depuis la date métier locale du `TimeProvider`, les bornes et la granularité transmises au backend. Le libellé affiché est construit depuis cette même sélection appliquée. Les presets relatifs sont recalculés au retour sur la page lorsque la date locale change ; Personnalisé est volontairement exclu de ce rafraîchissement automatique.

## 9. Nouvelle route Rapports

La navigation suit l'ordre validé : Tableau de bord, Ventes, Achats, Dépenses, Clients, Fournisseurs, Encaissements clients, Paiements fournisseurs, Caisse & Banque, **Rapports**, Administration.

`ReportsViewModel` / `ReportsView` proposent trois onglets distincts : Synthèse, Comparaison, Évolution.

## 10. Autorisation Rapports

Une capability dédiée `ResponsibleCapability.Reports` a été ajoutée, sans permission employé configurable et sans migration.

La protection existe à trois niveaux :

- visibilité de l'entrée dans la navigation ;
- contrôle de navigation directe dans `ShellViewModel` ;
- contrôle dans `ReportsViewModel` et dans le service `IFinancialReportsService`.

Les employés restent refusés même avec toutes les permissions configurables accordées.

## 11. Rapports — Synthèse

Synthèse analyse une période avec les quatre presets approuvés, sans semaine. Elle affiche Ventes, Achats, Dépenses, Résultat, Encaissements, Paiements et les deux graphiques issus du même read model que le Dashboard. Les positions actuelles n'y sont pas présentées comme des stocks historiques.

## 12. Rapports — Comparaison

Deux sélecteurs mois/année alimentent une requête snapshot unique pour Période A et Période B. La matrice couvre les six flux approuvés avec montants A/B et variation descriptive. Un dénominateur nul affiche `—`, jamais NaN/Infinity. Les résultats négatifs sont pris en charge sans jugement qualitatif.

## 13. Rapports — Évolution

Une année sélectionnée produit exactement janvier à décembre, avec mois vides à zéro, deux graphiques mensuels et six totaux annuels. Les positions actuelles Créances/Dettes/Caisse/Banque/Trésorerie ne sont pas affichées comme historiques.

## 14. Logique de comparaison réutilisée

`FinancialDashboardComparison.CalculatePercentage` est conservé. `FinancialDashboardQuery` garde sa période comparative optionnelle et le résultat partagé expose maintenant aussi les encaissements/paiements comparatifs requis par la matrice Rapports.

## 15. Read models nouveaux ou étendus

- `FinancialDashboardGranularity` : Daily / Monthly.
- `FinancialPeriodPreset` : Today / CurrentMonth / CurrentYear / Custom.
- `FinancialPeriodSelection` : bornes et règle déterministe de granularité.
- `IFinancialReportsService` : porte d'entrée responsable-only vers le moteur partagé.
- `FinancialDashboardQuery` : granularité et option d'exclure les positions actuelles.
- `FinancialDashboardResult` : granularité effective et deux flux comparatifs supplémentaires.
- `ReportsViewModel`, `ReportMonthOption`, `ReportComparisonRowViewModel` : présentation analytique Desktop.

## 16. SQL / requêtes

Une lecture de flux comprend cinq agrégats SQL : ventes, achats, dépenses, encaissements clients, paiements fournisseurs. Le regroupement est journalier ou mensuel selon le descripteur. Une comparaison lit les cinq mêmes familles pour B. Les appels Rapports utilisent `IncludeCurrentPositions: false` et évitent donc les lectures d'encours et de trésorerie inutiles. Aucun N+1, aucune écriture, aucune table de résultat et aucune migration.

## 17. Cohérence snapshot

Dashboard et Rapports utilisent la même implémentation `FinancialDashboardService`. Toutes les métriques liées d'une réponse, y compris A/B, sont lues sous une transaction SQLite différée commune et un même snapshot WAL. Les tests ASTRA-003 restent PASS 19/19, y compris les 50 bases WAL avec écriture concurrente réelle.

## 18. LiveCharts / Windows

- `LiveChartsCore.SkiaSharpView.WPF` reste épinglé en `2.0.5`, uniquement dans Desktop.
- TFM conservé : `net10.0-windows10.0.19041`.
- Aucun runtime Web et aucune hausse du minimum Windows.

## 19. Tests ajoutés / modifiés

Couverture permanente ajoutée : liste exacte des presets sans semaine, Aujourd'hui/Ce mois/Cette année, plage personnalisée courte/longue et validation, comparatif des six flux sur mois de longueurs différentes, agrégation mensuelle janvier–décembre avec mois nuls, totaux annuels, capability Rapports responsable-only, refus employé avec/sans toutes permissions, et réutilisation sans double comptage lors d'affectations d'avances.

Les tests existants d'indépendance stocks/période, trésorerie, résultat négatif, dénominateur zéro, période vide, bornes inclusives, paiements et snapshot restent actifs.

## 20. Résultats ciblés

- Tests de période : **PASS 7/7**, FAIL 0, SKIP 0.
- Tests service Dashboard (classe complète, y compris cas Rapports partagés) : **PASS 31/31**, FAIL 0, SKIP 0, 21 s.
- Tests explicitement nommés Rapports : **PASS 6/6**, FAIL 0, SKIP 0, 13 s.
- Tests snapshot Dashboard ASTRA-003 : **PASS 19/19**, FAIL 0, SKIP 0, 1 min 45 s.
- Lot ciblé Dashboard + période + autorisation : **PASS 53/53**, FAIL 0, SKIP 0.

## 21. Suite complète

Baseline V2 avant V2.1 : 1096.

Après V2.1 + Reports V1 : **PASS 1107/1107**, FAIL 0, SKIP 0. Exécution permanente indépendante : 5 min 03 s. Exécution Foundation : 7 min 38 s.

## 22. Build

**PASS** — compilation solution effectuée par Foundation avec sortie temporaire : 0 avertissement, 0 erreur, 44,97 s.

Une instance Desktop antérieure était ouverte (PID 18180) et verrouillait les DLL du dossier `bin` normal. Elle n'a pas été fermée. La même compilation Desktop vers `%TEMP%\FinancialManagement-V21-Build` a aussi passé avec 0 avertissement / 0 erreur. Aucun défaut source n'a été masqué.

## 23. Foundation

**PASS** — `scripts\verify-foundation.cmd`, 7/7 étapes : tool restore, restore, build, tests, migrations list, pending model changes, vulnérabilités. `OutDir` a été redirigé vers `%TEMP%\FinancialManagement-V21-Foundation` uniquement pour ne pas interrompre l'instance Desktop ouverte.

## 24. Migrations / schéma

- Liste inchangée : 001, 002, 003, 004 uniquement.
- `dotnet ef migrations has-pending-model-changes` : `No changes have been made to the model since the last migration.`
- Aucun fichier de migration ni modèle de base modifié.

## 25. Vulnérabilités

- Infrastructure, transitifs inclus : aucun package vulnérable connu.
- Desktop, transitifs inclus : aucun package vulnérable connu.

## 26. Fichiers modifiés

- `src/FinancialManagement.Application/Authorization/ResponsibleCapability.cs`
- `src/FinancialManagement.Application/Dashboard/FinancialDashboardQuery.cs`
- `src/FinancialManagement.Application/Dashboard/FinancialDashboardResult.cs`
- `src/FinancialManagement.Desktop/App.xaml`
- `src/FinancialManagement.Desktop/App.xaml.cs`
- `src/FinancialManagement.Desktop/FinancialManagement.Desktop.csproj`
- `src/FinancialManagement.Desktop/ViewModels/DashboardMetricViewModel.cs`
- `src/FinancialManagement.Desktop/ViewModels/DashboardViewModel.cs`
- `src/FinancialManagement.Desktop/ViewModels/DueAlertsViewModel.cs`
- `src/FinancialManagement.Desktop/ViewModels/ShellViewModel.cs`
- `src/FinancialManagement.Desktop/Views/DashboardView.xaml`
- `src/FinancialManagement.Desktop/Views/DueAlertsView.xaml`
- `src/FinancialManagement.Infrastructure/Dashboard/FinancialDashboardService.cs`
- `src/FinancialManagement.Infrastructure/InfrastructureServiceCollectionExtensions.cs`
- `tests/FinancialManagement.Tests/AuthorizationServiceTests.cs`
- `tests/FinancialManagement.Tests/FinancialDashboardServiceTests.cs`

## 27. Fichiers ajoutés non suivis

- `src/FinancialManagement.Application/Dashboard/FinancialDashboardComparison.cs`
- `src/FinancialManagement.Application/Dashboard/FinancialDashboardDailyPoint.cs`
- `src/FinancialManagement.Application/Dashboard/FinancialDashboardGranularity.cs`
- `src/FinancialManagement.Application/Dashboard/FinancialDashboardPartyBalance.cs`
- `src/FinancialManagement.Application/Dashboard/FinancialPeriodPreset.cs`
- `src/FinancialManagement.Application/Dashboard/FinancialPeriodSelection.cs`
- `src/FinancialManagement.Application/Dashboard/IFinancialReportsService.cs`
- `src/FinancialManagement.Desktop/ViewModels/DashboardPartyBalanceViewModel.cs`
- `src/FinancialManagement.Desktop/ViewModels/DashboardPeriodOption.cs`
- `src/FinancialManagement.Desktop/ViewModels/ReportComparisonRowViewModel.cs`
- `src/FinancialManagement.Desktop/ViewModels/ReportMonthOption.cs`
- `src/FinancialManagement.Desktop/ViewModels/ReportsViewModel.cs`
- `src/FinancialManagement.Desktop/Views/ReportsView.xaml`
- `src/FinancialManagement.Desktop/Views/ReportsView.xaml.cs`
- `tests/FinancialManagement.Tests/FinancialPeriodSelectionTests.cs`
- `DASHBOARD-V2-REPORT.md`

`DASHBOARD-V2-STARTUP-DIAGNOSIS.md` a été préservé sans suppression.

## 28. Git status

Branche `feature/dashboard-v2`, HEAD de départ inchangé. Tous les changements restent locaux et non indexés. `git diff --check` : PASS. Aucun commit/push/add/merge/rebase/reset/clean.

## 29. Limitations connues

- La matrice Comparaison V1 est volontairement le visuel analytique principal ; aucun petit graphique comparatif redondant n'a été ajouté.
- Les sélecteurs mois couvrent dix années passées et douze mois futurs ; Évolution expose l'année courante et les dix précédentes.
- L'instance Desktop actuellement ouverte contient l'ancien binaire chargé. Elle doit être fermée avant le lancement normal de cette nouvelle version.
- Aucune capture manuelle de cette version n'a été revue par le lead ; aucune acceptation visuelle n'est revendiquée.

## 30. Commande normale de lancement

Après fermeture de l'instance Desktop existante :

```powershell
dotnet run --project src/FinancialManagement.Desktop/FinancialManagement.Desktop.csproj
```

Les ACL ProgramData n'ont pas été modifiées.

## 31. MANUAL VISUAL REVIEW REQUIRED

**YES** — contrôle attendu à 1366×768 sur Dashboard (densité, quatre presets, custom, points quotidien/mensuel, positions, panneaux, échéances) et Rapports (trois onglets, matrice A/B, évolution annuelle).

## 32. FINAL TECHNICAL RESULT

**PASS**
