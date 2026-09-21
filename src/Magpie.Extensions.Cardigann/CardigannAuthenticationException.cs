using System;

namespace Magpie.Extensions.Cardigann;

internal sealed class CardigannAuthenticationException(string message) : Exception(message)
{
}
