# Format `.streamDeckProfile`

Ce document décrit l'archive telle que `StreamDeckProfileWriter` la produit.

## Arborescence

Un fichier `.streamDeckProfile` est une archive Zip :

```
MonProfil.streamDeckProfile
└── 7C3F9B2E-....-....-....-............sdProfile/
    ├── manifest.json
    ├── key_0_0.png              (optionnel)
    ├── key_1_0.png
    └── ...
```

Les visuels sont posés à plat, à la racine du dossier `.sdProfile`, et référencés par le
champ `Image` de chaque état. Un sous-dossier n'étant pas garanti d'être résolu par
l'application Elgato, c'est la disposition la plus sûre.

Le dossier racine porte un GUID en majuscules suivi de `.sdProfile`. Un GUID neuf est tiré
à chaque export, afin que deux exports successifs n'écrasent pas le même profil à l'import.

## `manifest.json`

```json
{
  "Name": "Visual Studio Code",
  "Device": {
    "UUID": "",
    "Model": "20GAT"
  },
  "Version": "1.0",
  "Actions": {
    "0,0": {
      "Name": "Hotkey",
      "Controller": "Keypad",
      "Settings": {
        "Hotkey": [
          {
            "KeyCmd": false,
            "KeyCtrl": true,
            "KeyModifiers": 3,
            "KeyOption": false,
            "KeyShift": true,
            "NativeCode": 83,
            "QTKeyCode": 83,
            "VKeyCode": 83
          }
        ],
        "IsMultiAction": false
      },
      "State": 0,
      "States": [
        {
          "FFamily": "",
          "FSize": "10",
          "FStyle": "",
          "FUnderline": "off",
          "Title": "Enregistrer\nsous",
          "TitleAlignment": "middle",
          "TitleColor": "#ffffff",
          "TitleShow": ""
        }
      ],
      "UUID": "com.elgato.streamdeck.system.hotkey"
    }
  }
}
```

### Champs

| Champ | Rôle |
| --- | --- |
| `Name` | Nom du profil affiché dans le logiciel Elgato. |
| `Device.Model` | Code produit du boîtier ciblé. Voir `StreamDeckDevices`. |
| `Device.UUID` | Numéro de série. Vide : le profil accepte tout boîtier du modèle. |
| `Actions` | Dictionnaire indexé `"colonne,ligne"`, origine en haut à gauche. |
| `Actions[].UUID` | `com.elgato.streamdeck.system.hotkey`, l'action « raccourci clavier » intégrée. |
| `Actions[].Controller` | `Keypad` pour une touche carrée. |
| `Settings.Hotkey` | Tableau d'une combinaison. |

### Combinaison de touches

Les quatre booléens et le masque décrivent les mêmes modificateurs ; les deux sont écrits
pour rester cohérent avec ce que produit le logiciel Elgato.

| Modificateur | Booléen | Bit du masque |
| --- | --- | --- |
| Maj | `KeyShift` | 1 |
| Ctrl | `KeyCtrl` | 2 |
| Alt | `KeyOption` (nom hérité de macOS) | 4 |
| Windows | `KeyCmd` | 8 |

Les trois codes de touche viennent de `KeyCatalog` :

- `VKeyCode` — code de touche virtuel Windows (`VK_S` = 0x53). C'est celui que l'application
  Stream Deck rejoue sous Windows.
- `QTKeyCode` — énumération `Qt::Key` du socle graphique de l'application Stream Deck.
  Identique au code ASCII pour les lettres et les chiffres, mais `Qt::Key_F1` vaut
  `0x01000030` là où `VK_F1` vaut `0x70`.
- `NativeCode` — code natif de la plateforme ; aligné sur `VKeyCode` sous Windows.

### Titres

`Title` accepte `\n` comme retour à la ligne. `StreamDeckProfileBuilder.WrapTitle` coupe à
neuf caractères par ligne sur trois lignes au maximum, ce qui correspond à ce qu'une touche
affiche à la taille de police par défaut.

## Contrôle après écriture

`StreamDeckProfileWriter.Verify` relit l'archive produite et vérifie :

1. la présence d'un `manifest.json` ;
2. un dossier racine en `.sdProfile` ;
3. un manifeste désérialisable ;
4. au moins une action ;
5. pour chaque action, une combinaison présente et un `VKeyCode` non nul.

L'export de l'interface comme celui de la ligne de commande appellent ce contrôle et
affichent son résultat, afin de ne jamais livrer une archive que le logiciel Elgato
refuserait d'ouvrir.
