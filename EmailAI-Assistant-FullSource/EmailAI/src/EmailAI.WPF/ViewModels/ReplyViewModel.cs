using CommunityToolkit.Mvvm.ComponentModel;

namespace EmailAI.WPF.ViewModels;

// Stub for future reply modal
public partial class ReplyViewModel : ObservableObject
{
    [ObservableProperty] private string _replyText = "";
    [ObservableProperty] private string _subject = "";
    [ObservableProperty] private string _to = "";
}
