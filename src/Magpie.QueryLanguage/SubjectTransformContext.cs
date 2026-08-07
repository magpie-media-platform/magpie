using Magpie.Models.Data;

namespace Magpie.QueryLanguage;

internal sealed class SubjectTransformContext
{
    private SubjectFilterCriteria? _subject;
    private MetadataCriteria? _metadata;

    public SubjectFilterCriteria Subject => _subject ??= new();
    public MetadataCriteria Metadata => _metadata ??= new();

    public SubjectFilterCriteria Build()
    {
        var result = _subject ?? new SubjectFilterCriteria();

        if (_metadata != null)
        {
            result = result with
            {
                Metadata = new() { And = [_metadata] }
            };
        }

        return result;
    }

    public void SetSubject(SubjectFilterCriteria criteria)
    {
        _subject = criteria;
    }

    public void SetMetadata(MetadataCriteria criteria)
    {
        _metadata = criteria;
    }
}
