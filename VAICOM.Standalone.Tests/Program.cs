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
                TestNumericAliasMatcher();
                TestProfileCommandRouter();
                TestMenuReleasePolicy();
                TestProxy();
                TestAudioConversion();
                TestMicrophoneEnumeration();
                TestMissingModelValidation();
                TestMissingVoskModelValidation();
                TestDcsOriginalSafety();
                if (args.Length > 0)
                {
                    if (Directory.Exists(args[0]))
                    {
                        using (var runtime = new VaicomRuntime(new StandaloneVoiceAttackProxy(installDcsFiles: false)))
                        {
                            runtime.Initialize();
                            TestVoskTranscription(args[0]).GetAwaiter().GetResult();
                        }
                    }
                    else
                    {
                        TestTranscription(args[0]).GetAwaiter().GetResult();
                    }
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
            Assert(recovered.RecoveredTranscript == "abort takeoff", "Recovered transcript reconstruction failed.");

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

        private static void TestProfileCommandRouter()
        {
            AssertRoute("chatter", "chatter", "Chatter");
            AssertRoute("configuration", "config", "Configuration");
            AssertRoute("configuration window reset", "config.resetwindow", "Configuration Window Reset");
            AssertRoute("link tune 1 2 decimal 3", "airio.dev.dl.tune", "Link Tune", "one", "two", "decimal", "three");
            AssertRoute("radio tune 2 5 1 decimal 0 2 5", "airio.dev.radio.tune", "Radio Tune", "two", "five", "one", "decimal", "zero", "two five");
            AssertRoute("radio tune 2 5 1 decimal 0", "airio.dev.radio.tune", "Radio Tune", "two", "five", "one", "decimal", "zero", "");
            AssertRoute("select AM 2 5 1 decimal 0 0", "dev.radio.setfrq", "Select", "am", "two", "five", "one", "decimal", "zero", "zero");
            AssertRoute("select 2 5 1 decimal 0 0", "dev.radio.setfrq", "Select", "", "two", "five", "one", "decimal", "zero", "zero");
            AssertRoute("select channel 25", "dev.radio.setchn", "Select Channel", "25");
            AssertRoute("TACAN tune X-Ray 1 2 9", "airio.dev.tacan.tune", "TACAN Tune", "x-ray", "one", "two", "nine");
            AssertRoute("laser code 6 8 5", "airio.dev.laser.code", "Laser Code", "six", "eight", "five");
            AssertRoute("map marker 10 to waypoint 2", "airio.map.markers", "Map Marker", "10", "to", "Waypoint 2");
            AssertRoute("map marker 0 to grid", "airio.map.navgrid", "Map Marker", "0", "to Grid");
            AssertRoute("orbit marker 10", "airio.map.markers.navigate", "orbit", "Marker", "10");
            AssertRoute("track marker 4", "airio.map.markers.track", "Track Marker", "4");
            AssertRoute("scan sector angels 70 at 150", "airio.dev.radar.sector", "Scan Sector Angels", "70", "at", "150");
            AssertRoute("scan sector angels 10 40", "airio.dev.radar.sector", "Scan Sector Angels", "10", "", "40");

            StandaloneProfileCommandRouter.TryMatch("radio tune 2 5 1 decimal 0 2 5", out StandaloneProfileCommand radioTune);
            var proxy = new StandaloneVoiceAttackProxy(installDcsFiles: false);
            proxy.SetTranscript("radio tune 2 5 1 decimal 0 2 5", radioTune.Segments);
            Assert(proxy.Utility.ParseTokens("{CMDSEGMENT:6}") == "25", "Standalone numeric segment conversion changed the AIRIO radio suffix contract.");

            Assert(!StandaloneProfileCommandRouter.TryMatch("select channel 31", out StandaloneProfileCommand ignored), "Out-of-range channel was accepted.");
            Assert(!StandaloneProfileCommandRouter.TryMatch("map marker 0 to waypoint 1", out ignored), "Out-of-range destination marker was accepted.");
            Assert(!StandaloneProfileCommandRouter.TryMatch("scan sector angels 71 at 40", out ignored), "Invalid radar step was accepted.");
            Assert(!StandaloneProfileCommandRouter.TryMatch("new command", out ignored), "Upstream demonstration command was exposed.");

            string[] grammarPhrases = new System.Collections.Generic.List<string>(StandaloneProfileCommandRouter.GrammarPhrases()).ToArray();
            Assert(grammarPhrases.Length > 64000, "Dynamic profile phrases were not added to the Vosk grammar.");
            Assert(Array.IndexOf(grammarPhrases, "configuration window reset") >= 0, "Configuration reset is missing from the Vosk grammar.");
            Assert(Array.IndexOf(grammarPhrases, "scan sector angels seventy at one hundred fifty") >= 0, "Radar sector phrase is missing from the Vosk grammar.");
        }

        private static void TestNumericAliasMatcher()
        {
            const string captured055 = "captured bogey bull 055 180";
            var aliases = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Captured Bogey Bull 045 120"] = "Action Captured Bogey Bull 045/120",
                ["Captured Bogey Bull 055 180"] = "Action Captured Bogey Bull 055/180"
            };
            AssertNumericAlias(aliases, "Captured Bogey Bull 055 180", captured055);
            AssertNumericAlias(aliases, "Captured Bogey Bull zero five five one eight zero", captured055);
            AssertNumericAlias(aliases, "Captured Bogey Bull oh five five one eighty", captured055);
            AssertNumericAlias(aliases, "Captured Bogey Bull zero five five one hundred eighty", captured055);
            AssertNumericAlias(aliases, "Captured Bogey Bull oh five five one hundred and eighty", captured055);
            AssertNumericAlias(aliases, "Captured Bogey Bull oh forty five one twenty", "captured bogey bull 045 120");
            Assert(!NumericAliasMatcher.TryRecoverTranscript(
                "Captured Bogey Bull zero five six one hundred eighty",
                aliases,
                out string ignored), "Unknown bullseye values matched a campaign command.");

            var proxy = new StandaloneVoiceAttackProxy(installDcsFiles: false);
            proxy.SetTranscript("Copy 9 Line");
            Assert(proxy.ParseTokens("{CMD}") == "Copy nine Line", "Lexical Nine Line normalization regressed.");

            string[] variants = new System.Collections.Generic.List<string>(
                NumericAliasMatcher.GrammarVariants("Captured Bogey Bull 055 180")).ToArray();
            Assert(Array.IndexOf(variants, "captured bogey bull oh five five one eighty") >= 0, "Aviation numeric phrase is missing from Vosk grammar variants.");
            Assert(Array.IndexOf(variants, "captured bogey bull zero five five one hundred eighty") >= 0, "Cardinal numeric phrase is missing from Vosk grammar variants.");
        }

        private static void AssertNumericAlias(
            System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, string>> aliases,
            string transcript,
            string expected)
        {
            Assert(NumericAliasMatcher.TryRecoverTranscript(transcript, aliases, out string recovered), "Numeric alias did not match: " + transcript);
            Assert(recovered == expected, "Numeric alias resolved incorrectly: " + transcript + " -> " + recovered);
        }

        private static void AssertRoute(string transcript, string context, params string[] segments)
        {
            Assert(StandaloneProfileCommandRouter.TryMatch(transcript, out StandaloneProfileCommand command), "Profile command did not match: " + transcript);
            Assert(command.Context == context, "Wrong context for " + transcript + ": " + command.Context);
            Assert(string.Join("|", command.Segments) == string.Join("|", segments), "Wrong segments for " + transcript + ": " + string.Join("|", command.Segments));
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
                string grammar = SpeechGrammar.BuildJson(out int phraseCount);
                Console.WriteLine("Vosk test grammar: " + phraseCount + " phrases.");
                var timer = System.Diagnostics.Stopwatch.StartNew();
                VoskTranscriptionResult result = await transcriber.TranscribeAsync(
                    audio,
                    grammar).ConfigureAwait(false);
                timer.Stop();
                Console.WriteLine("First full-grammar recognition: " + timer.Elapsed.TotalSeconds.ToString("0.000") + "s");
                Console.WriteLine("Grammar phrases omitted by installed vocabulary: " + result.OmittedGrammarPhrases);
                Console.WriteLine("Vosk synthetic transcription: " + result.Text + " (" + result.Confidence.ToString("0.000") + ")");
                Assert(result.Text.IndexOf("rearm", StringComparison.OrdinalIgnoreCase) >= 0, "Vosk did not recognize synthetic 'rearm'.");

                using (MemoryStream secondAudio = CreateSyntheticCommand("rearm"))
                {
                    timer.Restart();
                    result = await transcriber.TranscribeAsync(secondAudio, grammar).ConfigureAwait(false);
                    timer.Stop();
                    Console.WriteLine("Cached full-grammar recognition: " + timer.Elapsed.TotalSeconds.ToString("0.000") + "s");
                    Assert(result.Text.IndexOf("rearm", StringComparison.OrdinalIgnoreCase) >= 0, "Cached Vosk recognition failed.");
                }

                foreach (string profilePhrase in new[]
                {
                    "configuration",
                    "select channel twenty five",
                    "radio tune two five one decimal zero two five",
                    "scan sector angels ten at forty",
                    "laser code six eight five",
                    "map marker two to waypoint one",
                    "tacan tune x ray one two nine"
                })
                {
                    using (MemoryStream profileAudio = CreateSyntheticCommand(profilePhrase))
                    {
                        result = await transcriber.TranscribeAsync(profileAudio, grammar).ConfigureAwait(false);
                        Console.WriteLine("Vosk profile command: " + profilePhrase + " -> " + result.Text);
                        Assert(StandaloneProfileCommandRouter.TryMatch(result.Text, out StandaloneProfileCommand ignored), "Vosk did not produce a routable profile command for: " + profilePhrase);
                    }
                }

                using (MemoryStream starterAudio = CreateSyntheticCommand("run starter"))
                {
                    result = await transcriber.TranscribeAsync(starterAudio, grammar).ConfigureAwait(false);
                    Console.WriteLine("Vosk BF 109 command: run starter -> " + result.Text + " (" + result.Confidence.ToString("0.000") + ")");
                    Assert(SpeechRecognizerRouter.TryResolve(result, out string resolvedStarter), "Vosk rejected the BF 109 starter alias.");
                    Assert(
                        global::VAICOM.Database.Aliases.inputscancats["command"].TryGetValue(resolvedStarter, out string starterCommand)
                        && starterCommand == "runinertialstarter",
                        "Vosk did not resolve 'run starter' to the inertial starter command: " + resolvedStarter);
                }

                using (MemoryStream bullseyeAudio = CreateSyntheticCommand("captured bogey bull oh five five one eighty"))
                {
                    result = await transcriber.TranscribeAsync(bullseyeAudio, grammar).ConfigureAwait(false);
                    Console.WriteLine("Vosk numeric command: captured bogey bull oh five five one eighty -> " + result.Text);
                    timer.Restart();
                    bool resolved = SpeechRecognizerRouter.TryResolve(result, out string resolvedBullseye);
                    timer.Stop();
                    Console.WriteLine("Numeric alias recovery: " + timer.Elapsed.TotalMilliseconds.ToString("0.0") + "ms");
                    Assert(resolved, "Vosk rejected the spoken bullseye alias.");
                    Assert(
                        global::VAICOM.Database.Aliases.inputscancats["command"].TryGetValue(resolvedBullseye, out string bullseyeCommand)
                        && bullseyeCommand == "Action Captured Bogey Bull 055/180",
                        "Vosk did not resolve the spoken bullseye values to the canonical campaign command: " + resolvedBullseye);
                }
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
