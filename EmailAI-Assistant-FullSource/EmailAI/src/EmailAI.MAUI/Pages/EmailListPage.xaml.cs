using EmailAI.UI.Shared.ViewModels;

namespace EmailAI.MAUI.Pages;

public partial class EmailListPage : ContentPage
{
    private readonly MainViewModel _main;

    public EmailListPage(EmailListViewModel viewModel, MainViewModel main)
    {
        InitializeComponent();
        _main = main;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is not EmailListViewModel vm) return;

        if (_main.CurrentPage == "Search")
            vm.SetSearchMode();
        else
            _ = vm.InitializeMailViewAsync();

        if (!string.IsNullOrEmpty(_main.PendingEmailId))
        {
            var emailId = _main.PendingEmailId;
            _main.ClearPendingEmail();
            _ = vm.SelectEmailByIdAsync(emailId);
        }
    }
}
