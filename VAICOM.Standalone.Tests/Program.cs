using System;
using System.IO;
using System.Speech.Synthesis;
using System.Text;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VAICOM.Standalone.Speech;

namespace VAICOM.Standalone.Tests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                TestCommandLineOptions();
                TestPttInputArbiter();
                TestHostSettings();
                TestHostController();
                TestDeterministicAliasMatcher();
                TestMenuReleasePolicy();
                TestProxy();
                TestAudioConversion();
                TestMicrophoneEnumeration();
                TestMissingModelValidation();
                TestMissingVoskModelValidation();
                TestDcsOriginalSafety();
                if (args.Length > 0)
                {
                    TestTranscription(args[0]).GetAwaiter().GetResult();
                }
                if (args.Length > 1)
                {
                    TestVoskTranscription(args[1]).GetAwaiter().GetResult();
                }
                Console.WriteLine("All compiled standalone smoke tests passed.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void TestCommandLineOptions()
        {
            CommandLineOptions defaults = CommandLineOptions.Parse(new string[0]);
            PttBinding[] defaultBindings = defaults.GetPttBindings();
            Assert(defaultBindings.Length == 1, "Legacy default PTT binding count failed.");
            Assert(defaultBindings[0].Input.VirtualKey == VirtualKeys.F13 && defaultBindings[0].Tx == 1, "Legacy F13/TX1 defaults failed.");

            CommandLineOptions legacyTx2 = CommandLineOptions.Parse(new[] { "--ptt-key", "F15", "--tx", "TX2" });
            PttBinding[] legacyBindings = legacyTx2.GetPttBindings();
            Assert(legacyBindings.Length == 1 && legacyBindings[0].KeyName == "F15" && legacyBindings[0].Tx == 2, "Legacy PTT/TX pairing failed.");

            CommandLineOptions twoRadios = CommandLineOptions.Parse(new[] { "--ptt-key-tx1", "F13", "--ptt-key-tx2", "F14" });
            PttBinding[] radioBindings = twoRadios.GetPttBindings();
            Assert(radioBindings.Length == 2, "Two-radio PTT binding count failed.");
            Assert(radioBindings[0].KeyName == "F13" && radioBindings[0].Tx == 1, "TX1 PTT binding failed.");
            Assert(radioBindings[1].KeyName == "F14" && radioBindings[1].Tx == 2, "TX2 PTT binding failed.");

            AssertThrows(
                () => CommandLineOptions.Parse(new[] { "--ptt-key-tx1", "F13", "--ptt-key-tx2", "F13" }),
                "Duplicate PTT key validation failed.");
            AssertThrows(
                () => CommandLineOptions.Parse(new[] { "--ptt-key", "F13", "--ptt-key-tx2", "F14" }),
                "Mixed legacy and multi-radio PTT validation failed.");
            AssertThrows(
                () => CommandLineOptions.Parse(new[] { "--ptt-key", "F13", "--tx", "TX3" }),
                "Deferred TX3 voice PTT validation failed.");
            AssertThrows(
                () => CommandLineOptions.Parse(new[] { "--ptt-key", "0xB0" }),
                "Unpolled virtual-key validation failed.");
            CommandLineOptions textTx3 = CommandLineOptions.Parse(new[] { "--text", "flight rejoin", "--tx", "TX3" });
            Assert(textTx3.Tx == 3, "TX3 text command validation failed.");
            Assert(CommandLineOptions.Parse(new[] { "--asr", "hybrid" }).SpeechBackend == SpeechBackendIds.Hybrid, "ASR option failed.");
            Assert(Array.IndexOf(VirtualKeys.BindableKeys, VirtualKeys.LControl) >= 0, "Supported modifier PTT was not polled.");
        }

        private static void TestPttInputArbiter()
        {
            var down = new bool[2];
            var arbiter = new PttInputArbiter(new[]
            {
                new PttBinding(1, "A", 1),
                new PttBinding(2, "B", 2)
            });
            arbiter.Resync(input => down[input.VirtualKey - 1]);

            down[0] = true;
            down[1] = true;
            PttTransition transition = arbiter.Poll(input => down[input.VirtualKey - 1]);
            Assert(transition.Kind == PttTransitionKind.Pressed && transition.Binding.Tx == 1, "Simultaneous PTT priority failed.");

            down[0] = false;
            transition = arbiter.Poll(input => down[input.VirtualKey - 1]);
            Assert(transition.Kind == PttTransitionKind.Released && transition.Binding.Tx == 1, "Active PTT release ownership failed.");

            transition = arbiter.Poll(input => down[input.VirtualKey - 1]);
            Assert(transition.Kind == PttTransitionKind.None, "Held secondary PTT was activated without a fresh edge.");
            down[1] = false;
            arbiter.Poll(input => down[input.VirtualKey - 1]);
            down[1] = true;
            transition = arbiter.Poll(input => down[input.VirtualKey - 1]);
            Assert(transition.Kind == PttTransitionKind.Pressed && transition.Binding.Tx == 2, "Secondary PTT fresh edge failed.");
        }

        private static void TestHostSettings()
        {
            string directory = Path.Combine(Path.GetTempPath(), "VAICOM-HostSettings-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new StandaloneHostSettingsStore(directory);
                StandaloneHostSettings defaults = store.Load(out bool firstRun, out string error);
                Assert(firstRun && string.IsNullOrEmpty(error), "First-run host settings detection failed.");
                Assert(defaults.Tx1.VirtualKey == VirtualKeys.F13 && defaults.Tx2.VirtualKey == VirtualKeys.Parse("F14"), "Default two-radio settings failed.");
                Assert(defaults.SpeechBackend == SpeechBackendIds.Vosk, "Default Vosk backend failed.");

                defaults.MicrophoneDeviceNumber = 2;
                defaults.Tx1 = PttInputBinding.None();
                defaults.Tx2 = PttInputBinding.Keyboard(VirtualKeys.Parse("F15"), "F15");
                defaults.SpeechBackend = SpeechBackendIds.Hybrid;
                store.Save(defaults);

                StandaloneHostSettings loaded = store.Load(out firstRun, out error);
                Assert(!firstRun && string.IsNullOrEmpty(error), "Saved host settings reload failed.");
                Assert(loaded.MicrophoneDeviceNumber == 2, "Microphone persistence failed.");
                Assert(loaded.Tx1.Kind == PttInputKind.None, "Cleared PTT persistence failed.");
                Assert(loaded.Tx2.VirtualKey == VirtualKeys.Parse("F15"), "PTT persistence failed.");
                Assert(loaded.SpeechBackend == SpeechBackendIds.Hybrid, "ASR backend persistence failed.");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static void TestHostController()
        {
            string directory = Path.Combine(Path.GetTempPath(), "VAICOM-HostController-" + Guid.NewGuid().ToString("N"));
            try
            {
                CommandLineOptions options = CommandLineOptions.Parse(new[] { "--ptt-key-tx1", "F15" });
                using (var controller = new StandaloneHostController(directory, options))
                {
                    PttBinding[] bindings = controller.GetPttBindings();
                    Assert(bindings.Length == 2, "Partial TX1 override removed the default TX2 binding.");
                    Assert(bindings[0].Input.VirtualKey == VirtualKeys.Parse("F15"), "TX1 host override failed.");
                    Assert(bindings[1].Input.VirtualKey == VirtualKeys.Parse("F14"), "TX2 host default failed.");
                    Assert(controller.GetHostSnapshot() != null, "Host snapshot failed.");
                    controller.SelectSpeechBackend(SpeechBackendIds.Whisper);
                    Assert(controller.GetHostSnapshot().SpeechBackendId == SpeechBackendIds.Whisper, "Host ASR selection failed.");
                }
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static void TestProxy()
        {
            var proxy = new StandaloneVoiceAttackProxy(installDcsFiles: false);
            proxy.SetTranscript("flight rejoin", new[] { "flight", "rejoin" });
            Assert(proxy.ParseTokens("{CMD}") == "flight rejoin", "CMD token failed.");
            Assert(proxy.Utility.ParseTokens("{CMDSEGMENT:1}") == "rejoin", "Segment token failed.");

            proxy.SetTranscript("2, engage");
            Assert(proxy.CommandText == "2, engage", "Raw transcript preservation failed.");
            Assert(proxy.ParseTokens("{CMD}") == "two, engage", "Single-digit command normalization failed.");
            Assert(proxy.Utility.ParseTokens("{CMDSEGMENT:0}") == "2", "Numeric digit segment normalization failed.");

            proxy.SetTranscript("squawk 1, 2, 3");
            Assert(proxy.ParseTokens("{CMD}") == "squawk one two three", "Separated digit command normalization failed.");

            proxy.SetTranscript("squawk one two three");
            Assert(proxy.ParseTokens("{CMD}") == "squawk one two three", "Number-word command preservation failed.");
            Assert(proxy.Utility.ParseTokens("{CMDSEGMENT:1}") == "1", "Number-word segment normalization failed.");

            proxy.SetTranscript("channel two.");
            Assert(proxy.Utility.ParseTokens("{CMDSEGMENT:1}") == "2", "Punctuated number-word segment normalization failed.");

            proxy.SetLongPressInvoked(true);
            Assert(proxy.Utility.ParseTokens("{CMDLONGPRESSINVOKED}") == "1", "Long-press token failed.");
            Assert(proxy.Utility.ParseTokens("{TXTNUM:\"two five one decimal zero zero\"}") == "251.00", "Aviation number token failed.");
            Assert(proxy.Utility.ParseTokens("{TXTNUM:\"one hundred twenty three\"}") == "123", "Cardinal number token failed.");

            proxy.Dictation.Start(out string ignored);
            proxy.SetTranscript("note this");
            Assert(proxy.Utility.ParseTokens("{DICTATION:NEWLINE}") == "note this", "Dictation token failed.");
            Assert(!proxy.InstallDcsFiles, "DCS installation flag failed.");
        }

        private static void TestDeterministicAliasMatcher()
        {
            var aliases = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Abort Takeoff"] = "aborttakeoff",
                ["Request Takeoff"] = "requesttakeoff",
                ["Ready for Takeoff"] = "readytakeoff"
            };

            Assert(
                DeterministicAliasMatcher.NormalizeTranscript("a board to take off") == "abort takeoff",
                "Known ASR artifact normalization failed.");
            Assert(
                DeterministicAliasMatcher.NormalizeTranscript("aboard to take off") == "abort takeoff",
                "Joined ASR artifact normalization failed.");
            Assert(
                DeterministicAliasMatcher.NormalizeTranscript("abort to take off") == "abort takeoff",
                "Inserted-word ASR artifact normalization failed.");
            Assert(
                DeterministicAliasMatcher.NormalizeTranscript("Riam") == "rearm",
                "Rearm ASR artifact normalization failed.");
            Assert(
                DeterministicAliasMatcher.NormalizeTranscript("rear arm") == "rearm",
                "Split rearm normalization failed.");

            AliasRecoveryResult recovered = DeterministicAliasMatcher.Match("command", "abort take of", aliases);
            Assert(recovered.Accepted && recovered.Value == "aborttakeoff", "Deterministic fuzzy recovery failed.");

            AliasRecoveryResult rejected = DeterministicAliasMatcher.Match("command", "weather report for tomorrow", aliases);
            Assert(!rejected.Accepted, "Unrelated transcript was accepted by fuzzy recovery.");

            var ambiguousAliases = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Turn Right"] = "turnright",
                ["Turn Light"] = "turnlight"
            };
            AliasRecoveryResult ambiguous = DeterministicAliasMatcher.Match("command", "turn might", ambiguousAliases);
            Assert(
                !ambiguous.Accepted,
                "Ambiguous fuzzy recovery was accepted: " + ambiguous.Alias + " score " + ambiguous.Score
                + ", runner-up " + ambiguous.RunnerUpScore + ".");

            var shortAlias = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ready"] = "ready"
            };
            Assert(
                !DeterministicAliasMatcher.Match("command", "red", shortAlias).Accepted,
                "Short alias was fuzzily recovered.");
        }

        private static void TestMenuReleasePolicy()
        {
            Assert(
                !global::VAICOM.PushToTalk.PTT.ShouldCloseMenusOnPttRelease(true, false),
                "Standalone PTT release should keep navigable menus open.");
            Assert(
                global::VAICOM.PushToTalk.PTT.ShouldCloseMenusOnPttRelease(false, false),
                "VoiceAttack PTT release should retain the original menu-close behavior.");
            Assert(
                !global::VAICOM.PushToTalk.PTT.ShouldCloseMenusOnPttRelease(false, true),
                "TX Link should keep menus open on PTT release.");
        }

        private static void TestAudioConversion()
        {
            var pcm = new byte[PushToTalkRecorder.CaptureSampleRate * 2];
            using (MemoryStream wav = PushToTalkRecorder.ResampleToWav(pcm))
            {
                byte[] bytes = wav.ToArray();
                Assert(Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF", "WAV RIFF header failed.");
                Assert(Encoding.ASCII.GetString(bytes, 8, 4) == "WAVE", "WAV format header failed.");
                Assert(BitConverter.ToInt16(bytes, 22) == 1, "WAV channel count failed.");
                Assert(BitConverter.ToInt32(bytes, 24) == PushToTalkRecorder.TranscriptionSampleRate, "WAV sample rate failed.");
                Assert(BitConverter.ToInt16(bytes, 34) == 16, "WAV bit depth failed.");
                Assert(bytes.Length > 44, "WAV contains no audio data.");
            }
        }

        private static void TestMicrophoneEnumeration()
        {
            var devices = MicrophoneDevices.Enumerate();
            Console.WriteLine("Microphones found: " + devices.Count);
        }

        private static void TestMissingModelValidation()
        {
            string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                using (new WhisperTranscriber(missing))
                {
                }
            }
            catch (FileNotFoundException)
            {
                return;
            }

            throw new InvalidOperationException("Missing Whisper model validation failed.");
        }

        private static void TestMissingVoskModelValidation()
        {
            string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            try
            {
                using (new VoskTranscriber(missing))
                {
                }
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }

            throw new InvalidOperationException("Missing Vosk model validation failed.");
        }

        private static async Task TestTranscription(string modelPath)
        {
            using (var audio = new MemoryStream())
            {
                using (var synth = new SpeechSynthesizer())
                {
                    synth.SetOutputToWaveStream(audio);
                    synth.Speak("flight rejoin");
                }

                audio.Position = 0;
                using (var reader = new WaveFileReader(audio))
                using (var resampled = new MemoryStream())
                using (var transcriber = new WhisperTranscriber(modelPath))
                {
                    ISampleProvider source = reader.ToSampleProvider();
                    if (source.WaveFormat.Channels == 2)
                    {
                        source = new StereoToMonoSampleProvider(source);
                    }

                    var resampler = new WdlResamplingSampleProvider(source, PushToTalkRecorder.TranscriptionSampleRate);
                    WaveFileWriter.WriteWavFileToStream(resampled, resampler.ToWaveProvider16());
                    resampled.Position = 0;

                    string text = await transcriber.TranscribeAsync(resampled).ConfigureAwait(false);
                    Console.WriteLine("Whisper runtime: " + transcriber.RuntimeLibrary);
                    Console.WriteLine("Synthetic transcription: " + text);
                    Assert(text.IndexOf("flight", StringComparison.OrdinalIgnoreCase) >= 0, "Whisper transcription did not recognize 'flight'.");
                }
            }
        }

        private static async Task TestVoskTranscription(string modelPath)
        {
            using (MemoryStream audio = CreateSyntheticCommand("rearm"))
            using (var transcriber = new VoskTranscriber(modelPath))
            {
                VoskTranscriptionResult result = await transcriber.TranscribeAsync(
                    audio,
                    "[\"rearm\",\"rejoin\",\"abort takeoff\",\"inime\",\"[unk]\"]").ConfigureAwait(false);
                Console.WriteLine("Vosk synthetic transcription: " + result.Text + " (" + result.Confidence.ToString("0.000") + ")");
                Assert(result.Text.IndexOf("rearm", StringComparison.OrdinalIgnoreCase) >= 0, "Vosk did not recognize synthetic 'rearm'.");
                Assert(result.OmittedGrammarPhrases == 1, "Vosk vocabulary filtering did not omit the unknown grammar phrase.");
            }
        }

        private static MemoryStream CreateSyntheticCommand(string text)
        {
            using (var source = new MemoryStream())
            {
                using (var synth = new SpeechSynthesizer())
                {
                    synth.SetOutputToWaveStream(source);
                    synth.Speak(text);
                }

                source.Position = 0;
                using (var reader = new WaveFileReader(source))
                {
                    ISampleProvider samples = reader.ToSampleProvider();
                    if (samples.WaveFormat.Channels == 2)
                    {
                        samples = new StereoToMonoSampleProvider(samples);
                    }

                    var resampled = new WdlResamplingSampleProvider(samples, PushToTalkRecorder.TranscriptionSampleRate);
                    var output = new MemoryStream();
                    WaveFileWriter.WriteWavFileToStream(output, resampled.ToWaveProvider16());
                    output.Position = 0;
                    return output;
                }
            }
        }

        private static void TestDcsOriginalSafety()
        {
            string directory = Path.Combine(Path.GetTempPath(), "VAICOM-DcsSafety-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string absent = Path.Combine(directory, "absent.lua");
                global::VAICOM.FileManager.FileHandler.Lua.PreserveDcsOriginal(absent, "fallback", false, out bool originallyMissing);
                Assert(originallyMissing, "Missing DCS target was not recorded.");
                File.WriteAllText(absent, "generated by VAICOM");
                global::VAICOM.FileManager.FileHandler.Lua.PreserveDcsOriginal(absent, "fallback", false, out originallyMissing);
                Assert(originallyMissing, "Missing marker was lost on a repeated install.");
                Assert(!File.Exists(absent + ".vaicom-standalone.original"), "Generated target was mistaken for an original.");
                Assert(global::VAICOM.FileManager.FileHandler.Lua.RestorePreservedDcsOriginal(absent), "Generated target was not restorable.");
                Assert(!File.Exists(absent), "Generated target was not removed on restore.");

                string existing = Path.Combine(directory, "existing.lua");
                File.WriteAllText(existing, "pre-existing VAICOM integration");
                global::VAICOM.FileManager.FileHandler.Lua.PreserveDcsOriginal(existing, "fallback", false, out originallyMissing);
                Assert(!originallyMissing, "Existing DCS target was marked missing.");
                Assert(File.ReadAllText(existing + ".vaicom-standalone.original") == "pre-existing VAICOM integration", "Existing VAICOM file was not preserved.");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertThrows(Action action, string message)
        {
            try
            {
                action();
            }
            catch (ArgumentException)
            {
                return;
            }

            throw new InvalidOperationException(message);
        }
    }
}
