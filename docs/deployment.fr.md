# Déployer Caret dans une organisation

Guide destiné aux administrateurs informatiques : ce qu'est Caret, ce qu'il fait sur le poste et sur le réseau, et comment le déployer avec Microsoft Intune ou un autre outil. *(English version: [deployment.md](deployment.md).)*

## En bref

| | |
| --- | --- |
| **Quoi** | Un éditeur Markdown avec un convertisseur intégré de Word, Excel, PowerPoint, PDF et CSV vers Markdown |
| **Éditeur / source** | Open source, licence MIT : <https://github.com/fegyenc/Caret> |
| **Package** | MSIX, par utilisateur. Aucun droit d'administrateur requis ; n'installe ni service, ni pilote, ni tâche planifiée. |
| **Architectures** | x64 et ARM64 |
| **Prérequis** | Windows 10 version 1809 ou ultérieure ; Windows 11 recommandé. Le runtime Microsoft Edge WebView2, inclus dans Windows 11 et installé avec Microsoft 365 Apps sur Windows 10. .NET et le Windows App SDK sont inclus dans le package. |
| **Langues** | Anglais, français, espagnol (suit la langue d'affichage de Windows ; modifiable par l'utilisateur) |
| **Compte / connexion** | Aucun |
| **Télémétrie** | Aucune |

## Données et réseau

- **Les documents ne quittent jamais le poste.** L'édition, l'enregistrement et la conversion se font localement. Aucun service cloud n'est associé à Caret.
- **Données de l'application** : paramètres, fichiers récents, modèles et sauvegardes de récupération sont stockés dans le dossier de données du package (`%LOCALAPPDATA%\Packages\<nom de famille du package>\LocalState`). Ils sont supprimés avec l'application.
- **Connexions sortantes possibles :**

| Destination | Quand | Version Microsoft Store | Version GitHub | Désactivable |
| --- | --- | --- | --- | --- |
| `api.github.com` | Recherche de mises à jour, au plus une fois par jour | Jamais | Oui | Stratégie `DisableUpdateCheck` (ci-dessous), ou par l'utilisateur dans les Paramètres |
| `pypi.org`, `files.pythonhosted.org` | Installation du convertisseur facultatif MarkItDown, uniquement si l'utilisateur clique sur *Installer* | Jamais | Sur demande | Stratégie `DisableMarkItDownInstall` |
| Sites web cités dans une note | Images web affichées dans une note (comme un navigateur) | Oui | Oui | Non (dépend du contenu) |

La conversion Word, Excel, PowerPoint, PDF et CSV est intégrée et ne nécessite ni réseau ni Python. MarkItDown n'ajoute que des formats plus rares.

La politique de confidentialité se trouve dans [PRIVACY.md](../PRIVACY.md#français).

## Option A : application du Microsoft Store (recommandée)

Une fois Caret publié dans le Microsoft Store, Intune le déploie et le met à jour directement depuis le Store.

1. Centre d'administration Intune → **Applications** → **Windows** → **Ajouter** → type d'application **Application Microsoft Store (nouveau)**.
2. **Rechercher dans l'application Microsoft Store (nouveau)** → *Caret* → sélectionnez-la. L'ID Store (commençant par `9`) est renseigné.
3. **Comportement d'installation : Utilisateur.** Caret est une application par utilisateur.
4. Affectez-la à des groupes en **Disponible pour les appareils inscrits** (installation depuis le Portail d'entreprise) ou **Obligatoire**.

Intune maintient l'application à jour via le Store ; les utilisateurs n'ont pas besoin de compte Store. Le même ID fonctionne avec winget :

```
winget install --source msstore --id <ID Store>
```

## Option B : MSIX métier (line-of-business)

Pour les organisations qui n'utilisent pas le Store ou veulent contrôler chaque version :

1. Téléchargez le `.msix` depuis [GitHub Releases](https://github.com/fegyenc/Caret/releases), ou compilez-le depuis les sources.
2. **Signez-le avec le certificat de signature de code de votre organisation.** Le `Publisher` du package doit correspondre au sujet du certificat : il faut donc recompiler avec le nom de votre éditeur. Compilez depuis les sources avec les options de signature décrites dans [PACKAGING.md](../PACKAGING.md), après avoir défini `Identity/Publisher` dans `Package.appxmanifest` selon le sujet de votre certificat.
3. Vérifiez que le certificat est approuvé sur les postes (c'est généralement déjà le cas pour une autorité de signature interne).
4. Intune → Applications → Windows → Ajouter → **Application métier** → chargez le `.msix` → affectez.

Les mises à jour se redéploient de la même façon. Activez la stratégie `DisableUpdateCheck` pour que les utilisateurs ne soient pas avertis des versions GitHub non validées par votre organisation.

## Stratégies

Caret lit des valeurs DWORD sous `HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Caret` (ou le même chemin sous `HKEY_CURRENT_USER`). Définissez-les avec les préférences de stratégie de groupe, un script de correction Intune ou un profil de configuration.

| Valeur | Effet |
| --- | --- |
| `DisableUpdateCheck` = `1` | Pas de recherche de mises à jour sur GitHub ; le réglage est masqué et les Paramètres indiquent que les mises à jour sont gérées par votre organisation |
| `DisableMarkItDownInstall` = `1` | Caret n'exécute jamais `pip` ; pour les rares formats qui nécessitent MarkItDown, il explique comment l'installer |

Dans une installation Microsoft Store, ces deux comportements sont déjà actifs et ne peuvent pas être désactivés.

Exemple (en tant qu'administrateur) :

```
reg add HKLM\SOFTWARE\Policies\Caret /v DisableUpdateCheck /t REG_DWORD /d 1 /f
reg add HKLM\SOFTWARE\Policies\Caret /v DisableMarkItDownInstall /t REG_DWORD /d 1 /f
```

Les stratégies sont lues au démarrage de Caret.

## Sécurité

- **Capacités :** uniquement `runFullTrust`, la capacité standard des applications de bureau empaquetées (nécessaire pour ouvrir et enregistrer des fichiers à l'emplacement choisi par l'utilisateur). Comme tout programme de bureau, il peut utiliser le réseau ; les seules connexions qu'il établit sont listées ci-dessus.
- **Associations de fichiers :** Caret s'enregistre comme choix *Ouvrir avec* pour `.md`, `.markdown` et extensions similaires. Il ne devient pas l'application par défaut.
- **Liens dans les notes :** les liens web s'ouvrent dans le navigateur par défaut. Les liens vers des fichiers locaux n'ouvrent que des documents et des médias ; les exécutables et scripts sont affichés dans l'Explorateur de fichiers et jamais exécutés.
- **Composants tiers** et licences : [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md). Les convertisseurs de documents sont l'Open XML SDK de Microsoft (MIT) et PdfPig (Apache 2.0).

## Désinstaller Caret

Désinstallez depuis Paramètres → Applications, ou retirez l'affectation Intune (Désinstaller). Les données de l'application sont supprimées avec le package ; les notes de l'utilisateur et les fichiers `.md` convertis restent là où ils ont été enregistrés.
