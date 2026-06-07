using EmailAI.UI.Shared.ViewModels;

namespace EmailAI.MAUI.Pages;

public partial class DashboardPage : ContentPage
{
    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
