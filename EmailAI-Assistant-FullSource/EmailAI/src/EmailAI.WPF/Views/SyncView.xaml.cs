using CommunityToolkit.Mvvm.Input;
using EmailAI.WPF.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace EmailAI.WPF.Views;

public partial class SyncView : UserControl
{
    public SyncView() => InitializeComponent();

    private async void ConnectAccount_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SyncViewModel vm) return;
        await vm.ConnectWithPasswordAsync(MailPasswordBox.Password);
        if (vm.IsSignedIn)
            MailPasswordBox.Clear();
    }

    private async void SignInGoogle_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SyncViewModel vm) return;
        await vm.SignInGoogleCommand.ExecuteAsync(null);
    }

    private async void SignInMicrosoft_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SyncViewModel vm) return;
        await vm.SignInMicrosoftCommand.ExecuteAsync(null);
    }
}
