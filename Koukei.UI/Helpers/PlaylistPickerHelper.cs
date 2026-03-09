using Koukei.Bus.Models;
using Koukei.Bus.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace Koukei.UI.Helpers;

internal static class PlaylistPickerHelper
{
    public static async Task<PlaylistSummary?> ChooseOrCreateAsync(
        XamlRoot xamlRoot,
        IPlaylistBus playlistBus,
        Func<string, string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(playlistBus);
        ArgumentNullException.ThrowIfNull(getString);

        var playlists = await playlistBus.ListAsync();
        var picker = new ComboBox
        {
            Header = getString("PlaylistPicker_ChooseHeader", "Playlist"),
            DisplayMemberPath = nameof(PlaylistSummary.Name),
            ItemsSource = playlists,
            MinWidth = 280,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedIndex = playlists.Count > 0 ? 0 : -1
        };
        var dialog = new ContentDialog
        {
            Title = getString("PlaylistPicker_Title", "Add to playlist"),
            Content = picker,
            PrimaryButtonText = getString("PlaylistPicker_AddButton", "Add"),
            SecondaryButtonText = getString("PlaylistPicker_NewButton", "New playlist"),
            CloseButtonText = getString("PlaylistsPage_Dialog_CancelButton", "Cancel"),
            DefaultButton = playlists.Count > 0
                ? ContentDialogButton.Primary
                : ContentDialogButton.Secondary,
            IsPrimaryButtonEnabled = playlists.Count > 0,
            XamlRoot = xamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            return picker.SelectedItem as PlaylistSummary;
        }

        if (result != ContentDialogResult.Secondary)
        {
            return null;
        }

        var nameBox = new TextBox
        {
            MaxLength = 128,
            PlaceholderText = getString(
                "PlaylistsPage_Dialog_NamePlaceholder",
                "Playlist name")
        };
        var createDialog = new ContentDialog
        {
            Title = getString("PlaylistsPage_Dialog_CreateTitle", "Create playlist"),
            Content = nameBox,
            PrimaryButtonText = getString("PlaylistsPage_Dialog_CreateButton", "Create"),
            CloseButtonText = getString("PlaylistsPage_Dialog_CancelButton", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            XamlRoot = xamlRoot
        };
        nameBox.TextChanged += (_, _) =>
            createDialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(nameBox.Text);
        if (await createDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return await playlistBus.CreateAsync(nameBox.Text);
    }
}
