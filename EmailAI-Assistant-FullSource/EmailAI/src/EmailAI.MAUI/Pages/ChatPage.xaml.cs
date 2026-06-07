using EmailAI.UI.Shared.ViewModels;

namespace EmailAI.MAUI.Pages;

public partial class ChatPage : ContentPage
{
    public ChatPage(ChatViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
