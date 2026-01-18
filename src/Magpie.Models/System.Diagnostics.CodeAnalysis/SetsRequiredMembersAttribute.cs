
using System.ComponentModel;

#pragma warning disable IDE0130
namespace System.Diagnostics.CodeAnalysis;
#pragma warning restore IDE0130

[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
internal sealed class SetsRequiredMembersAttribute : Attribute
{
}
