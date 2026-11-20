using TinaClass.App.Common.Sample;
using TinaClass.App.Common.Settings;
using TinaClass.App.Features.Models;
using Xunit;

namespace TinaClass.App.Tests.Features.Models;

/// <summary>The sample mode's persistence: a model key or a switch survives a restart (a new service reads the same store).</summary>
public class SampleStoreTests
{
    [Fact]
    public async Task A_model_key_survives_a_restart()
    {
        var store = new MemorySettingsStore();
        SampleStore.Store = () => store;
        try
        {
            var first = new SampleModelsService();
            var meridian = (await first.ProvidersAsync(TestContext.Current.CancellationToken)).Single(p => p.Name == "Meridian");
            await first.SetKeyAsync(meridian.Id, "sk-persist-9876", TestContext.Current.CancellationToken);

            SampleStore.Reset(); // the next service reads the store again, as a restarted app does
            var second = new SampleModelsService();
            var again = (await second.ProvidersAsync(TestContext.Current.CancellationToken)).Single(p => p.Name == "Meridian");

            Assert.True(again.KeySet);
            Assert.EndsWith("9876", again.KeyHint);
            Assert.Equal("ok", again.Status);
        }
        finally
        {
            SampleStore.Store = null;
            SampleStore.Reset();
        }
    }

    [Fact]
    public async Task A_disabled_model_stays_disabled_after_a_restart()
    {
        var store = new MemorySettingsStore();
        SampleStore.Store = () => store;
        try
        {
            var first = new SampleModelsService();
            var vega = (await first.ModelsAsync(cancellation: TestContext.Current.CancellationToken)).Single(m => m.Code == "vega-reasoner");
            await first.UpdateModelAsync(vega, enabled: false, isDefault: null, cancellation: TestContext.Current.CancellationToken);

            SampleStore.Reset();
            var second = new SampleModelsService();

            Assert.DoesNotContain(await second.ModelsAsync(cancellation: TestContext.Current.CancellationToken), m => m.Code == "vega-reasoner" && m.Enabled);
        }
        finally
        {
            SampleStore.Store = null;
            SampleStore.Reset();
        }
    }
}
