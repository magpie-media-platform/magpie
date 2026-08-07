using System.Collections.Generic;
using System.Linq;

using Magpie.Models.Data;

namespace Magpie.Extensions.Wikidata.Mapping;

internal static class ImageMappingExtensions
{
    public static IReadOnlyList<Image> ToContentArtwork(
        this string? posterFilename,
        string? imageFilename,
        string? logoFilename)
    {
        var images = new List<Image>();
        images.AddImage(posterFilename, "poster", primary: true);
        if (images.Count > 0)
        {
            images.AddImage(imageFilename, "backdrop", primary: false);
        }
        else
        {
            images.AddImage(imageFilename, "poster", primary: true);
        }

        images.AddImage(logoFilename, "logo", primary: false);
        return images;
    }

    public static void AddImage(this ICollection<Image> images, string? filename, string type, bool primary)
    {
        var uri = filename.CommonsUri();
        if (uri is null || images.Any(image => image.Url == uri))
        {
            return;
        }

        images.Add(new Image
        {
            Type = type,
            Url = uri,
            Source = Constants.ProviderType,
            IsPrimary = primary,
            Ordinal = images.Count
        });
    }
}
