using Magpie.Models.Data;

namespace Magpie.QueryLanguage;

internal sealed class ContentTransformContext
{
    private ContentHierarchyItemCriteria? _contentItem;
    private MetadataCriteria? _metadata;

    public ContentHierarchyItemCriteria ContentItem => _contentItem ??= new();
    public MetadataCriteria Metadata => _metadata ??= new();

    public ContentFilterCriteria Build()
    {
        var result = new ContentFilterCriteria();

        if (_contentItem != null || _metadata != null)
        {
            var item = _contentItem ?? new();
            if (_metadata != null)
            {
                item = item with
                {
                    Metadata = new() { And = [_metadata] }
                };
            }

            result = result with
            {
                ContentHierarchyItem = new() { And = [item] }
            };
        }

        return result;
    }

    public void SetContentItem(ContentHierarchyItemCriteria criteria)
    {
        _contentItem = criteria;
    }

    public void SetMetadata(MetadataCriteria criteria)
    {
        _metadata = criteria;
    }
}
