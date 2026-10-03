# TRESORIA V1.0 — SECURITY AUDIT V1

Audit en lecture seule, avant packaging. Date : 12 septembre 2026.

## 1. Synthèse exécutive

**Décision : B — FIX REQUIRED BEFORE V1.**

Le socle comporte de vraies protections : contrôles dans les services, hachage Identity, transactions SQLite, validation des allocations, restauration sur staging avec contrôle strict du schéma et sauvegarde préalable. L'autorisation ne se limite pas à masquer des boutons. Aucun contournement du mot de passe ni passage direct Employé → Responsable par les services publics examinés n'a été démontré.

Deux problèmes HIGH sont établis : une session d'employé reste utilisable après désactivation/réinitialisation du mot de passe ; les ACL observées autorisent la lecture de la base par des utilisateurs Windows étrangers à l'application. Cinq risques MEDIUM concernent la maintenance WAL, les agrégats financiers, les mots de passe faibles, les bases plus récentes et la provenance des sauvegardes.

| BLOCKER | HIGH | MEDIUM | LOW | INFO | Total |
|---:|---:|---:|---:|---:|---:|
| 0 | 2 | 5 | 5 | 3 | 15 |

Validation effectuée sur une extraction temporaire du commit demandé : **build Release réussi, 0 avertissement / 0 erreur ; 1 230 tests réussis / 0 échec / 0 ignoré** (1 183 métier/infrastructure et 47 Desktop). Contrôle de vulnérabilités NuGet sur les six projets et dépendances transitives : aucune vulnérabilité signalée par la source interrogée. Cela ne prouve pas l'absence de toute vulnérabilité, notamment native.

Le garde-fou filesystem confirme que le fichier de production DB est inchangé et que WAL/SHM restent absents. Aucun lancement de Tresoria en production, aucune ouverture de cette base par SQLite, aucun correctif, aucune migration de production et aucune modification d'ACL n'ont été réalisés.

## 2. Source de vérité et limites de preuve

- `git fetch origin` effectué avant l'analyse.
- **Commit audité : `41a113c41cbbe23b633893594362e452c15ff6c3`**, exactement `origin/integration`.
- `origin/release/v1.0-rc` : **`fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba`**, conforme à la demande ; aucune modification de cette branche.
- HEAD du dépôt de travail : `a9a04cd911c809c70eecc33cc6ad5febbf2dd440`. Il n'a pas été changé. Les fichiers de ce HEAD n'ont pas servi à auditer integration.
- Extraction créée avec `git archive` dans `C:\Users\HP\AppData\Local\Temp\tresoria-security-v1-8d6dd690060c46f79ff5b0ba54b9a094\source`.
- Les rapports non suivis déjà présents et le répertoire `doc/` ont été conservés. Ils ne remplacent pas les preuves du commit audité.
- Aucun `AGENTS.md` trouvé dans l'arbre audité ou les emplacements parents vérifiés. Compétence utilisée : `C:\Users\HP\.codex\skills\security-review-starter\SKILL.md`, avec priorité à l'interdiction utilisateur de corriger le code.

**Convention de preuves :** les chemins `src/...`, `tests/...` et `scripts/...` ci-dessous sont relatifs à l'arbre Git de ce SHA, avec numéros de ligne de cet arbre, pas du HEAD local. [Arbre immuable audité](https://github.com/medaachaar-spec/logiciel-gestion-financiere/tree/41a113c41cbbe23b633893594362e452c15ff6c3).

Les preuves sont distinguées entre **statique**, **reproduction temporaire**, **observation de cette machine** et **exigence future**. Aucun installateur final ni paquet autoportant livré au client n'existe dans le périmètre vérifié. Les ACL observées sont celles de cette machine de développement, pas une mesure chez EL-BARKA. L'audit de secrets porte sur l'arbre du commit ; ce n'est pas une certification exhaustive de tout l'historique Git ou des fichiers personnels hors dépôt.

## 3. Produit, actifs et acteurs

Application Windows locale, WPF/MVVM, .NET 10, EF Core et SQLite ; distribution x64 autoportante prévue. Les actifs sont la confidentialité des clients/fournisseurs et des finances, l'exactitude des paiements/allocations/trésorerie, les comptes et leurs hachages, la disponibilité, les sauvegardes, les reçus, les exécutables et la clé privée de signature future.

| Acteur | Capacité réaliste et scénario | Limite principale |
|---|---|---|
| Employé normal | Saisie, consultation et erreur de montant dans ses modules | RBAC de service et validation métier |
| Employé malveillant | Fausses saisies autorisées, tentative de routes interdites, conservation d'une session révoquée | Contrôles applicatifs et supervision métier |
| Responsable | Administration, réinitialisation des mots de passe, restauration, transferts | Opérations privilégiées voulues ; erreurs possibles |
| Ancien employé avec identifiants | Nouvelle connexion refusée si inactif, mais ancienne session encore exploitable | SEC-001 |
| Personne devant un poste déverrouillé | Utilise l'identité Windows/application déjà ouverte | Verrouillage Windows et discipline utilisateur |
| Utilisateur Windows standard non autorisé | Lecture/copie si ACL trop larges ; création de fichiers dans les répertoires accessibles | SEC-002 ; aucune hypothèse de privilège admin |
| Utilisateur Windows exécutant Tresoria | Possède les droits nécessaires sur SQLite | Même jeton utilisé par l'application et ses autres programmes |
| Administrateur Windows local | Prise de possession, copie/modification DB et binaires, débogage | Pas de protection absolue raisonnable dans V1 |
| Malware sous le compte opérateur | Lit les fichiers accessibles, presse-papiers/mémoire, agit comme l'utilisateur | Hygiène Windows, contrôle d'exécution, sauvegardes séparées |
| Malware administrateur | Contrôle du système et des protections locales | Incident Windows/IT, restauration depuis une copie externe fiable |
| Copieur de fichiers d'installation | Récupère/décompile les assemblies et tente de les réutiliser | Signature/provenance et licence commerciale, pas impossibilité de copier |
| Copieur de DB/sauvegardes | Accès aux données et attaque hors ligne des hachages | ACL, chiffrement des supports, gestion des clés |
| Opérateur accidentel | Mauvaise sauvegarde, mauvaise société, restauration ancienne, double saisie | Confirmation, provenance, récupération et validations |

## 4. Frontières de confiance

1. **Utilisateur → UI → services** : les commandes UI améliorent l'ergonomie ; seuls les services constituent le contrôle applicatif utile. L'accès direct au service ne doit pas dépendre d'un bouton caché.
2. **Session → état persistant de l'utilisateur** : le rôle en mémoire ne prouve pas que le compte est toujours actif ou que son mot de passe n'a pas changé. C'est la rupture SEC-001.
3. **Processus Tresoria → SQLite local** : le moteur n'authentifie pas les rôles Tresoria. Un autre programme utilisant le même jeton Windows peut disposer des mêmes droits fichiers.
4. **Sauvegarde externe → staging → base courante** : une archive et un checksum ne constituent pas une provenance authentifiée.
5. **Compte standard → Program Files / ProgramData** : séparation entre code protégé et données modifiables. Des ACL différentes n'isolent pas deux rôles applicatifs partageant un compte Windows.
6. **Build/installateur → poste client** : authenticité du paquet, conservation des données et compatibilité du schéma doivent être garanties par le processus de livraison.
7. **Application → pilote/spooler d'impression** : le reçu quitte le processus ; la confiance dans le pilote et la conservation des travaux relèvent aussi de Windows.

Il n'existe pas de frontière de sécurité forte entre deux classes .NET chargées dans un même processus. `internal`, la réflexion ou le masquage des noms ne remplacent pas une isolation de processus/identité.

## 5. Surface d'attaque et revue adversariale

Entrées principales : connexion et premier démarrage, commandes de saisie/modification, permissions, fichiers `.fmbak`, DB/WAL/SHM locaux, répertoires de sauvegarde, boîte de sélection de fichiers et pilote d'impression. Aucun serveur HTTP, mise à jour distante, exécution de shell ou chargeur de plugins applicatif identifié dans le code de production examiné.

| Tentative demandée | Résultat de l'audit |
|---|---|
| Se connecter sans connaître le mot de passe | Aucun bypass cryptographique établi. Mot de passe très faible accepté ; accès fichier permet attaque hors ligne ou remplacement du hash si écriture autorisée. |
| Élever Employé en Responsable | Pas de méthode publique d'administration permettant à l'employé de le faire ; changement du rôle directement dans SQLite possible avec droits fichier suffisants. |
| Invoquer un backend Responsable | Refus démontré pour Administration et sauvegarde avec une vraie session Employé. Rôle Responsable périmé encore accepté par plusieurs autres services. |
| Modifier SQLite puis démarrer | Types financiers invalides contrôlés ; historique futur non bloqué, et contrôle de type insuffisant pour authentifier le contenu. |
| Remplacer une sauvegarde | Archive reconstruite avec nouveau hash, identité et mot de passe différents restaurée dans une base temporaire. |
| Rejouer un paiement | Requête identique acceptée plusieurs fois tant que le reliquat le permet ; aucun bypass de plafond d'allocation montré. |
| Modifier la trésorerie sans trace suffisante | Mutations via services autorisés conservant généralement auteur/date, mais absence d'un journal de sécurité durable complet. |
| Modifier les binaires installés | Dépend des ACL du futur Program Files ; signature absente du build local. Admin et malware sous identité privilégiée restent hors garantie. |
| Copier vers un autre PC | Aucune licence/activation observée. Possibilité attendue, pas démonstration d'une compromission de données à elle seule. |
| Lire des informations dans les fichiers | Lecture de la DB autorisée par les ACL observées ; archive non chiffrée ; pas de secret de production trouvé dans l'arbre. |
| Perturber le démarrage | Fichiers manquants/malformés généralement rejetés, mais historique plus récent accepté. Création anticipée de fichiers possible selon ACL. |
| DLL/répertoire inscriptible | Aucun chargeur dynamique maison identifié ; SQLite/Skia/HarfBuzz/OpenTK sont des dépendances natives à inclure dans le contrôle de packaging. |
| Abuser restauration/dialogues | Pas de Zip Slip trouvé : extraction vers un chemin généré, noms d'entrées stricts. Réseaux/USB et provenance restent à encadrer. |

## 6. Points forts déjà présents

- Hachage délégué à `PasswordHasher<object>` Identity, sel aléatoire ; aucun mot de passe de production préinstallé identifié.
- Connexion inactive refusée, message générique, effacement de la session avant tentative et à la déconnexion.
- Permission inconnue ou module inconnu refusé ; les droits Create/Modify des employés nécessitent aussi View.
- Administration réinterrogeant le compte actif et le rôle **dans la transaction**, avec vérification avant commit ; création d'employé imposant le rôle Employe ; protection du dernier Responsable actif.
- Premier Responsable créé seulement lorsqu'il n'y a aucun utilisateur, sous `BEGIN IMMEDIATE` ; pas de course évidente au bootstrap entre deux instances.
- Paiements/avances : montants strictement positifs, appartenance du tiers, cible unique par commande, exclusivité des types de cible, reliquat et disponibilité de l'avance vérifiés sous transaction d'écriture.
- Contraintes FK, CHECK sur les montants et leur classe de stockage INTEGER, index uniques de référence, singleton d'ouverture de trésorerie. Aucun fichier de migration 005 dans l'arbre.
- Références générées côté service ; SQL métier principalement LINQ paramétré. SQL brut examiné : constantes/identifiants internes et paramètres pour les valeurs.
- Sauvegarde via SQLite Backup API ; publication finale sans écrasement, format `.fmbak` borné, staging, schéma comparé au contrat compilé, intégrité SQLite/FK/allocations, sauvegarde PreRestore et remplacement atomique avec rollback sur exceptions gérées.
- Reçu obtenu dans un snapshot de lecture, sans `SaveChanges` dans le chemin d'impression ; annulation/échec du pilote n'annule pas le paiement.
- Correction WPF resource-only effective et testée ; factory design-time exclusivement en mémoire ; garde-fou ProgramData purement filesystem.

## 7. Tableau complet des constats

| ID | Sévérité | Constat | Base de preuve | Condition de sortie V1 |
|---|---|---|---|---|
| SEC-001 | HIGH | Révocation de compte/mot de passe non appliquée aux sessions existantes | Statique + paiement temporaire réussi après révocation | Corriger avant V1 |
| SEC-002 | HIGH | ACL ProgramData observées trop larges | ACL réelles en lecture seule + absence de durcissement dans le code | ACL installateur et recette avant livraison |
| SEC-003 | MEDIUM | Restauration sans exclusivité interprocessus prouvée ; statut busy de checkpoint ignoré | Statique + busy=1 ignoré en test temporaire | Corriger avant V1 |
| SEC-004 | MEDIUM | Montants persistés pouvant rendre les agrégats impossibles | Paiement Int64.MaxValue accepté puis erreur de trésorerie | Corriger avant V1 |
| SEC-005 | MEDIUM | Mot de passe d'un caractère accepté ; aucune limitation d'essais | Statique + bootstrap temporaire | Politique minimale et frein aux essais avant V1 |
| SEC-006 | MEDIUM | Démarrage accepté avec une migration future inconnue | Reproduction sur DB temporaire | Refus explicite avant V1 |
| SEC-007 | MEDIUM | Sauvegarde valide structurellement mais sans authenticité ni identité société | Archive modifiée/recalculée restaurée en temporaire | Procédure de provenance obligatoire ; crypto non exigée en V1 |
| SEC-008 | LOW | Traçabilité de sécurité durable incomplète | Services/admin/Trace et composition DI | Pack B |
| SEC-009 | LOW | Taille des textes et caractères de contrôle peu encadrés | Contrats, services, configurations et rendu de reçu | Pack B |
| SEC-010 | LOW | Pas de rétention/quota de sauvegardes applicatif | Service de sauvegarde | Procédure simple avant exploitation |
| SEC-011 | LOW | Pas d'idempotence des requêtes financières | Requête identique enregistrée trois fois | Risque résiduel documenté ; durcissement ultérieur ciblé |
| SEC-012 | LOW | Pipeline de livraison pas encore complètement fixé | Script Foundation, projets, absence de lockfiles/SDK fixé | Recette de packaging avant diffusion |
| SEC-013 | INFO | DB et sauvegardes en clair ; limites du RBAC face au même jeton Windows | Architecture directe SQLite | Acceptation informée et contrôles IT |
| SEC-014 | INFO | Copie/décompilation non empêchées ; signature/licence non configurées | Projets/build local | Signature recommandée et licence simple si nécessaire |
| SEC-015 | INFO | Garde-fou de tests valide mais détecteur, pas sandbox système | Code et 1 230 tests + fingerprints | Conserver ; ne pas présenter une garantie absolue |

Les lignes INFO décrivent des limites ou exigences, pas des vulnérabilités supplémentaires déguisées. Aucun résultat NuGet n'a été transformé en garantie générale de sécurité.

## 8. Constats BLOCKER

**Aucun.** Aucun scénario démontré ne justifie cette sévérité. Cela n'autorise pas la livraison sans traiter les conditions V1 identifiées.

## 9. Constats HIGH détaillés

### SEC-001 — Sessions non révoquées

- **Composant :** Authentication, Authorization et consommateurs (paiements, trésorerie, sauvegarde, suppressions, rapports).
- **Preuves exactes :** `src/FinancialManagement.Infrastructure/Authorization/AuthorizationService.cs:40-72` utilise l'identité et le rôle en mémoire et ne relit que `UserPermissions` pour un Employé ; `:75-78` vérifie les capacités Responsable exclusivement dans la session. `Authentication/AuthenticationService.cs:23-43` vérifie `IsActive` uniquement à la connexion. `Administration/UserAdministrationService.cs:73-88,124-135` modifie statut/hash sans invalider les autres sessions. `CustomerPayments/CustomerPaymentService.cs:311-321,396-399,529` autorise puis écrit sans revalidation du compte à la frontière transactionnelle. Même logique de capacité mémorisée dans `Backup/DatabaseBackupService.cs:587-630`.
- **Reproduction :** deux ServiceProviders indépendants sur une DB synthétique ; bootstrap et connexion normaux ; création d'un employé et permission CustomerPayments ; connexion employé ; désactivation par `SetActiveAsync` dans l'autre session ; `CreateAsync` du paiement renvoie `Success=True`. Après réactivation puis reset du mot de passe, le paiement avec l'ancienne session réussit encore. Aucun changement direct de DB n'est nécessaire pour ces deux cas.
- **Scénario / prérequis :** un ancien employé conserve une instance déjà connectée pendant que le Responsable retire son accès depuis une autre instance. Pas de privilège administrateur ni de mot de passe Responsable requis pour l'attaquant.
- **Impact :** lectures et écritures financières après retrait d'accès. L'effet dure jusqu'à fermeture/déconnexion ; pas de mécanisme d'expiration observé. Les permissions de module sont relues, donc retirer explicitement tous les droits peut réduire l'effet, sans corriger la désactivation.
- **Complément à ne pas confondre avec une élévation autonome :** une démotion du rôle directement dans la DB synthétique laisse la capacité trésorerie et la sauvegarde autorisées dans une ancienne session Responsable, alors que l'administration les refuse. Cette démotion a été injectée par le banc d'audit ; aucune UI de promotion/démotion n'est supposée.
- **Vraisemblance :** moyenne ; nécessite une session conservée et deux contextes/instances, mais les API normales suffisent pour le cas employé.
- **Remédiation minimale :** relire l'existence, l'activité, le rôle et un marqueur de validité de connexion avant opérations protégées ; vérifier dans la transaction pour les écritures et avant publication/remplacement pour la maintenance ; invalider après changement de mot de passe et lors des changements d'identité. Réutiliser les données existantes si possible, sans ajouter de migration 005 dans ce lot. Conserver le contrôle des permissions sur les services.
- **Blocage V1 : OUI.** Recette : ancienne session inactive/reset refusée pour lecture, paiement, avance, trésorerie, sauvegarde et suppression ; aucune écriture différée après révocation.

### SEC-002 — Exposition de ProgramData aux utilisateurs Windows non autorisés

- **Composant :** déploiement et stockage de production.
- **Preuves exactes :** `src/FinancialManagement.Desktop/App.xaml.cs:52-58` crée le répertoire et construit le chemin sans définition d'ACL ; `Configuration/ApplicationPaths.cs:13-25` cible CommonApplicationData ; `src/FinancialManagement.Infrastructure/Backup/DatabaseBackupOptions.cs:5-18` cible le même arbre. `Get-Acl` observe l'héritage actif sur racine, Data, DB et Backups. `BUILTIN\Utilisateurs` possède ReadAndExecute sur la DB, ainsi que ReadAndExecute et Write sur les répertoires. La règle Write des répertoires a ContainerInherit et **ne donne pas Write au fichier DB déjà présent**. Les identités nommées HP/INPT disposent, selon les objets, de FullControl/Modify ; SYSTEM et Administrateurs ont FullControl.
- **Scénario / prérequis :** un autre utilisateur Windows standard lit/copierait la DB et les fichiers de sauvegarde héritant de ces ACL, sans compte Tresoria. Les droits de création dans les répertoires permettent aussi de déposer des fichiers parasites ou d'occuper un nom attendu lorsqu'il est absent.
- **Impact :** fuite de données financières, clients et hachages de mots de passe ; perturbation possible par précréation. **L'écrasement ou suppression de la DB existante par tous les utilisateurs ordinaires n'est pas démontré et n'est pas affirmé.**
- **Vraisemblance :** élevée si cette configuration est reproduite sur un poste partagé ; présence confirmée sur la machine auditée, pas chez le client.
- **Remédiation minimale :** installateur créant un arbre dédié avec propriétaire Administrateurs/SYSTEM, DACL contrôlée, droits nécessaires seulement aux identités Windows d'exploitation explicitement choisies. Retirer les accès généraux Users/Everyone/Authenticated Users de cet arbre. Vérifier fichiers préexistants et fichiers créés après démarrage/restore, dont WAL/SHM, sans octroyer Modify au code dans Program Files.
- **Limite :** donner Modify à l'identité qui exécute le moteur SQLite autorise ses autres processus à modifier la DB. Les ACL ne séparent pas Responsable et Employé s'ils partagent ce jeton ; voir SEC-013.
- **Blocage V1 : OUI pour le déploiement.** Aucune ACL n'a été modifiée pendant l'audit. Test futur avec un vrai compte standard extérieur au groupe, pas simplement sous un compte administrateur.

## 10. Constats MEDIUM détaillés

### SEC-003 — Maintenance WAL et exclusion des autres utilisateurs de la base

- **Composant :** restauration et coordination.
- **Preuves :** `src/FinancialManagement.Infrastructure/Backup/DatabaseMaintenanceLock.cs:3-6` : `SemaphoreSlim` local au ServiceProvider ; `InfrastructureServiceCollectionExtensions.cs:98-106` enregistre les composants de maintenance. Aucun verrou d'instance machine ou contrat d'exclusivité partagé avec toutes les opérations financières trouvé. `Backup/DatabaseBackupService.cs:302-319` checkpoint, fermeture/purge du pool local, suppression best-effort des sidecars puis `File.Replace`. `:781-789` lance `PRAGMA wal_checkpoint(TRUNCATE)` avec `ExecuteNonQueryAsync`, sans lire les trois colonnes de résultat.
- **Reproduction :** sur `busy.db` synthétique, un lecteur garde un snapshot pendant qu'un autre écrit. Le PRAGMA retourne `busy=1, frames=4, checkpointed=3`. L'appel au helper de production par réflexion retourne néanmoins normalement. La réflexion sert uniquement à tester le helper ; ce n'est pas un exploit de frontière .NET.
- **Scénario / prérequis :** seconde instance légitime, lecture longue ou outil de maintenance gardant un handle pendant une restauration Responsable. Aucun admin requis pour une seconde instance autorisée.
- **Impact :** l'application n'établit pas que la base est réellement quiescente avant manipulation des fichiers. Risque de restauration perturbée ou de données concurrentes non couvertes. Windows peut refuser le remplacement grâce au partage des handles ; **aucune corruption effective ni perte de paiement n'a été reproduite**, et ce comportement protecteur ne remplace pas le contrôle attendu.
- **Vraisemblance :** faible à moyenne, liée aux instances multiples et à la maintenance ; sévérité MEDIUM, pas HIGH.
- **Remédiation :** exiger et vérifier l'exclusivité avant PreRestore jusqu'à la fin de validation/rollback, avec une stratégie simple adaptée à V1 (instance unique effective et drainage des opérations, ou verrou interprocessus coopératif). Lire et refuser le statut busy ; ne pas supprimer des sidecars appartenant à une connexion active. `ClearAllPools` ne ferme pas les connexions d'un autre processus.
- **Blocage V1 : OUI pour la restauration livrée.** Recette : lecteur/écrivain dans un autre processus, maintenance refusée proprement et DB/WAL/SHM préservés. [Contrat du PRAGMA SQLite](https://www.sqlite.org/pragma.html#pragma_wal_checkpoint).

### SEC-004 — Paiement extrême accepté, trésorerie ensuite illisible

- **Composant :** bornes financières et agrégats.
- **Preuves :** `src/FinancialManagement.Infrastructure/CustomerPayments/CustomerPaymentService.cs:329-332,363-368,501-529` n'impose pas la représentabilité des agrégats après l'écriture. `Treasury/TreasuryService.cs:164-189,196-210,273-301` additionne dans Int64/SQL SUM. `src/FinancialManagement.Desktop/Formatting/MoneyParser.cs:18-35` accepte jusqu'à Int64.MaxValue unités mineures.
- **Reproduction :** une vente synthétique de 10 000 unités mineures reçoit de petits paiements ; l'employé enregistre ensuite `AmountMinor=9223372036854775807` avec une allocation valide de 100. Résultat `Success=True`. `GetSummaryAsync` lève ensuite `SqliteException` pendant le calcul. Le solde non alloué autorisé n'est pas lui-même un défaut : c'est l'impossibilité de calculer la somme persistée qui l'est.
- **Scénario / prérequis :** employé disposant de Create sur les encaissements, saisissant un nombre extrême ; aucun accès brut à SQLite requis. Le parseur UI accepte mathématiquement la valeur `92233720368547758,07`.
- **Impact :** déni de service des calculs/synthèses concernés persistant après reconnexion, nécessitant récupération de données. Pas de wraparound silencieux montré : l'arithmétique échoue plutôt que d'afficher une valeur erronée.
- **Vraisemblance :** faible en saisie normale, moyenne pour un employé malveillant ; une seule opération extrême suffit après un paiement existant.
- **Remédiation :** refuser atomiquement les écritures dont les agrégats requis ne sont pas représentables ; couvrir encaissements, décaissements, dépenses, ventes/achats et ouverture/transferts selon les calculs utilisés. Des bornes par opération seules ne garantissent pas tous les totaux. Conserver les décisions admises sur avances et soldes d'ouverture négatifs ; aucun changement arbitraire des règles métier n'est demandé.
- **Blocage V1 : OUI.** Recette : dépassement refusé avant persistance ; tableaux de bord/rapports/trésorerie restent lisibles.

### SEC-005 — Politique de mot de passe et limitation des essais absentes

- **Composant :** bootstrap, gestion des utilisateurs et connexion.
- **Preuves :** `src/FinancialManagement.Infrastructure/Initialization/ApplicationInitializationService.cs:50-55` ; `Administration/UserAdministrationService.cs:47-52,124-134` : seul le caractère non vide est imposé au mot de passe. `Authentication/AuthenticationService.cs:16-35` : pas de compteur, délai ou restriction des essais. `src/FinancialManagement.Desktop/ViewModels/LoginViewModel.cs:70-84,120-123` : IsBusy évite seulement le chevauchement d'une soumission.
- **Reproduction :** premier Responsable et employé créés avec le mot de passe synthétique d'un caractère `x`, accepté par les services normaux.
- **Scénario / prérequis :** personne devant l'écran de connexion d'un poste accessible, connaissant/devinant un identifiant, ou détenteur d'une copie de DB pour l'attaque hors ligne.
- **Impact :** compromission de comptes faibles. Le bon hachage ne crée pas l'entropie absente du mot de passe. La temporisation de l'application ne protège pas une copie de DB.
- **Vraisemblance :** moyenne, particulièrement avec mots de passe initiaux choisis très courts par commodité.
- **Remédiation :** règle minimale cohérente dans les trois services de création/reset (par exemple phrase de passe d'au moins 12 caractères, sans composition artificielle), maximum raisonnable, frein progressif aux essais et message générique. Éviter le verrouillage définitif permettant un déni de service. Ne pas proposer un reset discret via simple modification d'un fichier. Prévoir une récupération client documentée.
- **Blocage V1 : OUI pour les créations/reset et un frein basique aux essais.** Aucune migration supplémentaire ni plateforme Identity web complète n'est nécessaire pour définir cette politique.
- **Nuance :** l'utilisation isolée de PasswordHasher n'active pas les fonctions UserManager/SignInManager de politique ou lockout. [Options Identity et PasswordHasher](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0#password-hasher-options).

### SEC-006 — Base plus récente non bloquée au démarrage

- **Composant :** démarrage/migrations/compatibilité de version.
- **Preuves :** `src/FinancialManagement.Infrastructure/FinancialDatabaseStartup.cs:39-54,75-106` : lit migrations appliquées/en attente sans vérifier que les appliquées forment un préfixe connu. `FinancialStorageValidator.cs:41-68` contrôle présence/classes INTEGER, pas identité exacte de l'historique. `src/FinancialManagement.Desktop/App.xaml.cs:63-69,111-122` laisse continuer après échec de sauvegarde quotidienne. La vérification stricte existe dans `Backup/DatabaseBackupService.cs:701-716`, mais pour les backups.
- **Reproduction :** ajout dans **la seule base synthétique** d'une entrée d'historique fictive `20990101000000_005_Unsupported` ; `FinancialDatabaseStartup.InitializeAsync` termine normalement et renvoie `DailyBackup.Success=False`. Aucun fichier de migration 005 n'a été créé et aucune base de production n'a été touchée.
- **Scénario / prérequis :** réinstallation d'un ancien binaire sur une base déjà montée de version, ou remplacement manuel du fichier par un utilisateur ayant les droits d'écriture. Ce n'est pas une capacité d'un employé limité à l'UI.
- **Impact :** utilisation et potentiellement écriture dans une base dont la compatibilité avec le binaire n'est pas établie. La reproduction prouve le défaut de refus de version, pas la corruption d'un schéma futur inconnu.
- **Vraisemblance :** moyenne lors des futures mises à jour/retours arrière ; faible pour une première installation propre.
- **Remédiation :** vérifier l'ordre et l'identité des migrations appliquées avant migration/accès métier ; refuser une version future avec consigne claire de réinstaller le bon binaire. Vérifier aussi le contrat de schéma attendu à la frontière de démarrage dans un lot ciblé ; cela ne rendra pas les données authentiques face à un écrivain local.
- **Blocage V1 : OUI pour livrer une première version disposant déjà de migration/restauration.** Aucun nouveau schéma n'est requis pour refuser un historique inconnu.

### SEC-007 — Provenance et société d'une sauvegarde non authentifiées

- **Composant :** format de backup et restauration privilégiée.
- **Preuves :** `src/FinancialManagement.Infrastructure/Backup/DatabaseBackupArtifact.cs:9-14` annonce explicitement que SHA-256 détecte la corruption accidentelle ; `:32-34,79-99,173-174` contient hash/longueur/date, sans clé ni identité société. `DatabaseBackupService.cs:227-264,748-756` contrôle schéma, allocations et existence d'un Responsable actif, pas la continuité des comptes/société. `src/FinancialManagement.Desktop/Services/BackupDialogService.cs:31-42` confirme essentiellement le nom du fichier.
- **Reproduction :** création d'une sauvegarde synthétique officielle ; extraction de database.sqlite ; modification du nom du Responsable et de son hash ; reconstruction des deux entrées ZIP non compressées et recalcul du SHA-256 ; restauration via le service normal : `Success=True`, puis connexion réussie avec le mot de passe substitué. Le banc fournit le hash, pas une attaque cryptographique.
- **Scénario / prérequis :** attaquant capable de fournir/substituer le fichier sélectionné **et** Responsable qui accepte la restauration ; ou erreur d'un Responsable choisissant la sauvegarde d'une autre installation. Un employé ne peut pas déclencher directement Restore via son rôle normal.
- **Impact :** remplacement des finances et de l'ensemble des comptes ; ancienne sauvegarde pouvant réactiver anciens identifiants et annuler des opérations récentes. Recalculer un hash public suffit à franchir le contrôle d'intégrité du conteneur, mais pas à contourner le contrôle de schéma.
- **Vraisemblance :** moyenne avec échanges USB/dossiers partagés ; dépend fortement de la procédure de sauvegarde.
- **Remédiation V1 :** provenance vérifiée, répertoire/transport protégés, copies connues du Responsable, identification/date/aperçu et avertissement que comptes et mots de passe seront remplacés. L'identité affichée dans une archive non signée réduit l'erreur mais n'authentifie pas son auteur. Autoriser consciemment une restauration ancienne : l'anti-rollback absolu empêcherait la récupération légitime.
- **Blocage V1 : CONDITIONNEL.** Une procédure stricte de provenance et de récupération est requise ; une signature/HMAC/chiffrement embarquant un secret universel dans l'exécutable n'est pas exigé et ne constituerait pas une solution valable. Authentification cryptographique seulement si un modèle de clés et de récupération réellement opérable est défini ultérieurement.

## 11. Constats LOW et INFO

### SEC-008 — LOW : journal de sécurité durable incomplet

`src/FinancialManagement.Infrastructure/Authentication/AuthenticationService.cs:11-47` et `Administration/UserAdministrationService.cs:73-135` ne produisent pas d'événement de sécurité persistant pour connexion/révocation/reset/permissions. `src/FinancialManagement.Desktop/App.xaml.cs:170-232` et la composition Infrastructure n'installent pas de sink fichier durable explicite ; Trace et ILogger optionnel servent principalement aux erreurs. `TreasuryService.cs:115-128` conserve auteur/date du transfert, mais ce n'est pas un historique de toutes les modifications. Les allocations d'avance (`CustomerAdvances/CustomerAdvanceService.cs:210-224`) n'enregistrent pas l'auteur de l'allocation indépendamment de l'auteur du paiement initial.

Conséquence : investigations difficiles après action privilégiée ou contestation. Ajouter un journal local borné d'événements (acteur, opération, cible, résultat, horodatage), sans secrets ni copie complète des données financières. Protection par ACL ; aucune promesse de non-répudiation contre le compte Windows écrivain ou l'admin. Pack B, pas de nouveau système SIEM imposé.

### SEC-009 — LOW : textes peu bornés

`Clients/ClientService.cs:37-57,213-234`, `Configurations/ClientConfiguration.cs:14`, `Configurations/UserConfiguration.cs:14-19` et les commandes de paiement/observations n'établissent pas de maximum uniforme. Recherche `HasMaxLength`/`MaxLength` sans contrôle pertinent trouvé dans le code source applicatif. Noms/observations avec chaînes très longues ou caractères de contrôle/bidirectionnels peuvent gêner listes, impression ou identification visuelle ; pas de preuve d'exécution de code.

Le reçu utilise `new Run(text)` (`CustomerPaymentReceiptDocumentFactory.cs:90-104`), donc le texte n'est pas interprété comme XAML. Prévoir limites métier raisonnables côté service et UI, refus des contrôles inutiles dans les identifiants, conservation des écritures arabes/françaises et des caractères Unicode légitimes. Éviter une normalisation agressive des noms. Pack B.

### SEC-010 — LOW : croissance des backups

`DatabaseBackupService.cs:33-75,514-555` crée des sauvegardes journalières/manuelles/PreRestore et des gardes par date ; aucune politique de rétention ou espace libre réservée identifiée. Les 52 fichiers présents dans Backups sur cette machine sont une observation de quantité, pas une preuve de saturation. Les archives ne sont pas compressées ; les temporaires et backups de sécurité multiplient le besoin en espace lors de maintenance.

Risque de disque plein et de perte de disponibilité. Définir une procédure simple de conservation et contrôle d'espace, une copie hors du poste et un avertissement exploitable ; ne jamais purger la dernière copie valide ou un PreMigration/PreRestore nécessaire avant validation de récupération. Ne pas confondre le backup local avec une protection contre ransomware/disque défaillant. Pack B/IT.

### SEC-011 — LOW : absence d'idempotence des commandes

`CustomerPaymentService.cs:305-306,501-529` et `SupplierPaymentService.cs:305-536` produisent une nouvelle référence par appel, sans identifiant d'opération stable. La reproduction a réutilisé exactement la même commande de 100 unités mineures et obtenu trois paiements distincts. Cela n'a pas dépassé le reliquat et correspond aussi à trois paiements légitimes de même montant : ne pas interdire cette possibilité par une déduplication naïve montant/date/client.

`src/FinancialManagement.Desktop/ViewModels/AsyncRelayCommand.cs:23-39` protège l'exécution simultanée d'une même commande UI ; les viewmodels gèrent IsBusy. Aucun double enregistrement par simple double-clic courant n'a été reproduit. Risque résiduel : deux instances, répétition manuelle ou réessai après résultat ambigu. Une clé d'opération réutilisée uniquement pour le même réessai est une piste post-V1, pas une refonte imposée ni une élévation de privilège.

### SEC-012 — LOW : contrôles de livraison à compléter

`scripts/verify-foundation.cmd:21-38` build/test en configuration par défaut et scan de vulnérabilités limité à Infrastructure ; Desktop et ses dépendances graphiques ne sont pas inclus par cette seule commande. L'inspection `dotnet list ... --vulnerable` ne constitue pas à elle seule une règle de refus basée sur la sévérité. L'arbre ne contient ni `global.json`, ni lockfile NuGet, ni pipeline CI/recette de signature/manifest de publication autoportante. `src/FinancialManagement.Desktop/FinancialManagement.Desktop.csproj:9-18` ne fixe pas de RID ou de publication autoportante. C'est cohérent avec un installateur encore prévu, mais insuffisant comme recette finale.

Fixer SDK/toolchain, dépendances résolues et contenu du paquet ; valider Release, scan de **toute** la solution et règles de refus explicites ; conserver les PDB privés utiles au support, distribuer seulement ce qui est nécessaire. Ne pas modifier le garde-fou de tests pour accélérer le packaging. Les PDB ne sont pas en soi un secret ni une barrière contre la décompilation.

### SEC-013 — INFO : confidentialité au repos et accès direct à SQLite

La connexion de `App.xaml.cs:55-58` ne contient pas de secret ni de configuration de chiffrement ; `.fmbak` est un ZIP non chiffré (`DatabaseBackupArtifact.cs:25-44`). DB, WAL et backups contiennent des données sensibles. Un compte Windows autorisé à écrire SQLite peut changer un montant, rôle ou hash via un autre programme ; l'application ne peut pas distinguer ce programme avec une ACL appliquée au même jeton.

Pour V1 : disque/support chiffré administré par le client et récupération maîtrisée, identités Windows limitées, pas de partage public du dossier, copies externes protégées. Un chiffrement SQLite futur demande un choix concret de garde des clés (DPAPI utilisateur/machine, secret fourni au démarrage, récupération) et une solution pour sauvegarde/restauration. Une clé livrée en dur dans les binaires est à proscrire. L'admin ou malware dans la session peut encore accéder aux données déchiffrées. Une vraie séparation contre les autres programmes du même utilisateur exigerait un service/identité distincte ; hors périmètre simple V1.

### SEC-014 — INFO : copie, signature et licence

Aucun mécanisme de licence, activation, obfuscation ou anti-tamper applicatif identifié. Le build Release local a le statut Authenticode **NotSigned** ; cela ne prouve pas qu'un futur paquet de livraison sera non signé. Les assemblies .NET sont inspectables. Copier les binaires reste possible aux utilisateurs qui peuvent les lire ; c'est compatible avec un répertoire Program Files en lecture/exécution.

Signer les exécutables propres à Tresoria et l'installateur, horodater les signatures et vérifier leur validité avant livraison/mise à jour. Authenticode établit origine/intégrité lors d'une vérification ; une signature seule n'oblige pas Windows à refuser tout code non signé et n'est pas un DRM. [Horodatage Authenticode Microsoft](https://learn.microsoft.com/en-us/windows/win32/seccrypto/time-stamping-authenticode-signatures).

Si une licence offline est nécessaire : document de licence signé avec clé privée gardée chez l'éditeur et clé publique dans l'application, identifiant client, activation/réactivation manuelle documentée. Éviter l'expiration courte obligatoire et l'attachement fragile à un unique composant matériel ; prévoir remplacement du PC et récupération urgente. Obfuscation éventuellement légère après tests WPF/reflection, sans en faire un prérequis V1. Un admin peut patcher la vérification : ce risque ne justifie ni pilote anti-tamper ni DRM invasif.

### SEC-015 — INFO : portée exacte de la protection de tests

`tests/FinancialManagement.Desktop.Tests/WpfTestHost.cs:34-43` construit `App(runProductionStartup:false)` ; `src/FinancialManagement.Desktop/App.xaml.cs:24-44` retourne avant résolution des chemins/services/DB dans ce mode. `WpfTestHostSafetyTests.cs:7-28` vérifie l'observation du callback et l'absence du démarrage production. La factory EF design-time (`FinancialManagementDesignTimeDbContextFactory.cs:9-16`) cible `:memory:`.

`scripts/verify-programdata-unchanged.ps1:33-54,96-124,163-187` lit les fichiers avec partage explicite, vérifie que l'application est arrêtée et compare existence/taille/date d'écriture/hash de DB/WAL/SHM. Il n'ouvre pas SQLite, ne restaure rien, et écrit le snapshot dans TEMP. Le script Foundation l'appelle avant et après, y compris sur échec normal d'une étape.

**Le chemin de tests actuel examiné ne déclenche pas App.OnStartup en production**, et les tests de backup/startup utilisent des chemins temporaires remplacés explicitement. La garde est un détecteur après exécution, pas une sandbox empêchant tout futur test mal écrit d'accéder au disque ; une interruption brutale de Foundation peut empêcher la vérification finale. Ne pas transformer « cette suite est isolée et la DB est restée inchangée » en « aucun code de test imaginable ne pourra jamais viser ProgramData ». Durcissement CI par compte/machine de test isolé possible ultérieurement.

## 12. Évaluation de l'authentification

Paramètres **mesurés dans la bibliothèque résolue**, à partir du format du hash synthétique : Identity V3, marqueur 1, PRF 2 (PBKDF2-HMAC-SHA512), 100 000 itérations, sel de 16 octets, sous-clé de 32 octets (61 octets au total avant Base64). Le code n'écrit pas sa propre fonction cryptographique ; les tests de sel aléatoire, bon/mauvais mot de passe et hashes malformés passent. La valeur par défaut des itérations est aussi documentée par Microsoft ; elle ne doit pas être confondue avec une politique de mot de passe ou de lockout.

`MicrosoftIdentityPasswordHasher.cs:24-34` accepte `SuccessRehashNeeded` comme succès sans réécrire le hash. Pas de faiblesse cryptographique démontrée ; prévoir un rehash opportuniste contrôlé après benchmarking lors d'une future augmentation des paramètres, et éviter d'imposer une réinitialisation générale sans motif.

Les comptes sont uniques par Username SQLite ; le login UI applique Trim alors que les services d'administration préservent le Username. Cela peut rendre des identifiants à espaces périphériques difficiles à utiliser ; durcissement de validation conseillé, sans changer silencieusement l'identité des comptes existants.

Les erreurs de mauvais utilisateur, inactif et mauvais mot de passe sont génériques dans l'UI. L'absence/inactivité renvoie avant le calcul PBKDF2 (`AuthenticationService.cs:27-32`) : différence temporelle possible pour l'énumération, non chronométrée ici. Un hash factice pour les identifiants inexistants est un durcissement secondaire, surtout après SEC-005.

Affichage du mot de passe : `LoginWindow.xaml.cs:51-106` choisit le contrôle visible, nettoie dans finally, efface le TextBox lors du masquage et réinitialise le mode. La fenêtre nettoie aussi à la fermeture. Le record `CredentialSubmission` et les strings .NET créent toutefois des copies en mémoire ; l'effacement UI ne garantit pas leur écrasement immédiat dans le heap/pagefile. Aucune persistance plaintext ni journalisation explicite de ces valeurs trouvée. Pas de recommandation irréaliste de mémoire inviolable contre l'admin/debugger.

Reset réservé à l'administration active, protection du dernier Responsable, pas de mot de passe par défaut livré. Absence de changement libre-service employé et de récupération automatique : limite opérationnelle à documenter ; ne pas ajouter de backdoor de support. SEC-001 reste le défaut principal de cycle de vie des comptes.

## 13. Évaluation autorisation / RBAC

| Fonction | Contrôle de service observé | Évaluation |
|---|---|---|
| Clients/fournisseurs : liste/détail/création/modification | Module correspondant + View/Create/Modify ; session | Réel, pas UI seulement ; SEC-001 |
| Suppression permanente d'un tiers | PermanentDelete Responsable + contrôles de références/FK | Employé refusé ; tiers utilisé non supprimable ; rôle périmé à corriger |
| Ventes/achats/dépenses | Module Create pour écrire, View pour lire | Validations backend présentes ; paiements initiaux font partie du flux de création autorisé |
| Encaissements/décaissements | Module de paiement View/Create | Contrôles avant opération ; SEC-001 et SEC-004 |
| Application d'avance | Module de paiement Modify | Vérifie source, tiers, disponibilité, allocation sous transaction |
| Lookups | Permission du module consommateur | Aide de saisie autorisée ; pas un accès anonyme général |
| Administration utilisateurs/permissions | Relit Users actif + Responsable sous transaction | Meilleur modèle de contrôle de l'arbre ; test direct employé refusé |
| Créances/dettes initiales | Capacité Administration | Backend protégé ; capacité basée sur session |
| Trésorerie | Capacité Treasury | Backend protégé ; transferts conservant auteur/date ; SEC-001/004 |
| Dashboard/Reports/alertes | Capacités Responsable | Contrôle dans les services, même en appel direct |
| Backup/Restore manuels | Capacité BackupRestore et identité recontrôlée aux étapes de commit | Employé refusé ; ne vérifie pas l'activité persistante du compte |
| Backup de démarrage | API distincte utilisable avant connexion | Besoin normal pour backup/migration ; pas une API réseau exposée |
| Impression reçu | Délègue aux lectures de paiement/obligations autorisées | Permission héritée des services de lecture |

Preuves supplémentaires : `FinancialDashboardService.cs:26-47`, `FinancialDueAlertService.cs:18-34`, `CustomerAdvanceService.cs:22-32`, `SupplierAdvanceService.cs:22-32`, `ClientService.cs:239-293`, `SupplierService.cs:239-293`, `InitialCustomerReceivableService.cs:20-35`, `InitialSupplierPayableService.cs:20-35`.

La navigation, CanExecute et IsBusy protègent le parcours UI, pas l'accès aux assemblies d'un processus arbitraire. Un programme local qui construit lui-même un ServiceProvider, injecte une fausse session ou écrit directement dans DbContext possède déjà un accès au code/DB ; ce n'est pas un appel distant anonyme à un serveur sécurisé. Les interdictions de rôles restent utiles dans le processus non modifié et doivent être cohérentes après révocation.

## 14. SQLite et ProgramData

Chemin de production : `C:\ProgramData\FinancialManagement\Data\FinancialManagement.db`. Le chemin est absolu et construit via CommonApplicationData ; pas de repli vers le répertoire courant ni de mot de passe dans une chaîne de connexion. Intercepteur : `foreign_keys=ON`, `journal_mode=WAL`, `synchronous=FULL`, `busy_timeout=5000` (`SqlitePragmaConnectionInterceptor.cs:10-16`). Les flags et FK sont des protections d'intégrité, pas une authentification des écrivains locaux.

Le contrôle SQL brut couvre les migrations figées, les PRAGMA et validateurs. Les noms interpolés de `FinancialStorageValidator` proviennent d'une liste de tables/colonnes compilée ; les valeurs externes du validateur de schéma sont paramétrées. Aucun chemin d'injection SQL via nom de client, observation, référence ou nom d'utilisateur n'a été établi. L'analyse ne prétend pas avoir fuzzé le moteur natif.

Au démarrage, présence/classes INTEGER sont contrôlées avant matérialisation EF et après migration. La restauration vérifie en plus schéma/FK/integrity_check et allocations. Un montant INTEGER plausible, une identité/permission modifiée ou un hash remplacé par un écrivain local peuvent rester valides : les CHECK ne prouvent pas qui a fait l'opération. Les CHECK/FK peuvent aussi être contournés par un outil ayant accès direct au fichier. Le démarrage ne doit pas être présenté comme une authentification de la base.

ACL observées : DB lisible par Users, modifiable par les identités explicitement privilégiées ; Data/Backups inscriptibles pour création par Users ; héritage actif. WAL/SHM absents pendant l'audit : leurs ACL effectives n'ont donc pas été mesurées, seulement les règles d'héritage susceptibles de s'appliquer. Aucun compte standard distinct n'a été connecté pour une épreuve d'accès ; la conclusion de lecture découle de l'ACE explicite observée.

Pour les utilisateurs Windows extérieurs au groupe d'exploitation, NTFS doit empêcher lecture/écriture/création/suppression. Pour l'employé utilisant le même compte Windows que Tresoria, une séparation forte contre SQLite manuel n'est pas atteignable par ces ACL. Pour l'admin local, ni ACL ni obfuscation ni chiffrement transparent ne garantissent l'impossibilité de falsifier/copier.

## 15. Backup / Restore

Le format actuel est **`.fmbak`, pas un `.db` brut accepté par le dialogue**. Il contient exactement `database.sqlite` et `manifest.json`. Une DB manipulée n'est pertinente au point d'entrée restore que si elle est emballée dans le format accepté. L'extension `.db` seule est rejetée (`DatabaseBackupService.cs:203-207`).

Stockage local par défaut : `C:\ProgramData\FinancialManagement\Backups`. Noms avec type, date UTC et GUID ; copies quotidiennes regroupées par date métier locale. Sauvegarde externe vers un répertoire existant choisi par le Responsable. Un chemin UNC peut être sélectionné : pas de traversée de privilège démontrée, mais le support reçoit des données confidentielles et peut provoquer une authentification Windows vers un partage. Préférer supports/partages approuvés.

Protections du conteneur : création sans écrasement, longueur DB limitée à 64 GiB et manifest à 16 KiB, répertoire central ZIP borné avant allocation, exactement deux entrées nommées, pas de concaténation du nom d'entrée dans le filesystem, copie bornée, hash vérifié. Ces plafonds évitent certaines amplifications et chemins arbitraires, mais une archive légitime très grande peut encore consommer beaucoup de disque/temps : prévoir espace disponible et annulation exploitable.

Le fichier source reste en lecture, staging unique dans le dossier de la DB, validation avant et après migration, rejet des objets de schéma inattendus (triggers/vues inclus hors contrat), contrôle de l'historique connu, FK, classe INTEGER et allocations ; au moins un Responsable actif après migration. La présence d'un Responsable ne garantit pas que le client connaît son mot de passe : SEC-007.

PreRestore obligatoire ; remplacement `File.Replace` sur le même volume ; vérification post-swap ; rollback en cas d'échec géré ; session locale effacée après succès. Cette stratégie couvre les exceptions prévues et la frontière d'annulation. Elle ne remplace pas une procédure de reprise après coupure électrique au milieu du remplacement/validation, ni un verrou interprocessus. Le rollback n'est pas garanti si le système perd le disque ou les permissions ; le service remonte alors une erreur critique et la UI demande de se reconnecter.

La sauvegarde journalière est déclenchée **au démarrage**, pas par une minuterie toutes les 24 h pendant une exécution ininterrompue. Un échec ne ferme pas la base : avertissement au Responsable. Cette disponibilité voulue doit être accompagnée d'une procédure de sauvegarde. Pas de purge automatique ni preuve d'authenticité/société/date métier du contenu par le simple nom du fichier.

## 16. Intégrité financière et impression

Les sommes d'allocation sont vérifiées dans les deux dimensions (document et paiement contributeur), avec contrôles d'appartenance, existence, méthode de paiement et bornes avant addition : `src/FinancialManagement.Infrastructure/AllocationIntegrity.cs:77-156`. Transactions d'écriture avant lecture des disponibilités pour paiements/avances ; les tests de concurrence, stockage financier et allocations existants passent. Les références uniques évitent collision de référence, pas duplication sémantique d'une commande.

La trésorerie est calculée à partir des mouvements et soldes d'ouverture ; l'application d'une avance ajoute des allocations et ne recrée pas un encaissement. Aucun scénario de création automatique d'argent sans ligne financière correspondante, de surallocation via les services non modifiés, ou de modification directe du paid/remaining par l'UI n'a été établi. Un utilisateur autorisé à saisir peut inventer une opération : cela nécessite supervision/reconciliation, pas seulement du code.

Les valeurs négatives autorisées pour l'ouverture et l'avance excédant une vente sont traitées comme choix métier admis. Les autres montants de flux sont strictement positifs ; dates saisies typées et intervalles de rapport vérifiés. SEC-004 porte uniquement sur la représentabilité des calculs persistés. SEC-011 porte sur la répétition d'une même intention, pas sur le droit de faire deux règlements identiques.

**Reçu V1 :** `CustomerPaymentReceiptService.cs:10-89` utilise un snapshot de lecture et recoupe paiement, allocations et historique. `CustomerPaymentReceiptPrinter.cs:59-92` utilise le dialogue Windows, crée le FlowDocument, attrape les exceptions et distingue Printed/Cancelled/Failed. Pas de mutation financière, pas de rollback du paiement après échec/cancel d'impression. Les tests service et présentation passent. Aucun vrai pilote ni imprimante client n'a été testé : un pilote malveillant/natif compromis relève de Windows, un reçu/spool/PDF peut exposer nom client et montants. Le titre du spool inclut la référence (`:83`). Ne pas partager une imprimante ou un dossier PDF sans contrôle ; pas de redesign du ticket proposé.

## 17. Logs, erreurs, configuration et secrets

Recherche de marqueurs de clés privées, formats de tokens courants, API/client secrets, mots de passe/credentials, fichiers PFX/P12/PEM/KEY/ENV et DB suivies. Les correspondances de mots de passe sont les fixtures/tests, lectures UI et validations ; pas de clé privée, API key, mot de passe administrateur préconfiguré ni DB de production suivie identifié. Le seed de migration concerne les catégories de dépenses, pas des utilisateurs (`001_InitialSchema.cs:494` et `ExpenseCategoryConfiguration.cs`). Les credentials de tests ne deviennent pas des comptes de production par le bootstrap normal.

Pas de connexion réseau applicative ni de secret distant requis trouvé. Pas d'activation `EnableSensitiveDataLogging`. Les messages UI sont généralement génériques ; l'erreur de migration affiche volontairement le chemin de récupération (`App.xaml.cs:244-255`). Trace écrit des exceptions complètes pouvant inclure stack traces, chemins et identifiants d'objets ; aucun mot de passe explicitement formaté dans les logs n'a été trouvé. Une future sortie fichier doit minimiser ces données et les garder hors des répertoires publics.

Les logs locaux annoncés dans le contexte produit ne sont pas un mécanisme durable démontré par cet arbre : aucun sink fichier/rotation configuré identifié ; `C:\ProgramData\FinancialManagement\Logs` est absent sur la machine. SEC-008 décrit cette lacune, sans prétendre qu'aucun diagnostic Windows ne peut exister.

`.gitignore` exclut bin/obj, DB/sidecars, Backups et Logs. Il ne suffit pas pour toutes les copies externes : les `.fmbak`, archives de support, dumps et futurs certificats doivent être exclus de la sélection de fichiers et contrôlés avant commit. L'audit n'a ni imprimé le contenu des sauvegardes de production ni lu les lignes de la DB réelle. Aucun source du projet n'a été envoyé à un scanner tiers ; les appels réseau ont concerné Git/NuGet et la documentation publique.

## 18. Application installée, fichiers et anti-tamper

L'application utilise un chemin de données absolu dans ProgramData ; les ressources WPF et branding sont compilées. Aucun `Process.Start`, `Assembly.LoadFrom`, `NativeLibrary.Load`, désérialisation polymorphe dangereuse ou interprétation de script externe trouvé dans le code de production examiné. Le seul JSON de restauration est un manifest typé. Des dépendances natives existent : la sécurité de leur recherche/chargement dépend du contenu de publication, des chemins et ACL Windows.

Installer uniquement le code sous Program Files protégé, en lecture/exécution pour les opérateurs. Pas de DLL/plugin/config exécutable chargé depuis Data, Backups, TEMP, dossier courant ou partage réseau. Définir le répertoire de travail du raccourci sur le répertoire installé ; ne pas exécuter en administrateur pour le fonctionnement quotidien. Les fichiers temporaires de backup contiennent la DB complète : même niveau de confidentialité que la base et nettoyage sûr des orphelins lors d'une procédure de maintenance vérifiée.

La signature détecte des modifications lors de la vérification et réduit les risques de distribution substituée. Elle n'empêche ni lecture/copie ni exécution de tout binaire modifié sans politique Windows appropriée. Un hash stocké à côté de l'exécutable peut être remplacé avec lui. Strong-name .NET n'est pas une protection anti-piratage. La licence offline peut rendre la copie moins commode, jamais impossible contre un administrateur qui patcherait le code.

Pour V1 : ACL, signatures et livraison fiable d'abord ; licence signée simple seulement si l'objectif commercial l'exige ; pas de driver, surveillance invasive, effacement des données à expiration, ni verrouillage du client sur changement de disque/PC. Obfuscation éventuelle après test exhaustif bindings WPF/MVVM, DI et sérialisation ; valeur limitée et coût de support à assumer.

## 19. Checklist de sécurité installateur pour DEV2

Ces exigences ne sont pas implémentées par cet audit.

- [ ] Publier depuis le SHA validé, en Release, `win-x64`, autoportant selon le plan produit ; utiliser un dossier de publication propre, jamais une copie globale de bin ou du dépôt.
- [ ] Program Files : propriétaire Administrateurs/SYSTEM, FullControl aux administrateurs/SYSTEM, lecture/exécution aux opérateurs ; aucun Modify/Write pour Users ou groupe d'exploitation. Inclure DLL natives et fichiers de runtime dans cette règle.
- [ ] Créer `C:\ProgramData\FinancialManagement` et sous-dossiers sous élévation initiale, avec DACL contrôlée ; ne pas accepter silencieusement une racine précréée appartenant à un utilisateur inattendu ou un reparse point pointant ailleurs.
- [ ] Définir explicitement les comptes/groupes Windows autorisés. Retirer les ACE larges héritées de ProgramData de l'arbre Tresoria. Ne pas appliquer un Deny global Users qui bloquerait aussi les membres autorisés ; construire une DACL allow minimale.
- [ ] Data/DB : droits Modify nécessaires au groupe Windows exécutant SQLite, FullControl Administrateurs/SYSTEM ; aucun accès aux autres utilisateurs. Le dossier doit permettre création/suppression/renommage des sidecars et fichiers staging requis.
- [ ] Vérifier DB, WAL, SHM, journal, staging et rollback réellement créés, avec un compte standard d'exploitation. Refuser lecture/écriture/création aux comptes standard non autorisés.
- [ ] Backups : accès limité aux identités opérationnelles nécessaires. Les sauvegardes automatiques s'exécutent sous le compte lançant Tresoria avant connexion ; ne pas prétendre réserver par ACL une écriture au rôle Responsable applicatif si le même compte Windows est partagé.
- [ ] Supports externes : expliquer que la sauvegarde contient toutes les finances et comptes. Dossier/support approuvé et protégé ; chiffrement du support avec récupération connue du client si utilisé.
- [ ] Logs/config : dossier dédié et limité, écriture uniquement aux identités nécessaires, rotation et absence de secrets ; ne pas mettre de code ou DLL dans un dossier de logs modifiable.
- [ ] Normaliser/vérifier les ACL des fichiers préexistants et **après restauration** : `File.Replace(ignoreMetadataErrors:true)` et héritage ne dispensent pas d'une recette ACL réelle.
- [ ] Signature Authenticode recommandée sur EXE/assemblies propres distribués et installateur ; horodatage ; clé privée de signature hors dépôt et hors poste client ; vérification de signature du paquet final.
- [ ] Raccourcis machine créés sous élévation, cible et répertoire de travail sous Program Files, aucun mot de passe dans les arguments ; liens du menu Démarrer commun protégés contre modification par les comptes ordinaires.
- [ ] Premier démarrage confié au Responsable désigné. Ne pas préinstaller un compte/admin universel ; ne pas lancer Tresoria avec le jeton élevé de l'installateur pour créer ses données.
- [ ] Élévation seulement pour installer/mettre à jour le code et régler les ACL. Exécution métier normale en compte standard. Ne pas exiger que les employés connaissent le mot de passe administrateur Windows.
- [ ] Mise à jour : arrêter/drainer toutes les instances, vérifier l'authenticité et la version, remplacer les binaires en état cohérent, conserver la DB et tous les backups ; tester l'interruption et l'espace insuffisant.
- [ ] Désinstallation : **conserver par défaut la DB, les backups et les données client**. Aucune suppression récursive générale de ProgramData. Une éventuelle suppression volontaire des données doit être une action distincte, clairement consentie et vérifiée ; pas nécessaire pour V1.
- [ ] Upgrade/réinstallation : ne jamais écraser la DB par une base vide de démonstration ni réinitialiser les utilisateurs ; ne pas revenir aux ACL héritées trop larges.
- [ ] Recette : installation propre, réinstallation, upgrade, désinstallation conservatrice, standard non autorisé, opérateur autorisé, antivirus/spooler, coupure de mise à jour et rollback. Aucun de ces scénarios installateur n'est certifié par le build actuel.

## 20. Exigences de mise à jour et maintenance

Pas d'updater en ligne nécessaire. Livrer un installateur signé par un canal connu, vérifier le bon éditeur et l'intégrité avant exécution. Conserver SHA de source, versions SDK/runtime et inventaire des binaires publiés. Un binaire autoportant embarque son runtime : il faut republier pour les correctifs de ce runtime, la mise à jour du runtime partagé du poste ne remplace pas son contenu.

Le code réalise déjà un backup PreMigration lorsqu'une base possède des migrations appliquées et qu'il en manque (`FinancialDatabaseStartup.cs:51-69`) ; échec de cette sauvegarde empêche la migration. Garder cette règle et ses tests. Une première base vide n'a pas de données client à sauvegarder. Le backup obtenu et sa procédure de récupération doivent rester disponibles si la migration échoue.

Avant migration : contrôler compatibilité/version, droits et espace, arrêt des autres instances, sauvegarde vérifiée. Après migration : vérification du schéma/intégrité et démarrage contrôlé. Une coupure peut laisser un verrou de migration EF à examiner ; ne pas supprimer aveuglément une table de lock ou un WAL, ni relancer une migration destructive sans diagnostic.

Retour arrière : remettre un couple **binaire + base compatible** vérifié. Une ancienne version ne doit pas écrire une base plus récente (SEC-006). La restauration d'une ancienne sauvegarde implique une perte volontaire des opérations postérieures et potentiellement d'anciens comptes/mots de passe (SEC-007). Faire une copie de sécurité avant recovery ; consigner la décision et contrôler les comptes après restauration. Aucun downgrade/migration inverse automatique demandé.

## 21. Dépendances, dépôt et packaging

SDK réellement utilisé : **10.0.302**, Windows x64 ; runtime partagé de test **10.0.10**. Packages Infrastructure directs : EF Core/EF Design/EF Sqlite et Microsoft.Extensions.Identity.Core **10.0.11**. Desktop : LiveChartsCore.SkiaSharpView.WPF **2.0.5**. Dépendances natives/transitives observées : SkiaSharp **3.119.0**, HarfBuzzSharp **8.3.1.1**, OpenTK **4.3.0** et SQLitePCLRaw **2.1.12**. Pas d'affirmation de vulnérabilité sur la seule ancienneté d'un numéro.

`dotnet list FinancialManagement.sln package --vulnerable --include-transitive --no-restore --format json` interroge `https://api.nuget.org/v3/index.json` et ne rapporte aucune dépendance vulnérable sur les six projets. Le résultat JSON est conservé hors dépôt. La restauration a réutilisé le cache local NuGet ; pas de reconstruction indépendante bit-à-bit des packages ni d'audit exhaustif de tous les moteurs natifs.

**Version SQLite native mesurée via `sqlite_version()` : 3.53.3.** Source id : `2026-06-26 20:14:12 d4c0e51e4aeb96955b99185ab9cde75c339e2c29c3f3f12428d364a10d782c62`. DLL x64 du build : SHA-256 `B7385D722C83FB52142A00477A726723745916D22A555711EE89834C1111FB2E`. La version de l'enveloppe NuGet ne doit pas être utilisée pour deviner la version native. Les notes SQLite indiquent la correction du WAL-reset dans les versions concernées ; la version effectivement chargée ici n'est pas classée vulnérable à ce problème. [Notes officielles SQLite 3.53.3](https://www.sqlite.org/releaselog/3_53_3.html), [release SQLitePCL.raw 2.1.12](https://github.com/ericsink/SQLitePCL.raw/releases/tag/v2.1.12).

Le build effectué est Release mais **framework-dependent** selon son runtimeconfig ; ce n'est pas la publication autoportante x64 finale. Pas de recette Authenticode/installer final validée. PDB présents possibles par comportement de build .NET ; conserver symboles et sources de support en privé, ne pas embarquer tests, fixtures, source, bases temporaires, backups, rapports d'audit ou certificats privés dans l'installateur.

Les recherches d'API dangereuses n'ont pas trouvé de shell/processus lancé par le code de production, chargement arbitraire d'assembly, BinaryFormatter ou client réseau. `Process.Start` existe dans le test du garde-fou PowerShell, avec arguments séparés, sans shell et fenêtre masquée. La désérialisation de sauvegarde passe par un DTO JSON attendu et un contrôle exact des champs. Pas d'upload du code à un scanner externe.

## 22. SECURITY FIX PACK A — avant installateur/livraison

Lot à recommander en premier, **à réaliser dans une autre tâche**, sans correctif dans cet audit :

1. **SEC-001 :** révoquer effectivement les sessions, avec contrôles communs appliqués par tous les services et frontières transactionnelles. Tester inactive/reset/rôle changé, anciennes instances et publication de backup.
2. **SEC-002 :** définir le contrat ACL avec DEV2 et la matrice de comptes Windows autorisés ; vérifier le résultat réel avant livraison. L'application ne doit pas demander des droits admin quotidiens.
3. **SEC-003 :** exclusivité effective de maintenance et refus d'un checkpoint busy ; test avec un second processus. Conserver PreRestore/staging/rollback et la correction WPF.
4. **SEC-004 :** empêcher la persistance d'opérations rendant les agrégats non représentables ; vérifier les chemins symétriques client/fournisseur et les synthèses, sans remettre en cause les avances ou soldes signés admis.
5. **SEC-005 :** validation minimale commune des nouveaux mots de passe/reset, limite supérieure raisonnable et temporisation progressive des échecs ; pas de compte/support universel.
6. **SEC-006 :** refus explicite d'un historique de migration inconnu/plus récent avant migration et ouverture métier.
7. **SEC-007 :** procédure de restauration de confiance, communication sur remplacement des comptes et perte des opérations postérieures ; documentation de récupération. Pas de DRM ni cryptographie artificielle dans ce lot.

Ordre de travail conseillé : sessions et intégrité de maintenance d'abord ; règles de stockage/calcul et compatibilité ensuite ; contrat installateur et recette de livraison en parallèle organisationnel. Aucun agent parallèle n'a été utilisé pour cet audit. Aucun nouveau fichier de migration 005 n'est nécessaire aux refus/validations ci-dessus ; toute évolution future de schéma devra faire l'objet d'une décision distincte.

Critère de sortie : reproductions adversariales désormais refusées proprement, suites existantes vertes, build Release sans avertissement, migrations toujours maîtrisées, garde DB/WAL/SHM PASS et recette ACL avec comptes standard. Ne pas supprimer un contrôle d'intégrité ou affaiblir un test pour obtenir le vert.

## 23. SECURITY FIX PACK B — durcissement simple avant déploiement client

- Journal local borné d'événements utiles (SEC-008), sans secrets, avec ACL et procédure de support.
- Limites raisonnables de textes/identifiants et traitement des contrôles visuels (SEC-009), sans casser Unicode arabe/français.
- Politique de rétention/contrôle d'espace et copie hors poste, avec exercice de restauration sur environnement isolé (SEC-010).
- Scan complet solution et inventaire du paquet, SDK fixé, validation signatures et exclusions de fichiers sensibles (SEC-012/014).
- Présentation plus informative du fichier de restauration (date/provenance/contenu synthétique), en précisant que ce n'est pas une preuve cryptographique (SEC-007).
- Guide client : verrouillage Windows, départ d'employé, compte standard, supports et imprimantes approuvés, récupération et assistance.

Pas de changement de schéma, de framework d'authentification ou d'architecture distribué à imposer uniquement pour ce durcissement ; choisir les mécanismes minimaux qui respectent les données existantes.

## 24. POST-V1 HARDENING

- Idempotence ciblée des réessais d'une même opération si le besoin se confirme ; aucune interdiction naïve de paiements identiques légitimes.
- Rehash opportuniste et paramètres PBKDF2 calibrés sur les postes réels ; contrôle des échecs/mots de passe compromis si exploitable offline sans dépendance fragile.
- Verrouillage applicatif après inactivité selon usage ; ne remplace pas verrouillage Windows et révocation backend.
- Licence offline signée simple avec procédure de réactivation si la protection commerciale l'exige ; obfuscation facultative après tests.
- Backups authentifiés/chiffrés seulement avec vraie garde et récupération de clés et besoin de confidentialité défini.
- Isolation CI supplémentaire, tests de coupure de courant/ressources, audit approfondi des dépendances natives et du paquet final.
- Séparation par service Windows/identité distincte uniquement si le produit promet à terme une résistance aux autres programmes exécutés par le même utilisateur ; hors scope V1.

## 25. Décision de sécurité de release

**B — FIX REQUIRED BEFORE V1.**

Motifs déterminants : une révocation produit ne retire pas réellement l'accès existant ; les ACL observées ne conviennent pas à la confidentialité d'un poste partagé ; la maintenance ne vérifie pas son checkpoint/exclusivité ; des écritures autorisées rendent les calculs impossibles ; l'authentification accepte des mots de passe triviaux ; le démarrage ne refuse pas une base de version future.

Les contrôles financiers et la restauration sont suffisamment structurés pour des corrections ciblées. Il n'y a pas de justification à classer toute l'application compromise, ni à repousser V1 pour du DRM, un service serveur ou une refonte cloud. **Ce SHA ne doit pas être présenté comme SECURITY READY FOR V1.** Après Pack A, recette installateur et conditions opérationnelles, une nouvelle validation pourra revoir cette décision ; cet audit ne préapprouve pas des correctifs non examinés.

## 26. Risques résiduels et acceptation

Le périmètre utilisateur accepte déjà la limite de l'administrateur Windows local. Les autres acceptations ci-dessous sont **proposées pour V1**, pas une approbation silencieuse donnée au nom du client :

- Admin local/compromission système : copie, remplacement, inspection mémoire et patch des binaires non empêchés absolument.
- Compte Windows ayant les droits DB : autres programmes de ce compte potentiellement capables de lire/écrire les mêmes données. Si le client refuse cette limite, l'architecture V1 directe SQLite ne remplit pas cette exigence.
- Responsable : pouvoir de reset/restauration et de saisie financière voulu ; supervision et récupération nécessaires.
- Backup non chiffré/non authentifié : acceptable uniquement sur supports et parcours de provenance contrôlés, avec information explicite sur les comptes restaurés.
- Copie/décompilation : traitée par provenance, licence/contrat et éventuellement activation simple, sans garantie d'impossibilité technique.
- Double saisie volontaire ou distincte : une application offline ne prouve pas qu'un mouvement monétaire réel a eu lieu ; reconciliation opérateur nécessaire.
- Papier/spool/PDF : informations exposées après impression, à gérer par accès aux imprimantes et supports.
- Perte du poste/ransomware : backup sur le même disque insuffisant ; copie externe connue et récupération testée.

SEC-001/002 et les corrections applicatives de Pack A ne sont pas marquées « acceptées » pour éviter de les corriger.

## 27. Commandes, contrôles et preuves conservées

### Commandes exécutées

Dans le dépôt d'origine, uniquement lecture Git/fichiers hormis fetch autorisé et création de ce rapport :

```text
git fetch origin
git rev-parse origin/integration origin/release/v1.0-rc
git rev-parse HEAD
git status --short
git ls-tree -r --name-only origin/integration
git archive --format=zip --output=<TEMP>/source.zip 41a113c41cbbe23b633893594362e452c15ff6c3
git diff --stat
```

Dans l'extraction temporaire :

```text
dotnet --info
dotnet restore FinancialManagement.sln --verbosity minimal
dotnet build FinancialManagement.sln -c Release --no-restore --verbosity minimal
dotnet test FinancialManagement.sln -c Release --no-build --no-restore --logger trx --results-directory ../test-results
dotnet list FinancialManagement.sln package --vulnerable --include-transitive --no-restore --format json
dotnet list src/FinancialManagement.Desktop/FinancialManagement.Desktop.csproj package --include-transitive --no-restore
dotnet tool restore
dotnet ef migrations list --project src/FinancialManagement.Infrastructure --configuration Release --no-build
dotnet ef migrations has-pending-model-changes --project src/FinancialManagement.Infrastructure --configuration Release --no-build
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts/verify-programdata-unchanged.ps1 -Mode Capture -SnapshotPath ../programdata-before.json
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File scripts/verify-programdata-unchanged.ps1 -Mode Verify -SnapshotPath ../programdata-before.json
dotnet run --project ../probe/AuditProbe.csproj -c Release
dotnet run --project ../probe2/AuditProbe.csproj -c Release
```

Autres lectures : `rg --files`, recherches `rg -n/-l` de sécurité, lecture numérotée des fichiers du snapshot, `Get-Acl`, `Get-AuthenticodeSignature` du build local, `Get-FileHash` de la DLL SQLite générée, inventaire metadata des fichiers/processus et parsing des résultats TRX/packages. Les sources publiques Microsoft/SQLite et release SQLitePCL.raw ont été consultées pour vérifier les comportements/version/signature ; pas de source projet envoyée à un scanner externe.

Deux commandes d'inspection ont été corrigées : un motif `rg` commençant par des tirets a nécessité `-e` ; les premières commandes EF avec `-c Release` cherchaient les sorties Debug et ont échoué, puis ont réussi avec `--configuration Release`. Une lecture numérotée avait une borne textuelle invalide et a été reprise. Ces erreurs n'ont pas modifié de source ou de base de production.

### Résultats

| Contrôle | Résultat |
|---|---|
| Références Git après fetch | Deux SHA conformes |
| Source auditée | Extraction exacte d'integration, pas HEAD local |
| Comparaison finale de l'extraction avec source.zip | 471 fichiers vérifiés par SHA-256, 0 modifié |
| Build Release | 0 warnings, 0 errors |
| Tests infrastructure/métier | 1 183/1 183 PASS |
| Tests Desktop | 47/47 PASS |
| Total | 1 230/1 230 PASS, aucun ignoré |
| NuGet solution entière, transitif | Aucune vulnérabilité signalée |
| EF migrations list | 001, 002, 003, 004 uniquement ; affichées Pending car factory en mémoire |
| EF pending model changes | Aucun changement de modèle |
| Garde ProgramData avant/après | PASS, DB inchangée, WAL/SHM absents |
| Signature du build EXE local | NotSigned |
| Probe révocation | Paiement accepté après désactivation/reset : défaut confirmé |
| Probe RBAC direct | Employé refusé par administration/backup |
| Probe agrégat | Paiement Int64.MaxValue persisté, exception de trésorerie |
| Probe backup altéré | Archive recalculée acceptée et mot de passe substitué actif |
| Probe version future | Startup réussi, backup quotidien échoué |
| Probe checkpoint occupé | busy=1, helper de production ne refuse pas |

Le script `verify-foundation.cmd` complet a été **audité statiquement, pas relancé comme wrapper**. Ses composants utiles ont été exécutés séparément en Release avec garde avant/après et scan plus large. Il serait inexact de présenter une nouvelle exécution du wrapper Foundation comme réalisée ; les résultats ci-dessus sont les résultats effectivement obtenus.

### Emplacement des éléments temporaires

Racine : `C:\Users\HP\AppData\Local\Temp\tresoria-security-v1-8d6dd690060c46f79ff5b0ba54b9a094`.

- `source.zip`, `source/` : extraction du SHA ; sources non corrigées.
- `restore.log`, `build-release.log`, `tests-release.log`, `test-results/*.trx`.
- `vulnerabilities.json`, `tools.log`, `migrations.log`, `pending-model.log`.
- `programdata-before.json` : fingerprints filesystem de DB/WAL/SHM, aucune ligne métier.
- `acl-evidence.json` : ACL lues, pas d'altération.
- `probe/Program.cs`, `probe.log`, `probe2/Program.cs`, `probe2.log` : bancs d'audit indépendants créés hors dépôt ; projets et DB exclusivement synthétiques sous ces répertoires. Les changements de hash, de rôle, de montant et l'entrée de migration fictive concernent ces seules DB.

Les éléments TEMP peuvent disparaître avec le nettoyage Windows. Les conclusions et étapes de reproduction nécessaires sont conservées dans ce rapport. Aucun backup de production n'a été copié dans ces fixtures.

## 28. Attestation de non-modification

```text
source modified: NO
project files/scripts/configuration modified: NO
production database/schema/migrations modified: NO
ProgramData modified: NO
Windows ACL/policy modified: NO
production application launched: NO
release/v1.0-rc modified: NO
git add: NO
commit: NO
push: NO
merge/rebase/reset/clean: NO
fix implemented: NO
```

Seul nouveau fichier dans le dépôt : **SECURITY-AUDIT-V1.md**, laissé non suivi et confirmé par `git status --short`. Les diffs de l'arbre de travail et de l'index sont vides ; HEAD et les deux références de source de vérité restent aux SHA indiqués. Les 471 fichiers extraits ont été comparés à l'archive Git par SHA-256 après exécution : aucun modifié. `git fetch` a actualisé les références distantes comme explicitement demandé. Les sorties de build/tests et bancs synthétiques ont été créés dans TEMP ; ils ne sont pas des modifications des fichiers source/projet audités. Les tests existants peuvent nettoyer leurs propres fixtures temporaires ; aucune suppression ou restauration de données client n'a été effectuée.

**FIN — audit terminé, aucun correctif implémenté.**
