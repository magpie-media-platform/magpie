using System.Collections.Generic;

using Magpie.Models;
using Magpie.Models.Enums;
using Magpie.Sdk.Configuration;

namespace Magpie.Extensions.Cardigann;

internal sealed class CardigannSettingsSchemaProvider : IExtensionSettingsSchemaProvider
{
    private static readonly ExtensionSettingsGroupDefinition SolverGroup = new()
    {
        ExtensionId = Constants.ExtensionId,
        Name = "magpie.cardigann",
        SettingsSchema =
        [
            new SettingSchemaDefinition
            {
                Key = "flareSolverrUrl",
                Type = DataType.String,
                DisplayName = "FlareSolverr URL",
                Description =
                    "The address of a FlareSolverr instance. Cardigann uses it to solve the Cloudflare " +
                    "challenge of a tracker. Keep the value empty to disable the solver.",
                Placeholder = "http://localhost:8191",
            },
        ],
    };

    public IEnumerable<ExtensionSettingsGroupDefinition> GetSettingsGroups()
    {
        return [SolverGroup];
    }
}
