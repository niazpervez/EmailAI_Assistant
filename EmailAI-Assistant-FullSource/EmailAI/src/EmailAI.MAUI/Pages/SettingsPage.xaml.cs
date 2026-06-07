using CommunityToolkit.Mvvm.Input;
using EmailAI.UI.Shared.ViewModels;

namespace EmailAI.MAUI.Pages;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void SaveApiKey_Click(object sender, EventArgs e)
    {
        if (BindingContext is not SettingsViewModel vm) return;

        if (vm.SaveApiKeyCommand is IAsyncRelayCommand asyncCommand)
            await asyncCommand.ExecuteAsync(ApiKeyEntry.Text);
        else
            vm.SaveApiKeyCommand.Execute(ApiKeyEntry.Text);
    }
}
