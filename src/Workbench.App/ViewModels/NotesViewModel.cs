using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Workbench.Core.Models;

namespace Workbench.App.ViewModels;

/// <summary>Free-form markdown notes: list on the left, editor on the right.</summary>
public sealed partial class NotesViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Project _project;
    private List<Note> _allNotes = [];

    public ObservableCollection<Note> Notes { get; } = [];

    [ObservableProperty]
    private Note? _selectedNote;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private string _editorTitle = string.Empty;

    [ObservableProperty]
    private string _editorContent = string.Empty;

    [ObservableProperty]
    private bool _isEditorEnabled;

    public NotesViewModel(AppServices services, Project project)
    {
        _services = services;
        _project = project;
        Reload(selectFirst: true);
    }

    private void Reload(bool selectFirst = false, long? selectId = null)
    {
        _allNotes = _services.Notes.GetForProject(_project.Id).ToList();
        ApplyFilter();

        if (selectId is { } id)
            SelectedNote = Notes.FirstOrDefault(n => n.Id == id);
        else if (selectFirst)
            SelectedNote = Notes.FirstOrDefault();
    }

    private void ApplyFilter()
    {
        var filter = FilterText.Trim();
        Notes.Clear();
        foreach (var note in _allNotes)
        {
            if (filter.Length == 0 ||
                note.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                note.Content.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                Notes.Add(note);
            }
        }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnSelectedNoteChanged(Note? value)
    {
        IsEditorEnabled = value is not null;
        EditorTitle = value?.Title ?? string.Empty;
        EditorContent = value?.Content ?? string.Empty;
    }

    [RelayCommand]
    private void NewNote()
    {
        var note = new Note
        {
            ProjectId = _project.Id,
            Title = "Untitled note",
            Content = string.Empty,
        };
        _services.Notes.Add(note);
        Reload(selectId: note.Id);
    }

    [RelayCommand]
    private void SaveNote()
    {
        if (SelectedNote is not { } note)
            return;

        note.Title = string.IsNullOrWhiteSpace(EditorTitle) ? "Untitled note" : EditorTitle.Trim();
        note.Content = EditorContent;
        _services.Notes.Update(note);
        Reload(selectId: note.Id);
    }

    [RelayCommand]
    private void DeleteNote()
    {
        if (SelectedNote is not { } note)
            return;

        _services.Notes.Delete(note.Id);
        Reload(selectFirst: true);
    }
}
