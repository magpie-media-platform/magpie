namespace Magpie.Extensions.Cardigann.Engine;

internal interface ICardigannLoader
{
    CardigannDefinition LoadString(string yaml, string origin);
}
