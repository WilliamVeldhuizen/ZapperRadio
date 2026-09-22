using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;
using ZapperRadio.Core.Models;
using ZapperRadio.Core.Settings;
using ZapperRadio.Shell;
using ZapperRadio.ViewModels;

namespace ZapperRadio;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        ViewModel = new MainViewModel(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragArea);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ZapperRadio.ico"));

        AppTitleBar.SizeChanged += (_, _) => KeepViewButtonClearOfCaptionButtons();
        KeepViewButtonClearOfCaptionButtons();

        PlaceWindow(ViewModel.IsCompact);
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidPositionChange || args.DidSizeChange)
            {
                RememberWindow(ViewModel.IsCompact);
            }
        };

        // The compact window is fitted around its favorites, which are still being loaded when it opens.
        Root.Loaded += (_, _) => FitCompactWindow();
        ViewModel.Favorites.CollectionChanged += (_, _) => FitCompactWindow();

        // What sits above the list grows after a fit too: a song line or a notice appearing takes its height
        // from the list, which then scrolls by a few pixels.
        Notices.SizeChanged += (_, e) => RefitWhenTaller(e);
        CompactNowPlaying.SizeChanged += (_, e) => RefitWhenTaller(e);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _hotkeys = new GlobalHotkeys(hwnd, DispatcherQueue);
        ApplyGlobalHotkeys();
        _systemMediaControls = SystemMediaControls.TryCreate(hwnd, DispatcherQueue, ViewModel);

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsCompact))
            {
                // IsCompact has already flipped, so the view being left is the other one.
                RememberWindow(!ViewModel.IsCompact);
                PlaceWindow(ViewModel.IsCompact);
                ViewModel.Save();
            }
            else if (e.PropertyName == nameof(MainViewModel.GlobalHotkeys))
            {
                ApplyGlobalHotkeys();
            }
        };

        AddKeyboardShortcuts();

        // Closing saves everything; the view model then has the updater install the update and start the app again.
        ViewModel.RestartRequested += (_, _) => Close();

        Closed += (_, _) =>
        {
            _hotkeys.Dispose();
            _systemMediaControls?.Dispose();
            ViewModel.Dispose();
        };

        _ = ViewModel.LoadCatalogAsync();
    }

    /// <summary>The size each view gets the first time it is used; after that, the size it was left at.</summary>
    private const int FullWidth = 1100;
    private const int FullHeight = 720;
    private const int CompactWidth = 340;
    private const int CompactHeight = 520;

    /// <summary>The shortest the compact window is ever fitted to, so a fit that goes wrong cannot make it useless.</summary>
    private const int CompactMinHeight = 200;

    /// <summary>How tall one favorite is in the compact window, used until there is a row to ask.</summary>
    private const double FavoriteRowHeight = 44;

    /// <summary>
    /// A little room under the last favorite. Rows are laid out on whole pixels at scales like 125% and 150%,
    /// so the list can come out a pixel taller than the sum says, and one pixel is all a scrollbar needs.
    /// </summary>
    private const double CompactSlack = 3;

    /// <summary>
    /// True while a view is being put in place. The steps that takes - restoring a maximized window, moving it,
    /// resizing it - each report a window that moved, and none of them is the user leaving it somewhere.
    /// </summary>
    private bool _placing;

    /// <summary>The text field inside the country box while it has focus.</summary>
    private TextBox? _countryText;

    /// <summary>The shortcuts that also work while another app has focus.</summary>
    private readonly GlobalHotkeys _hotkeys;

    /// <summary>The Windows media card, or null when Windows would not hand it out.</summary>
    private readonly SystemMediaControls? _systemMediaControls;

    public MainViewModel ViewModel { get; }

    public void BringToFront()
    {
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    /// <summary>
    /// Puts the window back where the view being shown was last left, maximized if that is how the full window
    /// was left. The first time a view is used it gets its default size, where the window already is.
    /// </summary>
    private void PlaceWindow(bool compact)
    {
        _placing = true;

        // A maximized window cannot be moved or resized, so the view being left is restored first. Windows takes
        // its own time over a restore, so the placement waits for the next turn of the message loop rather than
        // being undone by one that arrives after it.
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized } maximized)
        {
            maximized.Restore();
            if (DispatcherQueue.TryEnqueue(() => ApplyPlacement(ViewModel.IsCompact)))
            {
                return;
            }
        }

        ApplyPlacement(compact);
    }

    /// <summary>
    /// Sizes and positions the window for the view it now shows. The compact window is never maximized: it is a
    /// list of favorites and nothing else, and a screen full of that is the full window with the middle left out,
    /// so it is fitted to its favorites instead and its maximize button is turned off while it is up.
    /// </summary>
    private void ApplyPlacement(bool compact)
    {
        try
        {
            var saved = ViewModel.WindowPlacement(compact);
            var presenter = AppWindow.Presenter as OverlappedPresenter;
            if (presenter is not null)
            {
                presenter.IsMaximizable = !compact;
            }

            if (saved is not null)
            {
                MoveOnScreen(new PointInt32(saved.X, saved.Y), new SizeInt32(saved.Width, saved.Height));
            }
            else
            {
                var scale = Scale;
                MoveOnScreen(AppWindow.Position, new SizeInt32(
                    (int)((compact ? CompactWidth : FullWidth) * scale),
                    (int)((compact ? CompactHeight : FullHeight) * scale)));
            }

            if (!compact && saved is { IsMaximized: true })
            {
                presenter?.Maximize();
            }
        }
        finally
        {
            _placing = false;
        }

        if (compact)
        {
            FitCompactWindow();
        }
    }

    /// <summary>
    /// Fits the compact window around its favorites, so that every one of them is visible. The list is the only
    /// thing in that window that varies, so there is a right height for it: empty space under the last favorite
    /// and a scrollbar hiding the last few are both wrong. The width and the corner it sits in stay the ones it
    /// was left at, and a list taller than the screen is cut off by the screen rather than by the window.
    /// </summary>
    private void FitCompactWindow()
    {
        if (!ViewModel.IsCompact || AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            return;
        }

        // Nothing has been laid out yet when the app starts in the compact window; Loaded fits it then.
        if (Root.ActualHeight <= 0)
        {
            return;
        }

        Root.UpdateLayout();

        // The favorites are counted rather than measured. A ListView only lays out the rows that fit inside it,
        // so asking one how tall it would like to be answers with the height it already has, which is the very
        // thing being corrected here. One row is measured and the rest is arithmetic.
        var row = CompactFavorites.ContainerFromIndex(0) as ListViewItem;
        var rowHeight = row is { ActualHeight: > 0 } ? row.ActualHeight + row.Margin.Top + row.Margin.Bottom : FavoriteRowHeight;
        var list = ViewModel.Favorites.Count > 0
            ? (ViewModel.Favorites.Count * rowHeight) + CompactFavorites.Padding.Top + CompactFavorites.Padding.Bottom
            : CompactNoFavorites.DesiredSize.Height;

        var wanted = Root.RowDefinitions.Take(3).Sum(definition => definition.ActualHeight)
            + CompactView.Padding.Top + CompactView.Padding.Bottom + CompactView.RowSpacing
            + CompactNowPlaying.ActualHeight
            + CompactFavoritesCard.BorderThickness.Top + CompactFavoritesCard.BorderThickness.Bottom
            + list
            + CompactSlack;

        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var chrome = AppWindow.Size.Height - AppWindow.ClientSize.Height;
        var height = Math.Clamp((int)Math.Ceiling(wanted * Scale) + chrome, (int)(CompactMinHeight * Scale), work.Height);
        if (Math.Abs(height - AppWindow.Size.Height) <= 1)
        {
            return;
        }

        MoveOnScreen(AppWindow.Position, new SizeInt32(AppWindow.Size.Width, height));
    }

    /// <summary>
    /// Fits the compact window again when a part above the list changed height, not just width. The fit lays the
    /// window out again, which it cannot do from inside a layout pass, so it waits for the next turn.
    /// </summary>
    private void RefitWhenTaller(SizeChangedEventArgs e)
    {
        if (e.PreviousSize.Height > 0 && Math.Abs(e.NewSize.Height - e.PreviousSize.Height) >= 0.5)
        {
            DispatcherQueue.TryEnqueue(FitCompactWindow);
        }
    }

    /// <summary>
    /// Remembers where the window is now. A maximized window is no size to return to, so it keeps the size it had
    /// before it was maximized and only notes that it was maximized; a minimized one says nothing at all.
    /// </summary>
    private void RememberWindow(bool compact)
    {
        if (_placing || AppWindow.Presenter is not OverlappedPresenter presenter || presenter.State == OverlappedPresenterState.Minimized)
        {
            return;
        }

        // The compact window is never maximized, so a maximized one while it is up is the full window on its way
        // out, and its size is no size for the compact window to come back to.
        var maximized = presenter.State == OverlappedPresenterState.Maximized;
        if (maximized && compact)
        {
            return;
        }

        var restored = maximized ? ViewModel.WindowPlacement(compact) : null;

        ViewModel.RememberWindow(compact, new WindowPlacement
        {
            X = restored?.X ?? AppWindow.Position.X,
            Y = restored?.Y ?? AppWindow.Position.Y,
            Width = restored?.Width ?? AppWindow.Size.Width,
            Height = restored?.Height ?? AppWindow.Size.Height,
            IsMaximized = maximized,
        });
    }

    /// <summary>Moves the window, keeping it on the screen it lands on: a remembered spot may be gone with its monitor.</summary>
    private void MoveOnScreen(PointInt32 position, SizeInt32 size)
    {
        var work = DisplayArea.GetFromPoint(position, DisplayAreaFallback.Nearest).WorkArea;
        var x = Math.Clamp(position.X, work.X, Math.Max(work.X, work.X + work.Width - size.Width));
        var y = Math.Clamp(position.Y, work.Y, Math.Max(work.Y, work.Y + work.Height - size.Height));
        AppWindow.MoveAndResize(new RectInt32(x, y, size.Width, size.Height));
    }

    /// <summary>Keeps the view button left of the minimize, maximize and close buttons, whose width varies.</summary>
    private void KeepViewButtonClearOfCaptionButtons() =>
        ViewButton.Margin = new Thickness(0, 0, AppWindow.TitleBar.RightInset / Scale, 0);

    private double Scale => GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshCacheSummary();
        SettingsDialog.XamlRoot = Root.XamlRoot;
        await SettingsDialog.ShowAsync();
    }

    private void AddKeyboardShortcuts()
    {
        AddShortcut(VirtualKey.Space, () => ViewModel.TogglePlaybackCommand.Execute(null));
        AddShortcut(VirtualKey.M, () => ViewModel.ToggleMuteCommand.Execute(null));
        AddShortcut(VirtualKey.F, () =>
        {
            // The track history searches itself; every other tab searches the stations. A box only accepts
            // focus once its own tab is shown, so the switch is given a turn to happen first.
            if (ViewModel.SelectedTab == MainTab.PlayHistory)
            {
                HistorySearchBox.Focus(FocusState.Keyboard);
                return;
            }

            StationsTab.IsSelected = true;
            DispatcherQueue.TryEnqueue(() => SearchBox.Focus(FocusState.Keyboard));
        });
    }

    /// <summary>
    /// Claims the same actions system wide, on Ctrl+Alt instead of Ctrl: the in-window shortcuts are left where
    /// they are, because taking Ctrl+Space away from every other app would break typing and code completion.
    /// Whatever another app already holds is named in the settings, since only the rest is registered.
    /// </summary>
    private void ApplyGlobalHotkeys()
    {
        _hotkeys.UnregisterAll();
        if (!ViewModel.GlobalHotkeys)
        {
            ViewModel.GlobalHotkeyStatus = "";
            return;
        }

        var taken = new List<string>();
        Claim(VirtualKey.P, "Ctrl+Alt+P", () => ViewModel.TogglePlaybackCommand.Execute(null));
        Claim(VirtualKey.M, "Ctrl+Alt+M", () => ViewModel.ToggleMuteCommand.Execute(null));
        Claim(VirtualKey.Right, "Ctrl+Alt+→", () => ViewModel.PlayNextFavoriteCommand.Execute(null));
        Claim(VirtualKey.Left, "Ctrl+Alt+←", () => ViewModel.PlayPreviousFavoriteCommand.Execute(null));

        ViewModel.GlobalHotkeyStatus = taken.Count == 0
            ? ""
            : Localizer.Format(taken.Count == 1 ? "HotkeyInUseOne" : "HotkeyInUseMany", string.Join(", ", taken));

        void Claim(VirtualKey key, string name, Action action)
        {
            if (!_hotkeys.Register(key, action))
            {
                taken.Add(name);
            }
        }
    }

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) =>
        ViewModel.SelectedTab =
            sender.SelectedItem == ZapperTab ? MainTab.Zapper
            : sender.SelectedItem == FavoriteTracksTab ? MainTab.FavoriteTracks
            : sender.SelectedItem == PlayHistoryTab ? MainTab.PlayHistory
            : MainTab.Stations;

    private void AddShortcut(VirtualKey key, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Control };
        accelerator.Invoked += (_, args) =>
        {
            action();
            args.Handled = true;
        };
        Root.KeyboardAccelerators.Add(accelerator);
    }

    private void Favorite_ItemClick(object sender, ItemClickEventArgs e) =>
        ViewModel.Play(((FavoriteViewModel)e.ClickedItem).Station);

    private void Station_ItemClick(object sender, ItemClickEventArgs e) =>
        ViewModel.Play(((StationResultViewModel)e.ClickedItem).Station);

    /// <summary>
    /// Enter plays the best match, Down moves into the results to pick another one, and Escape empties the box,
    /// so a station can be found and played without reaching for the mouse.
    /// </summary>
    private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                await ViewModel.PlayBestMatchAsync();
                break;
            case VirtualKey.Down when ViewModel.Results.Count > 0:
                e.Handled = true;
                StationResults.ScrollIntoView(ViewModel.Results[0]);
                StationResults.UpdateLayout();
                (StationResults.ContainerFromIndex(0) as Control)?.Focus(FocusState.Keyboard);
                break;
            case VirtualKey.Escape when SearchBox.Text.Length > 0:
                e.Handled = true;
                SearchBox.Text = "";
                break;
        }
    }

    private void Country_GotFocus(object sender, RoutedEventArgs e)
    {
        // The box raises GotFocus again while typing; only entering it should reset the list.
        if (e.OriginalSource is not TextBox text || _countryText is not null)
        {
            return;
        }

        // Select the current country so typing replaces it, and show every country to pick from.
        // Deferred, because a mouse click places the caret after this event.
        _countryText = text;
        DispatcherQueue.TryEnqueue(() => text.SelectAll());
        CountryBox.ItemsSource = ViewModel.Countries;
        CountryBox.IsSuggestionListOpen = true;
    }

    private void Country_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            sender.ItemsSource = ViewModel.MatchCountries(sender.Text);
            sender.IsSuggestionListOpen = true;
        }
    }

    private void Country_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        // Enter picks the best match, so "neth" + Enter is enough.
        if ((args.ChosenSuggestion as string ?? ViewModel.MatchCountries(args.QueryText).FirstOrDefault()) is { } country)
        {
            ViewModel.SelectedCountry = country;
        }

        sender.Text = ViewModel.SelectedCountry;
        sender.IsSuggestionListOpen = false;
        _countryText?.SelectAll();
    }

    private void Country_LostFocus(object sender, RoutedEventArgs e)
    {
        // Checked afterwards, because focus also moves around inside the box.
        DispatcherQueue.TryEnqueue(() =>
        {
            for (var element = FocusManager.GetFocusedElement(CountryBox.XamlRoot) as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
            {
                if (element == CountryBox)
                {
                    return;
                }
            }

            // Leaving the box without choosing keeps the country that is actually selected.
            _countryText = null;
            CountryBox.Text = ViewModel.SelectedCountry;
        });
    }

    private void FavoriteLogo_ImageOpened(object sender, RoutedEventArgs e) =>
        ((FavoriteViewModel)((FrameworkElement)sender).DataContext).Logo.OnImageOpened();

    private void FavoriteLogo_ImageFailed(object sender, ExceptionRoutedEventArgs e) =>
        ((FavoriteViewModel)((FrameworkElement)sender).DataContext).Logo.OnImageFailed();

    private void NowPlayingLogo_ImageOpened(object sender, RoutedEventArgs e) => ViewModel.NowPlayingLogo.OnImageOpened();

    private void NowPlayingLogo_ImageFailed(object sender, ExceptionRoutedEventArgs e) => ViewModel.NowPlayingLogo.OnImageFailed();

    private void Favorite_PointerEntered(object sender, PointerRoutedEventArgs e) => SetFavoritePointerOver(sender, true);

    private void Favorite_PointerExited(object sender, PointerRoutedEventArgs e) => SetFavoritePointerOver(sender, false);

    private static void SetFavoritePointerOver(object sender, bool isPointerOver)
    {
        if (sender is FrameworkElement { DataContext: FavoriteViewModel favorite })
        {
            favorite.IsPointerOver = isPointerOver;
        }
    }

    private void Favorite_GotFocus(object sender, RoutedEventArgs e) => SetFavoriteFocus(e, true);

    private void Favorite_LostFocus(object sender, RoutedEventArgs e) => SetFavoriteFocus(e, false);

    /// <summary>Keeps the buttons of a favorite visible while it is reachable with the keyboard.</summary>
    private static void SetFavoriteFocus(RoutedEventArgs e, bool hasFocus)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: FavoriteViewModel favorite })
        {
            favorite.HasFocus = hasFocus;
        }
    }

    private void RemoveFavorite_Click(object sender, RoutedEventArgs e) =>
        ViewModel.RemoveFavorite((FavoriteViewModel)((FrameworkElement)sender).DataContext);

    private void PlayStation_Click(object sender, RoutedEventArgs e) =>
        ViewModel.Play(((StationResultViewModel)((FrameworkElement)sender).DataContext).Station);

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e) =>
        ViewModel.ToggleFavorite(((StationResultViewModel)((FrameworkElement)sender).DataContext).Station);

    private void CopyFavoriteTrack_Click(object sender, RoutedEventArgs e) =>
        CopyToClipboard(((FavoriteTrack)((FrameworkElement)sender).DataContext).Title);

    private void RemoveFavoriteTrack_Click(object sender, RoutedEventArgs e) =>
        ViewModel.RemoveFavoriteTrack((FavoriteTrack)((FrameworkElement)sender).DataContext);

    private void CopyHistoryTrack_Click(object sender, RoutedEventArgs e) =>
        CopyToClipboard(((PlayedTrackViewModel)((FrameworkElement)sender).DataContext).Title);

    private void ToggleHistoryTrackSaved_Click(object sender, RoutedEventArgs e) =>
        ViewModel.ToggleHistoryTrackSaved((PlayedTrackViewModel)((FrameworkElement)sender).DataContext);

    private async void OpenTrackInSpotify_Click(object sender, RoutedEventArgs e) =>
        await OpenTrackAsync(MusicService.Spotify, (FrameworkElement)sender);

    private async void OpenTrackOnYouTube_Click(object sender, RoutedEventArgs e) =>
        await OpenTrackAsync(MusicService.YouTube, (FrameworkElement)sender);

    /// <summary>
    /// Searches the service for the song named in the tag of the item that was clicked. The app of the service
    /// is asked first, so the song opens where you would save it, and its web player takes over when the app is
    /// not installed; asking beforehand keeps Windows from offering to go looking for one in the Store.
    /// </summary>
    private static async Task OpenTrackAsync(MusicService service, FrameworkElement source)
    {
        if (source.Tag is not string title || TrackLinks.Web(service, title) is not { } web)
        {
            return;
        }

        try
        {
            if (TrackLinks.App(service, title) is { } app
                && await Launcher.QueryUriSupportAsync(app, LaunchQuerySupportType.Uri) == LaunchQuerySupportStatus.Available)
            {
                await Launcher.LaunchUriAsync(app);
                return;
            }

            await Launcher.LaunchUriAsync(web);
        }
        catch (Exception)
        {
            // Nothing can be done about a browser or an app that refuses to open; the song is still in the list.
        }
    }

    private static void CopyToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);
}
