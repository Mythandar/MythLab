using System.ComponentModel;
using System.Windows;
using MythLab.App.ViewModels;

namespace MythLab.App.Views;

public partial class DeviceEditorWindow : Window
{
    private readonly DeviceEditorViewModel model;
    private bool saved;
    public DeviceEditorWindow(DeviceEditorViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        model.Saved += OnSaved;
        Loaded += async (_, _) => { NameInput.Focus(); await model.LoadInterfacesAsync(); };
        Closing += OnClosing;
        Closed += (_, _) => model.Saved -= OnSaved;
    }
    private void OnSaved(object? sender, EventArgs e) { saved = true; DialogResult = true; }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (model.IsBusy && !saved) e.Cancel = true;
    }
}
