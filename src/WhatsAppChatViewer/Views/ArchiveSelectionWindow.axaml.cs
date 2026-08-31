using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using WhatsAppChatViewer.ViewModels;

namespace WhatsAppChatViewer.Views;

public sealed partial class ArchiveSelectionWindow : Window
{
    private readonly TextBlock _selectionSummaryText;
    private readonly Button _openSelectedButton;

    public ArchiveSelectionWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _selectionSummaryText = this.FindControl<TextBlock>("SelectionSummaryText")
            ?? throw new InvalidOperationException("SelectionSummaryText was not created.");
        _openSelectedButton = this.FindControl<Button>("OpenSelectedButton")
            ?? throw new InvalidOperationException("OpenSelectedButton was not created.");
        Choices = new ObservableCollection<ArchiveChoiceViewModel>();
        DataContext = this;
    }

    public ArchiveSelectionWindow(IEnumerable<string> archivePaths)
        : this()
    {
        foreach (var path in archivePaths)
        {
            var choice = new ArchiveChoiceViewModel(path);
            choice.PropertyChanged += Choice_PropertyChanged;
            Choices.Add(choice);
        }

        UpdateSelectionState();
    }

    public ObservableCollection<ArchiveChoiceViewModel> Choices { get; }

    private void Choice_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ArchiveChoiceViewModel.IsSelected))
        {
            UpdateSelectionState();
        }
    }

    private void SelectAll_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var choice in Choices)
        {
            choice.IsSelected = true;
        }
    }

    private void Clear_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var choice in Choices)
        {
            choice.IsSelected = false;
        }
    }

    private void OpenSelected_Click(object? sender, RoutedEventArgs e)
    {
        var selected = Choices
            .Where(static choice => choice.IsSelected)
            .Select(static choice => choice.Path)
            .ToArray();
        if (selected.Length > 0)
        {
            Close(selected);
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) =>
        Close(null);

    private void UpdateSelectionState()
    {
        var selectedCount = Choices.Count(static choice => choice.IsSelected);
        _selectionSummaryText.Text = $"{selectedCount} of {Choices.Count} selected";
        _openSelectedButton.IsEnabled = selectedCount > 0;
        _openSelectedButton.Content = selectedCount == 1
            ? "Open 1 chat"
            : $"Open {selectedCount} chats";
    }
}
