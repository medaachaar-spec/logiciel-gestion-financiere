using System.Collections.ObjectModel;
using System.Windows.Input;
using FinancialManagement.Application.Authentication;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class ShellViewModel : ViewModelBase
{
    private PlaceholderPageViewModel? _currentPage;
    private string _currentPageTitle = string.Empty;
    private readonly ICurrentUserSession _currentUserSession;

    public ShellViewModel(ICurrentUserSession currentUserSession)
    {
        ArgumentNullException.ThrowIfNull(currentUserSession);
        _currentUserSession = currentUserSession;

        NavigateCommand = new RelayCommand(
            parameter =>
            {
                if (parameter is NavigationItemViewModel item && item.IsVisible)
                {
                    NavigateTo(item);
                }
            },
            parameter => parameter is NavigationItemViewModel { IsVisible: true });

        AddNavigationItem("Tableau de bord", "\uE80F", "dashboard", "La vue de synthèse sera intégrée dans une prochaine étape.");
        AddNavigationItem("Ventes", "\uE719", "sales", "Le contenu métier des ventes sera intégré dans une prochaine étape.");
        AddNavigationItem("Achats", "\uE7BF", "purchases", "Le contenu métier des achats sera intégré dans une prochaine étape.");
        AddNavigationItem("Dépenses", "\uE7C1", "expenses", "Le contenu métier des dépenses sera intégré dans une prochaine étape.");
        AddNavigationItem("Clients", "\uE77B", "customers", "Le contenu métier des clients sera intégré dans une prochaine étape.");
        AddNavigationItem("Fournisseurs", "\uE716", "suppliers", "Le contenu métier des fournisseurs sera intégré dans une prochaine étape.");
        AddNavigationItem("Encaissements clients", "\uE8C7", "customer-payments", "Le contenu métier des encaissements clients sera intégré dans une prochaine étape.");
        AddNavigationItem("Paiements fournisseurs", "\uE8C7", "supplier-payments", "Le contenu métier des paiements fournisseurs sera intégré dans une prochaine étape.");
        AddNavigationItem("Caisse & Banque", "\uE8A1", "treasury", "Le contenu métier de la caisse et de la banque sera intégré dans une prochaine étape.");
        AddNavigationItem("Administration", "\uE713", "administration", "Le contenu métier de l'administration sera intégré dans une prochaine étape.");

        NavigateTo(NavigationItems[0]);
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; } = [];

    public ICommand NavigateCommand { get; }

    public string AuthenticatedUsername => _currentUserSession.Username ?? string.Empty;

    public PlaceholderPageViewModel? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public string CurrentPageTitle
    {
        get => _currentPageTitle;
        private set => SetProperty(ref _currentPageTitle, value);
    }

    private void AddNavigationItem(string title, string iconGlyph, string pageKey, string description)
    {
        var page = new PlaceholderPageViewModel(title, description);
        NavigationItems.Add(new NavigationItemViewModel(title, iconGlyph, pageKey, page, NavigateCommand));
    }

    private void NavigateTo(NavigationItemViewModel item)
    {
        foreach (var navigationItem in NavigationItems)
        {
            navigationItem.IsSelected = ReferenceEquals(navigationItem, item);
        }

        CurrentPage = item.Page;
        CurrentPageTitle = item.Title;
    }
}
