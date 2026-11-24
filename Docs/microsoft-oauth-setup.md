# Relever une boîte Outlook / Hotmail avec un compte Microsoft (OAuth2)

Outlook.com et Hotmail refusent la connexion IMAP par mot de passe (erreur « The LOGIN command is disabled »). Mailflow se connecte donc avec votre compte Microsoft, par un **code d'appareil** : aucun mot de passe n'est saisi ni stocké dans Mailflow.

Ce qu'il faut savoir avant de commencer :

- **C'est gratuit.** L'inscription d'une application dans Microsoft Entra (Azure) ne coûte rien et ne demande ni abonnement ni carte bancaire.
- **Aucun secret n'est créé.** L'application est de type « client public » : il n'y a ni mot de passe d'application ni certificat à conserver. Seul l'identifiant d'application (un GUID, non secret) est enregistré dans Mailflow.
- **Chaque installation utilise sa propre application.** Vous inscrivez votre application une fois, et vous la réutilisez pour toutes vos boîtes Microsoft.
- Les jetons de connexion sont stockés chiffrés dans la base de Mailflow, jamais affichés.

## 1. Inscrire l'application (une seule fois)

Les libellés exacts peuvent légèrement varier selon les mises à jour du portail.

1. Ouvrez <https://entra.microsoft.com> (ou <https://portal.azure.com>, puis « Microsoft Entra ID ») et connectez-vous avec un compte Microsoft. Un compte personnel suffit : si le portail demande un annuaire, acceptez d'en créer un gratuit.
2. Menu **Applications** (ou **Identité > Applications**) > **Inscriptions d'applications** > **Nouvelle inscription**.
3. **Nom** : au choix, par exemple « Mailflow ».
4. **Types de comptes pris en charge** : choisissez **Comptes dans un annuaire organisationnel et comptes Microsoft personnels**. C'est indispensable pour une adresse Outlook.com ou Hotmail.
5. **URI de redirection** : laissez vide.
6. Cliquez sur **Inscrire**.
7. Sur la page de l'application, copiez l'**ID d'application (client)** : c'est un GUID de la forme `00000000-0000-0000-0000-000000000000`.

## 2. Autoriser le flux « client public »

1. Dans l'application, menu **Authentification** (ou **Authentication**).
2. Si une zone « Configuration de la plateforme » est proposée, cliquez sur **Ajouter une plateforme** > **Applications mobiles et de bureau**, cochez l'URI `https://login.microsoftonline.com/common/oauth2/nativeclient`, puis **Configurer**.
3. Onglet **Paramètres avancés** (Advanced settings) > **Autoriser les flux clients publics** (Allow public client flows) : **Oui**.
4. **Enregistrer**.

## 3. Ajouter les autorisations IMAP

Ce n'est pas Microsoft Graph : l'accès IMAP se trouve sous Exchange Online.

1. Menu **Autorisations de l'API** (API permissions) > **Ajouter une autorisation**.
2. Onglet **API utilisées par mon organisation** : cherchez **Office 365 Exchange Online** (si absent, essayez « Exchange Online » ou l'onglet « API Microsoft » > « Office 365 Exchange Online »).
3. Choisissez **Autorisations déléguées** (Delegated permissions).
4. Cochez **IMAP** > **IMAP.AccessAsUser.All**, puis **Ajouter des autorisations**.
5. Ajoutez aussi **offline_access** : **Ajouter une autorisation** > **Microsoft Graph** > **Autorisations déléguées** > **OpenId permissions** > **offline_access**. Elle permet de rester connecté sans ressaisir de code.
6. Aucun « consentement administrateur » n'est nécessaire pour un compte personnel : le consentement vous est demandé à la première connexion.

## 4. Renseigner Mailflow

1. Dans Mailflow, ouvrez **Paramètres**, carte **Connexion Microsoft**.
2. Collez l'**ID d'application (client)** copié à l'étape 1, puis **Enregistrer**. Le badge passe à « Configuré ».

## 5. Ajouter la boîte et se connecter

1. **Boîtes mail > Ajouter une boîte mail**. Choisissez le fournisseur **Outlook / Hotmail** : le mode de connexion passe automatiquement à **Compte Microsoft (OAuth2)** et le champ « Mot de passe » disparaît.
2. Renseignez le magasin, le nom affiché, l'adresse e-mail et l'identifiant (l'adresse e-mail complète), puis **Enregistrer**.
3. Rouvrez la boîte (**Modifier**). Dans le cadre **Connexion Microsoft**, cliquez sur **Se connecter avec Microsoft**.
4. Mailflow affiche un **code** et une adresse. Ouvrez cette adresse sur n'importe quel appareil (téléphone compris), saisissez le code, puis connectez-vous avec le compte Microsoft de la boîte et acceptez les autorisations.
5. Le cadre passe à **Connecté**. Vous pouvez vérifier avec **Tester la connexion**.

Le code expire au bout d'environ 15 minutes ; en cas d'échec ou d'expiration, cliquez sur **Réessayer**.

## En cas de problème

- **« Reconnexion requise »** dans la liste des boîtes : la connexion a expiré ou a été révoquée. Ouvrez la boîte et reconnectez-la (étape 5).
- **« L'identifiant d'application Microsoft n'est pas renseigné »** : voir l'étape 4.
- **Erreur à la saisie du code** indiquant que l'application n'est pas autorisée pour ce type de compte : revoyez le type de comptes à l'étape 1 (il se modifie dans **Manifeste**, clé `signInAudience` = `AzureADandPersonalMicrosoftAccount`).
- **Échec avec un compte professionnel ou scolaire** : l'administrateur du locataire peut avoir désactivé l'IMAP ou exiger un consentement administrateur.
- Pour déconnecter une boîte : bouton **Déconnecter** dans son cadre Connexion Microsoft. Pour tout révoquer côté Microsoft : <https://account.live.com/consent/Manage>.
