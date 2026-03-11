using Microsoft.Extensions.AI;
using SkiaSharp;

namespace llmaid;

/// <summary>
/// Handles image detection, MIME type resolution, and image loading/resizing for multimodal chat messages.
/// </summary>
internal static class ImageHelper
{
	private static readonly HashSet<string> _imageExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".svg", ".ico", ".avif"
	};

	/// <summary>
	/// Checks if a file is an image based on its extension.
	/// </summary>
	internal static bool IsImageFile(string filePath)
	{
		return _imageExtensions.Contains(Path.GetExtension(filePath));
	}

	/// <summary>
	/// Gets the MIME type for an image file based on its extension.
	/// </summary>
	internal static string GetMimeType(string extension)
	{
		return extension.ToLowerInvariant() switch
		{
			".png" => "image/png",
			".jpg" or ".jpeg" => "image/jpeg",
			".webp" => "image/webp",
			".gif" => "image/gif",
			".bmp" => "image/bmp",
			".svg" => "image/svg+xml",
			".ico" => "image/x-icon",
			".avif" => "image/avif",
			_ => "application/octet-stream"
		};
	}

	/// <summary>
	/// Loads and resizes an image file, returning it as a <see cref="DataContent"/> for multimodal chat messages.
	/// Always resizes to fit within <paramref name="maxDimension"/> while preserving aspect ratio.
	/// </summary>
	internal static async Task<DataContent> LoadContentAsync(string filePath, int maxDimension, CancellationToken cancellationToken)
	{
		var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
		var extension = Path.GetExtension(filePath).ToLowerInvariant();

		// SVG files can't be resized with SkiaSharp — pass through as-is
		if (extension == ".svg")
			return new DataContent(bytes, "image/svg+xml");

		using var originalBitmap = SKBitmap.Decode(bytes);
		if (originalBitmap == null)
			return new DataContent(bytes, GetMimeType(extension));

		var width = originalBitmap.Width;
		var height = originalBitmap.Height;

		// No resize needed — return original bytes with correct MIME type
		if (width <= maxDimension && height <= maxDimension)
			return new DataContent(bytes, GetMimeType(extension));

		// Calculate new dimensions preserving aspect ratio
		var scale = Math.Min((float)maxDimension / width, (float)maxDimension / height);
		var newWidth = (int)(width * scale);
		var newHeight = (int)(height * scale);

		using var resizedBitmap = originalBitmap.Resize(new SKImageInfo(newWidth, newHeight), SKSamplingOptions.Default);
		using var image = SKImage.FromBitmap(resizedBitmap);
		using var encodedData = image.Encode(SKEncodedImageFormat.Jpeg, 85);

		return new DataContent(encodedData.ToArray(), "image/jpeg");
	}
}
