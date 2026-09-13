using System.Windows.Media.Imaging;
using StreamDeckForge.Core.Discovery;
using StreamDeckForge.Core.Models;

namespace StreamDeckForge.App.ViewModels;

/// <summary>Une application dans la liste de gauche, avec son icone.</summary>
public sealed class AppRowViewModel
{
    private BitmapSource? _icon;
    private bool _iconLoaded;

    public AppRowViewModel(AppInfo application) => Application = application;

    public AppInfo Application { get; }

    public string Name => Application.Name;

    public string PathLabel => Application.ExecutablePath ?? "Chemin a resoudre";

    public string SourceLabel => Application.Source switch
    {
        AppSource.Registry => "Registre",
        AppSource.StartMenu => "Menu Demarrer",
        AppSource.Manual => "Ajout manuel",
        _ => string.Empty
    };

    /// <summary>Icone chargee a la demande : le scan complet n'en extrait aucune.</summary>
    public BitmapSource? Icon
    {
        get
        {
            if (_iconLoaded)
                return _icon;

            _iconLoaded = true;
            _icon = IconLoader.Load(Application.IconPath ?? Application.ExecutablePath);
            return _icon;
        }
    }
}
