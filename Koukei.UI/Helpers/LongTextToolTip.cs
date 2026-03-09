using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;

namespace Koukei.UI.Helpers;

public static class LongTextToolTip
{
    private const double MaximumToolTipWidth = 480;

    public static readonly DependencyProperty FullTextProperty = DependencyProperty.RegisterAttached(
        "FullText",
        typeof(string),
        typeof(LongTextToolTip),
        new PropertyMetadata(null, OnTextChanged));

    public static string? GetFullText(DependencyObject element)
    {
        return (string?)element.GetValue(FullTextProperty);
    }

    public static void SetFullText(DependencyObject element, string? value)
    {
        element.SetValue(FullTextProperty, value);
    }

    public static void SetText(DependencyObject element, string? value)
    {
        SetFullText(element, value);
    }

    public static string CreateMediaText(string? displayText, string? filePath)
    {
        var title = displayText?.Trim() ?? string.Empty;
        var fileName = string.IsNullOrWhiteSpace(filePath)
            ? string.Empty
            : Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return title;
        }

        if (string.IsNullOrWhiteSpace(title) ||
            string.Equals(title, fileName, StringComparison.CurrentCultureIgnoreCase) ||
            string.Equals(title, Path.GetFileNameWithoutExtension(fileName), StringComparison.CurrentCultureIgnoreCase))
        {
            return fileName;
        }

        return $"{title}{Environment.NewLine}{fileName}";
    }

    private static void OnTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not FrameworkElement element)
        {
            return;
        }

        var text = args.NewValue as string;
        if (string.IsNullOrWhiteSpace(text))
        {
            ToolTipService.SetToolTip(element, null);
            return;
        }

        ToolTipService.SetToolTip(
            element,
            new ToolTip
            {
                MaxWidth = MaximumToolTipWidth,
                Content = new TextBlock
                {
                    MaxWidth = MaximumToolTipWidth,
                    Text = text,
                    TextWrapping = TextWrapping.Wrap
                }
            });
    }
}
