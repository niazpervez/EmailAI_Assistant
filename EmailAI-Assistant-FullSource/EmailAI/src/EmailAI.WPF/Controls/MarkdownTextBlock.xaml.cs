using EmailAI.WPF.Helpers;
using System.Windows;
using System.Windows.Controls;

namespace EmailAI.WPF.Controls;

public partial class MarkdownTextBlock : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(MarkdownTextBlock),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public MarkdownTextBlock() => InitializeComponent();

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownTextBlock block)
            ChatMarkdownRenderer.Render(block.ContentPanel, e.NewValue as string ?? string.Empty);
    }
}
