using EmailAI.WPF.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace EmailAI.WPF.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
