![Magpie](logo.png)

# Books extension for Magpie

[Magpie](https://github.com/magpie-media-platform/magpie) is a self-hosted media manager for any media type, including movies, TV shows, music, and books, with the ability to add more through extensions.

The Books extension adds books to Magpie as a content type, with two separate [top-level levels](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/collection.md#content-types-and-hierarchy): **E-book** and **Audiobook**.
E-books accept EPUB, MOBI, AZW, AZW3, and PDF files. Audiobooks accept audio files such as M4B, MP3, and FLAC.
The default content subfolders are `ebooks` and `audiobooks`.

## Configuring a format and its quality

Books needs a metadata provider, normally [Open Library](https://github.com/magpie-media-platform/magpie/blob/main/src/Magpie.Extensions.OpenLibrary/README.md), and a Book [root folder](https://github.com/magpie-media-platform/magpie/blob/main/docs/administration/storage-and-services.md#adding-a-root-folder).

The extension supplies three e-book quality tier lists: **E-book - Retail and Reflowable**, **E-book - Broad Compatibility** (EPUB first), and **E-book - Archive and Scans** (scanned or OCR PDF [releases](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/search-and-download.md)).
Audiobooks use an audio quality tier list.

## Checking identity before import

Open Library results identify works, not editions. When you [add a book](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/collection.md#adding-a-title), the author, language, [edition identifiers](https://github.com/magpie-media-platform/magpie/blob/main/src/Magpie.Extensions.OpenLibrary/README.md#identifying-a-work-and-its-edition), and format tell editions apart.
In [Library import](https://github.com/magpie-media-platform/magpie/blob/main/docs/using/files.md#importing-existing-files), check that a reading format is not matched to the Audiobook item. Correct a wrong edition or format in the [metadata](https://github.com/magpie-media-platform/magpie/blob/main/docs/power-user/metadata.md) or the association.

---

[First-party extensions](https://github.com/magpie-media-platform/magpie/blob/main/docs/extensions.md) | [Documentation](https://github.com/magpie-media-platform/magpie/blob/main/docs/README.md)
