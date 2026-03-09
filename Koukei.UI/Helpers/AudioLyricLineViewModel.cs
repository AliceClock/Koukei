using Koukei.Audio;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FontWeight = Windows.UI.Text.FontWeight;

namespace Koukei.UI.Helpers;

public sealed class AudioLyricLineViewModel(AudioLyricLine line) : INotifyPropertyChanged
{
    private bool _isActive;

    public event PropertyChangedEventHandler? PropertyChanged;

    public TimeSpan? Timestamp { get; } = line.Timestamp;

    public string Text { get; } = line.Text;

    public string TimestampLabel => Timestamp is { } timestamp
        ? timestamp.TotalHours >= 1
            ? $"{(int)timestamp.TotalHours}:{timestamp.Minutes:00}:{timestamp.Seconds:00}"
            : $"{(int)timestamp.TotalMinutes}:{timestamp.Seconds:00}"
        : string.Empty;

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayOpacity));
            OnPropertyChanged(nameof(DisplayFontWeight));
            OnPropertyChanged(nameof(ActiveIndicatorVisibility));
        }
    }

    public double DisplayOpacity => IsActive ? 1 : 0.52;

    public FontWeight DisplayFontWeight => IsActive ? FontWeights.SemiBold : FontWeights.Normal;

    public Visibility ActiveIndicatorVisibility => IsActive
        ? Visibility.Visible
        : Visibility.Collapsed;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
