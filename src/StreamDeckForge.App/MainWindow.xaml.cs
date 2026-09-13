using System.Windows;
using StreamDeckForge.App.ViewModels;

namespace StreamDeckForge.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        // Le scan demarre des l'ouverture : le cahier des charges demande la liste
        // des logiciels en moins de trois secondes apres le lancement.
        Loaded += async (_, _) => await _viewModel.ScanAsync();
    }
}
