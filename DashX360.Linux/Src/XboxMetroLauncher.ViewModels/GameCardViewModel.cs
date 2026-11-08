using System;
using Avalonia.Media;
using XboxMetroLauncher.Models;

namespace XboxMetroLauncher.ViewModels;

public sealed class GameCardViewModel : ObservableObject
{
	public GameMetadata Game { get; }

	public string Title => Game.Title;

	public string TileTitle => BuildTileTitle(Game.Title);

	public string Subtitle
	{
		get
		{
			if (!IsSteamGame)
			{
				return "PC - Manual";
			}
			return "Steam - Imported";
		}
	}

	public string CoverArtPath => Game.CoverArtPath;

	public double CoverZoom => Game.CoverZoom;

	public double CoverOffsetX => Game.CoverOffsetX;

	public double CoverOffsetY => Game.CoverOffsetY;

	public string BackgroundArtPath => Game.BackgroundArtPath;

	public string DetailsStoreImagePath
	{
		get
		{
			if (!IsSteamGame || string.IsNullOrWhiteSpace(Game.StoreScreenshotPath))
			{
				return Game.HeaderImagePath;
			}
			return Game.StoreScreenshotPath;
		}
	}

	public bool IsFavorite => Game.IsFavorite;

	public bool IsSteamGame => string.Equals(Game.LaunchType, "Steam", StringComparison.OrdinalIgnoreCase);

	public bool IsManualGame => !IsSteamGame;

	public string DetailsSourceText
	{
		get
		{
			if (!IsSteamGame)
			{
				return "Manual";
			}
			return "Steam Imported";
		}
	}

	public string DetailsRatingLabel
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Game.Rating))
			{
				return Game.Rating.Trim().ToUpperInvariant();
			}
			return "NR";
		}
	}

	public string DetailsRatingDescription
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Game.Rating))
			{
				return DetailsRatingLabel + " rating information from Steam.";
			}
			if (!IsSteamGame)
			{
				return "Rating not provided. User-added games may include content from third-party stores.";
			}
			return "Rating not provided by Steam for this game.";
		}
	}

	public Brush AccentBrush { get; }

	public string DetailsGenreText
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Game.Genre) && !string.Equals(Game.Genre, "Imported", StringComparison.OrdinalIgnoreCase))
			{
				return Game.Genre;
			}
			return "Game";
		}
	}

	public string DetailsMultiplayerText
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Game.MultiplayerInfo))
			{
				return Game.MultiplayerInfo;
			}
			return "Multiplayer: None";
		}
	}

	public string DetailsCoOpText
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Game.CoOpInfo))
			{
				return Game.CoOpInfo;
			}
			return "Co-op: None";
		}
	}

	public string DetailsPlaytimeText
	{
		get
		{
			if (Game.Playtime <= TimeSpan.Zero)
			{
				return "Time played: Not tracked";
			}
			int num = (int)Game.Playtime.TotalHours;
			if (num > 0)
			{
				return $"Time played: {num}h {Game.Playtime.Minutes}m";
			}
			return $"Time played: {Game.Playtime.Minutes}m";
		}
	}

	public string DetailsReviewStarsText
	{
		get
		{
			int num = (int)Math.Round(Math.Clamp(Game.ReviewStarRating, 0.0, 5.0), MidpointRounding.AwayFromZero);
			return new string('★', num) + new string('☆', 5 - num);
		}
	}

	public string DetailsReviewCountText
	{
		get
		{
			if (Game.ReviewCount <= 0)
			{
				return string.Empty;
			}
			return $"({Game.ReviewCount:N0})";
		}
	}

	public GameCardViewModel(GameMetadata game, Brush accentBrush)
	{
		Game = game;
		AccentBrush = accentBrush;
	}

	public void Refresh()
	{
		OnPropertyChanged("Title");
		OnPropertyChanged("TileTitle");
		OnPropertyChanged("Subtitle");
		OnPropertyChanged("CoverArtPath");
		OnPropertyChanged("CoverZoom");
		OnPropertyChanged("CoverOffsetX");
		OnPropertyChanged("CoverOffsetY");
		OnPropertyChanged("BackgroundArtPath");
		OnPropertyChanged("DetailsStoreImagePath");
		OnPropertyChanged("IsFavorite");
		OnPropertyChanged("IsSteamGame");
		OnPropertyChanged("IsManualGame");
		OnPropertyChanged("DetailsSourceText");
		OnPropertyChanged("DetailsRatingLabel");
		OnPropertyChanged("DetailsRatingDescription");
		OnPropertyChanged("DetailsGenreText");
		OnPropertyChanged("DetailsMultiplayerText");
		OnPropertyChanged("DetailsCoOpText");
		OnPropertyChanged("DetailsPlaytimeText");
		OnPropertyChanged("DetailsReviewStarsText");
		OnPropertyChanged("DetailsReviewCountText");
	}

	private static string BuildTileTitle(string title)
	{
		if (!string.IsNullOrWhiteSpace(title))
		{
			return title.Trim();
		}
		return string.Empty;
	}
}
