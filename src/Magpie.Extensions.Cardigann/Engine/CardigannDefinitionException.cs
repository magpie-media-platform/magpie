using System;

namespace Magpie.Extensions.Cardigann.Engine;

internal sealed class CardigannDefinitionException : Exception
{
    public CardigannDefinitionException(string message, string definitionPath) : base($"{definitionPath}: {message}")
    {
    }

    public CardigannDefinitionException(string message, string definitionPath, Exception? innerException)
        : base($"{definitionPath}: {message}", innerException)
    {
    }
}
