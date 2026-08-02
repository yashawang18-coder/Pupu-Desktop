using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using Pupu.Desktop.ViewModels;

namespace Pupu.Desktop;

public partial class ControlWindow : Window
{
    private MainViewModel? _observedViewModel;
    public bool AllowClose { get; set; }

    public ControlWindow()
    {
        InitializeComponent();
        DataContextChanged += ControlWindow_DataContextChanged;
        Closed += (_, _) => ObserveViewModel(null);
    }

    private void ControlWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (AllowClose) return;
        e.Cancel = true;
        Hide();
    }

    private void ControlWindow_DataContextChanged(
        object sender,
        DependencyPropertyChangedEventArgs e) =>
        ObserveViewModel(e.NewValue as MainViewModel);

    private void ObserveViewModel(MainViewModel? viewModel)
    {
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _observedViewModel = viewModel;
        if (_observedViewModel is not null)
            _observedViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.ModelApiKey) ||
            sender is not MainViewModel viewModel ||
            !string.IsNullOrEmpty(viewModel.ModelApiKey))
            return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ModelApiKeyBox.Password.Length > 0)
                ModelApiKeyBox.Clear();
        }));
    }

    private void ChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        if (DataContext is MainViewModel viewModel && viewModel.SendChatCommand.CanExecute(null))
            viewModel.SendChatCommand.Execute(null);
        e.Handled = true;
    }

    private void ModelApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box && DataContext is MainViewModel viewModel)
            viewModel.ModelApiKey = box.Password;
    }
}
