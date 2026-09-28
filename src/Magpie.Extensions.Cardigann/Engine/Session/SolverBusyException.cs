using System;

namespace Magpie.Extensions.Cardigann.Engine.Session;

internal sealed class SolverBusyException() : Exception("The challenge solver is busy.");
