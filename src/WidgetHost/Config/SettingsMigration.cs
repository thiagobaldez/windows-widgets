using System;
using System.Text.Json.Nodes;

namespace WidgetHost.Config;

/// <summary>
/// Sobe o settings.json de versões antigas para a atual.
///
/// <para>Função pura sobre <see cref="JsonObject"/> para poder ser testada sem
/// tocar em disco. Perder a posição e as opções que o usuário escolheu é o
/// pior resultado possível de uma migração, então cada passo preserva
/// explicitamente o que já existia.</para>
/// </summary>
public static class SettingsMigration
{
    public const int CurrentVersion = 3;

    /// <summary>
    /// v1 não tinha campo <c>version</c>: as entradas eram chaveadas por
    /// <c>id</c> (o tipo) e desligadas por <c>enabled:false</c>. v2 separa
    /// <c>type</c> de <c>instanceId</c> para permitir mais de uma instância do
    /// mesmo widget, e ganha <c>scale</c>.
    /// </summary>
    public static JsonObject Upgrade(JsonObject root, Func<string> newInstanceId)
    {
        var version = root["version"]?.GetValue<int>() ?? 1;
        if (version >= CurrentVersion) return root;

        if (version < 2) root = UpgradeV1ToV2(root, newInstanceId);
        if (version < 3) root = UpgradeV2ToV3(root);

        root["version"] = CurrentVersion;
        return root;
    }

    /// <summary>
    /// v3 introduz variantes de layout (pequeno/médio/grande). Quem já tinha um
    /// widget na tela conhecia só o layout que hoje se chama "médio" — mudar o
    /// que a pessoa já via seria uma surpresa, não uma migração.
    /// </summary>
    private static JsonObject UpgradeV2ToV3(JsonObject root)
    {
        if (root["widgets"] is not JsonArray widgets) return root;

        foreach (var node in widgets)
        {
            if (node is JsonObject widget && widget["size"] is null)
                widget["size"] = "Medium";
        }

        return root;
    }

    private static JsonObject UpgradeV1ToV2(JsonObject root, Func<string> newInstanceId)
    {
        if (root["widgets"] is not JsonArray widgets) return root;

        var migrated = new JsonArray();

        foreach (var node in widgets)
        {
            if (node is not JsonObject widget) continue;

            // v1 desligava widget com enabled:false. Em v2, não estar na lista
            // é o que significa "fechado" — então a entrada desaparece.
            if (widget["enabled"] is JsonNode enabled && enabled.GetValue<bool>() == false)
                continue;

            var entry = new JsonObject
            {
                ["instanceId"] = newInstanceId(),
                ["type"] = widget["id"]?.DeepClone() ?? widget["type"]?.DeepClone(),
                ["x"] = widget["x"]?.DeepClone(),
                ["y"] = widget["y"]?.DeepClone(),
                ["size"] = "Medium",
                ["scale"] = 1.0,
                ["locked"] = widget["locked"]?.DeepClone() ?? false,
                ["options"] = widget["options"]?.DeepClone() ?? new JsonObject(),
            };

            migrated.Add(entry);
        }

        root["widgets"] = migrated;
        return root;
    }
}
