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
| **Harrison Mixbus 12 / 32C, Ardour** | `*.keys` et `*.bindings`, dans le dossier d'installation **et** dans `%LOCALAPPDATA%\Mixbus*` |
| VS Code, Insiders, VSCodium, Cursor, Windsurf | `%APPDATA%\<produit>\User\keybindings.json` |
| IDE JetBrains | `%APPDATA%\JetBrains\<produit>\keymaps\*.xml` |
| Sublime Text / Merge | `%APPDATA%\Sublime*\Packages\User\*.sublime-keymap` |
| Notepad++ | `%APPDATA%\Notepad++\shortcuts.xml` ou à côté de l'exécutable |

Ajouter un produit revient à implémenter `IKeybindingFileParser` et à l'ajouter à la liste
du constructeur de `ConfigFileExtractor`.

Pour les logiciels qu'aucun parseur ne reconnaît par son nom, `BindingFileFinder` prend le
relais : il ratisse le dossier d'installation et les dossiers de préférences dont le nom
évoque l'application (`%APPDATA%`, `%LOCALAPPDATA%`, `~/.config`) à la recherche de fichiers
`*.keys`, `*.bindings`, `*.keymap`, `keybindings.json`, `shortcuts.xml`… puis soumet chaque
fichier trouvé à tous les parseurs. **C'est le contenu qui décide, pas le nom** : un
`.keys` appartenant à un dérivé d'Ardour inconnu sera lu correctement sans qu'on ait eu à
le déclarer.

#### Cas particulier : Mixbus et Ardour

Mixbus est dérivé d'Ardour, et ces logiciels sont une exception heureuse : ils écrivent
**l'intégralité** de leurs raccourcis dans des fichiers XML, y compris les valeurs par
défaut livrées avec le produit. Ailleurs, seules les personnalisations de l'utilisateur
sont sur le disque. Le parseur lit donc la liste complète, sans que le logiciel ait besoin
d'être ouvert.

Le format est celui produit par `tools/fmt-bindings` en amont d'Ardour :

```xml
<BindingSet name="Mixbus">
 <Bindings name="Global">
  <Press>
   <Binding key="Control-s" action="Common/Save" group="File"/>
```

`ArdourBindingsParser` traduit les modificateurs logiques d'Ardour (`Primary` = Ctrl,
`Secondary` = Alt, `Tertiary` = Maj, `Level4` = Windows), accepte aussi bien
`Primary-a` que la variante accélérateur GTK `<Primary>a`, et convertit les noms de touches
GDK (`space`, `KP_Enter`, `bracketleft`) vers les codes attendus par Elgato. Les noms qui
désignent un caractère obtenu touche Maj enfoncée — `question`, `braceleft`, `plus`… —
reçoivent automatiquement le modificateur Maj, sans quoi le boîtier enverrait la mauvaise
frappe.

Vérifié sur un jeu de 20 bindings couvrant tous ces cas ; le fichier de test a été écrit
d'après le générateur officiel, **pas prélevé sur une installation Mixbus réelle**. Si un
raccourci manque à l'appel sur le poste de votre utilisateur, le plus simple est de
récupérer son fichier `.keys` : le parseur s'ajuste en quelques lignes.

Les trois méthodes tournent indépendamment : l'échec de l'une n'empêche pas les autres, et
leurs résultats sont fusionnés en dédupliquant par combinaison de touches, le libellé le
plus parlant l'emportant.

## Visuels des touches

Chaque touche reçoit un PNG 144 × 144 généré à l'export : fond sombre, pictogramme,
titre, combinaison, et un liseré de couleur. Le pictogramme et la couleur viennent de la
**famille** de la commande, déduite de son libellé et de son identifiant — enregistrement,
lecture, repères, fichier, édition, sélection, recherche, navigation, mise en forme, son,
fenêtre, réglages, aide. Les commandes d'une même famille se ressemblent, ce qui rend la
grille lisible d'un coup d'œil.

Le rendu passe par WPF (`DrawingVisual` + `RenderTargetBitmap`) et par les polices d'icônes
livrées avec Windows (*Segoe Fluent Icons*, repli sur *Segoe MDL2 Assets*) : aucune
dépendance graphique supplémentaire, aucune image à embarquer.

La reconnaissance se fait **mot à mot**, pas en sous-chaîne. C'est nécessaire : en simple
sous-chaîne, `playhead` déclenchait la famille *Lecture* à cause de `play`, et `Loop`
héritait du pictogramme de lecture parce que son identifiant contient `Transport`.

L'option est activée par défaut dans l'interface, et disponible en ligne de commande via
`--icons`. Décochée, les touches gardent l'icône par défaut de l'action Elgato avec le
titre par-dessus.

### Quelle couverture attendre ?

Aucune méthode ne marche partout, mais elles se complètent. En pratique :

| Type d'application | Ce qui fonctionne |
| --- | --- |
| Win32 / MFC classiques (regedit, mmc, vieux logiciels métier) | Méthode 1, sans ouvrir le logiciel |
| Office, WinForms, WPF, WinUI (Excel, Paint, Explorateur) | Méthode 2, logiciel ouvert |
| Éditeurs et DAW à fichiers de bindings (Mixbus, Ardour, VS Code, JetBrains…) | Méthode 3, liste complète et fiable |
| Electron et Qt sans fichier de bindings (Discord, Slack, Spotify…) | Méthode 2 seulement, et souvent maigre |

Le dernier cas est le seul angle mort réel : ces applications gardent leurs raccourcis en
dur dans leur code JavaScript ou C++, sans jamais les publier ni à Windows, ni sur le
disque. Pour celles-là, il n'existe pas de méthode d'extraction, seulement une saisie
manuelle ou une liste préétablie.

## Critères d'acceptation

Mesures faites sur le poste de développement (Windows 11 Pro 26200, .NET SDK 8.0.425).

| Critère | État |
| --- | --- |
| Lister les logiciels en moins de trois secondes | **Vérifié.** 98 applications détectées en 103 à 161 ms sur trois exécutions consécutives de `sdforge scan`. Registre et menu Démarrer sont scannés en parallèle, les `.lnk` lus par un parseur binaire plutôt que par COM, les icônes et les chemins manquants résolus paresseusement. |
| Obtenir les combinaisons par au moins une méthode | **Vérifié pour les trois méthodes.** Méthode 1 : 20 raccourcis sur `regedit.exe`, 15 sur `mmc.exe`, 10 sur `notepad.exe`. Méthode 2 : 3 raccourcis sur Paint, 8 sur Excel en lecture passive (3,5 s). Méthode 3 : 20 raccourcis sur un fichier de bindings Mixbus, 4 sur un `shortcuts.xml` Notepad++. |
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
- **Images de touches.** Les PNG sont écrits à la racine du dossier `.sdProfile` et
  référencés par le champ `Image` de chaque état. La disposition à plat est le choix le
  plus sûr, mais **elle n'a pas pu être confrontée au logiciel Elgato** : si les visuels
  n'apparaissaient pas à l'import, c'est le premier endroit où regarder.
- **Classement des visuels par mots-clés.** La famille d'une commande est devinée d'après
  son libellé. Un libellé inhabituel tombe sur le pictogramme générique — un clavier. Les
  familles se complètent dans `KeyImageRenderer.Categories`, une ligne par famille.
