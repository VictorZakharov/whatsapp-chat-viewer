using WhatsAppChatViewer.Models;

namespace WhatsAppChatViewer.ViewModels;

public sealed class ArchiveChoiceViewModel : ObservableObject
{
    private bool _isSelected = true;

    public ArchiveChoiceViewModel(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        FileName = System.IO.Path.GetFileName(path);
        Folder = System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
        SizeText = File.Exists(Path)
            ? FileSizeFormatter.Format(new FileInfo(Path).Length)
            : "Unavailable";
    }

    public string Path { get; }
    public string FileName { get; }
    public string Folder { get; }
    public string SizeText { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
