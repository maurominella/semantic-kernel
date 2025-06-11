// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel;

namespace AIPlugins;

public class LightsPlugin
{
    // Mock data for the lights
    private readonly List<LightModel> _lights = new()
        {
            new LightModel { Id = 1, Name = "Table Lamp", IsOn = false },
            new LightModel { Id = 2, Name = "Porch light", IsOn = false },
            new LightModel { Id = 3, Name = "Chandelier", IsOn = false }
        };

    [KernelFunction("reset_lights")]
    [Description("Reset the state of all lights")]
    [return: Description("An array of lights")]
    public async Task<List<LightModel>> ResetLightsAsync()
    {
        await Task.CompletedTask;  // This line removes the warning
        this._lights.ForEach(light => light.IsOn = false);
        return this._lights;
    }

    [KernelFunction("get_lights")]
    [Description("Gets a list of lights and their current state")]
    [return: Description("An array of lights")]
    public async Task<List<LightModel>> GetLightsAsync()
    {
        await Task.CompletedTask;  // This line removes the warning
        return this._lights;
    }

    [KernelFunction("change_state")]
    [Description("Changes the state of the light")]
    [return: Description("The updated state of the light; will return null if the light does not exist")]
    public async Task<LightModel?> ChangeStateAsync(int id, bool isOn)
    {
        var light = this._lights.FirstOrDefault(light => light.Id == id);

        if (light == null)
        {
            return null;
        }

        // Update the light with the new state
        light.IsOn = isOn;

        await Task.CompletedTask;  // This line removes the warning
        return light;
    }
}


public class LightModel
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("is_on")]
    public bool? IsOn { get; set; }
}