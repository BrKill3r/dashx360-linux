using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;
using XboxMetroLauncher.Views.Tabs;
using XboxMetroLauncher.Controls;
using XboxMetroLauncher.Input;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Services;
using XboxMetroLauncher.Themes;
using XboxMetroLauncher.Utilities;
using XboxMetroLauncher.ViewModels;
using XboxMetroLauncher.ViewModels.Tabs;
using XboxMetroLauncher.Views;

namespace XboxMetroLauncher;

public partial class MainWindow : Window
{
	private readonly record struct FocusCandidate(Avalonia.Controls.Button Button, Rect Bounds);

	private readonly record struct OverlayFocusCandidate(Avalonia.Controls.Control Control, Rect Bounds);

	private readonly DashboardViewModel _viewModel;

	private readonly ControllerInputService _controllerInputService;

	private readonly GlobalHotkeyService _guideHotkeyService;

	private readonly IAudioService _audioService;

	private readonly IFriendsService _friendsService;

	private readonly SocialIntegrationManager _socialIntegrationManager;

	private readonly ISteamCommunityService _steamCommunityService;

	private readonly DispatcherTimer _clockTimer;

	private readonly DispatcherTimer _performanceDebugTimer;

	private readonly Dictionary<string, Avalonia.Controls.Button> _lastFocusedButtonByTab = new Dictionary<string, Avalonia.Controls.Button>();

	private bool _bootBrowser;

	private XboxMetroLauncher.Platform.YouTubeTvSession? _youtubeTvBrowser;

	private DispatcherTimer? _bootStateTimer;

	private DateTime _bootStartedAt;

	private int _lastTabIndex = 1;

	private const string YouTubeTvUrl = "https://www.youtube.com/tv";

	private const string YouTubeTvUserAgent = "Mozilla/5.0 (SMART-TV; Linux; Tizen 6.0) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/4.0 TV Safari/537.36";

	private const double FocusZoneLeft = 64.0;

	private const double FocusZoneRight = 1120.0;

	private bool _bootSkipped;

	private bool _startupInitializationComplete;

	private bool _startupSoundPlayed;

	private bool _fakeLoadingStarted;

	private bool _isFakeLoadingActive;

	private bool _isMenuFakeLoadingActive;

	private bool _isMenuTransitionActive;

	private bool _guideWarmupScheduled;

	private bool _startupPrewarmScheduled;

	private bool _bootBrowserCleanupScheduled;

	private bool _youtubeTvNavigationStarted;

	private bool _signInToastSequenceActive;

	private bool _partyInviteToastActive;

	private string _toastActionUri = string.Empty;

	private int _settingsOpenCount;

	private int _gamesOpenCount;

	private int _achievementsOpenCount;

	private int _playedGamesOpenCount;

	private int _musicOpenCount;

	private int _appsOpenCount;

	private bool _isAnimatingTab;

	private bool _isFocusUpdateQueued;

	private int _queuedTabStep;

	private object? _lastRenderedTab;

	private bool _isMouseCursorHiddenForController;

	private bool _isHandlingControllerInput;

	private Point? _lastMousePosition;

	private IGuideWindow? _guideWindow;

	private GuideViewModel? _guideViewModel;

	private UIElement? _guideReturnFocusElement;

	private bool _restoreFocusAfterGuideClose;

	private nint _guideReturnWindowHandle;

	private bool _guideRestoreExternalWindow;

	private string _appliedThemeBackgroundPath = string.Empty;

	private string _appliedBingBackgroundPath = string.Empty;

	private FrameworkElement? _activeSystemSettingsPanel;

	private int _lastGameDetailsTabAnimationIndex;

	private int _libraryFocusRequestId;

	private static readonly string BingBackgroundRelativePath = System.IO.Path.Combine("Assets", "References", "penguin_bing_background.png");

	private static readonly string PerformanceDebugLogPath = System.IO.Path.Combine(AppPaths.LogsFolder, "performance-debug.log");






	public MainWindow()
	{
		//IL_01a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c2: Expected O, but got Unknown
		//IL_01da: Unknown result type (might be due to invalid IL or missing references)
		//IL_01df: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f8: Expected O, but got Unknown
		InitializeComponent();
		try
		{
			// Taskbar / Alt+Tab icon (_NET_WM_ICON); the menu entry uses the hicolor icons installed by install.sh.
			using System.IO.Stream iconStream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://DashX360/Icons/dashx360-256.png"));
			base.Icon = new WindowIcon(iconStream);
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.Icon");
		}
		WireWpfTunnelEvents();
		WireStyleEventSetters();
		base.PropertyChanged += (sender, e) => { if (e.Property == Window.WindowStateProperty) Window_OnStateChanged(sender, EventArgs.Empty); };
		string writableAppDataPath = GetWritableAppDataPath();
		if (!AppPaths.IsPartyLinkTestInstance)
		{
			MigrateLegacyUserData(writableAppDataPath);
		}
		JsonStore jsonStore = new JsonStore(writableAppDataPath);
		_friendsService = new FriendsService(jsonStore);
		ISettingsService settingsService = new SettingsService(jsonStore);
		LocalSocialIntegrationService localService = new LocalSocialIntegrationService(_friendsService);
		_steamCommunityService = new SteamCommunityService();
		IDashPartyLinkService dashPartyLinkService = new DashPartyLinkService(jsonStore);
		_socialIntegrationManager = new SocialIntegrationManager(_friendsService, localService, _steamCommunityService, dashPartyLinkService);
		IProfileService profileService = new ProfileService(jsonStore);
		IGameLibraryService libraryService = new JsonGameLibraryService(jsonStore);
		IImportExportService importExportService = new ImportExportService(libraryService, profileService, settingsService, writableAppDataPath);
		IRunningGameService runningGameService = new RunningGameService();
		DashboardViewModel viewModel = null;
		_viewModel = new DashboardViewModel(audioService: _audioService = new AudioService(() => viewModel?.Settings.PlayUiSounds ?? true, AudioHost, () => viewModel?.Settings.AudioOutputDeviceName ?? "Default", () => viewModel?.Settings.DashboardVolume ?? 1.0, () => viewModel?.Settings.DashboardSounds ?? "Metro"), libraryService: libraryService, launchService: new GameLaunchService(), searchService: new SearchService(), settingsService: settingsService, profileService: profileService, filePickerService: new AvaloniaFilePickerService(), importExportService: importExportService, steamLibraryScannerService: new SteamLibraryScannerService(), steamCommunityService: _steamCommunityService, themeService: new ThemeService(), startupRegistrationService: new LinuxStartupRegistrationService(), socialIntegrationManager: _socialIntegrationManager, runningGameService: runningGameService);
		viewModel = _viewModel;
		BladesArtworkHost.NavigationFrame += (from, to, progress, seconds, boundary) =>
		{
			BladesCenterHost.ApplyNavigationFrame(from, to, progress, boundary, BladesArtworkHost.NavigationOutgoingClip);
			BladesContentCanvas.Opacity = string.Equals(to, _viewModel.ActiveBladesBlade?.Key, StringComparison.OrdinalIgnoreCase)
				? Math.Clamp((seconds - 0.200) / 0.133, 0.0, 1.0) : 0.0;
			BladesContentCanvas.IsHitTestVisible = false;
		};
		BladesArtworkHost.NavigationCompleted += () =>
		{
			BladesCenterHost.FinishNavigation();
			BladesContentCanvas.Opacity = 1.0;
			BladesContentCanvas.IsHitTestVisible = true;
		};
		base.DataContext = _viewModel;
		_lastRenderedTab = _viewModel.CurrentTab;
		_controllerInputService = new ControllerInputService(HandleControllerInputAction, () => _viewModel.Settings.EnableControllerInput);
		_guideHotkeyService = new GlobalHotkeyService();
		_guideHotkeyService.HotkeyPressed += delegate
		{
			Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
			{
				HandleInputAction(DashboardInputAction.Guide);
			}, DispatcherPriority.Send, Array.Empty<object>());
		};
		_clockTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(6.0)
		};
		_clockTimer.Tick += delegate
		{
			_viewModel.UpdateClock();
		};
		_performanceDebugTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(20.0)
		};
		_performanceDebugTimer.Tick += delegate
		{
			WritePerformanceDebugReport();
		};
		_viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
		_viewModel.FriendsOverlayRequested += ViewModel_OnFriendsOverlayRequested;
		_viewModel.BladesSubmenuTransition = TransitionBladesSubmenuAsync;
		_viewModel.BladesMusicRequested += ViewModel_OnBladesMusicRequested;
		_viewModel.PartyOverlayRequested += ViewModel_OnPartyOverlayRequested;
		_viewModel.AchievementOverlayRequested += ViewModel_OnAchievementOverlayRequested;
		_viewModel.ToastRequested += ViewModel_OnToastRequested;
		ApplyDisplaySettings();
	}

	private static string GetWritableAppDataPath()
	{
		return AppPaths.UserDataFolder;
	}

	private static void MigrateLegacyUserData(string targetRoot)
	{
		if (!Directory.Exists(targetRoot))
		{
			Directory.CreateDirectory(targetRoot);
		}
		if (Directory.EnumerateFiles(targetRoot, "*.json", SearchOption.TopDirectoryOnly).Any())
		{
			return;
		}
		foreach (string item in AppPaths.LegacyDataRoots())
		{
			if (!Directory.Exists(item))
			{
				continue;
			}
			List<string> list = Directory.EnumerateFiles(item, "*.json", SearchOption.TopDirectoryOnly).ToList();
			if (list.Count == 0)
			{
				continue;
			}
			{
				foreach (string item2 in list)
				{
					string text = System.IO.Path.Combine(targetRoot, System.IO.Path.GetFileName(item2));
					if (!File.Exists(text))
					{
						File.Copy(item2, text, overwrite: false);
					}
				}
				break;
			}
		}
	}

	private async void Window_OnLoaded(object sender, RoutedEventArgs e)
	{
		try
		{
			await _viewModel.LoadStartupSettingsAsync();
			_audioService.WarmUp(ShouldRunBladesRunnerStartup() ? "dash_2ndLevelClose" : "startup");
			_viewModel.RefreshAudioOutputDevices();
			_viewModel.Settings.PropertyChanged += Settings_OnPropertyChanged;
			ApplyDisplaySettings();
			_controllerInputService.Start();
			_guideHotkeyService.Register(this);
			StartDeveloperAutomation();
			_clockTimer.Start();
			StartBootVideo();
			await _viewModel.InitializeAsync(reloadSettings: false);
			UpdateThemeBackgroundVisual(animate: false);
			UpdateBingBackgroundVisual(animate: false);
			ApplyDisplaySettings();
			UpdateAdjacentPreviewSnapshots();
			WritePerformanceDebugReport();
			_startupInitializationComplete = true;
			if (!_viewModel.IsBooting && !StartFakeLoadingIfReady())
			{
				PlayStartupSound();
				ScheduleStartupPrewarm();
				FocusFirstButton();
				_ = RunSignInToastSequenceAsync();
			}
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.Window_OnLoaded");
		}
	}

	private void Window_OnClosing(object? sender, WindowClosingEventArgs e)
	{
		base.Cursor = null;
		Mouse.OverrideCursor = null;
		_viewModel.Settings.PropertyChanged -= Settings_OnPropertyChanged;
		_viewModel.FriendsOverlayRequested -= ViewModel_OnFriendsOverlayRequested;
		_viewModel.BladesMusicRequested -= ViewModel_OnBladesMusicRequested;
		_viewModel.PartyOverlayRequested -= ViewModel_OnPartyOverlayRequested;
		_viewModel.AchievementOverlayRequested -= ViewModel_OnAchievementOverlayRequested;
		_viewModel.ToastRequested -= ViewModel_OnToastRequested;
		_guideWindow?.Close();
		_guideViewModel?.Dispose();
		_guideHotkeyService.Dispose();
		_controllerInputService.Dispose();
		_clockTimer.Stop();
		_performanceDebugTimer.Stop();
		CleanupYouTubeTvBrowser();
		CleanupBootBrowser();
		WritePerformanceDebugReport();
	}

	private void Window_OnStateChanged(object? sender, EventArgs e)
	{
		if (base.WindowState == WindowState.Minimized)
		{
			TrimResourcesForMinimizedDashboard();
		}
		else
		{
			RestoreResourcesAfterMinimizedDashboard();
		}
	}

	private void TrimResourcesForMinimizedDashboard()
	{
		try
		{
			ThemeBackgroundImage.Source = null;
			UltraWideThemeBackgroundImage.Source = null;
			BingBackgroundImage.Source = null;
			UltraWideBingBackgroundImage.Source = null;
			PreviousPreviewImage.Source = null;
			NextPreviewImage.Source = null;
			GameDetailsBackgroundImage.Source = null;
			GameDetailsGalleryImage.Source = null;
			PreviousPreviewLiveHost.Content = null;
			NextPreviewLiveHost.Content = null;
			TransitionLeftHost.Content = null;
			TransitionCenterHost.Content = null;
			TransitionRightHost.Content = null;
			ImageCacheService.ClearDecodedImages();
			_audioService.TrimCachedResources(keepGuideReady: true);
			GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
			GC.WaitForPendingFinalizers();
			GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: true);
			XboxMetroLauncher.Platform.NativeMemory.TrimWorkingSet();
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.TrimResourcesForMinimizedDashboard");
		}
	}

	private void RestoreResourcesAfterMinimizedDashboard()
	{
		try
		{
			GameDetailsBackgroundImage.ClearValue(Avalonia.Controls.Image.SourceProperty);
			GameDetailsGalleryImage.ClearValue(Avalonia.Controls.Image.SourceProperty);
			UpdateThemeBackgroundVisual(animate: false);
			UpdateBingBackgroundVisual(animate: false);
			ScheduleGuideAudioWarmup();
			Dispatcher.UIThread.BeginInvoke((Delegate)(Action)FocusFirstButton, DispatcherPriority.Background, Array.Empty<object>());
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.RestoreResourcesAfterMinimizedDashboard");
		}
	}

	private void Settings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		string propertyName = e.PropertyName;
		if ((propertyName == "DisplayResolution" || propertyName == "StartFullscreen") ? true : false)
		{
			if (propertyName == "DisplayResolution")
			{
				_viewModel.RefreshDisplayAspectRatioBindings();
			}
			ApplyDisplaySettings();
		}
		else if (propertyName == "DashboardStyle")
		{
			_viewModel.RefreshDashboardStyleBindings();
			UpdateThemeBackgroundVisual();
		}
	}

	private void ApplyDisplaySettings()
	{
		//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
		var (val, val2) = _viewModel.Settings.DisplayResolution switch
		{
			"21:9" => (1720.0, 720.0), 
			_ => (1920.0, 1080.0), 
		};
		if (_viewModel.Settings.StartFullscreen)
		{
			// Do not set CanResize=false here: on X11 Avalonia turns it into min=max size hints (1280x720), so the window
			// manager cannot grow the window to the monitor and the dashboard ends up in a corner over a black backdrop.
			base.CanResize = true;
			// MinWidth/MinHeight are logical units; with XWayland's x2 scaling they can exceed a 1080p monitor and
			// would also stop the compositor from fitting the fullscreen window to it.
			base.MinWidth = 0.0;
			base.MinHeight = 0.0;
			base.SystemDecorations = SystemDecorations.None;
			base.WindowState = WindowState.FullScreen;
			return;
		}
		base.WindowState = WindowState.Normal;
		base.SystemDecorations = SystemDecorations.Full;
		base.CanResize = true;
		Avalonia.Platform.Screen? screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
		double scaling = screen?.Scaling ?? 1.0;
		PixelRect workArea = screen?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
		base.MinWidth = Math.Min(1024.0, workArea.Width / scaling);
		base.MinHeight = Math.Min(576.0, workArea.Height / scaling);
		base.Width = Math.Min(val, workArea.Width / scaling);
		base.Height = Math.Min(val2, workArea.Height / scaling);
		base.Position = new PixelPoint(workArea.X + (int)Math.Max(0.0, (workArea.Width - base.Width * scaling) / 2.0), workArea.Y + (int)Math.Max(0.0, (workArea.Height - base.Height * scaling) / 2.0));
	}

	private void Window_OnPreviewKeyDown(object sender, Avalonia.Input.KeyEventArgs e)
	{
		DashboardInputAction action;
		if (_isFakeLoadingActive)
		{
			e.Handled = true;
		}
		else if (_viewModel.IsBooting)
		{
			SkipBootIntro(userInitiated: true);
			e.Handled = true;
		}
		else if (DashboardInputRouter.TryMapKey(e, out action))
		{
			e.Handled = true;
			if (TryRouteInputToGuide(action))
			{
				return;
			}
			if (TryHandleBladesAchievementsInputEarly(action))
			{
				return;
			}
			HandleInputAction(action);
		}
	}

	private void Window_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
	{
		ShowMouseCursor(updatePosition: true);
		if (_isFakeLoadingActive)
		{
			e.Handled = true;
		}
		else if (_viewModel.IsBooting)
		{
			SkipBootIntro(userInitiated: true);
			e.Handled = true;
		}
		else
		{
		}
	}

	private void Window_OnPreviewMouseMove(object sender, Avalonia.Input.PointerEventArgs e)
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		Point position = e.GetPosition(this);
		Point? lastMousePosition = _lastMousePosition;
		if (lastMousePosition.HasValue)
		{
			Point valueOrDefault = lastMousePosition.GetValueOrDefault();
			if (Math.Abs(position.X - valueOrDefault.X) < 2.0 && Math.Abs(position.Y - valueOrDefault.Y) < 2.0)
			{
				return;
			}
		}
		_lastMousePosition = position;
		ShowMouseCursor(updatePosition: false);
	}

	private void HandleControllerInputAction(DashboardInputAction action)
	{
		HideMouseCursorForController();
		if (TryRouteInputToGuide(action))
		{
			return;
		}
		if (TryHandleBladesAchievementsInputEarly(action))
		{
			return;
		}
		_isHandlingControllerInput = true;
		try
		{
			HandleInputAction(action);
		}
		finally
		{
			_isHandlingControllerInput = false;
		}
	}

	private void HandleInputAction(DashboardInputAction action)
	{
		try
		{
			HandleInputActionCore(action);
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.HandleInputAction");
			_isAnimatingTab = false;
			_isFocusUpdateQueued = false;
		}
	}

	private bool TryRouteInputToGuide(DashboardInputAction action)
	{
		try
		{
			IGuideWindow? guideWindow = _guideWindow;
			if (guideWindow == null)
			{
				return false;
			}
			if (guideWindow.IsGuideOpen)
			{
				guideWindow.HandleInput(action);
				return true;
			}
			return guideWindow.IsTransitioning;
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.TryRouteInputToGuide");
			return true;
		}
	}

	private bool TryHandleBladesAchievementsInputEarly(DashboardInputAction action)
	{
		try
		{
			return !_isFakeLoadingActive
				&& !_viewModel.IsBooting
				&& _viewModel.HandleBladesAchievementsSubmenuInput(action);
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.TryHandleBladesAchievementsInputEarly");
			return false;
		}
	}

	private void HideMouseCursorForController()
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		if (!_isMouseCursorHiddenForController)
		{
			_isMouseCursorHiddenForController = true;
			_lastMousePosition = Mouse.GetPosition(this);
			base.Cursor = Cursors.None;
			Mouse.OverrideCursor = Cursors.None;
		}
	}

	private void ShowMouseCursor(bool updatePosition)
	{
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		if (!_isMouseCursorHiddenForController)
		{
			if (updatePosition)
			{
				_lastMousePosition = Mouse.GetPosition(this);
			}
			return;
		}
		_isMouseCursorHiddenForController = false;
		if (updatePosition)
		{
			_lastMousePosition = Mouse.GetPosition(this);
		}
		base.Cursor = null;
		Mouse.OverrideCursor = null;
	}

	private void HandleInputActionCore(DashboardInputAction action)
	{
		if (Environment.GetEnvironmentVariable("DASHX360_INPUT_DEBUG") == "1")
		{
			Console.Error.WriteLine($"[input] core {action} fake={_isFakeLoadingActive} menuFake={_isMenuFakeLoadingActive} menuTrans={_isMenuTransitionActive} blades={_viewModel.IsBladesMenuTransitioning} guide={_guideWindow?.IsGuideOpen}");
		}
		if (_isFakeLoadingActive || _isMenuFakeLoadingActive || _isMenuTransitionActive || _viewModel.IsBladesMenuTransitioning)
		{
			return;
		}
		if (action == DashboardInputAction.Activate && TryHandleActiveToastAction())
		{
			return;
		}
		if (action == DashboardInputAction.Guide)
		{
			if (TryHandleActiveToastAction())
			{
				return;
			}
			IGuideWindow? guideWindow = _guideWindow;
			if (guideWindow == null || !guideWindow.IsTransitioning)
			{
				IGuideWindow? guideWindow2 = _guideWindow;
				if (guideWindow2 != null && guideWindow2.IsGuideOpen)
				{
					HideGuide();
				}
				else
				{
					ShowGuide();
				}
			}
			return;
		}
		IGuideWindow? guideWindow3 = _guideWindow;
		if (guideWindow3 != null && guideWindow3.IsGuideOpen)
		{
			_guideWindow.HandleInput(action);
		}
		else
		{
			if (base.WindowState == WindowState.Minimized || !base.IsVisible || !IsActiveForInput)
			{
				return;
			}
			if (_viewModel.IsYouTubeTvOpen && HandleYouTubeTvInput(action))
			{
				return;
			}
			if (_viewModel.IsBooting)
			{
				SkipBootIntro(userInitiated: true);
				return;
			}
			if (_viewModel.IsBladesAchievementsSubmenu && _viewModel.HandleBladesAchievementsSubmenuInput(action))
			{
				return;
			}
			if (_viewModel.IsBladesDashboardStyle && _viewModel.IsBladesSubmenuOpen && !_viewModel.IsDetailsOpen && !_viewModel.IsMyGamesOpen && !_viewModel.IsLauncherSettingsOpen && !_viewModel.IsSearchOverlayOpen)
			{
				_viewModel.HandleInput(action);
				if (!_viewModel.IsBladesAchievementsSubmenu)
				{
					QueueBladesMenuFocus();
				}
				return;
			}
			if (_viewModel.IsMusicNowPlayingScreen && IsFocusInside((DependencyObject)(object)MusicPlayerLeftPane) && ((uint)action <= 3u || action == DashboardInputAction.Activate))
			{
				if ((uint)action <= 3u)
				{
					TryMoveOverlayFocus(MusicPlayerOverlay, action);
					return;
				}
				if (DashboardInputRouter.ActivateFocusedElement())
				{
					return;
				}
			}
			if (_viewModel.HandleMusicBrowserInput(action))
			{
				return;
			}
			if (action == DashboardInputAction.Back && _viewModel.IsLauncherSettingsOpen && _activeSystemSettingsPanel != null && !_viewModel.IsDashboardCustomizerOpen && !_viewModel.IsThemeCreatorOpen && !_viewModel.IsSteamSetupOpen && !_viewModel.IsSpotifySetupOpen)
			{
				_audioService.Play("menu-out");
				ShowSystemSettingsCategories();
				return;
			}
			if (action == DashboardInputAction.Back && TryBeginMenuCloseTransition())
			{
				return;
			}
			bool flag = _viewModel.IsDetailsOpen;
			if (flag)
			{
				bool flag2 = (uint)(action - 12) <= 1u;
				flag = flag2;
			}
			if (flag)
			{
				_viewModel.MoveGameDetailsTab((action != DashboardInputAction.PreviousTab) ? 1 : (-1));
				QueueGameDetailsTileFocus();
				return;
			}
			if (_viewModel.IsDetailsOpen && (uint)action <= 3u)
			{
				if (!TryMoveOverlayFocus(GameDetailsOverlay, action))
				{
					QueueGameDetailsTileFocus();
				}
				return;
			}
			if (_viewModel.IsMyGamesOpen && ((uint)action <= 3u || (uint)(action - 12) <= 1u))
			{
				TryMoveMyGamesFocus(action);
				return;
			}
			flag = IsOverlayOpen();
			if (flag)
			{
				bool flag2 = (uint)(action - 12) <= 1u;
				flag = flag2;
			}
			if (flag)
			{
				return;
			}
			flag = _isAnimatingTab;
			if (flag)
			{
				bool flag2 = (((uint)action <= 1u || (uint)(action - 12) <= 1u) ? true : false);
				flag = flag2;
			}
			if (flag)
			{
				if (!IsOverlayOpen())
				{
					flag = ((action == DashboardInputAction.MoveLeft || action == DashboardInputAction.PreviousTab) ? true : false);
					_queuedTabStep = ((!flag) ? 1 : (-1));
				}
				return;
			}
			if (_viewModel.IsBladesDashboardStyle && !IsOverlayOpen())
			{
				_viewModel.HandleInput(action);
				QueueBladesMenuFocus();
				return;
			}
			if ((uint)(action - 12) <= 1u)
			{
				RememberFocusedButton();
			}
			if ((uint)action <= 3u)
			{
				if (TryRestoreOverlayFocus())
				{
					_viewModel.HandleInput(action);
					return;
				}
				bool flag3 = TryMoveDashboardFocus(action);
				if (!flag3 && action == DashboardInputAction.MoveLeft)
				{
					if (!IsOverlayOpen())
					{
						_viewModel.MoveTab(-1);
					}
				}
				else if (!flag3 && action == DashboardInputAction.MoveRight && !IsOverlayOpen())
				{
					_viewModel.MoveTab(1);
				}
				_viewModel.HandleInput(action);
			}
			else
			{
				if (action == DashboardInputAction.Activate && TryRestoreOverlayFocus())
				{
					return;
				}
				if (action == DashboardInputAction.Activate && Keyboard.FocusedElement is Avalonia.Controls.TextBox textBox)
				{
					if ((_viewModel.IsSearchOverlayOpen || _viewModel.CurrentTab?.Key == "bing") && _viewModel.SubmitSearchCommand.CanExecute(null))
					{
						_viewModel.SubmitSearchCommand.Execute(null);
					}
					return;
				}
				if (action == DashboardInputAction.Activate && DashboardInputRouter.ActivateFocusedElement())
				{
					_viewModel.HandleInput(action);
					return;
				}
				_viewModel.HandleInput(action);
				if (action != DashboardInputAction.Search || !_viewModel.IsSearchOverlayOpen)
				{
					return;
				}
				Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
				{
					try
					{
						SearchOverlayTextBox.Focus();
						SearchOverlayTextBox.SelectAll();
					}
					catch
					{
					}
				}, DispatcherPriority.Background, Array.Empty<object>());
			}
		}
	}

	private void ShowGuide()
	{
		try
		{
			EnsureGuideWindow();
			if (_guideWindow != null && !_guideWindow.IsTransitioning && !_guideWindow.IsGuideOpen)
			{
				RememberGuideReturnFocus();
				_audioService.Play("guide-open");
				_guideWindow.Open();
			}
		}
		catch (InvalidOperationException)
		{
			EnsureGuideWindow();
			if (_guideWindow != null && !_guideWindow.IsTransitioning && !_guideWindow.IsGuideOpen)
			{
				RememberGuideReturnFocus();
				_audioService.Play("guide-open");
				_guideWindow.Open();
			}
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.ShowGuide");
		}
	}

	private void EnsureGuideWindow()
	{
		bool blades = _viewModel.Settings.XboxGuide == "Blades" || _viewModel.Settings.XboxGuide == "Blades BETA";
		if (_guideWindow != null && (_guideWindow is BladesGuideWindow) == blades)
		{
			return;
		}
		if (_guideWindow != null)
		{
			_guideWindow.HiddenCompleted -= GuideWindow_OnHiddenCompleted;
			_guideWindow.Closed -= GuideWindow_OnClosed;
			try
			{
				_guideWindow.Close();
			}
			catch
			{
			}
		}
		_guideViewModel?.Dispose();
		_guideViewModel = new GuideViewModel(_viewModel, this, HideGuide, _audioService, _friendsService, _socialIntegrationManager, _steamCommunityService);
		_guideWindow = blades ? new BladesGuideWindow(_guideViewModel) : new GuideWindow(_guideViewModel);
		_guideWindow.HiddenCompleted += GuideWindow_OnHiddenCompleted;
		_guideWindow.Closed += GuideWindow_OnClosed;
	}

	private void AcceptDashPartyInviteFromToast(SocialFriend friend)
	{
		try
		{
			EnsureGuideWindow();
			if (_guideViewModel == null || _guideWindow == null || _guideWindow.IsTransitioning)
			{
				return;
			}
			RememberGuideReturnFocus();
			_guideViewModel.AcceptDashPartyInvite(friend);
			if (!_guideWindow.IsGuideOpen)
			{
				_audioService.Play("guide-open");
				_guideWindow.Open(resetToHome: false);
			}
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.AcceptDashPartyInviteFromToast");
		}
	}

	private bool TryHandleActiveToastAction()
	{
		if (!_partyInviteToastActive)
		{
			return false;
		}
		string toastActionUri = _toastActionUri;
		if (!string.IsNullOrWhiteSpace(toastActionUri))
		{
			_toastActionUri = string.Empty;
			OpenToastActionUri(toastActionUri);
			return true;
		}
		if (_viewModel.TryTakePendingDashPartyInvite(out SocialFriend friend))
		{
			AcceptDashPartyInviteFromToast(friend);
			return true;
		}
		return false;
	}

	private static void OpenToastActionUri(string uri)
	{
		try
		{
			XboxMetroLauncher.Platform.LinuxApps.ShellExecute(uri);
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.OpenToastActionUri");
		}
	}

	private void ScheduleGuideAudioWarmup()
	{
		if (_guideWarmupScheduled)
		{
			return;
		}
		_guideWarmupScheduled = true;
		Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
		{
			try
			{
				WarmUpGuideAudio();
			}
			catch (Exception exception)
			{
				App.LogException(exception, "MainWindow.ScheduleGuideAudioWarmup");
			}
		}, DispatcherPriority.Background, Array.Empty<object>());
	}

	private void WarmUpGuideAudio()
	{
		_audioService.WarmUp("guide-open");
		_audioService.WarmUp("guide-close");
		_audioService.WarmUp("guide-blade-open");
		_audioService.WarmUp("guide-hover");
		_audioService.WarmUp("guide-select");
		_audioService.WarmUp("guide-back");
		_audioService.WarmUp("guide-blade-switch-1");
		_audioService.WarmUp("guide-blade-switch-2");
		_audioService.WarmUp("guide-blade-switch-3");
		_audioService.WarmUp("guide-blade-switch-4");
		WarmUpBladesDashboardAudio();
	}

	private void WarmUpBladesDashboardAudio()
	{
		_audioService.WarmUp("page-left");
		_audioService.WarmUp("page-right");
		_audioService.WarmUp("tab");
		_audioService.WarmUp("select");
		_audioService.WarmUp("back");
		_audioService.WarmUp("focus");
		_audioService.WarmUp("hover");
		_audioService.WarmUp("menu-in");
		_audioService.WarmUp("menu-out");
		for (int index = 0; index <= 6; index++)
		{
			_audioService.WarmUp($"tab-switch-{index}");
		}
	}

	private void HideGuide()
	{
		try
		{
			if (_guideWindow != null && !_guideWindow.IsTransitioning && _guideWindow.IsGuideOpen)
			{
				_restoreFocusAfterGuideClose = _guideWindow.CloseGuide(playSound: true);
				if (!_guideRestoreExternalWindow)
				{
					Activate();
				}
			}
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.HideGuide");
		}
	}

	private void ViewModel_OnFriendsOverlayRequested(object? sender, EventArgs e)
	{
		try
		{
			OpenGuideFriendsOverlay();
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.ViewModel_OnFriendsOverlayRequested");
		}
	}

	private async void ViewModel_OnBladesMusicRequested(object? sender, EventArgs e)
	{
		try
		{
			if (_viewModel.Settings.EnableFakeLoading && ShouldRunMenuFakeLoading("music"))
			{
				BladesLoadingTitle.Text="Music";
				await RunMenuFakeLoadingSequenceAsync();
				BladesLoadingTitle.ClearValue(TextBlock.TextProperty);
			}
			EnsureGuideWindow();
			RememberGuideReturnFocus();
			if (_guideWindow is BladesGuideWindow blades) blades.OpenMusicFromDashboard();
			else _guideViewModel?.OpenGuideMusicMenuCommand.Execute(null);
		}
		catch (Exception ex) { App.LogException(ex,"Blades.OpenMusic"); }
	}

	private void OpenGuideFriendsOverlay()
	{
		EnsureGuideWindow();
		if (_guideWindow != null && _guideViewModel != null && !_guideWindow.IsTransitioning)
		{
			RememberGuideReturnFocus();
			_guideViewModel.OpenFriendsOverlayFromDashboard();
			_audioService.Play("guide-open");
			_guideWindow.Open(resetToHome: false);
		}
	}

	private void ViewModel_OnPartyOverlayRequested(object? sender, EventArgs e)
	{
		try
		{
			OpenGuidePartyOverlay();
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.ViewModel_OnPartyOverlayRequested");
		}
	}

	private void ViewModel_OnAchievementOverlayRequested(object? sender, DashboardViewModel.DashboardAchievementOpenRequest e)
	{
		try
		{
			OpenGuideAchievementOverlay(e);
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.ViewModel_OnAchievementOverlayRequested");
		}
	}

	private void OpenGuidePartyOverlay()
	{
		EnsureGuideWindow();
		if (_guideWindow != null && _guideViewModel != null && !_guideWindow.IsTransitioning)
		{
			RememberGuideReturnFocus();
			_guideViewModel.OpenPartyOverlayFromDashboard();
			_audioService.Play("guide-open");
			_guideWindow.Open(resetToHome: false);
		}
	}

	private void OpenGuideAchievementOverlay(DashboardViewModel.DashboardAchievementOpenRequest request)
	{
		EnsureGuideWindow();
		if (_guideWindow != null && _guideViewModel != null && !_guideWindow.IsTransitioning)
		{
			RememberGuideReturnFocus();
			_guideViewModel.OpenAchievementFromDashboard(request.SteamAppId, request.AchievementApiName);
			_audioService.Play("guide-open");
			_guideWindow.Open(resetToHome: false);
		}
	}

	private void ViewModel_OnToastRequested(object? sender, DashboardToastRequest e)
	{
		Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
		{
			_ = RunDashboardToastSequenceAsync(e);
		}, DispatcherPriority.Background, Array.Empty<object>());
	}

	private async Task RunDashboardToastSequenceAsync(DashboardToastRequest request)
	{
		bool hasToastAction = !string.IsNullOrWhiteSpace(request.ActionUri);
		if (request.AcceptPartyInviteWithGuide || hasToastAction)
		{
			_partyInviteToastActive = true;
			_toastActionUri = hasToastAction ? request.ActionUri.Trim() : string.Empty;
		}
		try
		{
			await RunSignInToastSequenceAsync(request.Line1, request.Line2).ConfigureAwait(continueOnCapturedContext: true);
		}
		finally
		{
			if (request.AcceptPartyInviteWithGuide || hasToastAction)
			{
				_partyInviteToastActive = false;
				_toastActionUri = string.Empty;
			}
		}
	}

	private void RememberGuideReturnFocus()
	{
		int foregroundPid = XboxMetroLauncher.Platform.LinuxWindows.GetForegroundProcessId();
		_guideRestoreExternalWindow = !IsActive && foregroundPid > 0 && foregroundPid != Environment.ProcessId;
		_guideReturnWindowHandle = (_guideRestoreExternalWindow ? foregroundPid : IntPtr.Zero);
		_guideReturnFocusElement = (_guideRestoreExternalWindow ? null : (Keyboard.FocusedElement as UIElement));
		RememberFocusedButton();
	}

	private void GuideWindow_OnHiddenCompleted(object? sender, EventArgs e)
	{
		if (sender is BladesGuideWindow && WindowState == WindowState.Minimized) return;
		if (_guideRestoreExternalWindow && RestoreExternalWindowAfterGuide())
		{
			_restoreFocusAfterGuideClose = false;
			_guideRestoreExternalWindow = false;
			_guideReturnWindowHandle = IntPtr.Zero;
			_guideReturnFocusElement = null;
			return;
		}
		Activate();
		if (!_restoreFocusAfterGuideClose)
		{
			_guideRestoreExternalWindow = false;
			_guideReturnWindowHandle = IntPtr.Zero;
		}
		else
		{
			_restoreFocusAfterGuideClose = false;
			RestoreFocusAfterGuide();
		}
	}

	private void GuideWindow_OnClosed(object? sender, EventArgs e)
	{
		if (_guideWindow != null)
		{
			_guideWindow.HiddenCompleted -= GuideWindow_OnHiddenCompleted;
			_guideWindow.Closed -= GuideWindow_OnClosed;
		}
		_guideViewModel?.Dispose();
		_guideViewModel = null;
		_guideWindow = null;
	}

	private void RestoreFocusAfterGuide()
	{
		Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
		{
			try
			{
				if (IsOverlayOpen())
				{
					FocusFirstButton();
				}
				else if (_guideReturnFocusElement != null && TryFocus(_guideReturnFocusElement))
				{
					RememberFocusedButton();
				}
				else
				{
					FocusFirstButton();
				}
			}
			catch (Exception exception)
			{
				App.LogException(exception, "MainWindow.RestoreFocusAfterGuide");
			}
			finally
			{
				_guideReturnFocusElement = null;
				_guideRestoreExternalWindow = false;
				_guideReturnWindowHandle = IntPtr.Zero;
			}
		}, DispatcherPriority.Input, Array.Empty<object>());
	}

	private bool RestoreExternalWindowAfterGuide()
	{
		if (_guideReturnWindowHandle == IntPtr.Zero)
		{
			return false;
		}
		return XboxMetroLauncher.Platform.LinuxWindows.Activate((int)_guideReturnWindowHandle);
	}

	public void PrepareGuideReturnToDashboard()
	{
		_guideRestoreExternalWindow = false;
		_guideReturnWindowHandle = IntPtr.Zero;
		_guideReturnFocusElement = null;
		_restoreFocusAfterGuideClose = false;
	}

	private void StartBootVideo()
	{
		bool useBladesStartup = ShouldUseBladesStartupVideoAudio();
		string text = AppPaths.FindFile(System.IO.Path.Combine("Assets", "Boot", useBladesStartup ? "Blades Startup.mp4" : "Boot Screen.mp4"));
		if (!File.Exists(text))
		{
			SkipBootIntro();
		}
		else if (!EnsureBootBrowser())
		{
			SkipBootIntro();
		}
		else
		{
			StartBrowserBootPlayback(text, useBladesStartup);
		}
	}

	private bool EnsureBootBrowser()
	{
		_bootBrowser = XboxMetroLauncher.Platform.GstNative.EnsureInitialized();
		return _bootBrowser;
	}

	private void StartBrowserBootPlayback(string bootVideoPath, bool useNativeAudio)
	{
		try
		{
			BootVideoHost.Background = Brushes.White;
			BootVideoHost.Play(bootVideoPath, 1.0, muted: !useNativeAudio);
			if (!useNativeAudio)
			{
				_ = PlayBootIntroSoundAfterBrowserStartsAsync();
			}
			_bootStartedAt = DateTime.UtcNow;
			if (_bootStateTimer == null)
			{
				_bootStateTimer = new DispatcherTimer
				{
					Interval = TimeSpan.FromMilliseconds(250.0)
				};
			}
			_bootStateTimer.Tick -= BootStateTimer_OnTick;
			_bootStateTimer.Tick += BootStateTimer_OnTick;
			_bootStateTimer.Start();
		}
		catch
		{
			SkipBootIntro();
		}
	}

	private void PlayStartupSound()
	{
		if (!_startupInitializationComplete || ShouldRunBladesRunnerStartup())
		{
			return;
		}
		if (_startupSoundPlayed)
		{
			return;
		}
		_startupSoundPlayed = true;
		try
		{
			_audioService.Play("startup");
		}
		catch
		{
		}
	}

	private void PlayNotificationSound()
	{
		_ = Task.Run(delegate
		{
			try
			{
				_audioService.Play("notify-popup");
			}
			catch
			{
			}
		});
	}

	private void ScheduleStartupPrewarm()
	{
		if (_startupPrewarmScheduled)
		{
			return;
		}
		_startupPrewarmScheduled = true;
		_ = RunStartupPrewarmLaterAsync();
	}

	private async Task RunStartupPrewarmLaterAsync()
	{
		try
		{
			await Task.Delay(5000);
			await Dispatcher.UIThread.InvokeAsync((Action)PrewarmStartupUi, DispatcherPriority.ApplicationIdle);
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.RunStartupPrewarmLaterAsync");
		}
	}

	private void PrewarmStartupUi()
	{
		try
		{
			WarmUpGuideAudio();
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.PrewarmStartupUi");
		}
	}

	private async Task RunSignInToastSequenceAsync(string? line1 = null, string? line2 = null)
	{
		if (_signInToastSequenceActive)
		{
			return;
		}
		try
		{
			_signInToastSequenceActive = true;
			PrepareSignInToastText(line1, line2);
			SignInToast.Opacity = 0.0;
			SignInToastGlow.Opacity = 0.0;
			SignInToastGlowScale.ScaleX = 0.0;
			SignInToastPill.Opacity = 0.0;
			SignInToastPillScale.ScaleX = 0.0;
			SignInToastPillHighlight.Opacity = 0.0;
			SignInToastPillHighlightScale.ScaleX = 0.0;
			SignInToastIcon.Opacity = 0.0;
			SignInToastIconSphere.Opacity = 1.0;
			SignInToastIconAlert.Opacity = 0.0;
			SignInToastText.Opacity = 0.0;
			SignInToastScale.ScaleX = 1.0;
			SignInToastScale.ScaleY = 1.0;
			SignInToastTransform.Y = 0.0;
			SignInToastIconScale.ScaleX = 0.56;
			SignInToastIconScale.ScaleY = 0.56;
			await Task.Delay(120);
			CubicEase toastEase = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			};
			SignInToast.Opacity = 1.0;
			Task task = AnimateDoubleAsync(SignInToastIcon, UIElement.OpacityProperty, 0.0, 1.0, 150, toastEase);
			Task task2 = AnimateDoubleAsync(SignInToastIconScale, ScaleTransform.ScaleXProperty, 0.56, 1.04, 180, toastEase);
			Task task3 = AnimateDoubleAsync(SignInToastIconScale, ScaleTransform.ScaleYProperty, 0.56, 1.04, 180, toastEase);
			await Dispatcher.UIThread.InvokeAsync((Action)delegate
			{
			}, DispatcherPriority.Render);
			Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
			{
				PlayNotificationSound();
			}, DispatcherPriority.Background, Array.Empty<object>());
			await Task.WhenAll(task, task2, task3);
			StartSignInIconBlink();
			Task task4 = AnimateDoubleAsync(SignInToastIconScale, ScaleTransform.ScaleXProperty, 1.04, 1.0, 115, toastEase);
			Task task5 = AnimateDoubleAsync(SignInToastIconScale, ScaleTransform.ScaleYProperty, 1.04, 1.0, 115, toastEase);
			await Task.WhenAll(task4, task5);
			await Task.Delay(55);
			Task task6 = AnimateDoubleAsync(SignInToastGlow, UIElement.OpacityProperty, 0.0, 0.34, 135, toastEase);
			Task task7 = AnimateDoubleAsync(SignInToastGlowScale, ScaleTransform.ScaleXProperty, 0.0, 1.0, 265, toastEase);
			Task task8 = AnimateDoubleAsync(SignInToastPill, UIElement.OpacityProperty, 0.0, 1.0, 110, toastEase);
			Task task9 = AnimateDoubleAsync(SignInToastPillScale, ScaleTransform.ScaleXProperty, 0.0, 1.0, 265, toastEase);
			Task task10 = AnimateDoubleAsync(SignInToastPillHighlight, UIElement.OpacityProperty, 0.0, 0.0, 120, toastEase);
			Task task11 = AnimateDoubleAsync(SignInToastPillHighlightScale, ScaleTransform.ScaleXProperty, 0.0, 1.0, 265, toastEase);
			await Task.WhenAll(task6, task7, task8, task9, task10, task11);
			await Task.Delay(55);
			await AnimateDoubleAsync(SignInToastText, UIElement.OpacityProperty, 0.0, 1.0, 185, null);
			await Task.Delay(2450);
			CubicEase hideEase = new CubicEase
			{
				EasingMode = EasingMode.EaseIn
			};
			await AnimateDoubleAsync(SignInToastText, UIElement.OpacityProperty, 1.0, 0.0, 185, hideEase);
			await Task.Delay(150);
			StopSignInIconBlink();
			Task task12 = AnimateDoubleAsync(SignInToastGlowScale, ScaleTransform.ScaleXProperty, 1.0, 0.0, 320, hideEase);
			Task task13 = AnimateDoubleAsync(SignInToastGlow, UIElement.OpacityProperty, 0.34, 0.0, 270, hideEase);
			Task task14 = AnimateDoubleAsync(SignInToastPillScale, ScaleTransform.ScaleXProperty, 1.0, 0.0, 320, hideEase);
			Task task15 = AnimateDoubleAsync(SignInToastPill, UIElement.OpacityProperty, 1.0, 0.0, 300, hideEase);
			Task task16 = AnimateDoubleAsync(SignInToastPillHighlightScale, ScaleTransform.ScaleXProperty, 1.0, 0.0, 300, hideEase);
			Task task17 = AnimateDoubleAsync(SignInToastPillHighlight, UIElement.OpacityProperty, 0.0, 0.0, 230, hideEase);
			Task task18 = AnimateDoubleAsync(SignInToastIconScale, ScaleTransform.ScaleXProperty, 1.0, 0.52, 210, hideEase);
			Task task19 = AnimateDoubleAsync(SignInToastIconScale, ScaleTransform.ScaleYProperty, 1.0, 0.52, 210, hideEase);
			Task task20 = AnimateDoubleAsync(SignInToastIcon, UIElement.OpacityProperty, 1.0, 0.0, 210, hideEase);
			await Task.WhenAll(task12, task13, task14, task15, task16, task17, task18, task19, task20);
			await AnimateDoubleAsync(SignInToast, UIElement.OpacityProperty, 1.0, 0.0, 80, hideEase);
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.RunSignInToastSequenceAsync");
		}
		finally
		{
			_signInToastSequenceActive = false;
			StopSignInIconBlink();
			SignInToast.Opacity = 0.0;
			SignInToastGlow.Opacity = 0.0;
			SignInToastGlowScale.ScaleX = 0.0;
			SignInToastPill.Opacity = 0.0;
			SignInToastPillScale.ScaleX = 0.0;
			SignInToastPillHighlight.Opacity = 0.0;
			SignInToastPillHighlightScale.ScaleX = 0.0;
			SignInToastIcon.Opacity = 0.0;
			SignInToastIconSphere.Opacity = 0.0;
			SignInToastIconAlert.Opacity = 1.0;
			SignInToastText.Opacity = 0.0;
			SignInToastScale.ScaleX = 1.0;
			SignInToastScale.ScaleY = 1.0;
			SignInToastTransform.Y = 0.0;
			SignInToastIconScale.ScaleX = 0.56;
			SignInToastIconScale.ScaleY = 0.56;
		}
	}

	private void PrepareSignInToastText(string? line1 = null, string? line2 = null)
	{
		string text = _viewModel.Profile?.Gamertag?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "Player";
		}
		_viewModel.NotificationToastLine1 = string.IsNullOrWhiteSpace(line1) ? (text + " signed") : line1;
		_viewModel.NotificationToastLine2 = string.IsNullOrWhiteSpace(line2) ? "in to Xbox LIVE" : line2;
	}

	private void PlayBootIntroSound()
	{
		try
		{
			_audioService.Play("boot-intro");
		}
		catch
		{
		}
	}

	private async Task PlayBootIntroSoundAfterBrowserStartsAsync()
	{
		try
		{
			await Task.Delay(120);
			if (_viewModel.IsBooting && !_bootSkipped)
			{
				PlayBootIntroSound();
			}
		}
		catch
		{
		}
	}

	private void BootStateTimer_OnTick(object? sender, EventArgs e)
	{
		try
		{
			if (BootVideoHost.HasEnded || DateTime.UtcNow - _bootStartedAt > TimeSpan.FromSeconds(12.0))
			{
				SkipBootIntro();
			}
		}
		catch
		{
			if (DateTime.UtcNow - _bootStartedAt > TimeSpan.FromSeconds(12.0))
			{
				SkipBootIntro();
			}
		}
	}

	private void SkipBootIntro(bool userInitiated = false)
	{
		if (!_bootSkipped)
		{
			_bootSkipped = true;
			DispatcherTimer? bootStateTimer = _bootStateTimer;
			if (bootStateTimer != null)
			{
				bootStateTimer.Stop();
			}
			_audioService.Stop("boot-intro");
			if (userInitiated)
			{
				_audioService.Stop("startup");
				CleanupBootBrowser();
			}
			bool num = StartFakeLoadingIfReady(bootHandoff: true);
			if (!userInitiated)
			{
				ScheduleBootBrowserCleanup();
			}
			_viewModel.IsBooting = false;
			if (!num && _startupInitializationComplete)
			{
				PlayStartupSound();
				ScheduleStartupPrewarm();
				_ = RunSignInToastSequenceAsync();
			}
		}
	}

	private void ScheduleBootBrowserCleanup()
	{
		if (_bootBrowserCleanupScheduled)
		{
			return;
		}
		_bootBrowserCleanupScheduled = true;
		_ = CleanupBootBrowserLaterAsync();
	}

	private async Task CleanupBootBrowserLaterAsync()
	{
		try
		{
			await Task.Delay(2000);
			await Dispatcher.UIThread.InvokeAsync((Action)CleanupBootBrowser, DispatcherPriority.ApplicationIdle);
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.CleanupBootBrowserLaterAsync");
		}
	}

	private void CleanupBootBrowser()
	{
		try
		{
			_audioService.Stop("boot-intro");
		}
		catch
		{
		}
		DispatcherTimer? bootStateTimer = _bootStateTimer;
		if (bootStateTimer != null)
		{
			bootStateTimer.Stop();
		}
		if (_bootStateTimer != null)
		{
			_bootStateTimer.Tick -= BootStateTimer_OnTick;
		}
		try
		{
			BootVideoHost.Stop();
			BootVideoHost.ReleaseFrame();
		}
		catch
		{
		}
		_bootBrowser = false;
	}

	private async void UpdateYouTubeTvHost()
	{
		if (_viewModel.IsYouTubeTvOpen)
		{
			await EnsureYouTubeTvBrowserAsync();
		}
		else
		{
			CleanupYouTubeTvBrowser();
		}
	}

	private Task EnsureYouTubeTvBrowserAsync()
	{
		try
		{
			if (_youtubeTvBrowser == null)
			{
				_youtubeTvBrowser = new XboxMetroLauncher.Platform.YouTubeTvSession();
				_youtubeTvBrowser.Exited += (_, _) => Dispatcher.UIThread.Post(() =>
				{
					if (_viewModel.IsYouTubeTvOpen)
					{
						_viewModel.CloseYouTubeTvCommand.Execute(null);
					}
				});
			}
			if (!_youtubeTvNavigationStarted)
			{
				_youtubeTvNavigationStarted = true;
				if (!_youtubeTvBrowser.Start())
				{
					throw new InvalidOperationException("No supported browser (Firefox/Chromium/Chrome) found.");
				}
			}
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.EnsureYouTubeTvBrowserAsync");
			_youtubeTvNavigationStarted = false;
			_viewModel.IsYouTubeTvOpen = false;
			_viewModel.StatusMessage = "YouTube TV could not open";
		}
		return Task.CompletedTask;
	}

	private void CleanupYouTubeTvBrowser()
	{
		_youtubeTvNavigationStarted = false;
		XboxMetroLauncher.Platform.YouTubeTvSession? session = _youtubeTvBrowser;
		_youtubeTvBrowser = null;
		if (session != null)
		{
			Task.Run(session.Dispose);
		}
		Activate();
	}

	private bool HandleYouTubeTvInput(DashboardInputAction action)
	{
		XboxMetroLauncher.Platform.YouTubeTvSession? session = _youtubeTvBrowser;
		if (action == DashboardInputAction.Options || action == DashboardInputAction.Guide && session?.HasVirtualKeyboard != true)
		{
			_viewModel.CloseYouTubeTvCommand.Execute(null);
			return true;
		}
		ushort? key = action switch
		{
			DashboardInputAction.MoveLeft => XboxMetroLauncher.Platform.UInputKeyboard.KeyLeft,
			DashboardInputAction.MoveRight => XboxMetroLauncher.Platform.UInputKeyboard.KeyRight,
			DashboardInputAction.MoveUp => XboxMetroLauncher.Platform.UInputKeyboard.KeyUp,
			DashboardInputAction.MoveDown => XboxMetroLauncher.Platform.UInputKeyboard.KeyDown,
			DashboardInputAction.Activate => XboxMetroLauncher.Platform.UInputKeyboard.KeyEnter,
			DashboardInputAction.Back => XboxMetroLauncher.Platform.UInputKeyboard.KeyEsc,
			DashboardInputAction.Details => XboxMetroLauncher.Platform.UInputKeyboard.KeyBackspace,
			DashboardInputAction.Search => XboxMetroLauncher.Platform.UInputKeyboard.KeySlash,
			DashboardInputAction.PreviousTab => XboxMetroLauncher.Platform.UInputKeyboard.KeyPageUp,
			DashboardInputAction.NextTab => XboxMetroLauncher.Platform.UInputKeyboard.KeyPageDown,
			_ => null
		};
		if (key.HasValue && session != null)
		{
			Task.Run(() => session.SendKey(key.Value));
		}
		return true;
	}

	private bool StartFakeLoadingIfReady(bool bootHandoff = false)
	{
		if (_fakeLoadingStarted || !_startupInitializationComplete || (_viewModel.IsBooting && !bootHandoff) || (!_viewModel.Settings.EnableFakeLoading && !ShouldRunBladesRunnerStartup()))
		{
			return false;
		}
		_fakeLoadingStarted = true;
		RunFakeLoadingSequenceAsync(bootHandoff);
		return true;
	}

	private async Task RunFakeLoadingSequenceAsync(bool bootHandoff)
	{
		_ = 16;
		try
		{
			_isFakeLoadingActive = true;
			BladesLoadingChrome.Visibility = Visibility.Collapsed;
			FakeLoadingOverlay.Visibility = Visibility.Visible;
			FakeLoadingOverlay.Opacity = (bootHandoff ? 1 : 0);
			FakeLoadingIndicator.Opacity = 1.0;
			DashboardContentHost.Opacity = 0.0;
			DashboardStartupScale.ScaleX = 0.965;
			DashboardStartupScale.ScaleY = 0.965;
			DashboardStartupTranslate.Y = 18.0;
			PrepareSignInToastText();
			SignInToast.Opacity = 0.0;
			SignInToastGlow.Opacity = 0.0;
			SignInToastGlowScale.ScaleX = 0.0;
			SignInToastPill.Opacity = 0.0;
			SignInToastPillScale.ScaleX = 0.0;
			SignInToastPillHighlight.Opacity = 0.0;
			SignInToastPillHighlightScale.ScaleX = 0.0;
			SignInToastIcon.Opacity = 0.0;
			SignInToastIconSphere.Opacity = 1.0;
			SignInToastIconAlert.Opacity = 0.0;
			SignInToastText.Opacity = 0.0;
			SignInToastScale.ScaleX = 1.0;
			SignInToastScale.ScaleY = 1.0;
			SignInToastTransform.Y = 0.0;
			SignInToastIconScale.ScaleX = 0.56;
			SignInToastIconScale.ScaleY = 0.56;
			StartFakeLoadingRingAnimation();
			PrewarmStartupUi();
			if (!_viewModel.Settings.EnableFakeLoading) FakeLoadingOverlay.Visibility=Visibility.Collapsed;
			if (!bootHandoff && _viewModel.Settings.EnableFakeLoading)
			{
				await AnimateDoubleAsync(FakeLoadingOverlay, UIElement.OpacityProperty, 0.0, 1.0, 260, null);
			}
			if (_viewModel.Settings.EnableFakeLoading)
			{
				await Task.Delay(1150);
				await AnimateDoubleAsync(FakeLoadingIndicator, UIElement.OpacityProperty, 1.0, 0.0, 120, null);
				await Task.Delay(40);
			}
			CubicEase easing = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			};
			StopFakeLoadingRingAnimation();
			if (ShouldRunBladesRunnerStartup())
			{
				PrepareBladesRunnerStartup();
				Task loadingFade = AnimateDoubleAsync(FakeLoadingOverlay, UIElement.OpacityProperty, 1.0, 0.0, 140, easing);
				Task opening = AnimateBladesRunnerStartupAsync(easing);
				await Task.WhenAll(loadingFade, opening);
				FakeLoadingOverlay.Visibility = Visibility.Collapsed;
			}
			else
			{
				PlayStartupSound();
				Task task = AnimateDoubleAsync(DashboardContentHost, UIElement.OpacityProperty, 0.0, 1.0, 470, easing);
				Task task2 = AnimateDoubleAsync(DashboardStartupScale, ScaleTransform.ScaleXProperty, 0.965, 1.0, 470, easing);
				Task task3 = AnimateDoubleAsync(DashboardStartupScale, ScaleTransform.ScaleYProperty, 0.965, 1.0, 470, easing);
				Task task4 = AnimateDoubleAsync(DashboardStartupTranslate, TranslateTransform.YProperty, 18.0, 0.0, 470, easing);
				Task task5 = AnimateDoubleAsync(FakeLoadingOverlay, UIElement.OpacityProperty, 1.0, 0.0, 520, easing);
				await Task.WhenAll(task, task2, task3, task4, task5);
			}
			FakeLoadingOverlay.Visibility = Visibility.Collapsed;
			_viewModel.IsBooting = false;
			_isFakeLoadingActive = false;
			QueueFocusFirstButton();
			await Task.Delay(120);
			CubicEase toastEase = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			};
			SignInToast.Opacity = 1.0;
			Task task6 = AnimateDoubleAsync(SignInToastIcon, UIElement.OpacityProperty, 0.0, 1.0, 150, toastEase);
			Task task7 = AnimateDoubleAsync(SignInToastIconScale, ScaleTransform.ScaleXProperty, 0.56, 1.04, 180, toastEase);
			Task task8 = AnimateDoubleAsync(SignInToastIconScale, ScaleTransform.ScaleYProperty, 0.56, 1.04, 180, toastEase);
			await Dispatcher.UIThread.InvokeAsync((Action)delegate
			{
			}, DispatcherPriority.Render);
			Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
			{
				PlayNotificationSound();
			}, DispatcherPriority.Background, Array.Empty<object>());
			await Task.WhenAll(task6, task7, task8);
			StartSignInIconBlink();
			Task task9 = AnimateDoubleAsync(SignInToastIconScale, ScaleTransform.ScaleXProperty, 1.04, 1.0, 115, toastEase);
			Task task10 = AnimateDoubleAsync(SignInToastIconScale, ScaleTransform.ScaleYProperty, 1.04, 1.0, 115, toastEase);
			await Task.WhenAll(task9, task10);
			await Task.Delay(55);
			Task task11 = AnimateDoubleAsync(SignInToastGlow, UIElement.OpacityProperty, 0.0, 0.34, 135, toastEase);
			Task task12 = AnimateDoubleAsync(SignInToastGlowScale, ScaleTransform.ScaleXProperty, 0.0, 1.0, 265, toastEase);
			Task task13 = AnimateDoubleAsync(SignInToastPill, UIElement.OpacityProperty, 0.0, 1.0, 110, toastEase);
			Task task14 = AnimateDoubleAsync(SignInToastPillScale, ScaleTransform.ScaleXProperty, 0.0, 1.0, 265, toastEase);
			Task task15 = AnimateDoubleAsync(SignInToastPillHighlight, UIElement.OpacityProperty, 0.0, 0.0, 120, toastEase);
			Task task16 = AnimateDoubleAsync(SignInToastPillHighlightScale, ScaleTransform.ScaleXProperty, 0.0, 1.0, 265, toastEase);
			await Task.WhenAll(task11, task12, task13, task14, task15, task16);
			await Task.Delay(55);
			await AnimateDoubleAsync(SignInToastText, UIElement.OpacityProperty, 0.0, 1.0, 185, null);
			await Task.Delay(2450);
			CubicEase hideEase = new CubicEase
			{
				EasingMode = EasingMode.EaseIn
			};
			await AnimateDoubleAsync(SignInToastText, UIElement.OpacityProperty, 1.0, 0.0, 185, hideEase);
			await Task.Delay(150);
			StopSignInIconBlink();
			Task task17 = AnimateDoubleAsync(SignInToastGlowScale, ScaleTransform.ScaleXProperty, 1.0, 0.0, 320, hideEase);
			Task task18 = AnimateDoubleAsync(SignInToastGlow, UIElement.OpacityProperty, 0.34, 0.0, 270, hideEase);
			Task task19 = AnimateDoubleAsync(SignInToastPillScale, ScaleTransform.ScaleXProperty, 1.0, 0.0, 320, hideEase);
			Task task20 = AnimateDoubleAsync(SignInToastPill, UIElement.OpacityProperty, 1.0, 0.0, 300, hideEase);
			Task task21 = AnimateDoubleAsync(SignInToastPillHighlightScale, ScaleTransform.ScaleXProperty, 1.0, 0.0, 300, hideEase);
			Task task22 = AnimateDoubleAsync(SignInToastPillHighlight, UIElement.OpacityProperty, 0.0, 0.0, 230, hideEase);
			Task task23 = AnimateDoubleAsync(SignInToastIconScale, ScaleTransform.ScaleXProperty, 1.0, 0.52, 210, hideEase);
			Task task24 = AnimateDoubleAsync(SignInToastIconScale, ScaleTransform.ScaleYProperty, 1.0, 0.52, 210, hideEase);
			Task task25 = AnimateDoubleAsync(SignInToastIcon, UIElement.OpacityProperty, 1.0, 0.0, 210, hideEase);
			await Task.WhenAll(task17, task18, task19, task20, task21, task22, task23, task24, task25);
			await AnimateDoubleAsync(SignInToast, UIElement.OpacityProperty, 1.0, 0.0, 80, hideEase);
			ScheduleGuideAudioWarmup();
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.RunFakeLoadingSequenceAsync");
		}
		finally
		{
			StopFakeLoadingRingAnimation();
			StopSignInIconBlink();
			FakeLoadingIndicator.Opacity = 0.0;
			DashboardContentHost.Opacity = 1.0;
			DashboardStartupScale.ScaleX = 1.0;
			DashboardStartupScale.ScaleY = 1.0;
			DashboardStartupTranslate.Y = 0.0;
			ResetBladesRunnerStartup();
			SignInToast.Opacity = 0.0;
			SignInToastGlow.Opacity = 0.0;
			SignInToastGlowScale.ScaleX = 0.0;
			SignInToastPill.Opacity = 0.0;
			SignInToastPillScale.ScaleX = 0.0;
			SignInToastPillHighlight.Opacity = 0.0;
			SignInToastPillHighlightScale.ScaleX = 0.0;
			SignInToastIcon.Opacity = 0.0;
			SignInToastIconSphere.Opacity = 0.0;
			SignInToastIconAlert.Opacity = 1.0;
			SignInToastText.Opacity = 0.0;
			SignInToastScale.ScaleX = 1.0;
			SignInToastScale.ScaleY = 1.0;
			SignInToastTransform.Y = 0.0;
			SignInToastIconScale.ScaleX = 0.56;
			SignInToastIconScale.ScaleY = 0.56;
			FakeLoadingOverlay.Opacity = 0.0;
			FakeLoadingOverlay.Visibility = Visibility.Collapsed;
			_isFakeLoadingActive = false;
			QueueFocusFirstButton();
		}
	}

	private bool ShouldRunBladesRunnerStartup()
	{
		return _viewModel != null
			&& string.Equals(_viewModel.Settings.DashboardStyle, "Blades", StringComparison.OrdinalIgnoreCase);
	}

	private bool ShouldUseBladesStartupVideoAudio()
	{
		return _viewModel != null
			&& string.Equals(_viewModel.Settings.DashboardStartup, "Blades", StringComparison.OrdinalIgnoreCase);
	}

	private void PrepareBladesRunnerStartup()
	{
		DashboardContentHost.BeginAnimation(UIElement.OpacityProperty, null);
		DashboardContentHost.Opacity = 1.0;
		DashboardStartupScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
		DashboardStartupScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
		DashboardStartupTranslate.BeginAnimation(TranslateTransform.YProperty, null);
		DashboardStartupScale.ScaleX = 1.0;
		DashboardStartupScale.ScaleY = 1.0;
		DashboardStartupTranslate.Y = 0.0;

		BladesBootRunnerOverlay.Visibility = Visibility.Visible;
		BladesBootRunnerOverlay.Opacity = 1.0;
		BladesStartupGreenReveal.Visibility = Visibility.Visible;
		BladesStartupGreenReveal.BeginAnimation(UIElement.OpacityProperty, null);
		BladesStartupGreenReveal.Opacity = 1.0;
		BladesBootLeftRunnerImage.Opacity = 1.0;
		BladesBootRightRunnerImage.Opacity = 1.0;
		BladesBootLeftRunnerOpenImage.Opacity = 0.0;
		BladesBootRightRunnerOpenImage.Opacity = 0.0;
		BladesBootLeftRunnerTranslate.BeginAnimation(TranslateTransform.XProperty, null);
		BladesBootRightRunnerTranslate.BeginAnimation(TranslateTransform.XProperty, null);
		BladesBootLeftRunnerTranslate.X = 714.0 - 739.0;
		BladesBootRightRunnerTranslate.X = 1228.0 - 1203.0;

		BladesArtworkHost.BeginAnimation(UIElement.OpacityProperty, null);
		BladesContentCanvas.BeginAnimation(UIElement.OpacityProperty, null);
		BladesCenterHost.BeginAnimation(UIElement.OpacityProperty, null);
		BladesCenterHost.Opacity = 0.0;
		BladesArtworkHost.StartupElapsedSeconds = 0.0;
		BladesArtworkHost.StartupOpenProgress = 0.0;
		BladesArtworkHost.Opacity = 1.0;
		BladesContentCanvas.Opacity = 0.0;
	}

	private async Task AnimateBladesRunnerStartupAsync(IEasingFunction easing)
	{
		await Dispatcher.UIThread.InvokeAsync((Action)delegate
		{
		}, DispatcherPriority.Render);

		await RunBladesStartupTimelineAsync();
		BladesArtworkHost.StartupElapsedSeconds = 1.167;
		BladesArtworkHost.StartupOpenProgress = 1.0;
		ResetBladesRunnerStartup();
	}

	private async Task RunBladesStartupTimelineAsync()
	{
		_audioService.Play("dash_2ndLevelClose");
		(TimeSpan Time, double LeftEdge, double RightEdge)[] runnerKeys =
		{
			(TimeSpan.FromSeconds(0.000), 714.0, 1228.0),
			(TimeSpan.FromSeconds(1.0 / 30.0), 691.0, 1250.0),
			(TimeSpan.FromSeconds(2.0 / 30.0), 649.0, 1290.0),
			(TimeSpan.FromSeconds(3.0 / 30.0), 610.0, 1328.0),
			(TimeSpan.FromSeconds(4.0 / 30.0), 571.0, 1365.0),
			(TimeSpan.FromSeconds(5.0 / 30.0), 533.0, 1402.0),
			(TimeSpan.FromSeconds(6.0 / 30.0), 497.0, 1437.0),
			(TimeSpan.FromSeconds(7.0 / 30.0), 448.0, 1484.0),
			(TimeSpan.FromSeconds(8.0 / 30.0), 429.0, 1502.0),
			(TimeSpan.FromSeconds(9.0 / 30.0), 396.0, 1534.0),
			(TimeSpan.FromSeconds(10.0 / 30.0), 363.0, 1565.0),
			(TimeSpan.FromSeconds(11.0 / 30.0), 332.0, 1595.0),
			(TimeSpan.FromSeconds(12.0 / 30.0), 301.0, 1624.0),
			(TimeSpan.FromSeconds(13.0 / 30.0), 271.0, 1653.0),
			(TimeSpan.FromSeconds(14.0 / 30.0), 242.0, 1681.0),
			(TimeSpan.FromSeconds(15.0 / 30.0), 213.0, 1708.0),
			(TimeSpan.FromSeconds(16.0 / 30.0), 185.0, 1734.0),
			(TimeSpan.FromSeconds(17.0 / 30.0), 177.0, 1739.0),
			(TimeSpan.FromSeconds(20.0 / 30.0), 175.0, 1739.0),
			(TimeSpan.FromSeconds(23.0 / 30.0), 169.0, 1741.0),
			(TimeSpan.FromSeconds(26.0 / 30.0), 160.0, 1743.0),
			(TimeSpan.FromSeconds(29.0 / 30.0), 148.0, 1746.0),
			(TimeSpan.FromSeconds(32.0 / 30.0), 128.0, 1749.0),
			(TimeSpan.FromSeconds(1.167), 105.0, 1751.0),
		};

		TimeSpan totalDuration = TimeSpan.FromSeconds(1.167);
		TimeSpan? firstRenderTime = null;
		TimeSpan? previousRenderTime = null;
		TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
		EventHandler handler = (_, args) =>
		{
			if (args is not RenderingEventArgs rendering || previousRenderTime == rendering.RenderingTime)
			{
				return;
			}
			previousRenderTime = rendering.RenderingTime;
			firstRenderTime ??= rendering.RenderingTime;
			TimeSpan elapsed = rendering.RenderingTime - firstRenderTime.Value;
			if (elapsed > totalDuration) elapsed = totalDuration;
			try
			{
				(double leftEdge, double rightEdge) = SampleRunnerEdges(runnerKeys, elapsed);
				ApplyBladesStartupFrame(elapsed.TotalSeconds, leftEdge, rightEdge);
				if (elapsed >= totalDuration) completion.TrySetResult();
			}
			catch (Exception exception)
			{
				completion.TrySetException(exception);
			}
		};
		CompositionTarget.Rendering += handler;
		try
		{
			await completion.Task;
		}
		finally
		{
			CompositionTarget.Rendering -= handler;
		}
	}

	private void ApplyBladesStartupFrame(double seconds, double leftEdge, double rightEdge)
	{
		const double leftRunnerInnerEdge = 739.0;
		const double rightRunnerInnerEdge = 1203.0;
		BladesBootLeftRunnerTranslate.X = leftEdge - leftRunnerInnerEdge;
		BladesBootRightRunnerTranslate.X = rightEdge - rightRunnerInnerEdge;

		double centerProgress = SmoothStep((seconds - 0.36) / 0.30);
		double greenProgress = SampleCurve(seconds, (0.000, 0.18), (0.100, 0.26), (0.200, 0.36), (0.300, 0.55), (0.400, 0.72), (0.500, 0.92), (0.567, 1.0));
		double sideProgress = SmoothStep((seconds - 0.533) / 0.634);
		double contentProgress = SampleCurve(seconds, (0.967, 0.0), (1.000, 0.12), (1.033, 0.36), (1.067, 0.57), (1.100, 0.83), (1.133, 1.0));

		BladesCenterHost.Opacity = centerProgress;
		BladesStartupGreenReveal.Opacity = 1.0 - greenProgress;
		BladesArtworkHost.StartupElapsedSeconds = seconds;
		BladesArtworkHost.StartupOpenProgress = sideProgress;
		BladesContentCanvas.Opacity = contentProgress;
		// Keep the curved shutters in front while the intact blades emerge.
		// The settled runners are already covered by the stack before this fade.
		BladesBootRunnerOverlay.Opacity = 1.0 - SmoothStep((seconds - 1.033) / 0.134);

		BladesBootLeftRunnerImage.Opacity = 1.0;
		BladesBootRightRunnerImage.Opacity = 1.0;
		BladesBootLeftRunnerOpenImage.Opacity = 0.0;
		BladesBootRightRunnerOpenImage.Opacity = 0.0;
	}

	private static (double LeftEdge, double RightEdge) SampleRunnerEdges(
		(TimeSpan Time, double LeftEdge, double RightEdge)[] keys,
		TimeSpan elapsed)
	{
		if (elapsed <= keys[0].Time)
		{
			return (keys[0].LeftEdge, keys[0].RightEdge);
		}

		for (int i = 1; i < keys.Length; i++)
		{
			if (elapsed <= keys[i].Time)
			{
				double local = (elapsed - keys[i - 1].Time).TotalSeconds / (keys[i].Time - keys[i - 1].Time).TotalSeconds;
				return (
					Lerp(keys[i - 1].LeftEdge, keys[i].LeftEdge, local),
					Lerp(keys[i - 1].RightEdge, keys[i].RightEdge, local));
			}
		}

		return (keys[^1].LeftEdge, keys[^1].RightEdge);
	}

	private static double SmoothStep(double value)
	{
		value = Math.Clamp(value, 0.0, 1.0);
		return value * value * (3.0 - (2.0 * value));
	}

	private static double Lerp(double from, double to, double progress)
	{
		return from + ((to - from) * progress);
	}

	private static double SampleCurve(double seconds, params (double Time, double Value)[] samples)
	{
		if (samples.Length == 0)
		{
			return 0.0;
		}

		if (seconds <= samples[0].Time)
		{
			return samples[0].Value;
		}

		for (int i = 1; i < samples.Length; i++)
		{
			if (seconds <= samples[i].Time)
			{
				double span = samples[i].Time - samples[i - 1].Time;
				double local = span <= 0.0 ? 1.0 : (seconds - samples[i - 1].Time) / span;
				return Lerp(samples[i - 1].Value, samples[i].Value, local);
			}
		}

		return samples[^1].Value;
	}

	private void ResetBladesRunnerStartup()
	{
		BladesBootLeftRunnerTranslate.BeginAnimation(TranslateTransform.XProperty, null);
		BladesBootRightRunnerTranslate.BeginAnimation(TranslateTransform.XProperty, null);
		BladesBootRunnerOverlay.BeginAnimation(UIElement.OpacityProperty, null);
		BladesStartupGreenReveal.BeginAnimation(UIElement.OpacityProperty, null);
		BladesCenterHost.BeginAnimation(UIElement.OpacityProperty, null);
		BladesContentCanvas.BeginAnimation(UIElement.OpacityProperty, null);
		BladesArtworkHost.BeginAnimation(UIElement.OpacityProperty, null);
		BladesBootLeftRunnerTranslate.X = 0.0;
		BladesBootRightRunnerTranslate.X = 0.0;
		BladesBootLeftRunnerImage.Opacity = 1.0;
		BladesBootRightRunnerImage.Opacity = 1.0;
		BladesBootLeftRunnerOpenImage.Opacity = 0.0;
		BladesBootRightRunnerOpenImage.Opacity = 0.0;
		BladesBootRunnerOverlay.Opacity = 0.0;
		BladesBootRunnerOverlay.Visibility = Visibility.Collapsed;
		BladesStartupGreenReveal.Opacity = 0.0;
		BladesStartupGreenReveal.Visibility = Visibility.Collapsed;
		BladesCenterHost.Opacity = 1.0;
		BladesArtworkHost.StartupElapsedSeconds = 1.167;
		BladesArtworkHost.StartupOpenProgress = 1.0;
		BladesArtworkHost.Opacity = 1.0;
		BladesContentCanvas.Opacity = 1.0;
	}

	private void StartFakeLoadingRingAnimation()
	{
		DoubleAnimation animation = new DoubleAnimation(0.0, 360.0, TimeSpan.FromMilliseconds(760.0))
		{
			RepeatBehavior = RepeatBehavior.Forever
		};
		FakeLoadingRingRotate.BeginAnimation(RotateTransform.AngleProperty, animation);
	}

	private void StopFakeLoadingRingAnimation()
	{
		FakeLoadingRingRotate.BeginAnimation(RotateTransform.AngleProperty, null);
	}

	private async Task TransitionBladesSubmenuAsync(string target, Action change)
	{
		if (_viewModel.IsBladesMenuTransitioning) return;
		_viewModel.IsBladesMenuTransitioning = true;
		bool wasOpen = _viewModel.IsBladesSubmenuOpen;
		bool opening = target.Length > 0;
		bool enteringFromRoot = !wasOpen && opening;
		try
		{
			BladesContentCanvas.IsHitTestVisible = false;
			await AnimateDoubleAsync(BladesContentCanvas, UIElement.OpacityProperty, 1, 0, 100, null);
			if (wasOpen != opening)
			{
				BladesArtworkHost.Visibility = Visibility.Visible;
				if (opening) change();
				await AnimateDoubleAsync(BladesArtworkHost, Controls.BladesArtworkHost.SubmenuExpansionProperty,
					opening ? 0 : 1, opening ? 1 : 0, 300, new CubicEase { EasingMode=EasingMode.EaseOut });
				if (!opening) change();
			}
			else { await Task.Delay(100); change(); }
			if (enteringFromRoot && _viewModel.Settings.EnableFakeLoading && ShouldRunBladesSubmenuFakeLoading(target))
			{
				BladesLoadingTitle.Text=target=="GamesLibrary" ? "My Games" : _viewModel.ActiveBladesTitle;
				await RunMenuFakeLoadingSequenceAsync();
				BladesLoadingTitle.ClearValue(TextBlock.TextProperty);
			}
			await AnimateDoubleAsync(BladesContentCanvas, UIElement.OpacityProperty, 0, 1, 180, null);
		}
		catch (Exception ex) { App.LogException(ex,"Blades.SubmenuTransition"); }
		finally
		{
			BladesArtworkHost.ClearValue(Wpf.VisibilityProperty);
			BladesArtworkHost.SubmenuExpansion = _viewModel.IsBladesSubmenuOpen ? 1 : 0;
			BladesArtworkHost.BeginAnimation(Controls.BladesArtworkHost.SubmenuExpansionProperty,null);
			BladesContentCanvas.BeginAnimation(UIElement.OpacityProperty,null);
			BladesContentCanvas.Opacity=1;
			BladesContentCanvas.IsHitTestVisible=true;
			_viewModel.IsBladesMenuTransitioning=false;
			QueueBladesMenuFocus();
		}
	}

	private bool ShouldRunBladesSubmenuFakeLoading(string target)
	{
		string kind = target switch
		{
			"GamesLibraryRoot" or "GamesLibrary" => "games",
			"Achievements" => "achievements",
			"PlayedGames" => "played-games",
			_ => string.Empty
		};
		return kind.Length > 0 && ShouldRunMenuFakeLoading(kind);
	}

	private bool MaybeRunMenuFakeLoading(string propertyName)
	{
		if (!_viewModel.Settings.EnableFakeLoading)
		{
			return false;
		}
		string kind;
		if (propertyName == "IsLauncherSettingsOpen" && _viewModel.IsLauncherSettingsOpen)
		{
			kind = "settings";
		}
		else if (propertyName == "IsMyGamesOpen" && _viewModel.IsMyGamesOpen)
		{
			if (string.Equals(_viewModel.LibraryMenuTitle, "My Apps", StringComparison.OrdinalIgnoreCase))
			{
				kind = "apps";
			}
			else if (string.Equals(_viewModel.LibraryMenuTitle, "My Games", StringComparison.OrdinalIgnoreCase))
			{
				kind = "games";
			}
			else
			{
				return false;
			}
		}
		else
		{
			return false;
		}
		if (!ShouldRunMenuFakeLoading(kind))
		{
			return false;
		}
		Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
		{
			if (GetActiveOverlay() is FrameworkElement frameworkElement)
			{
				frameworkElement.BeginAnimation(UIElement.OpacityProperty, null);
				frameworkElement.Opacity = 0.0;
			}
			_ = RunMenuFakeLoadingSequenceAsync();
		}, DispatcherPriority.Loaded, Array.Empty<object>());
		return true;
	}

	private bool ShouldRunMenuFakeLoading(string kind)
	{
		switch (kind)
		{
		case "settings":
			_settingsOpenCount++;
			return _settingsOpenCount % 4 == 0;
		case "games":
			_gamesOpenCount++;
			return _gamesOpenCount % 4 == 0;
		case "achievements":
			_achievementsOpenCount++;
			return _achievementsOpenCount % 4 == 0;
		case "played-games":
			_playedGamesOpenCount++;
			return _playedGamesOpenCount % 4 == 0;
		case "music":
			_musicOpenCount++;
			return _musicOpenCount % 4 == 0;
		case "apps":
			_appsOpenCount++;
			return _appsOpenCount % 4 == 0;
		default:
			return false;
		}
	}

	private async Task RunMenuFakeLoadingSequenceAsync()
	{
		if (_isFakeLoadingActive || _isMenuFakeLoadingActive || _viewModel.IsBooting)
		{
			return;
		}
		try
		{
			_isMenuFakeLoadingActive = true;
			BladesLoadingChrome.Visibility = Visibility.Visible;
			FakeLoadingOverlay.BeginAnimation(UIElement.OpacityProperty, null);
			FakeLoadingIndicator.BeginAnimation(UIElement.OpacityProperty, null);
			FakeLoadingOverlay.Visibility = Visibility.Visible;
			FakeLoadingOverlay.Opacity = 1.0;
			FakeLoadingIndicator.Opacity = 1.0;
			StartFakeLoadingRingAnimation();
			await Task.Delay(1080);
			CubicEase easing = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			};
			Task indicatorFade = AnimateDoubleAsync(FakeLoadingIndicator, UIElement.OpacityProperty, 1.0, 0.0, 180, easing);
			Task overlayFade = AnimateDoubleAsync(FakeLoadingOverlay, UIElement.OpacityProperty, 1.0, 0.0, 320, easing);
			await Task.WhenAll(indicatorFade, overlayFade);
			AnimateActiveOverlayIn();
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.RunMenuFakeLoadingSequenceAsync");
		}
		finally
		{
			StopFakeLoadingRingAnimation();
			FakeLoadingIndicator.Opacity = 0.0;
			FakeLoadingOverlay.Opacity = 0.0;
			FakeLoadingOverlay.Visibility = Visibility.Collapsed;
			_isMenuFakeLoadingActive = false;
		}
	}

	private void StartSignInIconBlink()
	{
		DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = new DoubleAnimationUsingKeyFrames
		{
			Duration = TimeSpan.FromMilliseconds(2160.0),
			RepeatBehavior = RepeatBehavior.Forever,
			FillBehavior = FillBehavior.HoldEnd
		};
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(960.0))));
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1080.0))));
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2040.0))));
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2160.0))));
		DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames2 = new DoubleAnimationUsingKeyFrames
		{
			Duration = TimeSpan.FromMilliseconds(2160.0),
			RepeatBehavior = RepeatBehavior.Forever,
			FillBehavior = FillBehavior.HoldEnd
		};
		doubleAnimationUsingKeyFrames2.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
		doubleAnimationUsingKeyFrames2.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(960.0))));
		doubleAnimationUsingKeyFrames2.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1080.0))));
		doubleAnimationUsingKeyFrames2.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2040.0))));
		doubleAnimationUsingKeyFrames2.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2160.0))));
		SignInToastIconSphere.BeginAnimation(UIElement.OpacityProperty, doubleAnimationUsingKeyFrames);
		SignInToastIconAlert.BeginAnimation(UIElement.OpacityProperty, doubleAnimationUsingKeyFrames2);
	}

	private void StopSignInIconBlink()
	{
		SignInToastIconSphere.BeginAnimation(UIElement.OpacityProperty, null);
		SignInToastIconAlert.BeginAnimation(UIElement.OpacityProperty, null);
		SignInToastIconSphere.Opacity = 0.0;
		SignInToastIconAlert.Opacity = 1.0;
	}

	private static Task AnimateDoubleAsync(IAnimatable target, DependencyProperty property, double from, double to, int milliseconds, IEasingFunction? easing)
	{
		TaskCompletionSource completion = new TaskCompletionSource();
		DoubleAnimation doubleAnimation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
		{
			EasingFunction = easing,
			FillBehavior = FillBehavior.HoldEnd
		};
		doubleAnimation.Completed += delegate
		{
			completion.TrySetResult();
		};
		target.BeginAnimation(property, doubleAnimation);
		return completion.Task;
	}

	private static async Task DelayedAnimateDoubleAsync(IAnimatable target, DependencyProperty property, double from, double to, int milliseconds, IEasingFunction? easing, int delayMilliseconds)
	{
		if (delayMilliseconds > 0)
		{
			await Task.Delay(delayMilliseconds);
		}

		await AnimateDoubleAsync(target, property, from, to, milliseconds, easing);
	}

	private void MusicFullscreenHint_OnMouseLeftButtonUp(object sender, PointerReleasedEventArgs e)
	{
		if (_viewModel.OpenMusicVisualizerFullscreenCommand.CanExecute(null))
		{
			_viewModel.OpenMusicVisualizerFullscreenCommand.Execute(null);
			e.Handled = true;
		}
	}

	private void MusicFolderLink_OnPreviewMouseLeftButtonUp(object sender, PointerReleasedEventArgs e)
	{
		if (_viewModel.OpenMusicFolderCommand.CanExecute(null))
		{
			_viewModel.OpenMusicFolderCommand.Execute(null);
			e.Handled = true;
		}
	}

	private void SettingsOption_OnFocusOrMouseEnter(object sender, PointerEventArgs e) => SettingsOption_OnFocusOrMouseEnter(sender, (RoutedEventArgs)e);

	private void SettingsOption_OnFocusOrMouseEnter(object sender, GotFocusEventArgs e) => SettingsOption_OnFocusOrMouseEnter(sender, (RoutedEventArgs)e);

	private void SettingsOption_OnFocusOrMouseEnter(object sender, RoutedEventArgs e)
	{
		if (sender is FrameworkElement { Tag: string tag } && !string.IsNullOrWhiteSpace(tag))
		{
			if (tag.Count((char character) => character == '|') >= 2)
			{
				string[] array = tag.Split('|', 3);
				UpdateSettingsDescription(array[1], array[2]);
			}
			else
			{
				string[] array2 = tag.Split('|', 2);
				UpdateSettingsDescription(array2[0], (array2.Length > 1) ? array2[1] : string.Empty);
			}
		}
	}

	private void AudioOutputDeviceComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (sender is Avalonia.Controls.ComboBox { SelectedItem: string selectedDevice } && !string.IsNullOrWhiteSpace(selectedDevice))
		{
			_viewModel.AudioOutputDeviceName = selectedDevice;
		}
	}

	private void SettingsCategory_OnClick(object sender, RoutedEventArgs e)
	{
		if (!(sender is FrameworkElement { Tag: string tag }))
		{
			return;
		}
		string[] array = tag.Split('|', 3);
		if (array.Length < 3)
		{
			return;
		}
		FrameworkElement frameworkElement2 = array[0] switch
		{
			"console" => SystemSettingsConsolePanel, 
			"dashboard" => SystemSettingsDashboardPanel, 
			"games" => SystemSettingsGamesPanel, 
			"audio" => SystemSettingsAudioPanel, 
			"data" => SystemSettingsDataPanel, 
			_ => null, 
		};
		if (frameworkElement2 != null)
		{
			_audioService.Play("settings-box");
			if (string.Equals(array[0], "audio", StringComparison.OrdinalIgnoreCase))
			{
				_viewModel.RefreshAudioOutputDevices();
			}
			OpenSystemSettingsPanel(frameworkElement2, array[1], array[2]);
		}
	}

	private void SettingsBackToCategories_OnClick(object sender, RoutedEventArgs e)
	{
		_audioService.Play("menu-out");
		ShowSystemSettingsCategories();
	}

	private void OpenSystemSettingsPanel(FrameworkElement panel, string title, string description)
	{
		SystemSettingsCategoryPanel.Visibility = Visibility.Collapsed;
		foreach (FrameworkElement systemSettingsPanel in GetSystemSettingsPanels())
		{
			systemSettingsPanel.Visibility = ((systemSettingsPanel != panel) ? Visibility.Collapsed : Visibility.Visible);
		}
		_activeSystemSettingsPanel = panel;
		SettingsHeaderTitle.Text = title;
		UpdateSettingsDescription(title, description);
		panel.BeginAnimation(UIElement.OpacityProperty, null);
		panel.Opacity = 1.0;
		EnsureOverlayTranslateTransform(panel).X = 0.0;
		Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
		{
			TryFocus(FindFocusableControl((DependencyObject?)(object)panel));
		}, DispatcherPriority.Background, Array.Empty<object>());
	}

	private void ShowSystemSettingsCategories()
	{
		foreach (FrameworkElement systemSettingsPanel in GetSystemSettingsPanels())
		{
			systemSettingsPanel.Visibility = Visibility.Collapsed;
		}
		_activeSystemSettingsPanel = null;
		SystemSettingsCategoryPanel.Visibility = Visibility.Visible;
		SettingsHeaderTitle.Text = "System Settings";
		UpdateSettingsDescription("System Settings", "Choose a settings category.");
		SystemSettingsCategoryPanel.BeginAnimation(UIElement.OpacityProperty, null);
		SystemSettingsCategoryPanel.Opacity = 1.0;
		EnsureOverlayTranslateTransform(SystemSettingsCategoryPanel).X = 0.0;
		Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
		{
			TryFocus(FindFocusableControl((DependencyObject?)(object)SystemSettingsCategoryPanel));
		}, DispatcherPriority.Background, Array.Empty<object>());
	}

	private IEnumerable<FrameworkElement> GetSystemSettingsPanels()
	{
		yield return SystemSettingsConsolePanel;
		yield return SystemSettingsDashboardPanel;
		yield return SystemSettingsGamesPanel;
		yield return SystemSettingsAudioPanel;
		yield return SystemSettingsDataPanel;
	}

	private void UpdateSettingsDescription(string title, string description)
	{
		SettingsDescriptionTitle.Text = title;
		SettingsDescriptionText.Text = description;
	}

	private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == "IsBladesSubmenuOpen" && !_viewModel.IsBladesSubmenuOpen && !_viewModel.IsBladesMenuTransitioning)
		{
			BladesArtworkHost.BeginAnimation(Controls.BladesArtworkHost.SubmenuExpansionProperty,null);
			BladesArtworkHost.SubmenuExpansion=0;
		}
		if (e.PropertyName == "CurrentTab")
		{
			AnimateTabChange();
			UpdateThemeBackgroundVisual();
			UpdateBingBackgroundVisual();
			QueueFocusFirstButton();
			return;
		}
		if (e.PropertyName == "SelectedBladesMenuIndex" || e.PropertyName == "ActiveBladesMenuIndex" || e.PropertyName == "ActiveBladesMenuItems")
		{
			if (!_viewModel.IsBladesAchievementsSubmenu)
			{
				QueueBladesMenuFocus();
			}
			return;
		}
		if (e.PropertyName == "IsBladesAchievementsSubmenu" || e.PropertyName == "IsBladesAchievementListFocused" || e.PropertyName == "SelectedBladesAchievementGameIndex" || e.PropertyName == "SelectedBladesAchievementIndex" || e.PropertyName == "BladesAchievementGameItems" || e.PropertyName == "BladesAchievementItems")
		{
			QueueBladesAchievementsFocus();
			return;
		}
		if (e.PropertyName == "IsYouTubeTvOpen")
		{
			UpdateYouTubeTvHost();
			UpdateThemeBackgroundVisual();
			UpdateBingBackgroundVisual();
			return;
		}
		bool flag;
		switch (e.PropertyName)
		{
		case "IsDetailsOpen":
		case "IsMyGamesOpen":
		case "IsLauncherSettingsOpen":
		case "IsProfileEditorOpen":
		case "IsThemeMenuOpen":
		case "IsThemeCreatorOpen":
		case "IsDashboardCustomizerOpen":
		case "IsSteamSetupOpen":
		case "IsMusicPlayerOpen":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			if (e.PropertyName == "IsLauncherSettingsOpen" && _viewModel.IsLauncherSettingsOpen && !_viewModel.IsDashboardCustomizerOpen && !_viewModel.IsThemeCreatorOpen && !_viewModel.IsSteamSetupOpen)
			{
				ShowSystemSettingsCategories();
			}
			if (!MaybeRunMenuFakeLoading(e.PropertyName))
			{
				Dispatcher.UIThread.BeginInvoke((Delegate)new Action(AnimateActiveOverlayIn), DispatcherPriority.Loaded, Array.Empty<object>());
			}
			UpdateThemeBackgroundVisual();
			UpdateBingBackgroundVisual();
			QueueFocusFirstButton();
		}
		else if (e.PropertyName == "CurrentThemeBackgroundPath")
		{
			UpdateThemeBackgroundVisual();
		}
		else if (e.PropertyName == "SelectedGameDetailsTabKey")
		{
			AnimateGameDetailsTabChange();
			QueueGameDetailsTileFocus();
		}
		else if (e.PropertyName == "SelectedMusicBrowserResultItem")
		{
			Dispatcher.UIThread.BeginInvoke((Delegate)(Action)ScrollMusicBrowserResultIntoView, DispatcherPriority.Loaded, Array.Empty<object>());
		}
		else if (e.PropertyName == "CurrentMusicTrack")
		{
			Dispatcher.UIThread.BeginInvoke((Delegate)(Action)ScrollCurrentMusicTrackIntoView, DispatcherPriority.Loaded, Array.Empty<object>());
		}
		else if (e.PropertyName == "SelectedMusicTrack")
		{
			Dispatcher.UIThread.BeginInvoke((Delegate)(Action)ScrollCurrentMusicTrackIntoView, DispatcherPriority.Loaded, Array.Empty<object>());
		}
	}

	private void UpdateThemeBackgroundVisual(bool animate = true)
	{
		try
		{
			string currentThemeBackgroundPath = _viewModel.CurrentThemeBackgroundPath;
			if (string.Equals(_appliedThemeBackgroundPath, currentThemeBackgroundPath, StringComparison.OrdinalIgnoreCase) && ThemeBackgroundLayer.Visibility == Visibility.Visible == !string.IsNullOrWhiteSpace(currentThemeBackgroundPath))
			{
				return;
			}
			ThemeBackgroundLayer.BeginAnimation(UIElement.OpacityProperty, null);
			UltraWideThemeBackgroundLayer.BeginAnimation(UIElement.OpacityProperty, null);
			if (string.IsNullOrWhiteSpace(currentThemeBackgroundPath))
			{
				_appliedThemeBackgroundPath = string.Empty;
				if (!animate)
				{
					ThemeBackgroundLayer.Opacity = 0.0;
					ThemeBackgroundLayer.Visibility = Visibility.Collapsed;
					ThemeBackgroundImage.Source = null;
					UltraWideThemeBackgroundLayer.Opacity = 0.0;
					UltraWideThemeBackgroundLayer.Visibility = Visibility.Collapsed;
					UltraWideThemeBackgroundImage.Source = null;
					return;
				}
				DoubleAnimation doubleAnimation = new DoubleAnimation(ThemeBackgroundLayer.Opacity, 0.0, TimeSpan.FromMilliseconds(260.0))
				{
					EasingFunction = new SineEase
					{
						EasingMode = EasingMode.EaseOut
					}
				};
				doubleAnimation.Completed += delegate
				{
					ThemeBackgroundLayer.Visibility = Visibility.Collapsed;
					ThemeBackgroundImage.Source = null;
					UltraWideThemeBackgroundLayer.Visibility = Visibility.Collapsed;
					UltraWideThemeBackgroundImage.Source = null;
				};
				ThemeBackgroundLayer.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
				UltraWideThemeBackgroundLayer.BeginAnimation(UIElement.OpacityProperty, doubleAnimation.Clone());
				return;
			}
			BitmapSource decodedImage = ImageCacheService.GetDecodedImage(AppPathResolver.Resolve(currentThemeBackgroundPath), 1920);
			if (decodedImage == null)
			{
				_appliedThemeBackgroundPath = string.Empty;
				ThemeBackgroundLayer.Opacity = 0.0;
				ThemeBackgroundLayer.Visibility = Visibility.Collapsed;
				ThemeBackgroundImage.Source = null;
				UltraWideThemeBackgroundLayer.Opacity = 0.0;
				UltraWideThemeBackgroundLayer.Visibility = Visibility.Collapsed;
				UltraWideThemeBackgroundImage.Source = null;
				return;
			}
			_appliedThemeBackgroundPath = currentThemeBackgroundPath;
			ThemeBackgroundLayer.Visibility = Visibility.Visible;
			ThemeBackgroundImage.Source = decodedImage;
			UltraWideThemeBackgroundLayer.Visibility = Visibility.Visible;
			UltraWideThemeBackgroundImage.Source = decodedImage;
			if (!animate)
			{
				ThemeBackgroundLayer.Opacity = 1.0;
				UltraWideThemeBackgroundLayer.Opacity = 1.0;
				return;
			}
			ThemeBackgroundLayer.Opacity = 0.0;
			UltraWideThemeBackgroundLayer.Opacity = 0.0;
			ThemeBackgroundLayer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(320.0))
			{
				EasingFunction = new SineEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
			UltraWideThemeBackgroundLayer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(320.0))
			{
				EasingFunction = new SineEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.UpdateThemeBackgroundVisual");
		}
	}

	private void UpdateBingBackgroundVisual(bool animate = true)
	{
		try
		{
			bool flag = string.Equals(_viewModel.CurrentTab?.Key, "bing", StringComparison.OrdinalIgnoreCase);
			if (!flag && string.IsNullOrEmpty(_appliedBingBackgroundPath) && BingBackgroundLayer.Visibility != Visibility.Visible)
			{
				return;
			}
			string text = (flag ? AppPaths.ResolvePath(BingBackgroundRelativePath) : string.Empty);
			bool flag2 = BingBackgroundLayer.Visibility == Visibility.Visible;
			if (string.Equals(_appliedBingBackgroundPath, text, StringComparison.OrdinalIgnoreCase) && flag2 == flag)
			{
				return;
			}
			BingBackgroundLayer.BeginAnimation(UIElement.OpacityProperty, null);
			UltraWideBingBackgroundLayer.BeginAnimation(UIElement.OpacityProperty, null);
			if (string.IsNullOrWhiteSpace(text))
			{
				_appliedBingBackgroundPath = string.Empty;
				if (!animate)
				{
					BingBackgroundLayer.Opacity = 0.0;
					BingBackgroundLayer.Visibility = Visibility.Collapsed;
					BingBackgroundImage.Source = null;
					UltraWideBingBackgroundLayer.Opacity = 0.0;
					UltraWideBingBackgroundLayer.Visibility = Visibility.Collapsed;
					UltraWideBingBackgroundImage.Source = null;
					return;
				}
				DoubleAnimation doubleAnimation = new DoubleAnimation(BingBackgroundLayer.Opacity, 0.0, TimeSpan.FromMilliseconds(300.0))
				{
					EasingFunction = new SineEase
					{
						EasingMode = EasingMode.EaseOut
					}
				};
				doubleAnimation.Completed += delegate
				{
					BingBackgroundLayer.Visibility = Visibility.Collapsed;
					BingBackgroundImage.Source = null;
					UltraWideBingBackgroundLayer.Visibility = Visibility.Collapsed;
					UltraWideBingBackgroundImage.Source = null;
				};
				BingBackgroundLayer.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
				UltraWideBingBackgroundLayer.BeginAnimation(UIElement.OpacityProperty, doubleAnimation.Clone());
				return;
			}
			BitmapSource decodedImage = ImageCacheService.GetDecodedImage(text, 1920);
			if (decodedImage == null)
			{
				_appliedBingBackgroundPath = string.Empty;
				BingBackgroundLayer.Opacity = 0.0;
				BingBackgroundLayer.Visibility = Visibility.Collapsed;
				BingBackgroundImage.Source = null;
				UltraWideBingBackgroundLayer.Opacity = 0.0;
				UltraWideBingBackgroundLayer.Visibility = Visibility.Collapsed;
				UltraWideBingBackgroundImage.Source = null;
				return;
			}
			_appliedBingBackgroundPath = text;
			BingBackgroundLayer.Visibility = Visibility.Visible;
			BingBackgroundImage.Source = decodedImage;
			UltraWideBingBackgroundLayer.Visibility = Visibility.Visible;
			UltraWideBingBackgroundImage.Source = decodedImage;
			if (!animate)
			{
				BingBackgroundLayer.Opacity = 1.0;
				UltraWideBingBackgroundLayer.Opacity = 1.0;
				return;
			}
			BingBackgroundLayer.Opacity = 0.0;
			UltraWideBingBackgroundLayer.Opacity = 0.0;
			BingBackgroundLayer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(320.0))
			{
				EasingFunction = new SineEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
			UltraWideBingBackgroundLayer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(320.0))
			{
				EasingFunction = new SineEase
				{
					EasingMode = EasingMode.EaseOut
				}
			});
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.UpdateBingBackgroundVisual");
		}
	}

	private void AnimateTabChange()
	{
		if (_viewModel.Tabs.Count == 0)
		{
			return;
		}
		int num = ((_viewModel.CurrentTab == null) ? _lastTabIndex : _viewModel.Tabs.IndexOf(_viewModel.CurrentTab));
		if (num < 0)
		{
			num = Math.Clamp(_lastTabIndex, 0, _viewModel.Tabs.Count - 1);
		}
		object lastRenderedTab = _lastRenderedTab;
		DashboardTabViewModel currentTab = _viewModel.CurrentTab;
		if (lastRenderedTab == currentTab)
		{
			_lastTabIndex = num;
			return;
		}
		int num2 = ((num >= _lastTabIndex) ? 1 : (-1));
		_lastTabIndex = num;
		_lastRenderedTab = currentTab;
		_isAnimatingTab = true;
		_queuedTabStep = 0;
		try
		{
			ContentSlide.BeginAnimation(TranslateTransform.XProperty, null);
			ContentHost.BeginAnimation(UIElement.OpacityProperty, null);
			TabTransitionSlide.BeginAnimation(TranslateTransform.XProperty, null);
			TabTransitionLayer.BeginAnimation(UIElement.OpacityProperty, null);
			AdjacentPreviewLayer.BeginAnimation(UIElement.OpacityProperty, null);
			PreviousPreviewOffset.BeginAnimation(TranslateTransform.XProperty, null);
			NextPreviewOffset.BeginAnimation(TranslateTransform.XProperty, null);
			UpdateAdjacentPreviewSnapshots();
			PrepareLiveAdjacentPreviews();
			PrepareTabTransitionStrip(lastRenderedTab, currentTab, num2);
			ContentHost.Opacity = 0.0;
			ContentSlide.X = 0.0;
			TabTransitionLayer.Visibility = Visibility.Visible;
			TabTransitionLayer.Opacity = 1.0;
			AdjacentPreviewLayer.Visibility = Visibility.Visible;
			AdjacentPreviewLayer.Opacity = 0.36;
			BeginAdjacentPreviewOffsetAnimation();
			double num3 = -1280.0;
			double toValue = ((num2 > 0) ? (-2560.0) : 0.0);
			TabTransitionSlide.X = num3;
			TimeSpan timeSpan = TimeSpan.FromMilliseconds(420.0);
			DoubleAnimation doubleAnimation = new DoubleAnimation(num3, toValue, timeSpan)
			{
				EasingFunction = new QuarticEase
				{
					EasingMode = EasingMode.EaseInOut
				}
			};
			doubleAnimation.Completed += delegate
			{
				FinishTabAnimation();
			};
			TabTransitionSlide.BeginAnimation(TranslateTransform.XProperty, doubleAnimation);
		}
		catch
		{
			_isAnimatingTab = false;
		}
	}

	private void BeginAdjacentPreviewOffsetAnimation()
	{
		double fromLeft = -104.0;
		double fromRight = 104.0;
		TimeSpan duration = TimeSpan.FromMilliseconds(420.0);
		IEasingFunction easingFunction = new QuarticEase
		{
			EasingMode = EasingMode.EaseInOut
		};
		PreviousPreviewOffset.X = fromLeft;
		NextPreviewOffset.X = fromRight;
		PreviousPreviewOffset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromLeft, 0.0, duration)
		{
			EasingFunction = easingFunction
		});
		NextPreviewOffset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromRight, 0.0, duration)
		{
			EasingFunction = easingFunction
		});
	}

	private void PrepareTabTransitionStrip(object? oldTab, object? newTab, int direction)
	{
		if (direction > 0)
		{
			TransitionLeftHost.Content = null;
			TransitionCenterHost.Content = oldTab;
			TransitionRightHost.Content = newTab;
		}
		else
		{
			TransitionLeftHost.Content = newTab;
			TransitionCenterHost.Content = oldTab;
			TransitionRightHost.Content = null;
		}
	}

	private object? GetTabNear(object? tab, int step)
	{
		if (!(tab is DashboardTabViewModel item))
		{
			return null;
		}
		int num = _viewModel.Tabs.IndexOf(item) + step;
		if (num < 0 || num >= _viewModel.Tabs.Count)
		{
			return null;
		}
		return _viewModel.Tabs[num];
	}

	private void FinishTabAnimation()
	{
		_isAnimatingTab = false;
		TabTransitionLayer.Visibility = Visibility.Collapsed;
		TabTransitionLayer.Opacity = 0.0;
		TabTransitionSlide.X = -1280.0;
		PreviousPreviewOffset.X = 0.0;
		NextPreviewOffset.X = 0.0;
		TransitionLeftHost.Content = null;
		TransitionCenterHost.Content = null;
		TransitionRightHost.Content = null;
		ContentHost.Opacity = 1.0;
		ContentSlide.X = 0.0;
		UpdateAdjacentPreviewSnapshots();
		PrepareLiveAdjacentPreviews();
		AdjacentPreviewLayer.Visibility = Visibility.Visible;
		AdjacentPreviewLayer.Opacity = 0.36;
		if (IsVideoTabCurrent() && !IsOverlayOpen())
		{
			_lastFocusedButtonByTab.Remove(_viewModel.CurrentTab.Key);
			QueueFocusFirstButton();
		}
		if (_queuedTabStep == 0 || IsOverlayOpen())
		{
			_queuedTabStep = 0;
			return;
		}
		int step = _queuedTabStep;
		_queuedTabStep = 0;
		Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
		{
			_viewModel.MoveTab(step);
		}, DispatcherPriority.Input, Array.Empty<object>());
	}

	private void FadeAdjacentPreviewLayer(double toOpacity, double milliseconds)
	{
		DoubleAnimation doubleAnimation = new DoubleAnimation(toOpacity, TimeSpan.FromMilliseconds(milliseconds))
		{
			EasingFunction = new SineEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		AdjacentPreviewLayer.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
	}

	private void AnimateActiveOverlayIn()
	{
		if (GetActiveOverlay() is FrameworkElement { IsVisible: not false } frameworkElement)
		{
			if (frameworkElement == MyGamesOverlay)
			{
				AnimateLibraryMenuIn();
			}
			else if (frameworkElement == LauncherSettingsOverlay)
			{
				AnimateSettingsMenuIn();
			}
			else if (frameworkElement == MusicPlayerOverlay)
			{
				AnimateMusicPlayerIn();
			}
			else if (frameworkElement == GameDetailsOverlay)
			{
				AnimateGameDetailsIn();
			}
			else if (frameworkElement == ThemeMenuOverlay)
			{
				AnimateThemeMenuIn();
			}
			else if (frameworkElement == ProfileEditorOverlay)
			{
				AnimateProfileMenuIn();
			}
			else
			{
				AnimateOverlayIn(frameworkElement);
			}
		}
	}

	private static void AnimateOverlayIn(FrameworkElement overlay)
	{
		overlay.BeginAnimation(UIElement.OpacityProperty, null);
		TranslateTransform translateTransform = EnsureOverlayTranslateTransform(overlay);
		translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
		translateTransform.BeginAnimation(TranslateTransform.YProperty, null);
		overlay.Opacity = 0.0;
		translateTransform.X = 48.0;
		translateTransform.Y = 0.0;
		overlay.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(135.0))
		{
			EasingFunction = new SineEase
			{
				EasingMode = EasingMode.EaseOut
			}
		});
		translateTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(215.0))
		{
			EasingFunction = new QuarticEase
			{
				EasingMode = EasingMode.EaseOut
			}
		});
	}

	private void AnimateLibraryMenuIn()
	{
		BeginLayerAnimation(MyGamesOverlay, EnsureOverlayTranslateTransform(MyGamesOverlay), 1.0, 0.0, 0.0, 1.0, 120, 0);
		BeginLayerAnimation(MyGamesBackdrop, null, 0.0, 0.0, 0.26, 0.26, 260, 0);
		BeginLayerAnimation(MyGamesFilterChrome, MyGamesFilterChromeTranslate, -24.0, 0.0, 0.0, 1.0, 240, 40);
		BeginLayerAnimation(MyGamesHeaderChrome, MyGamesHeaderChromeTranslate, 42.0, 0.0, 0.0, 1.0, 260, 60);
		BeginLayerAnimation(MyGamesStrip, MyGamesStripTranslate, 168.0, 0.0, 0.0, 1.0, 360, 35);
		BeginLayerAnimation(MyGamesFooterChrome, MyGamesFooterChromeTranslate, 28.0, 0.0, 0.0, 1.0, 210, 145);
	}

	private void AnimateSettingsMenuIn()
	{
		BeginLayerAnimation(LauncherSettingsOverlay, EnsureOverlayTranslateTransform(LauncherSettingsOverlay), 1.0, 0.0, 0.0, 1.0, 120, 0);
		BeginLayerAnimation(SettingsHeaderTitle, SettingsHeaderTitleTranslate, 36.0, 0.0, 0.0, 1.0, 250, 35);
		BeginLayerAnimation(SettingsPanelFrame, SettingsPanelFrameTranslate, 132.0, 0.0, 0.0, 1.0, 360, 20);
		SystemSettingsCategoryPanel.BeginAnimation(UIElement.OpacityProperty, null);
		SystemSettingsCategoryPanel.Opacity = 1.0;
		EnsureOverlayTranslateTransform(SystemSettingsCategoryPanel).X = 0.0;
		BeginLayerAnimation(SettingsFooterChrome, SettingsFooterChromeTranslate, 26.0, 0.0, 0.0, 1.0, 210, 160);
	}

	private void AnimateMusicPlayerIn()
	{
		BeginLayerAnimation(MusicPlayerOverlay, EnsureOverlayTranslateTransform(MusicPlayerOverlay), 1.0, 0.0, 0.0, 1.0, 130, 0);
		BeginLayerAnimation(MusicPlayerHeader, MusicPlayerHeaderTranslate, 34.0, 0.0, 0.0, 1.0, 240, 35);
		BeginLayerAnimation(MusicPlayerContentGrid, MusicPlayerContentTranslate, 112.0, 0.0, 0.0, 1.0, 335, 20);
		BeginLayerAnimation(MusicPlayerLeftPane, MusicPlayerLeftPaneTranslate, 30.0, 0.0, 0.0, 1.0, 250, 105);
		BeginLayerAnimation(MusicPlayerTrackPane, MusicPlayerTrackPaneTranslate, 54.0, 0.0, 0.0, 1.0, 270, 135);
		BeginLayerAnimation(MusicPlayerFooterChrome, MusicPlayerFooterChromeTranslate, 24.0, 0.0, 0.0, 1.0, 205, 170);
	}

	private void AnimateGameDetailsIn()
	{
		BeginLayerAnimation(GameDetailsOverlay, EnsureOverlayTranslateTransform(GameDetailsOverlay), 1.0, 0.0, 0.0, 1.0, 120, 0);
		BeginLayerAnimation(GameDetailsTintLayer, null, 0.0, 0.0, 0.0, 1.0, 210, 0);
		BeginLayerAnimation(GameDetailsBackgroundImage, null, 0.0, 0.0, 0.0, 0.46, 270, 25);
		BeginLayerAnimation(GameDetailsShadeLayer, null, 0.0, 0.0, 0.0, 1.0, 250, 40);
		BeginLayerAnimation(GameDetailsGradientLayer, null, 0.0, 0.0, 0.0, 1.0, 250, 55);
		BeginLayerAnimation(GameDetailsTabsChrome, GameDetailsTabsChromeTranslate, -42.0, 0.0, 0.0, 1.0, 270, 55);
		BeginLayerAnimation(GameDetailsTitleChrome, GameDetailsTitleChromeTranslate, 48.0, 0.0, 0.0, 1.0, 280, 80);
		AnimateVisibleGameDetailsPanel(GameDetailsOverviewPanel, GameDetailsOverviewPanelTranslate, 118.0, 95);
		AnimateVisibleGameDetailsPanel(GameDetailsDetailsPanel, GameDetailsDetailsPanelTranslate, 90.0, 95);
		AnimateVisibleGameDetailsPanel(GameDetailsExtrasPanel, GameDetailsExtrasPanelTranslate, 90.0, 95);
		AnimateVisibleGameDetailsPanel(GameDetailsGalleryPanel, GameDetailsGalleryPanelTranslate, 90.0, 95);
		BeginLayerAnimation(GameDetailsInfoPanel, GameDetailsInfoPanelTranslate, 58.0, 0.0, 0.0, 1.0, 285, 145);
		BeginLayerAnimation(GameDetailsFooterChrome, GameDetailsFooterChromeTranslate, 26.0, 0.0, 0.0, 1.0, 210, 190);
	}

	private void AnimateThemeMenuIn()
	{
		BeginLayerAnimation(ThemeMenuOverlay, EnsureOverlayTranslateTransform(ThemeMenuOverlay), 0.0, 0.0, 0.0, 1.0, 95, 0);
		BeginLayerAnimation(ThemeMenuBackdrop, null, 0.0, 0.0, 0.0, 1.0, 180, 0);
		BeginLayerAnimation(ThemeMenuPanel, ThemeMenuPanelTranslate, 64.0, 0.0, 0.0, 1.0, 285, 15);
		BeginLayerAnimation(ThemeMenuFooterChrome, ThemeMenuFooterChromeTranslate, 24.0, 0.0, 0.0, 1.0, 205, 110);
	}

	private void AnimateProfileMenuIn()
	{
		BeginLayerAnimation(ProfileEditorOverlay, EnsureOverlayTranslateTransform(ProfileEditorOverlay), 0.0, 0.0, 0.0, 1.0, 95, 0);
		BeginLayerAnimation(ProfileHeaderTitle, ProfileHeaderTitleTranslate, 34.0, 0.0, 0.0, 1.0, 240, 35);
		BeginLayerAnimation(ProfileTopGamerPictureButton, ProfileTopGamerPictureTranslate, 0.0, 0.0, 0.0, 1.0, 210, 45);
		BeginLayerAnimation(ProfileClockChrome, ProfileClockChromeTranslate, 34.0, 0.0, 0.0, 1.0, 240, 55);
		BeginLayerAnimation(ProfilePanelFrame, ProfilePanelFrameTranslate, 118.0, 0.0, 0.0, 1.0, 340, 20);
		BeginLayerAnimation(ProfileFooterChrome, ProfileFooterChromeTranslate, 24.0, 0.0, 0.0, 1.0, 205, 145);
	}

	private static void AnimateVisibleGameDetailsPanel(FrameworkElement panel, TranslateTransform translateTransform, double fromX, int delayMs)
	{
		if (panel.Visibility != Visibility.Visible)
		{
			panel.BeginAnimation(UIElement.OpacityProperty, null);
			panel.Opacity = 1.0;
			translateTransform.X = 0.0;
			return;
		}
		BeginLayerAnimation(panel, translateTransform, fromX, 0.0, 0.0, 1.0, 340, delayMs);
	}

	private static void BeginLayerAnimation(FrameworkElement element, TranslateTransform? translateTransform, double fromX, double toX, double fromOpacity, double toOpacity, int durationMs, int delayMs)
	{
		element.BeginAnimation(UIElement.OpacityProperty, null);
		element.Opacity = fromOpacity;
		TimeSpan duration = TimeSpan.FromMilliseconds(durationMs);
		DoubleAnimation opacityAnimation = new DoubleAnimation(fromOpacity, toOpacity, duration)
		{
			BeginTime = TimeSpan.FromMilliseconds(delayMs),
			EasingFunction = new SineEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		element.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
		if (translateTransform == null)
		{
			return;
		}
		translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
		translateTransform.BeginAnimation(TranslateTransform.YProperty, null);
		translateTransform.X = fromX;
		translateTransform.Y = 0.0;
		translateTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, toX, duration)
		{
			BeginTime = TimeSpan.FromMilliseconds(delayMs),
			EasingFunction = new QuarticEase
			{
				EasingMode = EasingMode.EaseOut
			}
		});
	}

	private bool TryBeginMenuCloseTransition()
	{
		FrameworkElement overlay;
		if (_viewModel.IsMyGamesOpen)
		{
			overlay = MyGamesOverlay;
		}
		else if (_viewModel.IsLauncherSettingsOpen && !_viewModel.IsDashboardCustomizerOpen && !_viewModel.IsThemeCreatorOpen && !_viewModel.IsSteamSetupOpen)
		{
			overlay = LauncherSettingsOverlay;
		}
		else
		{
			return false;
		}
		_isMenuTransitionActive = true;
		if (overlay == MyGamesOverlay)
		{
			AnimateLibraryMenuOut(FinishMenuCloseTransition);
		}
		else
		{
			AnimateSettingsMenuOut(FinishMenuCloseTransition);
		}
		return true;

		void FinishMenuCloseTransition()
		{
			try
			{
				_viewModel.BackCommand.Execute(null);
			}
			finally
			{
				_isMenuTransitionActive = false;
				overlay.Opacity = 1.0;
				EnsureOverlayTranslateTransform(overlay).X = 0.0;
			}
		}
	}

	private void BackHint_OnMouseLeftButtonUp(object sender, PointerReleasedEventArgs e)
	{
		e.Handled = true;
		if (_viewModel.IsLauncherSettingsOpen && _activeSystemSettingsPanel != null && !_viewModel.IsDashboardCustomizerOpen && !_viewModel.IsThemeCreatorOpen && !_viewModel.IsSteamSetupOpen && !_viewModel.IsSpotifySetupOpen)
		{
			_audioService.Play("menu-out");
			ShowSystemSettingsCategories();
			return;
		}
		if (TryBeginMenuCloseTransition())
		{
			return;
		}
		if (_viewModel.BackCommand.CanExecute(null))
		{
			_viewModel.BackCommand.Execute(null);
		}
	}

	private void AnimateLibraryMenuOut(Action completed)
	{
		BeginLayerAnimation(MyGamesFilterChrome, MyGamesFilterChromeTranslate, 0.0, -34.0, 1.0, 0.0, 145, 0);
		BeginLayerAnimation(MyGamesHeaderChrome, MyGamesHeaderChromeTranslate, 0.0, 48.0, 1.0, 0.0, 160, 0);
		BeginLayerAnimation(MyGamesStrip, MyGamesStripTranslate, 0.0, 150.0, 1.0, 0.0, 210, 0);
		BeginLayerAnimation(MyGamesFooterChrome, MyGamesFooterChromeTranslate, 0.0, 24.0, 1.0, 0.0, 130, 0);
		BeginLayerAnimation(MyGamesBackdrop, null, 0.0, 0.0, 0.26, 0.0, 190, 0);
		AnimateOverlayOut(MyGamesOverlay, completed);
	}

	private void AnimateSettingsMenuOut(Action completed)
	{
		BeginLayerAnimation(SettingsHeaderTitle, SettingsHeaderTitleTranslate, 0.0, 42.0, 1.0, 0.0, 145, 0);
		BeginLayerAnimation(SettingsPanelFrame, SettingsPanelFrameTranslate, 0.0, 136.0, 1.0, 0.0, 215, 0);
		BeginLayerAnimation(SettingsFooterChrome, SettingsFooterChromeTranslate, 0.0, 24.0, 1.0, 0.0, 130, 0);
		AnimateOverlayOut(LauncherSettingsOverlay, completed);
	}

	private static void AnimateOverlayOut(FrameworkElement overlay, Action completed)
	{
		overlay.BeginAnimation(UIElement.OpacityProperty, null);
		TranslateTransform translateTransform = EnsureOverlayTranslateTransform(overlay);
		translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
		translateTransform.BeginAnimation(TranslateTransform.YProperty, null);
		translateTransform.X = 0.0;
		translateTransform.Y = 0.0;
		DoubleAnimation doubleAnimation = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(150.0))
		{
			EasingFunction = new SineEase
			{
				EasingMode = EasingMode.EaseIn
			}
		};
		doubleAnimation.Completed += delegate
		{
			completed();
		};
		overlay.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
		translateTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(112.0, TimeSpan.FromMilliseconds(210.0))
		{
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseIn
			}
		});
	}

	private static TranslateTransform EnsureOverlayTranslateTransform(FrameworkElement overlay)
	{
		if (overlay.RenderTransform is TransformGroup group)
		{
			TranslateTransform translate = group.Children.OfType<TranslateTransform>().FirstOrDefault();
			if (translate != null)
			{
				return translate;
			}
		}
		if (overlay.RenderTransform is TranslateTransform existingTranslate)
		{
			return existingTranslate;
		}
		TranslateTransform translateTransform = new TranslateTransform();
		overlay.RenderTransform = translateTransform;
		return translateTransform;
	}

	private void UpdateAdjacentPreviewSnapshots()
	{
		PreviousPreviewImage.Source = ((_viewModel.PreviousTab == null) ? null : CreatePreviewSnapshot(_viewModel.PreviousTab, 0.0 - _viewModel.LeftPreviewContentLeft));
		NextPreviewImage.Source = ((_viewModel.NextTab == null) ? null : CreatePreviewSnapshot(_viewModel.NextTab, 0.0 - _viewModel.RightPreviewContentLeft));
	}

	private void PrepareLiveAdjacentPreviews()
	{
		PreviousPreviewLiveHost.Content = null;
		NextPreviewLiveHost.Content = null;
		NextPreviewLiveHost.ClearValue(Canvas.LeftProperty);
		PreviousPreviewLiveLayer.Visibility = Visibility.Collapsed;
		NextPreviewLiveLayer.Visibility = Visibility.Collapsed;
		bool hasPrevious = _viewModel.PreviousTab != null && PreviousPreviewImage.Source != null;
		bool hasNext = _viewModel.NextTab != null && NextPreviewImage.Source != null;
		PreviousPreviewImage.Visibility = hasPrevious ? Visibility.Visible : Visibility.Collapsed;
		NextPreviewImage.Visibility = hasNext ? Visibility.Visible : Visibility.Collapsed;
	}

	private static bool IsHeavyPreviewTab(object? tab)
	{
		return tab?.GetType().Name == "AppsTabViewModel";
	}

	private BitmapSource? CreatePreviewSnapshot(object? tab, double cropX)
	{
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_010d: Unknown result type (might be due to invalid IL or missing references)
		//IL_014c: Unknown result type (might be due to invalid IL or missing references)
		if (tab == null)
		{
			return null;
		}
		try
		{
			ContentControl contentPresenter = new ContentControl();
			contentPresenter.Content = tab;
			contentPresenter.Width = 1280.0;
			contentPresenter.Height = 502.0;
			// Offscreen controls only get styles/templates when attached to the tree: host it briefly in AudioHost.
			AudioHost.Children.Add(contentPresenter);
			try
			{
			contentPresenter.Measure(new Size(1280.0, 502.0));
			contentPresenter.Arrange(new Rect(0.0, 0.0, 1280.0, 502.0));
			contentPresenter.UpdateLayout();
			using RenderTargetBitmap full = new RenderTargetBitmap(new PixelSize(1280, 502), new Vector(96, 96));
			full.Render(contentPresenter);
			return BitmapOps.Crop(full, new PixelRect((int)Math.Clamp(cropX, 0.0, 1176.0), 0, 104, 502));
			}
			finally
			{
				AudioHost.Children.Remove(contentPresenter);
			}

		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.CreatePreviewSnapshot");
			return null;
		}
	}

	private void QueueFocusFirstButton()
	{
		if (_isFocusUpdateQueued)
		{
			return;
		}
		_isFocusUpdateQueued = true;
		Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
		{
			try
			{
				_isFocusUpdateQueued = false;
				FocusFirstButton();
			}
			catch (Exception exception)
			{
				_isFocusUpdateQueued = false;
				App.LogException(exception, "MainWindow.QueueFocusFirstButton");
			}
		}, DispatcherPriority.Background, Array.Empty<object>());
	}

	private void QueueGameDetailsTileFocus()
	{
		Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
		{
			if (_viewModel.IsDetailsOpen)
			{
				TryFocus(GetFirstVisibleDetailsButton() ?? FindFocusableControl((DependencyObject?)(object)GameDetailsOverviewPanel) ?? FindFocusableControl((DependencyObject?)(object)GameDetailsOverlay));
			}
		}, DispatcherPriority.Loaded, Array.Empty<object>());
	}

	private void FocusFirstButton()
	{
		if (_viewModel.IsDetailsOpen)
		{
			TryFocus(GetFirstVisibleDetailsButton() ?? FindFocusableControl((DependencyObject?)(object)GameDetailsOverlay));
		}
		else if (_viewModel.IsMyGamesOpen)
		{
			FocusLibraryGameButton(_viewModel.SelectedGame);
		}
		else if (_viewModel.IsLauncherSettingsOpen)
		{
			if (_viewModel.IsDashboardCustomizerOpen)
			{
				TryFocus(DashboardCustomizerPreviousTabButton ?? FindFocusableControl((DependencyObject?)(object)DashboardCustomizerOverlay));
			}
			else if (_viewModel.IsThemeCreatorOpen)
			{
				TryFocus(ChooseThemeHomeBackgroundButton ?? FindFocusableControl((DependencyObject?)(object)ThemeCreatorOverlay));
			}
			else
			{
				TryFocus(FindFocusableControl((DependencyObject?)(object)LauncherSettingsOverlay));
			}
		}
		else if (_viewModel.IsThemeMenuOpen)
		{
			TryFocus(FindThemeMenuFocusButton() ?? FindFocusableControl((DependencyObject?)(object)ThemeMenuOverlay));
		}
		else if (_viewModel.IsProfileEditorOpen)
		{
			TryFocus(ProfileMenuViewGamesButton ?? FindFocusableControl((DependencyObject?)(object)ProfileEditorOverlay));
		}
		else if (_viewModel.IsMusicPlayerOpen)
		{
			TryFocus(FindFocusableControl((DependencyObject?)(object)MusicPlayerOverlay));
		}
		else if (_viewModel.IsBladesDashboardStyle)
		{
			TryFocusBladesMenuButton();
		}
		else
		{
			List<FocusCandidate> dashboardFocusCandidates = GetDashboardFocusCandidates();
			if (dashboardFocusCandidates.Count > 0)
			{
				FocusDefaultButton(dashboardFocusCandidates);
			}
			else
			{
				TryFocus(FindVisualChild<Avalonia.Controls.Button>((DependencyObject?)(object)ContentHost));
			}
		}
	}

	/// <summary>WPF EventSetters of GameDetailsTileButtonStyle (Avalonia themes have no event setters).</summary>
	private void WireStyleEventSetters()
	{
		object? theme = this.TryFindResource("GameDetailsTileButtonStyle");
		if (theme == null)
		{
			return;
		}
		bool IsTile(Avalonia.Controls.Button button) => ReferenceEquals(button.Theme, theme) && ReferenceEquals(TopLevel.GetTopLevel(button), this);
		InputElement.PointerEnteredEvent.AddClassHandler<Avalonia.Controls.Button>((button, e) => { if (IsTile(button)) GameDetailsTileButton_OnFocusOrMouseEnter(button, e); });
		InputElement.GotFocusEvent.AddClassHandler<Avalonia.Controls.Button>((button, e) => { if (IsTile(button)) GameDetailsTileButton_OnFocusOrMouseEnter(button, e); });
		InputElement.PointerExitedEvent.AddClassHandler<Avalonia.Controls.Button>((button, e) => { if (IsTile(button)) GameDetailsTileButton_OnFocusOrMouseLeave(button, e); });
		InputElement.LostFocusEvent.AddClassHandler<Avalonia.Controls.Button>((button, e) => { if (IsTile(button)) GameDetailsTileButton_OnFocusOrMouseLeave(button, e); });
	}

	private void GameDetailsTileButton_OnFocusOrMouseEnter(object sender, RoutedEventArgs e)
	{
		if (sender is Avalonia.Controls.Button button)
		{
			Avalonia.Controls.Panel.SetZIndex(button, 12);
			AnimateGameDetailsTileLift(button, 1.13, -7.0, -8.0, 145);
		}
	}

	private void GameDetailsTileButton_OnFocusOrMouseLeave(object sender, RoutedEventArgs e)
	{
		if (sender is Avalonia.Controls.Button button && (e is Avalonia.Input.PointerEventArgs || (!button.IsKeyboardFocusWithin && !button.IsMouseOver)))
		{
			Avalonia.Controls.Panel.SetZIndex(button, 0);
			AnimateGameDetailsTileLift(button, 1.0, 0.0, 0.0, 120);
		}
	}

	private static void AnimateGameDetailsTileLift(Avalonia.Controls.Button button, double scale, double x, double y, int milliseconds)
	{
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		TransformGroup transformGroup = button.RenderTransform as TransformGroup;
		if (transformGroup == null)
		{
			transformGroup = new TransformGroup();
			transformGroup.Children.Add(new ScaleTransform(1.0, 1.0));
			transformGroup.Children.Add(new TranslateTransform(0.0, 0.0));
			button.RenderTransform = transformGroup;
			button.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
		}
		ScaleTransform scaleTransform = transformGroup.Children.OfType<ScaleTransform>().FirstOrDefault();
		TranslateTransform translateTransform = transformGroup.Children.OfType<TranslateTransform>().FirstOrDefault();
		if (scaleTransform != null && translateTransform != null)
		{
			CubicEase easingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseOut
			};
			scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, TimeSpan.FromMilliseconds(milliseconds))
			{
				EasingFunction = easingFunction
			});
			scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, TimeSpan.FromMilliseconds(milliseconds))
			{
				EasingFunction = easingFunction
			});
			translateTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(milliseconds))
			{
				EasingFunction = easingFunction
			});
			translateTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(milliseconds))
			{
				EasingFunction = easingFunction
			});
		}
	}

	private bool TryMoveDashboardFocus(DashboardInputAction action)
	{
		//IL_0193: Unknown result type (might be due to invalid IL or missing references)
		//IL_0198: Unknown result type (might be due to invalid IL or missing references)
		//IL_019d: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_01da: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_020b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0210: Unknown result type (might be due to invalid IL or missing references)
		//IL_0226: Unknown result type (might be due to invalid IL or missing references)
		//IL_022b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0249: Unknown result type (might be due to invalid IL or missing references)
		//IL_024b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0241: Unknown result type (might be due to invalid IL or missing references)
		//IL_0246: Unknown result type (might be due to invalid IL or missing references)
		if (Keyboard.FocusedElement is Avalonia.Controls.TextBox)
		{
			return false;
		}
		if (_viewModel.IsLauncherSettingsOpen && DashboardInputRouter.TryAdjustFocusedSetting(action))
		{
			return true;
		}
		if (_viewModel.IsMusicPlayerOpen)
		{
			return TryMoveOverlayFocus(MusicPlayerOverlay, action);
		}
		if (_viewModel.IsDetailsOpen)
		{
			return TryMoveOverlayFocus(GameDetailsOverlay, action);
		}
		if (_viewModel.IsMyGamesOpen)
		{
			return TryMoveMyGamesFocus(action);
		}
		if (_viewModel.IsLauncherSettingsOpen)
		{
			if (_viewModel.IsDashboardCustomizerOpen)
			{
				return TryMoveOverlayFocus(DashboardCustomizerOverlay, action);
			}
			if (_viewModel.IsThemeCreatorOpen)
			{
				return TryMoveOverlayFocus(ThemeCreatorOverlay, action);
			}
			return TryMoveOverlayFocus(LauncherSettingsOverlay, action);
		}
		if (_viewModel.IsProfileEditorOpen)
		{
			return TryMoveProfileMenuFocus(action);
		}
		if (_viewModel.IsThemeMenuOpen)
		{
			return TryMoveThemeMenuFocus(action);
		}
		List<FocusCandidate> dashboardFocusCandidates = GetDashboardFocusCandidates();
		if (dashboardFocusCandidates.Count == 0)
		{
			return SafeMoveFocus(action);
		}
		IInputElement focusedElement = Keyboard.FocusedElement;
		Avalonia.Controls.Button currentButton = focusedElement as Avalonia.Controls.Button;
		if (currentButton == null || !dashboardFocusCandidates.Any((FocusCandidate candidate) => candidate.Button == currentButton))
		{
			return FocusDefaultButton(dashboardFocusCandidates);
		}
		Point currentCenter = GetCenter(dashboardFocusCandidates.First((FocusCandidate candidate) => candidate.Button == currentButton).Bounds);
		Vector direction = (Vector)(action switch
		{
			DashboardInputAction.MoveLeft => new Vector(-1.0, 0.0), 
			DashboardInputAction.MoveRight => new Vector(1.0, 0.0), 
			DashboardInputAction.MoveUp => new Vector(0.0, -1.0), 
			DashboardInputAction.MoveDown => new Vector(0.0, 1.0), 
			_ => new Vector(0.0, 0.0), 
		});
		var anon = (from item in (from candidate in dashboardFocusCandidates
				where candidate.Button != currentButton
				select new
				{
					Candidate = candidate,
					Center = GetCenter(candidate.Bounds)
				}).Select(item =>
			{
				//IL_0008: Unknown result type (might be due to invalid IL or missing references)
				//IL_000e: Unknown result type (might be due to invalid IL or missing references)
				//IL_0013: Unknown result type (might be due to invalid IL or missing references)
				//IL_0018: Unknown result type (might be due to invalid IL or missing references)
				//IL_0052: Unknown result type (might be due to invalid IL or missing references)
				//IL_0057: Unknown result type (might be due to invalid IL or missing references)
				//IL_0030: Unknown result type (might be due to invalid IL or missing references)
				//IL_0035: Unknown result type (might be due to invalid IL or missing references)
				//IL_0088: Unknown result type (might be due to invalid IL or missing references)
				//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
				//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
				//IL_008f: Unknown result type (might be due to invalid IL or missing references)
				//IL_0094: Unknown result type (might be due to invalid IL or missing references)
				FocusCandidate candidate = item.Candidate;
				Vector delta = item.Center - currentCenter;
				DashboardInputAction dashboardInputAction = action;
				Point center;
				double num;
				if ((uint)dashboardInputAction > 1u)
				{
					center = item.Center;
					num = Math.Abs(center.Y - currentCenter.Y);
				}
				else
				{
					center = item.Center;
					num = Math.Abs(center.X - currentCenter.X);
				}
				double primary = num;
				dashboardInputAction = action;
				double secondary;
				if (!((uint)dashboardInputAction <= 1u))
				{
					center = item.Center;
					secondary = Math.Abs(center.X - currentCenter.X);
				}
				else
				{
					center = item.Center;
					secondary = Math.Abs(center.Y - currentCenter.Y);
				}
				return new
				{
					Candidate = candidate,
					Delta = delta,
					Primary = primary,
					Secondary = secondary
				};
			})
			where Vector.Dot(item.Delta, direction) > 1.0
			orderby item.Secondary * 2.4 + item.Primary, item.Primary
			select item).FirstOrDefault();
		if (anon == null)
		{
			RememberFocusedButton();
			return false;
		}
		if (!TryFocus(anon.Candidate.Button))
		{
			return false;
		}
		RememberFocusedButton();
		return true;
	}

	private List<FocusCandidate> GetDashboardFocusCandidates()
	{
		try
		{
			return (from button in FindVisualChildren<Avalonia.Controls.Button>((DependencyObject?)(object)ContentHost)
				where button.IsEffectivelyVisible && button.IsEnabled && button.Focusable
				select new FocusCandidate(button, GetElementBounds(button, ContentHost))).Where(delegate(FocusCandidate candidate)
			{
				//IL_0002: Unknown result type (might be due to invalid IL or missing references)
				//IL_0007: Unknown result type (might be due to invalid IL or missing references)
				//IL_001c: Unknown result type (might be due to invalid IL or missing references)
				//IL_0021: Unknown result type (might be due to invalid IL or missing references)
				Rect bounds = candidate.Bounds;
				if (bounds.Width > 0.0)
				{
					bounds = candidate.Bounds;
					return bounds.Height > 0.0;
				}
				return false;
			}).Where(delegate(FocusCandidate candidate)
			{
				//IL_0002: Unknown result type (might be due to invalid IL or missing references)
				//IL_0007: Unknown result type (might be due to invalid IL or missing references)
				//IL_000c: Unknown result type (might be due to invalid IL or missing references)
				Point center = GetCenter(candidate.Bounds);
				return center.X >= 64.0 && center.X <= 1120.0;
			}).ToList();
		}
		catch
		{
			return new List<FocusCandidate>();
		}
	}

	private void RememberFocusedButton()
	{
		if (_viewModel.CurrentTab != null && Keyboard.FocusedElement is Avalonia.Controls.Button value)
		{
			if (IsVideoTabCurrent())
			{
				FocusCandidate focusCandidate = GetDashboardFocusCandidates().FirstOrDefault((FocusCandidate candidate) => candidate.Button == value);
				if (focusCandidate.Button == null || !IsLeftRailDashboardCandidate(focusCandidate))
				{
					_lastFocusedButtonByTab.Remove(_viewModel.CurrentTab.Key);
					return;
				}
			}
			_lastFocusedButtonByTab[_viewModel.CurrentTab.Key] = value;
		}
	}

	private void QueueBladesMenuFocus()
	{
		Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
		{
			TryFocusBladesMenuButton();
		}, DispatcherPriority.Loaded, Array.Empty<object>());
	}

	private void QueueBladesAchievementsFocus()
	{
		Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
		{
			TryFocusBladesAchievementsPane();
		}, DispatcherPriority.Loaded, Array.Empty<object>());
	}

	private bool TryFocusBladesAchievementsPane()
	{
		try
		{
			if (!_viewModel.IsBladesAchievementsSubmenu)
			{
				return false;
			}
			if (_viewModel.IsBladesAchievementListFocused)
			{
				if (_viewModel.SelectedBladesAchievementIndex >= 0 && _viewModel.SelectedBladesAchievementIndex < BladesAchievementListBox.Items.Count)
				{
					BladesAchievementListBox.ScrollIntoView(BladesAchievementListBox.Items[_viewModel.SelectedBladesAchievementIndex]);
				}
				BladesAchievementListBox.UpdateLayout();
				return TryFocus(BladesAchievementListBox);
			}
			if (_viewModel.SelectedBladesAchievementGameIndex >= 0 && _viewModel.SelectedBladesAchievementGameIndex < BladesAchievementGameListBox.Items.Count)
			{
				BladesAchievementGameListBox.ScrollIntoView(BladesAchievementGameListBox.Items[_viewModel.SelectedBladesAchievementGameIndex]);
			}
			BladesAchievementGameListBox.UpdateLayout();
			return TryFocus(BladesAchievementGameListBox);
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.TryFocusBladesAchievementsPane");
			return false;
		}
	}

	private void BladesTrayClick(object sender, RoutedEventArgs e) => _viewModel.ActivateBladesTray();

	private bool TryFocusBladesMenuButton()
	{
		try
		{
			if (_viewModel.IsBladesTraySelected && !_viewModel.IsBladesSubmenuOpen) return TryFocus(BladesTrayButton);
			List<Avalonia.Controls.Button> buttons = FindVisualChildren<Avalonia.Controls.Button>((DependencyObject?)(object)BladesDashboardOverlay)
				.Where((Avalonia.Controls.Button button) => button.IsEffectivelyVisible && string.Equals(button.Tag as string, "BladesMenuOption", StringComparison.OrdinalIgnoreCase))
				.ToList();
			if (buttons.Count == 0)
			{
				return false;
			}
			int index = Math.Clamp(_viewModel.ActiveBladesMenuIndex, 0, buttons.Count - 1);
			var selected=buttons.FirstOrDefault(button => button.DataContext is BladesMenuItemViewModel item && item.IsSelected) ?? buttons[index];
			selected.BringIntoView();
			return TryFocus(selected);
		}
		catch (Exception exception)
		{
			App.LogException(exception, "MainWindow.TryFocusBladesMenuButton");
			return false;
		}
	}

	private bool IsVideoTabCurrent()
	{
		DashboardTabViewModel? currentTab = _viewModel.CurrentTab;
		return string.Equals(currentTab?.Key, "media", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(currentTab?.Key, "video", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(currentTab?.Name, "video", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsLeftRailDashboardCandidate(FocusCandidate candidate)
	{
		Point center = GetCenter(candidate.Bounds);
		return center.X < 360.0;
	}

	private bool IsOverlayOpen()
	{
		if (!_viewModel.IsMyGamesOpen && !_viewModel.IsLauncherSettingsOpen && !_viewModel.IsProfileEditorOpen && !_viewModel.IsThemeMenuOpen && !_viewModel.IsThemeCreatorOpen && !_viewModel.IsDashboardCustomizerOpen && !_viewModel.IsSteamSetupOpen && !_viewModel.IsMusicPlayerOpen && !_viewModel.IsYouTubeTvOpen && !_viewModel.IsSearchOverlayOpen && !_viewModel.IsDetailsOpen)
		{
			return _viewModel.IsQuickMenuOpen;
		}
		return true;
	}

	private bool TryRestoreOverlayFocus()
	{
		DependencyObject activeOverlay = GetActiveOverlay();
		if (activeOverlay == null || IsFocusInside(activeOverlay))
		{
			return false;
		}
		FocusFirstButton();
		return true;
	}

	private void ScrollMusicBrowserResultIntoView()
	{
		int selectedIndex = _viewModel.MusicBrowserResultItems.IndexOf(_viewModel.SelectedMusicBrowserResultItem);
		ScrollItemIntoView(MusicBrowserResultsScrollViewer, selectedIndex, 48.0);
	}

	private void ScrollCurrentMusicTrackIntoView()
	{
		int selectedIndex = _viewModel.MusicTracks.IndexOf(_viewModel.SelectedMusicTrack ?? _viewModel.CurrentMusicTrack);
		ScrollItemIntoView(MusicPlayerTracksScrollViewer, selectedIndex, 48.0);
	}

	private static void ScrollItemIntoView(ScrollViewer scrollViewer, int selectedIndex, double itemHeight)
	{
		if (selectedIndex < 0 || itemHeight <= 0.0)
		{
			return;
		}
		double itemTop = selectedIndex * itemHeight;
		double itemBottom = itemTop + itemHeight;
		double viewportTop = scrollViewer.VerticalOffset;
		double viewportBottom = viewportTop + scrollViewer.ViewportHeight;
		if (itemTop < viewportTop)
		{
			scrollViewer.ScrollToVerticalOffset(itemTop);
		}
		else if (itemBottom > viewportBottom)
		{
			scrollViewer.ScrollToVerticalOffset(itemBottom - scrollViewer.ViewportHeight);
		}
	}

	private DependencyObject? GetActiveOverlay()
	{
		if (_viewModel.IsDetailsOpen)
		{
			return (DependencyObject?)(object)GameDetailsOverlay;
		}
		if (_viewModel.IsMyGamesOpen)
		{
			return (DependencyObject?)(object)MyGamesOverlay;
		}
		if (_viewModel.IsLauncherSettingsOpen)
		{
			if (_viewModel.IsDashboardCustomizerOpen)
			{
				return (DependencyObject?)(object)DashboardCustomizerOverlay;
			}
			if (_viewModel.IsThemeCreatorOpen)
			{
				return (DependencyObject?)(object)ThemeCreatorOverlay;
			}
			if (_viewModel.IsSteamSetupOpen)
			{
				return (DependencyObject?)(object)SteamSetupOverlay;
			}
			return (DependencyObject?)(object)LauncherSettingsOverlay;
		}
		if (_viewModel.IsProfileEditorOpen)
		{
			return (DependencyObject?)(object)ProfileEditorOverlay;
		}
		if (_viewModel.IsThemeMenuOpen)
		{
			return (DependencyObject?)(object)ThemeMenuOverlay;
		}
		if (_viewModel.IsMusicPlayerOpen)
		{
			return (DependencyObject?)(object)MusicPlayerOverlay;
		}
		if (_viewModel.IsYouTubeTvOpen)
		{
			return (DependencyObject?)(object)YouTubeTvOverlay;
		}
		return null;
	}

	private Avalonia.Controls.Button? GetFirstVisibleDetailsButton()
	{
		if (IsElementVisible(SteamDetailsLaunchButton))
		{
			return SteamDetailsLaunchButton;
		}
		if (IsElementVisible(ManualDetailsLaunchButton))
		{
			return ManualDetailsLaunchButton;
		}
		return null;
	}

	private static bool IsElementVisible(FrameworkElement? element)
	{
		if (element != null && element.IsEffectivelyVisible)
		{
			return element.Visibility == Visibility.Visible;
		}
		return false;
	}

	private static bool IsFocusInside(DependencyObject overlay)
	{
		IInputElement focusedElement = Keyboard.FocusedElement;
		DependencyObject val = (DependencyObject)((focusedElement is DependencyObject) ? focusedElement : null);
		if (val == null)
		{
			return false;
		}
		for (DependencyObject val2 = val; val2 != null; val2 = VisualTreeHelper.GetParent(val2))
		{
			if (val2 == overlay)
			{
				return true;
			}
		}
		return false;
	}

	private static bool SafeMoveFocus(DashboardInputAction action)
	{
		try
		{
			return DashboardInputRouter.MoveFocus(action);
		}
		catch
		{
			return false;
		}
	}

	private bool FocusDefaultButton(IReadOnlyCollection<FocusCandidate> buttons)
	{
		if (_viewModel.CurrentTab != null && _lastFocusedButtonByTab.TryGetValue(_viewModel.CurrentTab.Key, out Avalonia.Controls.Button remembered) && remembered.IsEffectivelyVisible && remembered.IsEnabled)
		{
			FocusCandidate rememberedCandidate = buttons.FirstOrDefault((FocusCandidate candidate) => candidate.Button == remembered);
			if (rememberedCandidate.Button != null && ShouldUseRememberedDashboardFocus(rememberedCandidate))
			{
				return TryFocus(remembered);
			}
		}
		IEnumerable<FocusCandidate> focusOrder;
	if (IsVideoTabCurrent())
	{
		focusOrder = buttons.OrderBy(delegate(FocusCandidate candidate)
		{
			return IsLeftRailDashboardCandidate(candidate) ? 0 : 1;
		}).ThenBy(delegate(FocusCandidate candidate)
		{
			Point center = GetCenter(candidate.Bounds);
			return Math.Abs(center.Y - 210.0);
			}).ThenBy(delegate(FocusCandidate candidate)
			{
				Point center = GetCenter(candidate.Bounds);
				return Math.Abs(center.X - 120.0);
			});
		}
		else
		{
			focusOrder = buttons.OrderBy(delegate(FocusCandidate candidate)
			{
				Point center = GetCenter(candidate.Bounds);
				return Math.Abs(center.Y - 210.0);
			}).ThenBy(delegate(FocusCandidate candidate)
			{
				Point center = GetCenter(candidate.Bounds);
				return Math.Abs(center.X - 560.0);
			});
		}
		return TryFocus(focusOrder.FirstOrDefault().Button);
	}

	private bool ShouldUseRememberedDashboardFocus(FocusCandidate candidate)
	{
		if (!IsVideoTabCurrent())
		{
			return true;
		}
		return IsLeftRailDashboardCandidate(candidate);
	}

		private bool TryMoveOverlayFocus(FrameworkElement overlay, DashboardInputAction action)
		{
			//IL_007d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0082: Unknown result type (might be due to invalid IL or missing references)
			//IL_0087: Unknown result type (might be due to invalid IL or missing references)
			//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00da: Unknown result type (might be due to invalid IL or missing references)
			//IL_00df: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f5: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
			//IL_0110: Unknown result type (might be due to invalid IL or missing references)
			//IL_0115: Unknown result type (might be due to invalid IL or missing references)
			//IL_0133: Unknown result type (might be due to invalid IL or missing references)
			//IL_0135: Unknown result type (might be due to invalid IL or missing references)
			//IL_012b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0130: Unknown result type (might be due to invalid IL or missing references)
			List<OverlayFocusCandidate> overlayFocusCandidates = GetOverlayFocusCandidates(overlay);
			if (overlayFocusCandidates.Count == 0)
			{
				return SafeMoveFocus(action);
			}
			Avalonia.Controls.Control currentControl = GetFocusedOverlayControl(overlay);
			if (Environment.GetEnvironmentVariable("DASHX360_FOCUS_DEBUG") == "1")
			{
				Console.Error.WriteLine($"[focus] {action} overlay={overlay.Name} current={currentControl?.GetType().Name}#{currentControl?.Name} focused={Keyboard.FocusedElement?.GetType().Name} candidates={string.Join(",", overlayFocusCandidates.Select(c => c.Control.GetType().Name + "@" + (int)c.Bounds.X + "," + (int)c.Bounds.Y))}");
			}
			if (currentControl == null || !overlayFocusCandidates.Any((OverlayFocusCandidate candidate) => candidate.Control == currentControl))
			{
				return TryFocus(overlayFocusCandidates[0].Control);
			}
			Point currentCenter = GetCenter(overlayFocusCandidates.First((OverlayFocusCandidate candidate) => candidate.Control == currentControl).Bounds);
			Vector direction = (Vector)(action switch
			{
				DashboardInputAction.MoveLeft => new Vector(-1.0, 0.0), 
				DashboardInputAction.MoveRight => new Vector(1.0, 0.0), 
				DashboardInputAction.MoveUp => new Vector(0.0, -1.0), 
				DashboardInputAction.MoveDown => new Vector(0.0, 1.0), 
				_ => new Vector(0.0, 0.0), 
			});
			var anon = (from item in (from candidate in overlayFocusCandidates
					where candidate.Control != currentControl
					select new
					{
						Candidate = candidate,
						Center = GetCenter(candidate.Bounds)
					}).Select(item =>
				{
					//IL_0008: Unknown result type (might be due to invalid IL or missing references)
					//IL_000e: Unknown result type (might be due to invalid IL or missing references)
					//IL_0013: Unknown result type (might be due to invalid IL or missing references)
					//IL_0018: Unknown result type (might be due to invalid IL or missing references)
					//IL_0052: Unknown result type (might be due to invalid IL or missing references)
					//IL_0057: Unknown result type (might be due to invalid IL or missing references)
					//IL_0030: Unknown result type (might be due to invalid IL or missing references)
					//IL_0035: Unknown result type (might be due to invalid IL or missing references)
					//IL_0088: Unknown result type (might be due to invalid IL or missing references)
					//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
					//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
					//IL_008f: Unknown result type (might be due to invalid IL or missing references)
					//IL_0094: Unknown result type (might be due to invalid IL or missing references)
					OverlayFocusCandidate candidate = item.Candidate;
					Vector delta = item.Center - currentCenter;
					DashboardInputAction dashboardInputAction = action;
					Point center;
					double num;
					if ((uint)dashboardInputAction > 1u)
					{
						center = item.Center;
						num = Math.Abs(center.Y - currentCenter.Y);
					}
					else
					{
						center = item.Center;
						num = Math.Abs(center.X - currentCenter.X);
					}
					double primary = num;
					dashboardInputAction = action;
					double secondary;
					if (!((uint)dashboardInputAction <= 1u))
					{
						center = item.Center;
						secondary = Math.Abs(center.X - currentCenter.X);
					}
					else
					{
						center = item.Center;
						secondary = Math.Abs(center.Y - currentCenter.Y);
					}
					return new
					{
						Candidate = candidate,
						Delta = delta,
						Primary = primary,
						Secondary = secondary
					};
				})
				where Vector.Dot(item.Delta, direction) > 1.0
				orderby item.Secondary * 2.2 + item.Primary, item.Primary
				select item).FirstOrDefault();
			if (anon == null)
			{
				return SafeMoveFocus(action);
			}
			return TryFocus(anon.Candidate.Control);
		}

		private static Avalonia.Controls.Control? GetFocusedOverlayControl(FrameworkElement overlay)
		{
			IInputElement focusedElement = Keyboard.FocusedElement;
			DependencyObject val = (DependencyObject)((focusedElement is DependencyObject) ? focusedElement : null);
			if (val == null)
			{
				return null;
			}
			for (DependencyObject val2 = val; val2 != null; val2 = GetParentObject(val2))
			{
				if ((object)val2 == overlay)
				{
					return null;
				}
				if (val2 is Avalonia.Controls.Control result)
				{
					return result;
				}
			}
			return null;
		}

		private bool TryMoveMyGamesFocus(DashboardInputAction action)
		{
			if (_viewModel.IsLibraryShowingApps)
			{
				return TryMoveAppLibraryFocus(action);
			}
			DashboardInputAction dashboardInputAction = action;
			if ((uint)dashboardInputAction > 1u && (uint)(dashboardInputAction - 12) > 1u)
			{
				return true;
			}
			List<GameCardViewModel> list = _viewModel.LibraryMenuGames.ToList();
			if (list.Count == 0)
			{
				return true;
			}
			GameCardViewModel gameCardViewModel = _viewModel.SelectedGame ?? list.FirstOrDefault();
			if (gameCardViewModel == null)
			{
				_viewModel.SelectGame(list[0]);
				Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
				{
					FocusLibraryGameButton(_viewModel.SelectedGame);
				}, DispatcherPriority.Render, Array.Empty<object>());
				return true;
			}
			int num = list.IndexOf(gameCardViewModel);
			if (num < 0)
			{
				num = 0;
			}
			bool isPageStep = action == DashboardInputAction.PreviousTab || action == DashboardInputAction.NextTab;
			bool movingRight = action == DashboardInputAction.MoveRight || action == DashboardInputAction.NextTab;
			int num2;
			if (isPageStep)
			{
				const int pageSize = 6;
				int pageStart = num / pageSize * pageSize;
				int lastPageStart = (list.Count - 1) / pageSize * pageSize;
				num2 = Math.Clamp(pageStart + (movingRight ? pageSize : (-pageSize)), 0, lastPageStart);
			}
			else
			{
				num2 = (movingRight ? Math.Min(num + 1, list.Count - 1) : Math.Max(num - 1, 0));
			}
			if (num2 == num)
			{
				return true;
			}
			List<GameCardViewModel> list2 = _viewModel.VisibleLibraryMenuGames.ToList();
			GameCardViewModel nextGame = list[num2];
			_viewModel.SelectGame(nextGame);
			if (isPageStep)
			{
				_audioService.Play(movingRight ? "page-right" : "page-left");
			}
			if (list2.Count != _viewModel.VisibleLibraryMenuGames.Count || !list2.Zip(_viewModel.VisibleLibraryMenuGames).All(((GameCardViewModel First, GameCardViewModel Second) pair) => pair.First == pair.Second))
			{
				Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
				{
					AnimateMyGamesPageShift(movingRight ? DashboardInputAction.MoveRight : DashboardInputAction.MoveLeft);
				}, DispatcherPriority.Render, Array.Empty<object>());
			}
			Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
			{
				FocusLibraryGameButton(nextGame);
			}, DispatcherPriority.Render, Array.Empty<object>());
			return true;
		}

		private bool TryMoveAppLibraryFocus(DashboardInputAction action)
		{
			if ((uint)action > 3u)
			{
				return true;
			}
			List<AppLibraryTileViewModel> list = _viewModel.AppLibraryTiles.ToList();
			if (list.Count == 0)
			{
				return true;
			}
			AppLibraryTileViewModel appLibraryTileViewModel = _viewModel.SelectedAppLibraryTile ?? list[0];
			int num = list.IndexOf(appLibraryTileViewModel);
			if (num < 0)
			{
				num = 0;
			}
			int num2 = FindNearestAppLibraryTileIndex(list, num, action);
			AppLibraryTileViewModel appLibraryTileViewModel2 = list[num2];
			if (_viewModel.SelectedAppLibraryTile != appLibraryTileViewModel2)
			{
				_viewModel.SelectedAppLibraryTile = appLibraryTileViewModel2;
			}
			Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
			{
				FocusAppLibraryTileButton(appLibraryTileViewModel2);
			}, DispatcherPriority.Render, Array.Empty<object>());
			return true;
		}

		private static int FindNearestAppLibraryTileIndex(IReadOnlyList<AppLibraryTileViewModel> tiles, int currentIndex, DashboardInputAction action)
		{
			if (currentIndex < 0 || currentIndex >= tiles.Count)
			{
				return 0;
			}
			AppLibraryTileViewModel current = tiles[currentIndex];
			double currentCenterX = current.Left + current.Width / 2.0;
			double currentCenterY = current.Top + current.Height / 2.0;
			int bestIndex = currentIndex;
			double bestScore = double.MaxValue;
			for (int i = 0; i < tiles.Count; i++)
			{
				if (i == currentIndex)
				{
					continue;
				}
				AppLibraryTileViewModel candidate = tiles[i];
				double candidateCenterX = candidate.Left + candidate.Width / 2.0;
				double candidateCenterY = candidate.Top + candidate.Height / 2.0;
				double deltaX = candidateCenterX - currentCenterX;
				double deltaY = candidateCenterY - currentCenterY;
				bool isCandidate = action switch
				{
					DashboardInputAction.MoveLeft => deltaX < -1.0,
					DashboardInputAction.MoveRight => deltaX > 1.0,
					DashboardInputAction.MoveUp => deltaY < -1.0,
					DashboardInputAction.MoveDown => deltaY > 1.0,
					_ => false,
				};
				if (!isCandidate)
				{
					continue;
				}
				double primaryDistance = action == DashboardInputAction.MoveLeft || action == DashboardInputAction.MoveRight ? Math.Abs(deltaX) : Math.Abs(deltaY);
				double crossAxisDistance = action == DashboardInputAction.MoveLeft || action == DashboardInputAction.MoveRight ? Math.Abs(deltaY) : Math.Abs(deltaX);
				double score = primaryDistance * 1000.0 + crossAxisDistance;
				if (score < bestScore)
				{
					bestScore = score;
					bestIndex = i;
				}
			}
			return bestIndex;
		}

		private void AnimateMyGamesPageShift(DashboardInputAction action)
		{
			if (MyGamesStripTranslate != null && MyGamesStrip != null)
			{
				try
				{
					MyGamesScrollViewer.ScrollToHorizontalOffset(_viewModel.LibraryMenuScrollOffset);
				}
				catch
				{
				}
				double x = ((action == DashboardInputAction.MoveRight) ? 120.0 : (-120.0));
				MyGamesStripTranslate.BeginAnimation(TranslateTransform.XProperty, null);
				MyGamesStrip.BeginAnimation(UIElement.OpacityProperty, null);
				MyGamesStripTranslate.X = x;
				MyGamesStrip.Opacity = 0.86;
				TimeSpan timeSpan = TimeSpan.FromMilliseconds(230.0);
				CubicEase easingFunction = new CubicEase
				{
					EasingMode = EasingMode.EaseOut
				};
				MyGamesStripTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0.0, timeSpan)
				{
					EasingFunction = easingFunction
				});
				MyGamesStrip.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150.0))
				{
					EasingFunction = new SineEase
					{
						EasingMode = EasingMode.EaseOut
					}
				});
			}
		}

		private void AnimateGameDetailsTabChange()
		{
			if (!_viewModel.IsDetailsOpen || GameDetailsOverlay == null)
			{
				_lastGameDetailsTabAnimationIndex = GetGameDetailsTabIndex(_viewModel.SelectedGameDetailsTabKey);
				return;
			}
			int gameDetailsTabIndex = GetGameDetailsTabIndex(_viewModel.SelectedGameDetailsTabKey);
			double x = ((gameDetailsTabIndex >= _lastGameDetailsTabAnimationIndex) ? 46.0 : (-46.0));
			_lastGameDetailsTabAnimationIndex = gameDetailsTabIndex;
			TimeSpan timeSpan = TimeSpan.FromMilliseconds(180.0);
			foreach (FrameworkElement item in from element in GameDetailsOverlay.Children.OfType<FrameworkElement>().Skip(4)
				where element.IsEffectivelyVisible
				select element)
			{
				TranslateTransform translateTransform = item.RenderTransform as TranslateTransform;
				if (translateTransform == null)
				{
					translateTransform = (TranslateTransform)(item.RenderTransform = new TranslateTransform());
				}
				translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
				item.BeginAnimation(UIElement.OpacityProperty, null);
				translateTransform.X = x;
				item.Opacity = 0.96;
				translateTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0.0, timeSpan)
				{
					EasingFunction = new CubicEase
					{
						EasingMode = EasingMode.EaseOut
					}
				});
				item.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(130.0))
				{
					EasingFunction = new SineEase
					{
						EasingMode = EasingMode.EaseOut
					}
				});
			}
		}

		private static int GetGameDetailsTabIndex(string key)
		{
			return key switch
			{
				"details" => 1, 
				"extras" => 2, 
				"gallery" => 3, 
				_ => 0, 
			};
		}

		private void MyGamesScrollViewer_OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
		{
			e.Handled = true;
		}

	private void FocusLibraryGameButton(GameCardViewModel? game, int remainingRetries = 2, int requestId = 0)
	{
		if (_viewModel.IsDetailsOpen)
		{
			return;
		}
		if (game == null)
		{
			return;
		}
			if (requestId == 0)
			{
				requestId = ++_libraryFocusRequestId;
			}
			else if (requestId != _libraryFocusRequestId)
			{
				return;
			}
			try
			{
				MyGamesScrollViewer.ScrollToHorizontalOffset(_viewModel.LibraryMenuScrollOffset);
			}
			catch
			{
			}
			Avalonia.Controls.Button element = GetLibraryGameButtonForItem(game);
			if (TryFocus(element))
			{
				return;
			}
			if (remainingRetries > 0)
			{
				Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
				{
					FocusLibraryGameButton(game, remainingRetries - 1, requestId);
				}, DispatcherPriority.Render, Array.Empty<object>());
			}
			else if (!TryFocus(element))
			{
				FocusFirstButton();
			}
		}

		private Avalonia.Controls.Button? GetLibraryGameButtonForItem(GameCardViewModel game)
		{
			DependencyObject container = MyGamesItemsControl?.ContainerFromItem(game) as DependencyObject;
			if (container == null)
			{
				return null;
			}
			return FindVisualChildren<Avalonia.Controls.Button>(container).FirstOrDefault((Avalonia.Controls.Button button) => button.CommandParameter == game);
		}

		private void FocusAppLibraryTileButton(AppLibraryTileViewModel? tile, int remainingRetries = 2)
		{
			if (tile == null)
			{
				return;
			}
			ScrollAppLibraryTileIntoView(tile);
			Avalonia.Controls.Button element = GetAppLibraryTileButtonForItem(tile);
			if (TryFocus(element))
			{
				return;
			}
			if (remainingRetries > 0)
			{
				Dispatcher.UIThread.BeginInvoke((Delegate)(Action)delegate
				{
					FocusAppLibraryTileButton(tile, remainingRetries - 1);
				}, DispatcherPriority.Render, Array.Empty<object>());
			}
		}

		private Avalonia.Controls.Button? GetAppLibraryTileButtonForItem(AppLibraryTileViewModel tile)
		{
			DependencyObject container = MyAppsItemsControl?.ContainerFromItem(tile) as DependencyObject;
			if (container == null)
			{
				return null;
			}
			return FindVisualChildren<Avalonia.Controls.Button>(container).FirstOrDefault((Avalonia.Controls.Button button) => button.CommandParameter == tile);
		}

		private void MyAppsScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
		{
			if (sender is ScrollViewer scrollViewer)
			{
				scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - (e.Delta.Y + e.Delta.X) * 120.0);
				e.Handled = true;
			}
		}

		private void AppLibraryTile_OnMouseEnter(object sender, Avalonia.Input.PointerEventArgs e)
		{
			if (sender is Avalonia.Controls.Button { CommandParameter: AppLibraryTileViewModel tile })
			{
				_viewModel.SelectedAppLibraryTile = tile;
				ScrollAppLibraryTileIntoView(tile);
			}
		}

		private void ScrollAppLibraryTileIntoView(AppLibraryTileViewModel tile)
		{
			try
			{
				if (MyAppsScrollViewer == null)
				{
					return;
				}
				double viewportLeft = MyAppsScrollViewer.HorizontalOffset;
				double viewportRight = viewportLeft + MyAppsScrollViewer.ViewportWidth;
				const double margin = 26.0;
				if (tile.Left < viewportLeft + margin)
				{
					MyAppsScrollViewer.ScrollToHorizontalOffset(Math.Max(0.0, tile.Left - margin));
				}
				else if (tile.Right > viewportRight - margin)
				{
					MyAppsScrollViewer.ScrollToHorizontalOffset(tile.Right - MyAppsScrollViewer.ViewportWidth + margin);
				}
			}
			catch (Exception exception)
			{
				App.LogException(exception, "MainWindow.ScrollAppLibraryTileIntoView");
			}
		}

		private List<OverlayFocusCandidate> GetLibraryGameButtons()
		{
			return (from button in FindVisualChildren<Avalonia.Controls.Button>((DependencyObject?)(object)MyGamesOverlay)
				where button.IsEffectivelyVisible && button.IsEnabled && button.Focusable && button.CommandParameter is GameCardViewModel
				select new OverlayFocusCandidate(button, GetElementBounds(button, MyGamesOverlay))).Where(delegate(OverlayFocusCandidate candidate)
			{
				//IL_0002: Unknown result type (might be due to invalid IL or missing references)
				//IL_0007: Unknown result type (might be due to invalid IL or missing references)
				//IL_001c: Unknown result type (might be due to invalid IL or missing references)
				//IL_0021: Unknown result type (might be due to invalid IL or missing references)
				Rect bounds = candidate.Bounds;
				if (bounds.Width > 0.0)
				{
					bounds = candidate.Bounds;
					return bounds.Height > 0.0;
				}
				return false;
			}).OrderBy(delegate(OverlayFocusCandidate candidate)
			{
				//IL_0002: Unknown result type (might be due to invalid IL or missing references)
				//IL_0007: Unknown result type (might be due to invalid IL or missing references)
				Rect bounds = candidate.Bounds;
				return bounds.Left;
			}).ToList();
		}

		private static List<OverlayFocusCandidate> GetOverlayFocusCandidates(FrameworkElement overlay)
		{
			try
			{
				return (from control in FindVisualChildren<Avalonia.Controls.Control>((DependencyObject?)(object)overlay)
					where control.IsEffectivelyVisible && control.IsEnabled && control.Focusable
					select new OverlayFocusCandidate(control, GetElementBounds(control, overlay))).Where(delegate(OverlayFocusCandidate candidate)
				{
					//IL_0002: Unknown result type (might be due to invalid IL or missing references)
					//IL_0007: Unknown result type (might be due to invalid IL or missing references)
					//IL_001c: Unknown result type (might be due to invalid IL or missing references)
					//IL_0021: Unknown result type (might be due to invalid IL or missing references)
					Rect bounds = candidate.Bounds;
					if (bounds.Width > 0.0)
					{
						bounds = candidate.Bounds;
						return bounds.Height > 0.0;
					}
					return false;
				}).Where(delegate(OverlayFocusCandidate candidate)
				{
					//IL_0002: Unknown result type (might be due to invalid IL or missing references)
					//IL_0007: Unknown result type (might be due to invalid IL or missing references)
					//IL_000c: Unknown result type (might be due to invalid IL or missing references)
					//IL_003f: Unknown result type (might be due to invalid IL or missing references)
					//IL_0044: Unknown result type (might be due to invalid IL or missing references)
					//IL_0059: Unknown result type (might be due to invalid IL or missing references)
					//IL_005e: Unknown result type (might be due to invalid IL or missing references)
					Point center = GetCenter(candidate.Bounds);
					if (center.X >= -200.0 && center.X <= overlay.Bounds.Width + 200.0)
					{
						Rect bounds = candidate.Bounds;
						if (bounds.Bottom >= -200.0)
						{
							bounds = candidate.Bounds;
							return bounds.Top <= overlay.Bounds.Height + 1200.0;
						}
					}
					return false;
				}).ToList();
			}
			catch
			{
				return new List<OverlayFocusCandidate>();
			}
		}

		private static Rect GetElementBounds(FrameworkElement element, Visual relativeTo)
		{
			//IL_0025: Unknown result type (might be due to invalid IL or missing references)
			//IL_002a: Unknown result type (might be due to invalid IL or missing references)
			//IL_002f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0033: Unknown result type (might be due to invalid IL or missing references)
			//IL_0038: Unknown result type (might be due to invalid IL or missing references)
			//IL_003c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0041: Unknown result type (might be due to invalid IL or missing references)
			//IL_0045: Unknown result type (might be due to invalid IL or missing references)
			//IL_004a: Unknown result type (might be due to invalid IL or missing references)
			//IL_004d: Unknown result type (might be due to invalid IL or missing references)
			try
			{
				return element.TransformToAncestor(relativeTo).TransformBounds(new Rect(0.0, 0.0, element.Bounds.Width, element.Bounds.Height));
			}
			catch (InvalidOperationException)
			{
				return Rect.Empty;
			}
			catch (ArgumentException)
			{
				return Rect.Empty;
			}
			catch
			{
				return Rect.Empty;
			}
		}

		private static Point GetCenter(Rect rect)
		{
			//IL_0032: Unknown result type (might be due to invalid IL or missing references)
			return new Point(rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0);
		}

		private bool ShouldBringFocusedElementIntoView(FrameworkElement element)
		{
			if (!IsOverlayOpen() && IsDescendantOf(element, ContentHost))
			{
				return false;
			}
			return true;
		}

		private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
		{
			DependencyObject? dependencyObject = child;
			while (dependencyObject != null)
			{
				if (dependencyObject == ancestor)
				{
					return true;
				}
				dependencyObject = GetParentObject(dependencyObject);
			}
			return false;
		}

		private bool TryFocus(UIElement? element)
		{
			if (element == null || !element.IsEffectivelyVisible || !element.Focusable)
			{
				return false;
			}
			if (element is Avalonia.Controls.Control && !element.IsEnabled)
			{
				return false;
			}
			try
			{
				bool num = element.Focus();
				bool flag = _viewModel.IsMyGamesOpen && element is Avalonia.Controls.Button button && button.CommandParameter is GameCardViewModel;
				if (num && element is FrameworkElement frameworkElement && !flag && ShouldBringFocusedElementIntoView(frameworkElement))
				{
					frameworkElement.BringIntoView();
				}
				if (num && !_viewModel.IsDetailsOpen && element is Avalonia.Controls.Button { CommandParameter: GameCardViewModel commandParameter })
				{
					_viewModel.SelectGame(commandParameter);
				}
				return num;
			}
			catch
			{
				return false;
			}
		}

		private bool TryMoveThemeMenuFocus(DashboardInputAction action)
		{
			List<Avalonia.Controls.Button> themeButtons = FindVisualChildren<Avalonia.Controls.Button>((DependencyObject?)(object)ThemeMenuOverlay)
				.Where((Avalonia.Controls.Button button) => button.IsEffectivelyVisible && button.IsEnabled && button.Focusable && button.Command == _viewModel.SelectThemeCommand)
				.ToList();
			if (themeButtons.Count == 0)
			{
				return false;
			}
			if (action != DashboardInputAction.MoveUp && action != DashboardInputAction.MoveDown)
			{
				return TryFocus(themeButtons.Contains(Keyboard.FocusedElement as Avalonia.Controls.Button) ? (Avalonia.Controls.Button)Keyboard.FocusedElement : themeButtons[0]);
			}
			Avalonia.Controls.Button? currentButton = Keyboard.FocusedElement as Avalonia.Controls.Button;
			int currentIndex = currentButton == null ? -1 : themeButtons.IndexOf(currentButton);
			int nextIndex = currentIndex < 0 ? 0 : currentIndex + (action == DashboardInputAction.MoveDown ? 1 : -1);
			nextIndex = Math.Max(0, Math.Min(themeButtons.Count - 1, nextIndex));
			return TryFocus(themeButtons[nextIndex]);
		}

		private bool TryMoveProfileMenuFocus(DashboardInputAction action)
		{
			List<Avalonia.Controls.Button> menuButtons = FindVisualChildren<Avalonia.Controls.Button>((DependencyObject?)(object)ProfileEditorOverlay)
				.Where((Avalonia.Controls.Button button) => button.IsEffectivelyVisible && button.IsEnabled && button.Focusable && string.Equals(button.Tag as string, "ProfileMenuOption", StringComparison.OrdinalIgnoreCase))
				.ToList();
			List<Avalonia.Controls.TextBox> editFields = FindVisualChildren<Avalonia.Controls.TextBox>((DependencyObject?)(object)ProfileEditorOverlay)
				.Where((Avalonia.Controls.TextBox textBox) => textBox.IsEffectivelyVisible && textBox.IsEnabled && textBox.Focusable && string.Equals(textBox.Tag as string, "ProfileEditField", StringComparison.OrdinalIgnoreCase))
				.ToList();
			if (menuButtons.Count == 0)
			{
				return false;
			}
			Avalonia.Controls.TextBox? currentTextBox = Keyboard.FocusedElement as Avalonia.Controls.TextBox;
			if (currentTextBox != null && editFields.Contains(currentTextBox))
			{
				if (action == DashboardInputAction.MoveLeft)
				{
					return TryFocus(ProfileMenuEditButton ?? menuButtons[Math.Min(1, menuButtons.Count - 1)]);
				}
				if (action == DashboardInputAction.MoveUp || action == DashboardInputAction.MoveDown)
				{
					int currentFieldIndex = editFields.IndexOf(currentTextBox);
					int nextFieldIndex = currentFieldIndex + (action == DashboardInputAction.MoveDown ? 1 : -1);
					nextFieldIndex = Math.Max(0, Math.Min(editFields.Count - 1, nextFieldIndex));
					return TryFocus(editFields[nextFieldIndex]);
				}
				return TryFocus(currentTextBox);
			}
			if (_viewModel.IsProfileMenuEditing && action == DashboardInputAction.MoveRight && editFields.Count > 0)
			{
				return TryFocus(editFields[0]);
			}
			if (action != DashboardInputAction.MoveUp && action != DashboardInputAction.MoveDown)
			{
				return TryFocus(menuButtons.Contains(Keyboard.FocusedElement as Avalonia.Controls.Button) ? (Avalonia.Controls.Button)Keyboard.FocusedElement : menuButtons[0]);
			}
			Avalonia.Controls.Button? currentButton = Keyboard.FocusedElement as Avalonia.Controls.Button;
			int currentIndex = currentButton == null ? -1 : menuButtons.IndexOf(currentButton);
			int nextIndex = currentIndex < 0 ? 0 : currentIndex + (action == DashboardInputAction.MoveDown ? 1 : -1);
			nextIndex = Math.Max(0, Math.Min(menuButtons.Count - 1, nextIndex));
			return TryFocus(menuButtons[nextIndex]);
		}

		private static UIElement? FindFocusableControl(DependencyObject? root)
		{
			return FindVisualChildren<UIElement>(root).FirstOrDefault((UIElement element) => element is Avalonia.Controls.Control && element.IsEffectivelyVisible && element.IsEnabled && element.Focusable);
		}

		private Avalonia.Controls.Button? FindThemeMenuFocusButton()
		{
			List<Avalonia.Controls.Button> themeButtons = FindVisualChildren<Avalonia.Controls.Button>((DependencyObject?)(object)ThemeMenuOverlay)
				.Where((Avalonia.Controls.Button button) => button.IsEffectivelyVisible && button.IsEnabled && button.Focusable && button.Command == _viewModel.SelectThemeCommand)
				.ToList();
			if (themeButtons.Count == 0)
			{
				return null;
			}
			if (_viewModel.SelectedTheme != null)
			{
				Avalonia.Controls.Button? selectedButton = themeButtons.FirstOrDefault((Avalonia.Controls.Button button) => button.CommandParameter is DashboardTheme theme && string.Equals(theme.Name, _viewModel.SelectedTheme.Name, StringComparison.OrdinalIgnoreCase));
				if (selectedButton != null)
				{
					return selectedButton;
				}
			}
			return themeButtons[0];
		}

		private static T? FindVisualChild<T>(DependencyObject? root) where T : DependencyObject
		{
			if (root == null)
			{
				return default(T);
			}
			int childrenCount;
			try
			{
				childrenCount = VisualTreeHelper.GetChildrenCount((Visual)root);
			}
			catch
			{
				return default(T);
			}
			for (int i = 0; i < childrenCount; i++)
			{
				DependencyObject child;
				try
				{
					child = VisualTreeHelper.GetChild((Visual)root, i);
				}
				catch
				{
					continue;
				}
				T val = (T)(object)((child is T) ? child : null);
				if (val != null)
				{
					return val;
				}
				T val2 = FindVisualChild<T>(child);
				if (val2 != null)
				{
					return val2;
				}
			}
			return default(T);
		}

		private static DependencyObject? GetParentObject(DependencyObject current)
		{
			if (current is Visual visual)
			{
				return VisualTreeHelper.GetParent(visual);
			}
			return (current as StyledElement)?.Parent;
		}

		private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? root) where T : DependencyObject
		{
			if (root == null)
			{
				yield break;
			}
			int childCount;
			try
			{
				childCount = VisualTreeHelper.GetChildrenCount((Visual)root);
			}
			catch
			{
				yield break;
			}
			for (int i = 0; i < childCount; i++)
			{
				DependencyObject child;
				try
				{
					child = VisualTreeHelper.GetChild((Visual)root, i);
				}
				catch
				{
					continue;
				}
				T val = (T)(object)((child is T) ? child : null);
				if (val != null)
				{
					yield return val;
				}
				foreach (T item in FindVisualChildren<T>(child))
				{
					yield return item;
				}
			}
		}

		private void WritePerformanceDebugReport()
		{
			try
			{
				ImageCacheService.ImageCacheSnapshot snapshot = ImageCacheService.GetSnapshot();
				Process currentProcess = Process.GetCurrentProcess();
				bool num = _clockTimer.IsEnabled;
				DispatcherTimer? bootStateTimer = _bootStateTimer;
				int value = (num ? 1 : 0) + ((bootStateTimer != null && bootStateTimer.IsEnabled) ? 1 : 0) + (_performanceDebugTimer.IsEnabled ? 1 : 0) + (_viewModel.IsMusicProgressTimerActive ? 1 : 0) + (_controllerInputService.IsRunning ? 1 : 0) + (_guideViewModel?.ActiveTimerCount ?? 0);
				string[] contents = new string[13]
				{
					"[PERFORMANCE]",
					$"timestamp: {DateTime.Now:O}",
					$"ram working set: {(double)currentProcess.WorkingSet64 / 1024.0 / 1024.0:0.0} MB",
					$"ram private bytes: {(double)currentProcess.PrivateMemorySize64 / 1024.0 / 1024.0:0.0} MB",
					$"loaded image count: {snapshot.LoadedImageCount}",
					$"loaded cover count: {snapshot.LoadedCoverCount}",
					$"visible my games tiles: {_viewModel.VisibleLibraryMenuGames.Count}",
					$"largest cached image: {snapshot.LargestPixelWidth}x{snapshot.LargestPixelHeight}",
					$"active timers: {value}",
					"visualizer running: " + ((MusicVisualizer.ActiveRendererCount > 0) ? "yes" : "no"),
					$"visualizer instances active: {MusicVisualizer.ActiveRendererCount}",
					"audio analysis running: " + (_viewModel.IsAudioAnalysisRunning ? "yes" : "no"),
					"music playing: " + (_viewModel.IsMusicPlaying ? "yes" : "no")
				};
				Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PerformanceDebugLogPath));
				File.WriteAllLines(PerformanceDebugLogPath, contents);
			}
			catch (Exception exception)
			{
				App.LogException(exception, "MainWindow.WritePerformanceDebugReport");
			}
		}
	}
