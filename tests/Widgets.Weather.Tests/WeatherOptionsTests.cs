using System.Text.Json.Nodes;
using Widgets.Weather;

namespace Widgets.Weather.Tests;

public class WeatherOptionsTests
{
    [Fact]
    public void Round_trip_preserva_todos_os_campos()
    {
        var original = new WeatherOptions
        {
            Latitude = -23.5505,
            Longitude = -46.6333,
            LocationName = "São Paulo — São Paulo, Brasil",
            UseFahrenheit = true,
            RefreshMinutes = 30,
        };

        var restored = WeatherOptions.FromJson(original.ToJson());

        Assert.Equal(original.Latitude, restored.Latitude);
        Assert.Equal(original.Longitude, restored.Longitude);
        Assert.Equal(original.LocationName, restored.LocationName);
        Assert.Equal(original.UseFahrenheit, restored.UseFahrenheit);
        Assert.Equal(original.RefreshMinutes, restored.RefreshMinutes);
    }

    [Fact]
    public void Bloco_ausente_vira_default_utilizavel()
    {
        var options = WeatherOptions.FromJson(null);

        Assert.False(options.HasCoordinates);
        Assert.Equal(15, options.RefreshMinutes);
    }

    [Fact]
    public void Bloco_com_lixo_nao_derruba_o_widget()
    {
        // Campo com tipo errado: cai no default em vez de estourar na inicialização.
        var junk = new JsonObject { ["latitude"] = "isso não é número" };

        var options = WeatherOptions.FromJson(junk);

        Assert.False(options.HasCoordinates);
        Assert.Equal(15, options.RefreshMinutes);
    }

    [Fact]
    public void HasCoordinates_exige_latitude_E_longitude()
    {
        Assert.False(new WeatherOptions { Latitude = -23.5 }.HasCoordinates);
        Assert.False(new WeatherOptions { Longitude = -46.6 }.HasCoordinates);
        Assert.True(new WeatherOptions { Latitude = -23.5, Longitude = -46.6 }.HasCoordinates);
    }
}


