using Avalonia.Headless.XUnit;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Common.Ui;
using TinaClass.App.Features.Models;
using TinaClass.Contracts.Models;
using Xunit;

namespace TinaClass.App.Tests.Features.Models;

/// <summary>The models page and its service: providers with their status, keys that only say whether they are set, the model table.</summary>
public class ModelsTests
{
    private static ModelsViewModel Open(string language = "en-US")
    {
        Ui.Open(language);
        Ui.Go(AppPages.Models);
        return Ui.Service<ModelsViewModel>();
    }

    [AvaloniaFact]
    public void The_page_lists_the_providers_and_the_models_with_their_status()
    {
        var models = Open();
        Assert.True(Ui.Until(() => models.Providers.Count > 0));

        Assert.Equal(5, models.Providers.Count);
        Assert.Equal(6, models.Models.Count);
        // One provider has no key: its status says so, in a warning tone.
        var missing = models.Providers.Single(p => p.Dto.Status == "no_key");
        Assert.Equal("Meridian", missing.Name);
        Assert.Equal(StatusTone.Warning, missing.Tone);
        // The built-in Echo provider is last and is described as offline.
        Assert.Equal(ProviderProtocols.Echo, models.Providers[^1].Dto.Protocol);
    }

    [AvaloniaFact]
    public async Task A_key_goes_in_and_only_its_last_characters_come_back()
    {
        var models = Open();
        Assert.True(Ui.Until(() => models.Providers.Count > 0));
        var meridian = models.Providers.Single(p => p.Name == "Meridian");

        await models.SetKeyAsync(meridian.Id, "sk-secret-value-1234");

        Assert.True(meridian.Dto.KeySet);
        Assert.EndsWith("1234", meridian.Dto.KeyHint);
        Assert.DoesNotContain("secret", meridian.Dto.KeyHint);
        Assert.Equal("ok", meridian.Dto.Status);

        await models.ClearKeyAsync(meridian.Id);
        Assert.False(meridian.Dto.KeySet);
        Assert.Null(meridian.Dto.KeyHint);
        Assert.Equal("no_key", meridian.Dto.Status);
    }

    [AvaloniaFact]
    public async Task Testing_a_provider_without_a_key_fails_with_key_required()
    {
        Open();
        var result = await Ui.Service<IModelsService>().TestAsync(TinaClass.Contracts.Sample.SampleIds.Provider(4));
        Assert.False(result.Ok);
        Assert.Equal("key_required", result.Error);
    }

    [AvaloniaFact]
    public async Task A_model_can_be_made_the_default_and_the_chat_follows()
    {
        var models = Open();
        Assert.True(Ui.Until(() => models.Models.Count > 0));
        var flash = models.Models.Single(m => m.Code == "aurora-flash");
        Assert.False(flash.IsDefault);

        await flash.MakeDefaultAsync();

        Assert.True(flash.IsDefault);
        Assert.Single(models.Models, m => m.IsDefault);
        Assert.Equal("aurora-flash", (await Ui.Service<IModelsService>().ModelsAsync()).Single(m => m.IsDefault).Code);
    }

    [AvaloniaFact]
    public void A_disabled_model_is_not_offered_to_the_chat()
    {
        var models = Open();
        Assert.True(Ui.Until(() => models.Models.Count > 0));

        models.Models.Single(m => m.Code == "vega-reasoner").IsEnabled = false;

        Assert.True(Ui.Until(() => !Ui.Service<IModelsService>().ModelsAsync().Result.Any(m => m.Code == "vega-reasoner")));
    }

    [AvaloniaFact]
    public void The_amounts_follow_the_currency_option()
    {
        var models = Open();
        Assert.True(Ui.Until(() => models.Models.Count > 0));
        Assert.StartsWith("¥", models.Models.Single(m => m.Code == "aurora-pro").Price);

        TinaClass.Contracts.Common.Money.Configure("USD");
        try
        {
            Assert.StartsWith("$", models.Models.Single(m => m.Code == "aurora-pro").Price);
            Assert.Equal("$12.85", TinaClass.Contracts.Common.Money.Format(1285));
        }
        finally
        {
            TinaClass.Contracts.Common.Money.Configure("CNY");
        }
        Assert.Equal("¥12.85", TinaClass.Contracts.Common.Money.Format(1285));
    }
}
