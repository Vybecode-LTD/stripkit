using FluentAssertions;
using NSubstitute;
using StripKit.Models;
using StripKit.Services;
using StripKit.ViewModels;
using Xunit;

namespace StripKit.Tests;

/// <summary>
/// The Generate tab's view model, with a mocked generation service and a real encrypted secret store
/// + real importer. Covers the key-gating, per-provider key save/reload, the success path (which
/// validates by importing the SVG and then enables the Create handoff), and the two failure paths
/// (service error, and a reply that won't import). The Avalonia preview bitmap can't render under a
/// plain unit test, so it's best-effort null — the result state is what's asserted.
/// </summary>
public class GenerateViewModelTests
{
    const string LayeredKnobSvg =
        """
        <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100" viewBox="0 0 100 100">
          <g id="body"><circle cx="50" cy="50" r="40" fill="#333"/></g>
          <g id="pointer"><line x1="50" y1="50" x2="50" y2="12" stroke="#fff" stroke-width="6"/></g>
        </svg>
        """;

    static (GenerateViewModel vm, IAssetGenerationService gen, DpapiSecretStore secrets, string[] temps) Build()
    {
        var secretsPath = Path.Combine(Path.GetTempPath(), $"stripkit_secrets_{Guid.NewGuid():N}.dat");
        var settingsPath = Path.Combine(Path.GetTempPath(), $"stripkit_settings_{Guid.NewGuid():N}.json");

        var gen = Substitute.For<IAssetGenerationService>();
        gen.AvailableProviders.Returns([AiProvider.Claude, AiProvider.OpenAI, AiProvider.Gemini]);
        gen.DefaultModelFor(Arg.Any<AiProvider>()).Returns(ci => $"default-{ci.Arg<AiProvider>()}");
        gen.ModelsFor(Arg.Any<AiProvider>()).Returns(["test-model", "test-alt"]);

        var secrets = new DpapiSecretStore(secretsPath);
        var vm = new GenerateViewModel(gen, secrets, new SettingsService(settingsPath),
                                       new LayeredImportService(), Substitute.For<IFileDialogService>(),
                                       Substitute.For<IKitBuilder>());
        return (vm, gen, secrets, [secretsPath, settingsPath]);
    }

    static void Cleanup(string[] paths)
    {
        foreach (var p in paths)
            try { if (File.Exists(p)) File.Delete(p); } catch { /* best-effort */ }
    }

    static void StubReply(IAssetGenerationService gen, GenerationResult result) =>
        gen.GenerateAsync(Arg.Any<GenerationRequest>(), Arg.Any<AiProvider>(), Arg.Any<string>(),
                          Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(result);

    // Stubs GenerateSetAsync to return one successful item per requested type, each carrying the given
    // SVG (a real layered SVG so the VM's import-based preview succeeds against the real importer).
    static void StubSet(IAssetGenerationService gen, string svg) =>
        gen.GenerateSetAsync(Arg.Any<GenerationRequest>(), Arg.Any<IReadOnlyList<ComponentType>>(),
                             Arg.Any<AiProvider>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(ci =>
           {
               var types = ci.Arg<IReadOnlyList<ComponentType>>();
               IReadOnlyList<GenerationSetItem> items =
                   types.Select(t => new GenerationSetItem(t, GenerationResult.Ok(svg))).ToList();
               return Task.FromResult(items);
           });

    // Stubs GenerateVariationsAsync to return `count` successful takes carrying the given SVG.
    static void StubVariations(IAssetGenerationService gen, string svg) =>
        gen.GenerateVariationsAsync(Arg.Any<GenerationRequest>(), Arg.Any<int>(),
                                    Arg.Any<AiProvider>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(ci =>
           {
               int count = ci.ArgAt<int>(1);
               IReadOnlyList<GenerationResult> r = Enumerable.Range(0, count).Select(_ => GenerationResult.Ok(svg)).ToList();
               return Task.FromResult(r);
           });

    [Fact]
    public void Generate_is_gated_on_a_present_api_key()
    {
        var (vm, _, _, temps) = Build();
        try
        {
            vm.GenerateCommand.CanExecute(null).Should().BeFalse("no key entered yet");
            vm.ApiKey = "sk-something";
            vm.GenerateCommand.CanExecute(null).Should().BeTrue();
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public void Saving_a_key_persists_it_and_switching_provider_reloads_per_provider()
    {
        var (vm, _, secrets, temps) = Build();
        try
        {
            vm.SelectedProvider = AiProvider.Claude;
            vm.ApiKey = "claude-key";
            vm.SaveKeyCommand.Execute(null);
            vm.HasKey.Should().BeTrue();
            secrets.Get(AiProvider.Claude).Should().Be("claude-key");

            vm.SelectedProvider = AiProvider.OpenAI;
            vm.ApiKey.Should().BeEmpty("no key is stored for OpenAI yet");
            vm.HasKey.Should().BeFalse();
            vm.Model.Should().Be("default-OpenAI", "the model resets to the provider default");

            vm.SelectedProvider = AiProvider.Claude;
            vm.ApiKey.Should().Be("claude-key", "switching back reloads the per-provider key");
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task A_successful_generation_produces_a_result_and_enables_the_create_handoff()
    {
        var (vm, gen, _, temps) = Build();
        try
        {
            StubReply(gen, GenerationResult.Ok(LayeredKnobSvg));
            vm.ApiKey = "sk-test";

            string? handedOff = null;
            ComponentType handedType = default;
            vm.UseInCreateRequested += (p, t) => { handedOff = p; handedType = t; };

            await vm.GenerateCommand.ExecuteAsync(null);

            vm.HasResult.Should().BeTrue(vm.StatusMessage);
            vm.GeneratedSvg.Should().Contain("pointer");
            vm.UseInCreateCommand.CanExecute(null).Should().BeTrue();
            vm.SaveSvgCommand.CanExecute(null).Should().BeTrue();

            vm.UseInCreateCommand.Execute(null);
            handedOff.Should().NotBeNull("the handoff fires with a path");
            handedType.Should().Be(ComponentType.RotaryKnob, "the knob default carries its type to Create");
            File.Exists(handedOff!).Should().BeTrue("the path is a real temp SVG the Create tab can import");
            File.ReadAllText(handedOff!).Should().Contain("<svg");
            File.Delete(handedOff!);
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task A_thrown_service_error_is_caught_and_surfaced_not_propagated()
    {
        // The broad catch in GenerateAsync exists so a generation can NEVER take the app down.
        // Make the service throw a raw exception and assert the command swallows it gracefully.
        var (vm, gen, _, temps) = Build();
        try
        {
            gen.GenerateAsync(Arg.Any<GenerationRequest>(), Arg.Any<AiProvider>(), Arg.Any<string>(),
                              Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns<Task<GenerationResult>>(_ => throw new InvalidOperationException("boom"));
            vm.ApiKey = "sk-test";

            var act = async () => await vm.GenerateCommand.ExecuteAsync(null);

            await act.Should().NotThrowAsync("a generation failure must never crash the app");
            vm.HasResult.Should().BeFalse();
            vm.StatusMessage.Should().Contain("Generation failed");
            vm.LastRawResponse.Should().Contain("boom", "the detail is kept for diagnosis");
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task A_custom_model_id_not_in_the_suggestions_is_honored()
    {
        // The model input is free-text (editable), so a model the user types — or a pinned one that
        // the provider later delists — must still be sent, not silently dropped to a suggestion.
        var (vm, gen, _, temps) = Build();
        try
        {
            string? sentModel = null;
            gen.GenerateAsync(Arg.Any<GenerationRequest>(), Arg.Any<AiProvider>(), Arg.Any<string>(),
                              Arg.Do<string>(m => sentModel = m), Arg.Any<CancellationToken>())
               .Returns(GenerationResult.Fail("stop here"));
            vm.ApiKey = "sk-test";
            vm.Model = "my-private-model-9000";   // not in SuggestedModels

            await vm.GenerateCommand.ExecuteAsync(null);

            sentModel.Should().Be("my-private-model-9000", "a typed/delisted model id is sent verbatim");
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task Generate_passes_the_type_body_accent_and_effects_into_the_request()
    {
        // VM-layer lock: the new colour + effect + control-type fields must reach the GenerationRequest
        // (the service test proves they reach the prompt; this proves the VM populates them at all).
        var (vm, gen, _, temps) = Build();
        try
        {
            GenerationRequest? captured = null;
            gen.GenerateAsync(Arg.Do<GenerationRequest>(r => captured = r), Arg.Any<AiProvider>(),
                              Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(GenerationResult.Fail("stop here"));   // fail early — we only assert the request
            vm.ApiKey = "sk-test";
            vm.GenerateControlType = ComponentType.Button;
            vm.BodyColorHex = "#102030";
            vm.AccentColorHex = "#A0B0C0";
            vm.HasDropShadow = true;
            vm.HasMetallicSheen = true;
            vm.CanvasSize = 768;
            vm.Style = GenerationStyle.Vintage;

            await vm.GenerateCommand.ExecuteAsync(null);

            captured.Should().NotBeNull();
            captured!.ComponentType.Should().Be(ComponentType.Button);
            captured.BodyColor.Should().Be("#102030");
            captured.AccentColor.Should().Be("#A0B0C0");
            captured.HasDropShadow.Should().BeTrue();
            captured.HasMetallicSheen.Should().BeTrue();
            captured.HasOuterGlow.Should().BeFalse();
            captured.CanvasSize.Should().Be(768);
            captured.Style.Should().Be(GenerationStyle.Vintage);
            captured.Layered.Should().BeTrue("a button is layered (off/on state groups)");
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task A_meter_is_requested_as_a_layered_off_on_pair()
    {
        var (vm, gen, _, temps) = Build();
        try
        {
            GenerationRequest? captured = null;
            gen.GenerateAsync(Arg.Do<GenerationRequest>(r => captured = r), Arg.Any<AiProvider>(),
                              Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(GenerationResult.Fail("stop here"));
            vm.ApiKey = "sk-test";
            vm.GenerateControlType = ComponentType.Meter;

            await vm.GenerateCommand.ExecuteAsync(null);

            captured.Should().NotBeNull();
            captured!.ComponentType.Should().Be(ComponentType.Meter);
            captured.Layered.Should().BeTrue("a meter is layered (off/on groups)");
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task A_toggle_is_requested_as_a_layered_off_on_pair()
    {
        var (vm, gen, _, temps) = Build();
        try
        {
            GenerationRequest? captured = null;
            gen.GenerateAsync(Arg.Do<GenerationRequest>(r => captured = r), Arg.Any<AiProvider>(),
                              Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(GenerationResult.Fail("stop here"));
            vm.ApiKey = "sk-test";
            vm.GenerateControlType = ComponentType.Toggle;

            await vm.GenerateCommand.ExecuteAsync(null);

            captured.Should().NotBeNull();
            captured!.ComponentType.Should().Be(ComponentType.Toggle);
            captured.Layered.Should().BeTrue("a toggle is layered (off/on groups)");
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task A_structurally_weak_knob_auto_retries_once()
    {
        var (vm, gen, _, temps) = Build();
        try
        {
            // First take: a knob body with no pointer (won't animate). Second: a proper body + pointer.
            const string noPointer =
                """<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100" viewBox="0 0 100 100"><g id="body"><circle cx="50" cy="50" r="40" fill="#333"/></g></svg>""";
            gen.GenerateAsync(Arg.Any<GenerationRequest>(), Arg.Any<AiProvider>(), Arg.Any<string>(),
                              Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(GenerationResult.Ok(noPointer), GenerationResult.Ok(LayeredKnobSvg));
            vm.ApiKey = "sk-test";
            vm.GenerateControlType = ComponentType.RotaryKnob;

            await vm.GenerateCommand.ExecuteAsync(null);

            await gen.Received(2).GenerateAsync(Arg.Any<GenerationRequest>(), Arg.Any<AiProvider>(), Arg.Any<string>(),
                                                Arg.Any<string>(), Arg.Any<CancellationToken>());
            vm.HasResult.Should().BeTrue();
            vm.GeneratedSvg.Should().Contain("pointer", "the good second take is kept");
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task A_well_formed_knob_does_not_retry()
    {
        var (vm, gen, _, temps) = Build();
        try
        {
            StubReply(gen, GenerationResult.Ok(LayeredKnobSvg));
            vm.ApiKey = "sk-test";
            vm.GenerateControlType = ComponentType.RotaryKnob;

            await vm.GenerateCommand.ExecuteAsync(null);

            await gen.Received(1).GenerateAsync(Arg.Any<GenerationRequest>(), Arg.Any<AiProvider>(), Arg.Any<string>(),
                                                Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task Refine_is_gated_then_updates_the_current_result()
    {
        var (vm, gen, _, temps) = Build();
        try
        {
            StubReply(gen, GenerationResult.Ok(LayeredKnobSvg));
            vm.ApiKey = "sk-test";
            await vm.GenerateCommand.ExecuteAsync(null);
            vm.HasResult.Should().BeTrue(vm.StatusMessage);

            vm.RefineCommand.CanExecute(null).Should().BeFalse("no instruction yet");

            gen.RefineAsync(Arg.Any<GenerationRequest>(), Arg.Any<string>(), Arg.Any<string>(),
                            Arg.Any<AiProvider>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(GenerationResult.Ok(LayeredKnobSvg));
            vm.RefineInstruction = "thicker pointer";
            vm.RefineCommand.CanExecute(null).Should().BeTrue();

            await vm.RefineCommand.ExecuteAsync(null);

            vm.HasResult.Should().BeTrue();
            vm.RefineInstruction.Should().BeEmpty("cleared after a successful refine");
        }
        finally { Cleanup(temps); }
    }

    // ---- prompt seeds ----

    [Fact]
    public void Seeds_start_with_the_built_in_library()
    {
        var (vm, _, _, temps) = Build();
        try
        {
            vm.Seeds.Should().NotBeEmpty();
            vm.Seeds.Should().OnlyContain(s => s.IsBuiltIn, "a fresh profile has only the built-ins");
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public void Applying_a_seed_fills_the_style_inputs()
    {
        var (vm, _, _, temps) = Build();
        try
        {
            vm.SelectedSeed = vm.Seeds.First(s => s.Name == "Vintage hardware");
            vm.ApplySeedCommand.Execute(null);

            vm.Style.Should().Be(GenerationStyle.Vintage);
            vm.AccentColorHex.Should().Be("#E0B050");
            vm.HasMetallicSheen.Should().BeTrue();
            vm.StyleNotes.Should().Contain("knurled");
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public void Saving_a_seed_adds_it_and_persists_it_then_delete_removes_it()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"stripkit_seed_{Guid.NewGuid():N}.json");
        var settings = new SettingsService(settingsPath);
        var gen = Substitute.For<IAssetGenerationService>();
        gen.AvailableProviders.Returns([AiProvider.Claude]);
        gen.ModelsFor(Arg.Any<AiProvider>()).Returns(["m"]);
        gen.DefaultModelFor(Arg.Any<AiProvider>()).Returns("m");
        var secretsPath = Path.Combine(Path.GetTempPath(), $"stripkit_seedsecrets_{Guid.NewGuid():N}.dat");
        try
        {
            var vm = new GenerateViewModel(gen, new DpapiSecretStore(secretsPath), settings,
                                           new LayeredImportService(), Substitute.For<IFileDialogService>(),
                                           Substitute.For<IKitBuilder>());
            vm.AccentColorHex = "#ABCDEF";
            vm.SeedName = "My Seed";
            vm.SaveSeedCommand.Execute(null);

            vm.Seeds.Should().Contain(s => s.Name == "My Seed" && !s.IsBuiltIn);
            settings.Settings.GenerateSeeds.Should().ContainSingle(s => s.Name == "My Seed");
            new SettingsService(settingsPath).Settings.GenerateSeeds.Should().ContainSingle(s => s.AccentColor == "#ABCDEF",
                "the seed persisted to disk");

            // A fresh VM reloads it, can select and delete it.
            var vm2 = new GenerateViewModel(gen, new DpapiSecretStore(secretsPath), settings,
                                            new LayeredImportService(), Substitute.For<IFileDialogService>(),
                                            Substitute.For<IKitBuilder>());
            vm2.SelectedSeed = vm2.Seeds.First(s => s.Name == "My Seed");
            vm2.DeleteSeedCommand.CanExecute(null).Should().BeTrue("a user seed can be deleted");
            vm2.DeleteSeedCommand.Execute(null);
            vm2.Seeds.Should().NotContain(s => s.Name == "My Seed");
        }
        finally
        {
            foreach (var p in new[] { settingsPath, secretsPath }) try { if (File.Exists(p)) File.Delete(p); } catch { }
        }
    }

    [Fact]
    public void A_built_in_seed_cannot_be_deleted()
    {
        var (vm, _, _, temps) = Build();
        try
        {
            vm.SelectedSeed = vm.Seeds.First(s => s.IsBuiltIn);
            vm.DeleteSeedCommand.CanExecute(null).Should().BeFalse("built-ins are read-only");
        }
        finally { Cleanup(temps); }
    }

    // ---- matching set ----

    [Fact]
    public void Generate_set_is_gated_on_a_key_and_at_least_one_chosen_type()
    {
        var (vm, _, _, temps) = Build();
        try
        {
            vm.GenerateSetCommand.CanExecute(null).Should().BeFalse("no key entered");
            vm.ApiKey = "sk-test";
            vm.GenerateSetCommand.CanExecute(null).Should().BeTrue("key + the default ticked types");
            foreach (var o in vm.SetTypeOptions) o.Include = false;
            vm.GenerateSetCommand.CanExecute(null).Should().BeFalse("nothing ticked");
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task Generate_set_builds_one_result_per_included_type_and_enables_save()
    {
        var (vm, gen, _, temps) = Build();
        try
        {
            StubSet(gen, LayeredKnobSvg);
            vm.ApiKey = "sk-test";
            int included = vm.SetTypeOptions.Count(o => o.Include);
            included.Should().BeGreaterThan(0);

            await vm.GenerateSetCommand.ExecuteAsync(null);

            vm.SetResults.Should().HaveCount(included);
            vm.SetResults.Select(r => r.ComponentType)
              .Should().Equal(vm.SetTypeOptions.Where(o => o.Include).Select(o => o.Type));
            vm.SetResults.Should().OnlyContain(r => r.IsSuccess);
            vm.HasSetResults.Should().BeTrue();
            vm.SaveSetCommand.CanExecute(null).Should().BeTrue();
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task Regenerating_a_set_item_after_a_cancel_still_works_and_does_not_abort()
    {
        var (vm, gen, _, temps) = Build();
        try
        {
            StubSet(gen, LayeredKnobSvg);
            StubReply(gen, GenerationResult.Ok(LayeredKnobSvg));   // Regenerate uses GenerateAsync
            vm.ApiKey = "sk-test";

            await vm.GenerateSetCommand.ExecuteAsync(null);
            var item = vm.SetResults.First();

            // A prior cancel leaves the shared set-CTS cancelled. Before the fix, Regenerate reused it
            // (??=) so this new action aborted immediately with "Regeneration cancelled."
            vm.CancelSetCommand.Execute(null);

            await vm.RegenerateSetItemCommand.ExecuteAsync(item);

            vm.SetStatus.Should().NotContainEquivalentOf("cancelled");
            vm.SetResults.Should().Contain(r => r.IsSuccess);
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task Using_a_set_item_hands_off_its_own_path_and_type()
    {
        var (vm, gen, _, temps) = Build();
        try
        {
            StubSet(gen, LayeredKnobSvg);
            string? handedPath = null;
            ComponentType handedType = default;
            vm.UseInCreateRequested += (p, t) => { handedPath = p; handedType = t; };
            vm.ApiKey = "sk-test";

            await vm.GenerateSetCommand.ExecuteAsync(null);
            var knob = vm.SetResults.First(r => r.ComponentType == ComponentType.RotaryKnob);
            vm.UseSetItemInCreateCommand.Execute(knob);

            handedPath.Should().Be(knob.SvgPath).And.NotBeNull();
            handedType.Should().Be(ComponentType.RotaryKnob, "each set member hands off as its own type");
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task Generate_variations_fills_the_grid_with_takes_of_the_selected_type()
    {
        var (vm, gen, _, temps) = Build();
        try
        {
            StubVariations(gen, LayeredKnobSvg);
            vm.ApiKey = "sk-test";
            vm.GenerateControlType = ComponentType.RotaryKnob;
            vm.VariationCount = 4;

            await vm.GenerateVariationsCommand.ExecuteAsync(null);

            vm.SetResults.Should().HaveCount(4);
            vm.SetResults.Should().OnlyContain(r => r.ComponentType == ComponentType.RotaryKnob);
            vm.SetResults.Select(r => r.Label).Should().Contain("Knob #1").And.Contain("Knob #4");
            vm.HasSetResults.Should().BeTrue();
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task A_failed_generation_shows_the_error_and_produces_no_result()
    {
        var (vm, gen, _, temps) = Build();
        try
        {
            StubReply(gen, GenerationResult.Fail("Claude: rate limited or out of quota."));
            vm.ApiKey = "sk-test";

            await vm.GenerateCommand.ExecuteAsync(null);

            vm.HasResult.Should().BeFalse();
            vm.StatusMessage.Should().Contain("rate limited");
            vm.UseInCreateCommand.CanExecute(null).Should().BeFalse();
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task A_reply_that_will_not_import_does_not_become_a_result()
    {
        var (vm, gen, _, temps) = Build();
        try
        {
            StubReply(gen, GenerationResult.Ok("this is not really an svg"));
            vm.ApiKey = "sk-test";

            await vm.GenerateCommand.ExecuteAsync(null);

            vm.HasResult.Should().BeFalse();
            vm.StatusMessage.Should().Contain("couldn't be read");
        }
        finally { Cleanup(temps); }
    }

    // ---- build kit (one-click) ----

    [Fact]
    public void Build_kit_is_gated_on_having_at_least_one_successful_set_result()
    {
        var (vm, _, _, temps) = Build();
        try
        {
            vm.BuildKitCommand.CanExecute(null).Should().BeFalse("no set generated yet");
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task Build_kit_gathers_the_successful_sources_and_invokes_the_builder_with_the_chosen_folder()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"stripkit_kit_{Guid.NewGuid():N}.json");
        var secretsPath = Path.Combine(Path.GetTempPath(), $"stripkit_kitsec_{Guid.NewGuid():N}.dat");
        var outDir = Path.Combine(Path.GetTempPath(), $"stripkit_kitout_{Guid.NewGuid():N}");
        try
        {
            var gen = Substitute.For<IAssetGenerationService>();
            gen.AvailableProviders.Returns([AiProvider.Claude]);
            gen.DefaultModelFor(Arg.Any<AiProvider>()).Returns("m");
            gen.ModelsFor(Arg.Any<AiProvider>()).Returns(["m"]);
            StubSet(gen, LayeredKnobSvg);

            var dialogs = Substitute.For<IFileDialogService>();
            dialogs.OpenFolderAsync(Arg.Any<string>()).Returns(outDir);

            var kit = Substitute.For<IKitBuilder>();
            kit.BuildAsync(Arg.Any<IReadOnlyList<KitControlSource>>(), Arg.Any<KitBuildOptions>(), Arg.Any<CancellationToken>())
               .Returns(ci => Task.FromResult(new KitBuildResult(
                   ci.Arg<IReadOnlyList<KitControlSource>>()
                     .Select(s => new KitControlResult { Type = s.Type, Success = true, AssetPath = "x.png" }).ToList(),
                   Path.Combine(outDir, "modern.skin.json"), outDir)));

            var vm = new GenerateViewModel(gen, new DpapiSecretStore(secretsPath), new SettingsService(settingsPath),
                                           new LayeredImportService(), dialogs, kit);
            vm.ApiKey = "sk-test";
            foreach (var o in vm.SetTypeOptions) o.Include = o.Type is ComponentType.RotaryKnob or ComponentType.Button;

            await vm.GenerateSetCommand.ExecuteAsync(null);
            int expected = vm.SetResults.Count(r => r.IsSuccess);
            expected.Should().Be(2);
            vm.BuildKitCommand.CanExecute(null).Should().BeTrue();

            await vm.BuildKitCommand.ExecuteAsync(null);

            await kit.Received(1).BuildAsync(
                Arg.Is<IReadOnlyList<KitControlSource>>(l =>
                    l.Count == expected
                    && l.Any(s => s.Type == ComponentType.RotaryKnob)
                    && l.Any(s => s.Type == ComponentType.Button)),
                Arg.Is<KitBuildOptions>(o => o.OutputDirectory == outDir),
                Arg.Any<CancellationToken>());
            vm.LastKitDirectory.Should().Be(outDir);
            vm.SetStatus.Should().Contain("skin.json");
        }
        finally
        {
            foreach (var p in new[] { settingsPath, secretsPath }) try { if (File.Exists(p)) File.Delete(p); } catch { }
            try { Directory.Delete(outDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Build_kit_does_nothing_when_the_folder_pick_is_cancelled()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"stripkit_kitc_{Guid.NewGuid():N}.json");
        var secretsPath = Path.Combine(Path.GetTempPath(), $"stripkit_kitcsec_{Guid.NewGuid():N}.dat");
        try
        {
            var gen = Substitute.For<IAssetGenerationService>();
            gen.AvailableProviders.Returns([AiProvider.Claude]);
            gen.DefaultModelFor(Arg.Any<AiProvider>()).Returns("m");
            gen.ModelsFor(Arg.Any<AiProvider>()).Returns(["m"]);
            StubSet(gen, LayeredKnobSvg);

            var dialogs = Substitute.For<IFileDialogService>();
            dialogs.OpenFolderAsync(Arg.Any<string>()).Returns((string?)null);   // user cancels
            var kit = Substitute.For<IKitBuilder>();

            var vm = new GenerateViewModel(gen, new DpapiSecretStore(secretsPath), new SettingsService(settingsPath),
                                           new LayeredImportService(), dialogs, kit);
            vm.ApiKey = "sk-test";
            await vm.GenerateSetCommand.ExecuteAsync(null);

            await vm.BuildKitCommand.ExecuteAsync(null);

            await kit.DidNotReceive().BuildAsync(Arg.Any<IReadOnlyList<KitControlSource>>(),
                Arg.Any<KitBuildOptions>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            foreach (var p in new[] { settingsPath, secretsPath }) try { if (File.Exists(p)) File.Delete(p); } catch { }
        }
    }

    [Fact]
    public async Task Build_kit_command_is_re_queried_when_set_generation_toggles()
    {
        // The "Build kit" button is event-driven (CanExecuteChanged). CanBuildKit gates on !IsGeneratingSet,
        // so IsGeneratingSet's true→false transition MUST re-query BuildKitCommand — otherwise the button
        // stays greyed out after a successful Generate set even though CanExecute() would return true. This
        // guards the [NotifyCanExecuteChangedFor(nameof(BuildKitCommand))] on _isGeneratingSet.
        var (vm, gen, _, temps) = Build();
        try
        {
            StubSet(gen, LayeredKnobSvg);
            vm.ApiKey = "sk-test";
            await vm.GenerateSetCommand.ExecuteAsync(null);
            vm.SetResults.Should().Contain(r => r.IsSuccess);

            int notifications = 0;
            vm.BuildKitCommand.CanExecuteChanged += (_, _) => notifications++;
            vm.IsGeneratingSet = true;
            vm.IsGeneratingSet = false;

            notifications.Should().BeGreaterThan(0, "toggling IsGeneratingSet must re-query Build kit's CanExecute");
        }
        finally { Cleanup(temps); }
    }

    [Fact]
    public async Task Regenerate_is_a_no_op_during_a_kit_build_so_it_cannot_cancel_the_build()
    {
        // Regenerate and BuildKit share _setCts, so a Regenerate click mid-build would cancel the build.
        // The guard must short-circuit while IsBuildingKit is set.
        var (vm, gen, _, temps) = Build();
        try
        {
            StubSet(gen, LayeredKnobSvg);
            vm.ApiKey = "sk-test";
            await vm.GenerateSetCommand.ExecuteAsync(null);
            var item = vm.SetResults.First();

            vm.IsBuildingKit = true;
            vm.SetStatus = "building-sentinel";
            await vm.RegenerateSetItemCommand.ExecuteAsync(item);

            vm.SetStatus.Should().Be("building-sentinel", "Regenerate must not start (and cancel) a build");
        }
        finally { Cleanup(temps); }
    }
}
