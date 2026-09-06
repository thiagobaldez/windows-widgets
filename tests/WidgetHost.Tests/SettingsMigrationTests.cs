using System.Text.Json.Nodes;
using WidgetHost.Config;

namespace WidgetHost.Tests;

/// <summary>
/// Perder a posição e as opções que o usuário escolheu é o pior resultado
/// possível de uma migração — pior que não migrar.
/// </summary>
public class SettingsMigrationTests
{
    private static int _counter;
    private static string NextId() => $"id{++_counter}";

    private static JsonObject V1() => JsonNode.Parse("""
    {
      "startWithWindows": true,
      "widgets": [
        {
          "id": "weather",
          "enabled": true,
          "x": 2236,
          "y": 24,
          "locked": true,
          "options": {
            "latitude": -23.5505199,
            "longitude": -46.6333094,
            "locationName": "São Paulo, São Paulo",
            "useFahrenheit": false,
            "refreshMinutes": 10
          }
        }
      ]
    }
    """)!.AsObject();

    [Fact]
    public void V1_vira_v2_preservando_posicao_bloqueio_e_opcoes()
    {
        var migrated = SettingsMigration.Upgrade(V1(), NextId);

        Assert.Equal(SettingsMigration.CurrentVersion, migrated["version"]!.GetValue<int>());

        var widget = migrated["widgets"]!.AsArray()[0]!.AsObject();
        Assert.Equal("weather", widget["type"]!.GetValue<string>());
        Assert.Equal(2236, widget["x"]!.GetValue<int>());
        Assert.Equal(24, widget["y"]!.GetValue<int>());
        Assert.True(widget["locked"]!.GetValue<bool>());
        Assert.Equal(1.0, widget["scale"]!.GetValue<double>());

        // O bloco de opções é opaco para o host: tem que passar intacto.
        var options = widget["options"]!.AsObject();
        Assert.Equal(10, options["refreshMinutes"]!.GetValue<int>());
        Assert.Equal("São Paulo, São Paulo", options["locationName"]!.GetValue<string>());
    }

    [Fact]
    public void V1_ganha_instanceId_distinto_do_tipo()
    {
        var migrated = SettingsMigration.Upgrade(V1(), NextId);
        var widget = migrated["widgets"]!.AsArray()[0]!.AsObject();

        var instanceId = widget["instanceId"]!.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(instanceId));
        Assert.NotEqual("weather", instanceId);
    }

    [Fact]
    public void Widget_desligado_em_v1_desaparece_em_v2()
    {
        // Em v1 "fechado" era enabled:false. Em v2 é não estar na lista.
        var v1 = V1();
        v1["widgets"]!.AsArray()[0]!["enabled"] = false;

        var migrated = SettingsMigration.Upgrade(v1, NextId);

        Assert.Empty(migrated["widgets"]!.AsArray());
    }

    [Fact]
    public void Migrar_duas_vezes_nao_muda_nada_nem_gera_ids_novos()
    {
        var once = SettingsMigration.Upgrade(V1(), NextId);
        var firstId = once["widgets"]!.AsArray()[0]!["instanceId"]!.GetValue<string>();

        var twice = SettingsMigration.Upgrade(once, NextId);
        var secondId = twice["widgets"]!.AsArray()[0]!["instanceId"]!.GetValue<string>();

        Assert.Equal(firstId, secondId);
        Assert.Single(twice["widgets"]!.AsArray());
    }

    [Fact]
    public void V2_ganha_tamanho_medio_sem_perder_o_resto()
    {
        // Quem já tinha um widget na tela conhecia só o layout que hoje se
        // chama "médio". Promover para outro seria surpresa, não migração.
        var v2 = JsonNode.Parse("""
        {
          "version": 2,
          "startWithWindows": false,
          "widgets": [
            { "instanceId": "abc", "type": "weather", "x": 10, "y": 20,
              "scale": 1.6, "locked": true, "options": { "refreshMinutes": 30 } }
          ]
        }
        """)!.AsObject();

        var result = SettingsMigration.Upgrade(v2, NextId);
        var widget = result["widgets"]!.AsArray()[0]!.AsObject();

        Assert.Equal(3, result["version"]!.GetValue<int>());
        Assert.Equal("Medium", widget["size"]!.GetValue<string>());
        Assert.Equal("abc", widget["instanceId"]!.GetValue<string>());
        Assert.Equal(1.6, widget["scale"]!.GetValue<double>());
        Assert.True(widget["locked"]!.GetValue<bool>());
        Assert.Equal(30, widget["options"]!["refreshMinutes"]!.GetValue<int>());
    }

    [Fact]
    public void V1_pula_direto_para_v3_passando_pelos_dois_degraus()
    {
        var migrated = SettingsMigration.Upgrade(V1(), NextId);
        var widget = migrated["widgets"]!.AsArray()[0]!.AsObject();

        Assert.Equal(3, migrated["version"]!.GetValue<int>());
        Assert.Equal("weather", widget["type"]!.GetValue<string>());   // degrau v1->v2
        Assert.Equal("Medium", widget["size"]!.GetValue<string>());    // degrau v2->v3
    }

    [Fact]
    public void Arquivo_ja_em_v3_passa_intacto()
    {
        var v3 = JsonNode.Parse("""
        {
          "version": 3,
          "widgets": [
            { "instanceId": "abc", "type": "weather", "size": "Large",
              "scale": 1.0, "locked": false, "options": {} }
          ]
        }
        """)!.AsObject();

        var result = SettingsMigration.Upgrade(v3, NextId);

        Assert.Equal("Large", result["widgets"]!.AsArray()[0]!["size"]!.GetValue<string>());
    }

    [Fact]
    public void V1_sem_lista_de_widgets_nao_estoura()
    {
        var v1 = JsonNode.Parse("""{ "startWithWindows": false }""")!.AsObject();

        var migrated = SettingsMigration.Upgrade(v1, NextId);

        Assert.Equal(SettingsMigration.CurrentVersion, migrated["version"]!.GetValue<int>());
    }

    [Fact]
    public void V1_sem_posicao_salva_migra_com_posicao_nula()
    {
        // Widget que nunca chegou a ser posicionado: o host decide o lugar depois.
        var v1 = JsonNode.Parse("""
        { "widgets": [ { "id": "weather", "enabled": true, "options": {} } ] }
        """)!.AsObject();

        var migrated = SettingsMigration.Upgrade(v1, NextId);
        var widget = migrated["widgets"]!.AsArray()[0]!.AsObject();

        Assert.Equal("weather", widget["type"]!.GetValue<string>());
        Assert.Null(widget["x"]);
        Assert.Null(widget["y"]);
    }
}

