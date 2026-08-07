using System.ComponentModel;

#pragma warning disable IDE0130
namespace System.Runtime.CompilerServices;
#pragma warning restore IDE0130

[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.All)]
internal sealed class RequiredMemberAttribute : Attribute
{
}
