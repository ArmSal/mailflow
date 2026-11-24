# Contribuer et étendre Mailflow

Ce guide explique **où modifier le code** pour les extensions les plus courantes. Lisez d'abord la section « Architecture » du [README](README.md) : `Domain` ← `Application` ← `Infrastructure` ← `Web`, aucune dépendance ne remonte, et le cœur ne parle aux services externes qu'à travers des interfaces.

## Avant de commencer

```powershell
dotnet build
dotnet test
```

Règles : le moteur de règles et le pipeline sont testés en priorité ; écrivez un test qui échoue avant de corriger un bug ; ne committez jamais d'identifiants, de vraies factures ni de noms de clients (anonymisez les PDF d'exemple).

## Ajouter un fournisseur de messagerie IMAP

Une seule ligne dans [`ImapPresets.cs`](src/Mailflow.Infrastructure/Mail/ImapPresets.cs) :

```csharp
new("Mon fournisseur", "imap.exemple.fr", 993, true, "Remarque affichée à l'utilisateur."),
```

Le menu « Fournisseur » du formulaire de boîte mail lit cette liste ; un test vérifie qu'ils restent identiques. Tout serveur IMAP fonctionne déjà via « IMAP personnalisé ». Ajoutez un cas dans `ImapPresetsTests` si vous voulez figer l'hôte.

## Ajouter un champ de condition de règle

1. Ajoutez la valeur dans l'énumération `ConditionField` ([`Enums.cs`](src/Mailflow.Domain/Enums/Enums.cs)).
2. Si la donnée n'existe pas encore, ajoutez-la à [`MatchContext`](src/Mailflow.Domain/Rules/MatchContext.cs) et renseignez-la dans `MessagePipeline` **et** dans `MailboxRuleTester` (test de règle).
3. Ajoutez le `case` dans [`ConditionEvaluator`](src/Mailflow.Domain/Rules/ConditionEvaluator.cs).
4. Ajoutez le libellé français dans [`RuleSupport.cs`](src/Mailflow.Web/Pages/Rules/RuleSupport.cs).
5. Ajoutez des tests dans `RuleEngineTests`.

Les énumérations sont stockées en texte : **aucune migration** n'est nécessaire pour une nouvelle valeur.

## Ajouter une action de règle

1. Ajoutez la valeur dans `ActionType`.
2. Traitez-la dans [`MessagePipeline`](src/Mailflow.Application/Processing/MessagePipeline.cs) (les actions y sont interprétées, y compris la règle « une seule action de chaque famille »).
3. Libellé dans `RuleSupport.cs` et, si l'action a des paramètres, validation dans [`Rules/Edit.cshtml.cs`](src/Mailflow.Web/Pages/Rules/Edit.cshtml.cs) ; un nouveau champ sur `RuleAction` demande une migration (voir plus bas).
4. Tests dans `PipelineTests` : vérifiez surtout l'ordre (une action sur la boîte mail ne s'exécute qu'**après** l'enregistrement) et le comportement en cas d'échec.

## Ajouter un backend d'impression, de stockage ou de messagerie

Implémentez l'interface correspondante (dans [`Abstractions.cs`](src/Mailflow.Application/Abstractions/Abstractions.cs)) dans `Infrastructure`, puis enregistrez-la dans l'extension d'injection de dépendances.

| Besoin | Interface | Exemple existant |
|---|---|---|
| Autre imprimante (PrintNode, CUPS…) | `IPrinterService` | `SumatraPrinterService` |
| Stockage distant (cloud, partage réseau) | `IDocumentStore` | `FileSystemDocumentStore` |
| Autre protocole (Graph API, POP3, OAuth2) | `IMailClient` | `MailKitClient` |
| Autre extraction de texte (OCR) | `IPdfTextReader` | `PdfPigTextReader` |
| Autre stockage de secrets | `ISecretStore` | `DpapiSecretStore` |

`IMailClient` est pensée pour IMAP (dossiers, UID). Un protocole très différent peut demander d'ajuster l'interface : ouvrez d'abord une *issue*. L'OAuth2 demande en plus de stocker des jetons au lieu d'un mot de passe.

## Modifier la base de données

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations add NomDeLaMigration -p src/Mailflow.Infrastructure -s src/Mailflow.Infrastructure
```

Les migrations s'appliquent automatiquement au démarrage. Ne modifiez jamais une migration déjà publiée. **Toute modification d'une entité du domaine exige sa migration** : sans elle l'application refuse de démarrer (les tests d'intégration le détectent). Pour une nouvelle colonne obligatoire, donnez une valeur par défaut qui convient aux lignes existantes.

## Sécurité : à ne pas casser

- Ne supprimer **jamais** un e-mail ; ne jamais ouvrir ni exécuter une pièce jointe.
- Ouvrir un PDF depuis le tableau de bord se fait **par identifiant de document**, jamais avec un chemin venant du navigateur.
- Tout formulaire POST garde le jeton anti-falsification ; pas de `Html.Raw` sur des données utilisateur.
- L'impression ne dépend que des expéditeurs autorisés : toute modification de `PrintPolicy` ou de `ExtractAddress` demande des tests dédiés.
- Connexion non chiffrée interdite, sauf vers `localhost` (tests).

## Limites connues

Pensé pour **un PC par magasin** : SQLite, un seul worker séquentiel, pas de tableau de bord central multi-magasins, pas de système de modules complémentaires. Voir la feuille de route du README.
