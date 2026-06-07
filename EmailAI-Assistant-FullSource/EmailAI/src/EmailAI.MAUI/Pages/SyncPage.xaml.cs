using EmailAI.UI.Shared.ViewModels;

namespace EmailAI.MAUI.Pages;

public partial class SyncPage : ContentPage
{
    public SyncPage(SyncViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void ConnectAccount_Click(object sender, EventArgs e)
    {
        if (BindingContext is SyncViewModel vm)
            await vm.ConnectWithPasswordAsync(PasswordEntry.Text);
    }
}
