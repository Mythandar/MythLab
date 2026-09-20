using System.Windows;
using MythLab.Core.Settings;

namespace MythLab.App;

public static class ThemeManager
{
    // WPF still marks runtime theme switching experimental in .NET 10.
    // Isolate the opt-in so a future API change affects only this adapter.
#pragma warning disable WPF0001
    public static void Apply(AppTheme theme) => Application.Current.ThemeMode = theme switch
    {
        AppTheme.Dark => ThemeMode.Dark,
        AppTheme.Light => ThemeMode.Light,
        _ => ThemeMode.System
    };
#pragma warning restore WPF0001
}
