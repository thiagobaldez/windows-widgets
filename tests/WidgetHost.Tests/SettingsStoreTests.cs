using System;
using System.IO;
using System.Text.Json.Nodes;
using WidgetHost.Config;
using Widgets.Abstractions;

namespace WidgetHost.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"ww-settings-{Guid.NewGuid():N}.json");

    [Fact]
    public void Tamanho_e_gravado_como_texto_nao_como_numero()
    {
        // Número no JSON quebraria em silêncio se a ordem do enum mudasse.
        var store = new SettingsStore(_path);
        var settings = new HostSettings();
        settings.Widgets.Add(new WidgetInstanceSettings
        {
            InstanceId = "abc", Type = "weather", Size = WidgetSize.Large,
        });

        store.Save(settings);

        Assert.Contains("\"Large\"", File.ReadAllText(_path));
        Assert.Equal(WidgetSize.Large, store.Load().Widgets[0].Size);
    }

    [Fact]
    public void Round_trip_preserva_a_instancia()
    {
        var store = new SettingsStore(_path);
        var settings = new HostSettings { StartWithWindows = true };
        settings.Widgets.Add(new WidgetInstanceSettings
        {
            InstanceId = "abc",
            Type = "weather",
            X = 100,
            Y = 200,
            Size = WidgetSize.Small,
            Scale = 1.4,
            Locked = true,
            Options = new JsonObject { ["refreshMinutes"] = 30 },
        });

        store.Save(settings);
        var loaded = store.Load();

        var widget = Assert.Single(loaded.Widgets);
        Assert.Equal("abc", widget.InstanceId);
        Assert.Equal("weather", widget.Type);
        Assert.Equal(100, widget.X);
        Assert.Equal(WidgetSize.Small, widget.Size);
        Assert.Equal(1.4, widget.Scale);
        Assert.True(widget.Locked);
        Assert.Equal(30, widget.Options["refreshMinutes"]!.GetValue<int>());
        Assert.True(loaded.StartWithWindows);
    }

    [Fact]
    public void Arquivo_v1_no_disco_e_migrado_na_leitura()
    {
        File.WriteAllText(_path, """
        {
          "startWithWindows": false,
          "widgets": [
            { "id": "weather", "enabled": true, "x": 2236, "y": 24,
              "locked": false, "options": { "refreshMinutes": 10 } }
          ]
        }
        """);

        var loaded = new SettingsStore(_path).Load();

        var widget = Assert.Single(loaded.Widgets);
        Assert.Equal("weather", widget.Type);
        Assert.Equal(2236, widget.X);
        Assert.Equal(1.0, widget.Scale);
        Assert.False(string.IsNullOrWhiteSpace(widget.InstanceId));
    }

    [Fact]
    public void Migracao_e_gravada_no_disco_para_nao_repetir()
    {
        // Se a migração não fosse persistida, cada abertura geraria instanceIds
        // novos e a identidade da instância nunca estabilizaria.
        File.WriteAllText(_path, """
        { "widgets": [ { "id": "weather", "enabled": true, "options": {} } ] }
        """);

        var first = new SettingsStore(_path).Load().Widgets[0].InstanceId;
        var second = new SettingsStore(_path).Load().Widgets[0].InstanceId;

        Assert.Equal(first, second);
    }

    [Fact]
    public void Duas_instancias_do_mesmo_tipo_convivem()
    {
        var store = new SettingsStore(_path);
        var settings = new HostSettings();
        settings.Widgets.Add(new WidgetInstanceSettings { InstanceId = "a", Type = "weather", X = 10, Y = 10 });
        settings.Widgets.Add(new WidgetInstanceSettings { InstanceId = "b", Type = "weather", X = 500, Y = 10 });

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(2, loaded.Widgets.Count);
        Assert.Equal(new[] { "a", "b" }, Array.ConvertAll(loaded.Widgets.ToArray(), w => w.InstanceId));
    }

    [Fact]
    public void Json_corrompido_cai_no_default_em_vez_de_derrubar_o_app()
    {
        File.WriteAllText(_path, "{ isso nao e json");

        var loaded = new SettingsStore(_path).Load();

        Assert.Empty(loaded.Widgets);
    }

    [Fact]
    public void Arquivo_ausente_devolve_default()
    {
        var loaded = new SettingsStore(_path).Load();

        Assert.Empty(loaded.Widgets);
        Assert.False(loaded.StartWithWindows);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
