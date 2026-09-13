using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using StreamDeckForge.App.Infrastructure;
using StreamDeckForge.Core.Discovery;
using StreamDeckForge.Core.Extraction;
using StreamDeckForge.Core.Models;
using StreamDeckForge.Core.Rendering;
using StreamDeckForge.Core.StreamDeck;

namespace StreamDeckForge.App.ViewModels;

/// <summary>Etat et actions de la fenetre principale.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly ApplicationCatalog _catalog = new();
    private readonly ShortcutExtractionService _extraction = new();
    private readonly List<AppRowViewModel> _allApplications = [];

    private string _searchText = string.Empty;
    private AppRowViewModel? _selectedApplication;
    private ShortcutRowViewModel? _selectedShortcut;
    private StreamDeckDevice _device = StreamDeckDevices.Standard;
    private string _profileName = "Nouveau profil";
    private string _status = "Pret.";
    private string _extractionReport = string.Empty;
    private bool _isBusy;
    private bool _useAcceleratorTables = true;
    private bool _useUiAutomation = true;
    private bool _useConfigFiles = true;
    private bool _scanCompanionModules;
    private bool _expandMenus = true;
    private bool _generateIcons = true;
    private ProfileLayout _layout;

    public MainViewModel()
    {
        _layout = new ProfileLayout(_device, _profileName);

        ScanCommand = new RelayCommand(async () => await ScanAsync(), () => !IsBusy);
        AddExecutableCommand = new RelayCommand(AddExecutable, () => !IsBusy);
        ExtractCommand = new RelayCommand(
            async () => await ExtractAsync(),
            () => !IsBusy && SelectedApplication is not null);
        AutoFillCommand = new RelayCommand(AutoFill, () => Shortcuts.Count > 0);
        ClearGridCommand = new RelayCommand(ClearGrid, () => Cells.Any(c => c.IsAssigned));
        AssignCommand = new RelayCommand(parameter => Assign(parameter as KeyCellViewModel));
        ExportCommand = new RelayCommand(Export, () => Cells.Any(c => c.IsAssigned));

        BuildCells();
    }

    public ObservableCollection<AppRowViewModel> Applications { get; } = [];

    public ObservableCollection<ShortcutRowViewModel> Shortcuts { get; } = [];

    public ObservableCollection<KeyCellViewModel> Cells { get; } = [];

    public IReadOnlyList<StreamDeckDevice> Devices => StreamDeckDevices.All;

    public ICommand ScanCommand { get; }

    public ICommand AddExecutableCommand { get; }

    public ICommand ExtractCommand { get; }

    public ICommand AutoFillCommand { get; }

    public ICommand ClearGridCommand { get; }

    public ICommand AssignCommand { get; }

    public ICommand ExportCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyFilter();
        }
    }

    public AppRowViewModel? SelectedApplication
    {
        get => _selectedApplication;
        set
        {
            if (!SetProperty(ref _selectedApplication, value) || value is null)
                return;

            // Le chemin d'une entree registre peut n'etre connu qu'a la selection.
            ApplicationCatalog.ResolveExecutable(value.Application);
            ProfileName = value.Name;
            Status = value.Application.HasExecutable
                ? $"{value.Name} : {value.Application.ExecutablePath}"
                : $"{value.Name} : executable introuvable, les methodes statiques seront indisponibles.";
        }
    }

    public ShortcutRowViewModel? SelectedShortcut
    {
        get => _selectedShortcut;
        set => SetProperty(ref _selectedShortcut, value);
    }

    public StreamDeckDevice Device
    {
        get => _device;
        set
        {
            if (!SetProperty(ref _device, value))
                return;

            _layout = new ProfileLayout(value, ProfileName);
            BuildCells();
            OnPropertyChanged(nameof(CapacityLabel));
        }
    }

    public string ProfileName
    {
        get => _profileName;
        set
        {
            if (SetProperty(ref _profileName, value))
                _layout.ProfileName = value;
        }
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string ExtractionReport
    {
        get => _extractionReport;
        set => SetProperty(ref _extractionReport, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public bool UseAcceleratorTables
    {
        get => _useAcceleratorTables;
        set => SetProperty(ref _useAcceleratorTables, value);
    }

    public bool UseUiAutomation
    {
        get => _useUiAutomation;
        set => SetProperty(ref _useUiAutomation, value);
    }

    public bool UseConfigFiles
    {
        get => _useConfigFiles;
        set => SetProperty(ref _useConfigFiles, value);
    }

    public bool ScanCompanionModules
    {
        get => _scanCompanionModules;
        set => SetProperty(ref _scanCompanionModules, value);
    }

    public bool ExpandMenus
    {
        get => _expandMenus;
        set => SetProperty(ref _expandMenus, value);
    }

    /// <summary>
    /// Fabrique un visuel par touche : pictogramme et couleur deduits de la commande.
    /// Sans cela, le boitier affiche le titre sur l'icone par defaut d'Elgato.
    /// </summary>
    public bool GenerateIcons
    {
        get => _generateIcons;
        set => SetProperty(ref _generateIcons, value);
    }

    public string CapacityLabel =>
        $"{Cells.Count(c => c.IsAssigned)} / {Device.KeyCount} touches";

    public async Task ScanAsync()
    {
        IsBusy = true;
        Status = "Scan des applications installees...";

        try
        {
            var result = await _catalog.ScanAsync().ConfigureAwait(true);

            _allApplications.Clear();
            _allApplications.AddRange(result.Applications.Select(app => new AppRowViewModel(app)));
            ApplyFilter();

            Status =
                $"{result.Applications.Count} applications detectees en " +
                $"{result.Duration.TotalMilliseconds:F0} ms.";
        }
        catch (Exception ex)
        {
            Status = $"Echec du scan : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        var query = SearchText;

        var filtered = _allApplications
            .Where(row => string.IsNullOrWhiteSpace(query) ||
                          row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                          row.PathLabel.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(400)
            .ToList();

        Applications.Clear();
        foreach (var row in filtered)
            Applications.Add(row);
    }

    private void AddExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choisir un executable",
            Filter = "Applications (*.exe)|*.exe|Tous les fichiers (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
            return;

        var row = new AppRowViewModel(ApplicationCatalog.FromExecutable(dialog.FileName));
        _allApplications.Insert(0, row);
        Applications.Insert(0, row);
        SelectedApplication = row;
    }

    public async Task ExtractAsync()
    {
        if (SelectedApplication is null)
            return;

        var methods = new List<ExtractionMethod>();
        if (UseAcceleratorTables) methods.Add(ExtractionMethod.AcceleratorTable);
        if (UseUiAutomation) methods.Add(ExtractionMethod.UiAutomation);
        if (UseConfigFiles) methods.Add(ExtractionMethod.ConfigFile);

        if (methods.Count == 0)
        {
            Status = "Selectionnez au moins une methode d'extraction.";
            return;
        }

        IsBusy = true;
        Status = "Extraction en cours...";
        ExtractionReport = string.Empty;

        try
        {
            var context = new ExtractionContext
            {
                Application = SelectedApplication.Application,
                ScanCompanionModules = ScanCompanionModules,
                ExpandMenus = ExpandMenus
            };

            var aggregated = await _extraction.ExtractAsync(context, methods).ConfigureAwait(true);

            Shortcuts.Clear();
            foreach (var shortcut in aggregated.Shortcuts)
                Shortcuts.Add(new ShortcutRowViewModel(shortcut));

            ExtractionReport = string.Join(
                Environment.NewLine,
                aggregated.Results.Select(r => $"{(r.Succeeded ? "OK" : "--")}  {Label(r.Method)} : {r.Message}"));

            Status = aggregated.Shortcuts.Count > 0
                ? $"{aggregated.Shortcuts.Count} raccourci(s) extraits."
                : "Aucun raccourci trouve par les methodes selectionnees.";
        }
        catch (Exception ex)
        {
            Status = $"Echec de l'extraction : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Label(ExtractionMethod method) => method switch
    {
        ExtractionMethod.AcceleratorTable => "Tables d'accelerateurs",
        ExtractionMethod.UiAutomation => "UI Automation",
        ExtractionMethod.ConfigFile => "Fichiers de configuration",
        _ => method.ToString()
    };

    private void BuildCells()
    {
        Cells.Clear();

        for (var row = 0; row < Device.Rows; row++)
        for (var column = 0; column < Device.Columns; column++)
            Cells.Add(new KeyCellViewModel(column, row));

        OnPropertyChanged(nameof(CapacityLabel));
    }

    private void AutoFill()
    {
        _layout.ClearAll();

        var selected = Shortcuts
            .Where(row => row.IsSelected)
            .ToList();

        foreach (var row in selected)
        {
            var free = FirstFreeCell();
            if (free is null)
                break;

            _layout.Assign(free.Column, free.Row, row.Definition, row.Title);
            free.Title = row.Title;
            free.Keys = row.Keys;
        }

        var placed = Cells.Count(c => c.IsAssigned);
        var overflow = selected.Count - placed;

        Status = overflow > 0
            ? $"{placed} touches remplies ; {overflow} raccourci(s) sans place sur ce modele."
            : $"{placed} touches remplies.";

        OnPropertyChanged(nameof(CapacityLabel));
    }

    private KeyCellViewModel? FirstFreeCell() => Cells.FirstOrDefault(c => !c.IsAssigned);

    private void ClearGrid()
    {
        _layout.ClearAll();

        foreach (var cell in Cells)
            cell.Clear();

        OnPropertyChanged(nameof(CapacityLabel));
        Status = "Grille videe.";
    }

    /// <summary>
    /// Clic sur une touche : y place le raccourci selectionne, ou la libere si aucun
    /// raccourci n'est selectionne dans la liste.
    /// </summary>
    private void Assign(KeyCellViewModel? cell)
    {
        if (cell is null)
            return;

        if (SelectedShortcut is null)
        {
            _layout.Clear(cell.Column, cell.Row);
            cell.Clear();
            OnPropertyChanged(nameof(CapacityLabel));
            return;
        }

        _layout.Assign(cell.Column, cell.Row, SelectedShortcut.Definition, SelectedShortcut.Title);
        cell.Title = SelectedShortcut.Title;
        cell.Keys = SelectedShortcut.Keys;

        OnPropertyChanged(nameof(CapacityLabel));
        Status = $"{SelectedShortcut.Keys} place en {cell.Position}.";
    }

    private void Export()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Exporter le profil Stream Deck",
            FileName = StreamDeckProfileWriter.SuggestFileName(ProfileName),
            Filter = "Profil Stream Deck (*.streamDeckProfile)|*.streamDeckProfile",
            DefaultExt = StreamDeckProfileWriter.FileExtension,
            AddExtension = true
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            _layout.ProfileName = ProfileName;

            if (GenerateIcons)
                KeyImageRenderer.Apply(_layout);

            var result = StreamDeckProfileWriter.Write(_layout, dialog.FileName);
            var (valid, message) = StreamDeckProfileWriter.Verify(result.FilePath);

            Status = valid
                ? $"Exporte : {Path.GetFileName(result.FilePath)} - {message}"
                : $"Fichier ecrit mais invalide : {message}";
        }
        catch (Exception ex)
        {
            Status = $"Echec de l'exportation : {ex.Message}";
        }
    }
}
