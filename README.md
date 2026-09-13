# StreamDeckForge

Générateur de profils Elgato Stream Deck à partir des raccourcis clavier des applications
installées sur la machine. Scan du poste, extraction des combinaisons de touches par trois
méthodes complémentaires, placement sur une grille, export d'un fichier
`.streamDeckProfile` importable dans le logiciel Elgato.

## Pile technique

| Élément | Choix |
| --- | --- |
| Plateforme | .NET 8 (`net8.0-windows`) |
| Interface | WPF, style Fluent Windows 11 (option explicitement prévue par le cahier des charges) |
| APIs système | P/Invoke `kernel32` (ressources PE), `user32` (icônes), `System.Windows.Automation` (UI Automation) |
| Sérialisation | `System.Text.Json` |
| Archive | `System.IO.Compression` |
| Dépendances NuGet | **aucune** |

Le projet ne référence aucun paquet NuGet : `restore` fonctionne hors ligne et il n'y a rien
à auditer côté chaîne d'approvisionnement. `Vanara.PInvoke`, cité comme option dans le
cahier des charges, a été remplacé par des déclarations `DllImport` directes (une
soixantaine de lignes au total, dans `NativeResources.cs` et `IconLoader.cs`).

WinUI 3 a été écarté au profit de WPF : WinUI 3 impose le workload Windows App SDK et un
packaging MSIX ou un déploiement embarqué, sans bénéfice pour une application de bureau
mono-fenêtre.

## Structure

```
StreamDeckForge.sln
src/
  StreamDeckForge.Core/          Bibliothèque : découverte, extraction, génération
    Discovery/                   Registre, menu Démarrer, lecture .lnk, icônes
    Extraction/                  Les trois méthodes + orchestration
      ConfigFile/                Parseurs dédiés (VS Code, JetBrains, Sublime, Notepad++)
    Keys/                        Table VK / Qt et analyse des écritures "Ctrl+Shift+S"
    StreamDeck/                  Manifeste, grille, écriture de l'archive
  StreamDeckForge.App/           Interface WPF
  StreamDeckForge.Cli/           `sdforge` : mêmes fonctions en ligne de commande
docs/
  format-profil.md               Format de l'archive .streamDeckProfile tel qu'implémenté
```

## Compilation et exécution

```bash
dotnet build StreamDeckForge.sln -c Release
```

```bash
dotnet run --project src/StreamDeckForge.App
```

La ligne de commande sert aussi d'outil de vérification :

```bash
dotnet run --project src/StreamDeckForge.Cli -- selftest
```

```bash
dotnet run --project src/StreamDeckForge.Cli -- export --app "notepad++" --out profil.streamDeckProfile --device xl
```

## Livrer l'application à quelqu'un d'autre

Deux formats, selon la contrainte : la taille du transfert ou l'absence de prérequis.

| Commande | Résultat | Prérequis sur le poste de destination |
| --- | --- | --- |
| `.\publish.ps1` | `publish\StreamDeckForge.exe`, **68 Mo** | aucun, le runtime est embarqué |
| `.\publish.ps1 -FrameworkDependent` | `publish-leger\StreamDeckForge.exe`, **0,3 Mo** | *Runtime .NET 8 Desktop*, à installer une fois |

La version légère passe par courriel ; c'est celle à privilégier dès qu'une pièce jointe
est limitée en taille. Le runtime s'installe depuis
[dotnet.microsoft.com](https://dotnet.microsoft.com/fr-fr/download/dotnet/8.0) (colonne
*.NET Desktop Runtime*, x64) ou par `winget install Microsoft.DotNet.DesktopRuntime.8`.

La version autonome ne descend pas plus bas : WPF interdit le *trimming*, et ses 68 Mo sont
déjà le résultat de la compression des assemblies embarquées
(`EnableCompressionInSingleFile`). Pour la transmettre malgré une limite de 30 Mo, il faut
une archive en plusieurs volumes ou un lien de partage.

`publish-leger\LISEZ-MOI.txt` accompagne la version légère : prérequis, avertissement
SmartScreen et mode d'emploi en français, destiné à l'utilisateur final.

Attention : un exécutable téléchargé ou copié depuis un autre poste déclenche
l'avertissement SmartScreen de Windows au premier lancement (« Informations
complémentaires » puis « Exécuter quand même »), faute de signature de code.

## Les trois méthodes d'extraction

### Méthode 1 — tables d'accélérateurs Win32 (statique)

`AcceleratorTableExtractor` ouvre le binaire avec
`LoadLibraryEx(LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE)` — aucun code de
la cible n'est exécuté, et le chargement réussit quelle que soit son architecture — puis
énumère les ressources `RT_ACCELERATOR`. Chaque entrée fait huit octets : indicateurs, code
de touche, identifiant de commande. Le nom de la commande est recherché dans la table
`RT_STRING` au même identifiant, convention MFC qui y range
`« texte de barre d'état\nlibellé court »`.

Limite intrinsèque : seules les applications Win32 classiques publient ces tables.
Electron, Qt, WinUI, WPF et WinForms n'en produisent pas.

### Méthode 2 — UI Automation (dynamique)

`UiAutomationExtractor` attache `AutomationElement` à la fenêtre principale du processus et
parcourt l'arborescence en lisant la propriété `AcceleratorKey`, que menus, rubans et
boutons renseignent avec l'écriture affichée à l'utilisateur. L'option « Déplier les menus »
utilise `ExpandCollapsePattern` : les menus Win32 ne se peuplent qu'à l'ouverture, sans quoi
l'arborescence lue est vide. Décocher l'option rend l'exploration purement passive.

L'application cible doit être démarrée et avoir une fenêtre visible. Une application lancée
en tant qu'administrateur n'est pas lisible depuis un processus non élevé.

### Méthode 3 — parseurs dédiés

`ConfigFileExtractor` localise les fichiers de configuration connus et délègue à un parseur
par produit :

| Produit | Fichier |
| --- | --- |
| VS Code, Insiders, VSCodium, Cursor, Windsurf | `%APPDATA%\<produit>\User\keybindings.json` |
| IDE JetBrains | `%APPDATA%\JetBrains\<produit>\keymaps\*.xml` |
| Sublime Text / Merge | `%APPDATA%\Sublime*\Packages\User\*.sublime-keymap` |
| Notepad++ | `%APPDATA%\Notepad++\shortcuts.xml` ou à côté de l'exécutable |

Ajouter un produit revient à implémenter `IKeybindingFileParser` et à l'ajouter à la liste
du constructeur de `ConfigFileExtractor`.

Les trois méthodes tournent indépendamment : l'échec de l'une n'empêche pas les autres, et
leurs résultats sont fusionnés en dédupliquant par combinaison de touches, le libellé le
plus parlant l'emportant.

## Critères d'acceptation

Mesures faites sur le poste de développement (Windows 11 Pro 26200, .NET SDK 8.0.425).

| Critère | État |
| --- | --- |
| Lister les logiciels en moins de trois secondes | **Vérifié.** 98 applications détectées en 103 à 161 ms sur trois exécutions consécutives de `sdforge scan`. Registre et menu Démarrer sont scannés en parallèle, les `.lnk` lus par un parseur binaire plutôt que par COM, les icônes et les chemins manquants résolus paresseusement. |
| Obtenir les combinaisons par au moins une méthode | **Vérifié pour les trois méthodes.** Méthode 1 : 20 raccourcis sur `regedit.exe`, 15 sur `mmc.exe`, 10 sur `notepad.exe`. Méthode 2 : 3 raccourcis sur Paint, 8 sur Excel en lecture passive (3,5 s). Méthode 3 : 4 raccourcis sur un `shortcuts.xml` Notepad++. |
| Fichier `.streamDeckProfile` valide et importable | **Partiellement vérifié.** `sdforge selftest` produit et relit un profil valide pour les quatre modèles ; un export réel depuis `regedit.exe` donne 20 actions dans une archive conforme (`exemples/`). En revanche **l'import dans le logiciel Elgato n'a pas pu être testé** : il n'est pas installé sur ce poste. |

Le seul point non démontré est donc l'import final. Le manifeste suit le format décrit dans
[docs/format-profil.md](docs/format-profil.md) et `exemples/Editeur du Registre.streamDeckProfile`
permet de le tester en une manipulation.

## Limites connues

- **Codes produit des modèles.** Le champ `Device.Model` du manifeste porte le code Elgato
  du boîtier (`20GAI` Mini, `20GAA` Stream Deck, `20GBD` MK.2, `20GAT` XL). Ces valeurs sont
  reprises des profils produits par le logiciel Elgato ; si un boîtier attend autre chose,
  la valeur est à corriger dans `StreamDeckDevices`.
- **Une seule page.** Un profil exporté couvre une page de touches. Les raccourcis
  excédentaires sont signalés mais non placés ; les dossiers et profils imbriqués ne sont
  pas générés.
- **Disposition clavier.** Les codes de touches OEM (`;`, `,`, `/`…) correspondent à la
  disposition US, la seule que l'on puisse dériver d'un code de touche virtuel sans
  connaître la disposition active du poste.
- **Accords à deux temps.** `Ctrl+K Ctrl+S` est écarté : une touche Stream Deck n'émet
  qu'une combinaison.
- **UI Automation ne voit que l'interface déjà construite.** Sur un ruban Office, seul
  l'onglet affiché est lu en mode passif ; sur Excel cela donne huit raccourcis, pas la
  totalité. Cocher « Déplier les menus » en trouve davantage, au prix d'une interaction
  réelle avec la fenêtre de l'application.
- **Noms de commandes en lecture statique.** Les tables d'accélérateurs ne contiennent que
  des identifiants numériques. Le nom est retrouvé dans la table de chaînes, ce qui ne
  fonctionne de façon fiable que pour les applications MFC. Ailleurs, une chaîne sans
  rapport pourrait être lue : un filtre de plausibilité l'écarte, au prix d'un libellé
  « Commande 768 » qu'il faut renommer à la main dans l'interface. C'est ce qui arrive sur
  Notepad, alors que regedit donne bien « Actualise la fenêtre » ou « Renomme la sélection ».
- **Images de touches.** L'export ne pose que des titres ; les visuels sont laissés à
  l'icône par défaut de l'action Elgato. La structure `Images/` est déjà écrite par
  `StreamDeckProfileWriter` si l'on renseigne `KeyAssignment.ImagePng`.
