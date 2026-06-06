using CommunityToolkit.Mvvm.Input;
using EmailAI.WPF.ViewModels;
using System.Windows.Controls;

namespace EmailAI.WPF.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void SaveApiKey_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        try
        {
            if (vm.SaveApiKeyCommand is IAsyncRelayCommand asyncCommand)
                await asyncCommand.ExecuteAsync(ApiKeyBox.Password);
            else
                vm.SaveApiKeyCommand.Execute(ApiKeyBox.Password);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Could not save API key:\n\n{ex.Message}",
                "Save Failed",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }
}
