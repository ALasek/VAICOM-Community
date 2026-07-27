using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VAICOM.Standalone.Speech;

namespace VAICOM.Standalone
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                return RunAsync(args).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static async Task<int> RunAsync(string[] args)
        {
            CommandLineOptions options = CommandLineOptions.Parse(args);
            if (options.ShowHelp)
            {
                ShowHelp();
                return 0;
            }

            if (options.ListDevices)
            {
                ShowDevices();
                return 0;
            }

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VAICOM-Standalone");
            string defaultModel = Path.Combine(baseDirectory, "Models", WhisperModelDownloader.SmallEnglishModelFileName);
            string modelPath = Path.GetFullPath(options.ModelPath ?? defaultModel);
            string voskModelPath = Path.Combine(baseDirectory, "Models", "vosk-model-small-en-us-0.15");

            if (options.SelfTest)
            {
                await StandaloneSelfTest.RunAsync(modelPath, Console.Out).ConfigureAwait(false);
                return 0;
            }

            if (options.DownloadModel)
            {
                Console.WriteLine("Downloading Whisper small.en model...");
                modelPath = await WhisperModelDownloader.DownloadSmallEnglishAsync(Path.GetDirectoryName(modelPath)).ConfigureAwait(false);
                Console.WriteLine("Model ready: " + modelPath);
                return 0;
            }

            using (var host = new StandaloneHostController(dataDirectory, options))
            {
                var proxy = new StandaloneVoiceAttackProxy(
                    baseDirectory,
                    dataDirectory,
                    Path.Combine(baseDirectory, "Sounds"),
                    options.InstallDcsFiles,
                    options.DcsPath,
                    host);
                using (var runtime = new VaicomRuntime(proxy))
                {
                    host.SetDcsStatusProvider(() => runtime.IsDcsConnected);
                    runtime.LogWritten += entry => Console.WriteLine("VAICOM: " + entry.Message);
                    runtime.CommandExecuted += command => Console.WriteLine("VA command requested: " + command);

                    Console.WriteLine("Initializing VAICOM" + (options.InstallDcsFiles ? " with DCS file integration..." : " without changing DCS files..."));
                    runtime.Initialize();

                    if (options.InitializeOnly)
                    {
                        Console.WriteLine("VAICOM initialization completed.");
                        return 0;
                    }

                    if (!string.IsNullOrWhiteSpace(options.Text))
                    {
                        if (!runtime.IsDcsConnected)
                        {
                            Console.Error.WriteLine("DCS is not connected; --text requires a live DCS session.");
                            return 3;
                        }
                        SubmitText(runtime, options.Tx, options.Text);
                        return 0;
                    }

                    string selectedBackend = host.SpeechBackend;
                    if ((selectedBackend == SpeechBackendIds.Whisper || selectedBackend == SpeechBackendIds.Hybrid)
                        && !File.Exists(modelPath))
                    {
                        Console.Error.WriteLine("The selected " + selectedBackend + " backend requires a Whisper model: " + modelPath);
                        Console.Error.WriteLine("Run with --download-model once, supply --model PATH, or select Vosk in the Host tab.");
                        return 2;
                    }

                    if ((selectedBackend == SpeechBackendIds.Vosk || selectedBackend == SpeechBackendIds.Hybrid)
                        && !Directory.Exists(voskModelPath))
                    {
                        Console.Error.WriteLine("Vosk model not found: " + voskModelPath);
                        Console.Error.WriteLine("Run build-standalone.ps1 to download and package it.");
                        return 2;
                    }

                    using (var transcriber = new SpeechRecognizerRouter(modelPath, voskModelPath))
                    using (var cancellation = new CancellationTokenSource())
                    {
                        string grammar = SpeechGrammar.BuildJson(out int grammarCount);
                        host.SetWhisperRuntime(transcriber.RuntimeDescription);
                        host.SetStatus("Ready");
                        Console.WriteLine("Speech engines: " + transcriber.RuntimeDescription);
                        Console.WriteLine("Vosk grammar: " + grammarCount + " VAICOM aliases and profile commands.");
                        Console.CancelKeyPress += (sender, eventArgs) =>
                        {
                            eventArgs.Cancel = true;
                            cancellation.Cancel();
                        };

                        if (host.IsFirstRun || options.OpenConfig)
                        {
                            runtime.Invoke("config");
                        }

                        PttBinding[] bindings = host.GetPttBindings();
                        Console.WriteLine("Ready. " + FormatBindings(bindings) + "; release to transcribe. Ctrl+C exits.");
                        await RunPushToTalkLoop(runtime, transcriber, host, cancellation.Token).ConfigureAwait(false);
                    }
                }
            }

            return 0;
        }

        private static async Task RunPushToTalkLoop(
            VaicomRuntime runtime,
            SpeechRecognizerRouter transcriber,
            StandaloneHostController host,
            CancellationToken cancellationToken)
        {
            PushToTalkRecorder recorder = null;
            PttInputArbiter arbiter = null;
            int recorderVersion = -1;
            int bindingsVersion = -1;
            bool recording = false;
            int activeTx = 0;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (host.ConsumeConfigShortcutRequest())
                    {
                        runtime.Invoke("config.resetwindow");
                    }

                    if (activeTx == 0)
                    {
                        host.ProcessBindingCapture();
                    }

                    if (!recording && recorderVersion != host.MicrophoneVersion)
                    {
                        PushToTalkRecorder replacement = new PushToTalkRecorder(host.MicrophoneDeviceNumber);
                        recorder?.Dispose();
                        recorder = replacement;
                        recorderVersion = host.MicrophoneVersion;
                    }

                    if (!recording && bindingsVersion != host.BindingsVersion)
                    {
                        PttBinding[] bindings = host.GetPttBindings();
                        arbiter = bindings.Length == 0 ? null : new PttInputArbiter(bindings);
                        arbiter?.Resync(host.IsInputDown);
                        bindingsVersion = host.BindingsVersion;
                        Console.WriteLine("PTT bindings: " + FormatBindings(bindings));
                    }

                    if (host.IsCapturingBinding)
                    {
                        arbiter?.Resync(host.IsInputDown);
                        await Delay(cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    PttTransition transition = arbiter?.Poll(host.IsInputDown) ??
                        new PttTransition(PttTransitionKind.None, null);
                    if (transition.Kind == PttTransitionKind.Pressed)
                    {
                        try
                        {
                            recorder.Start();
                            recording = true;
                            activeTx = transition.Binding.Tx;
                            runtime.PressPtt(transition.Binding.Tx);
                            host.SetStatus("Listening on TX" + activeTx);
                            host.SetError(string.Empty);
                            Console.WriteLine("Listening on TX" + activeTx + "...");
                        }
                        catch (Exception exception)
                        {
                            if (recorder.IsRecording)
                            {
                                try
                                {
                                    await recorder.StopAsync().ConfigureAwait(false);
                                }
                                catch
                                {
                                }
                            }
                            recording = false;
                            if (activeTx != 0)
                            {
                                try
                                {
                                    runtime.ReleasePtt(activeTx);
                                }
                                catch
                                {
                                }
                            }
                            activeTx = 0;
                            host.SetStatus("Ready");
                            host.SetError(exception.Message);
                            Console.Error.WriteLine("Could not start recording: " + exception.Message);
                            arbiter.Resync(host.IsInputDown);
                        }
                    }
                    else if (transition.Kind == PttTransitionKind.Released)
                    {
                        host.SetStatus("Transcribing TX" + transition.Binding.Tx);
                        try
                        {
                            await TranscribeAndSubmit(
                                runtime,
                                recorder,
                                transcriber,
                                transition.Binding.Tx,
                                host,
                                cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            recording = false;
                            activeTx = 0;
                            host.SetStatus("Ready");
                        }

                        arbiter.Resync(host.IsInputDown);
                    }

                    await Delay(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                if (recording && recorder != null)
                {
                    try
                    {
                        await recorder.StopAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                if (activeTx != 0)
                {
                    runtime.ReleasePtt(activeTx);
                }

                recorder?.Dispose();
            }
        }

        private static string FormatBindings(PttBinding[] bindings)
        {
            if (bindings.Length == 0)
            {
                return "no PTT assigned";
            }

            var descriptions = new string[bindings.Length];
            for (int index = 0; index < bindings.Length; index++)
            {
                descriptions[index] = "hold " + bindings[index].KeyName + " for TX" + bindings[index].Tx;
            }

            return string.Join(" or ", descriptions);
        }

        private static async Task TranscribeAndSubmit(
            VaicomRuntime runtime,
            PushToTalkRecorder recorder,
            SpeechRecognizerRouter transcriber,
            int tx,
            StandaloneHostController host,
            CancellationToken cancellationToken)
        {
            try
            {
                using (MemoryStream wav = await recorder.StopAsync().ConfigureAwait(false))
                {
                    var stopwatch = Stopwatch.StartNew();
                    SpeechRecognitionResult result = await transcriber.TranscribeAsync(
                        wav,
                        host.SpeechBackend,
                        cancellationToken).ConfigureAwait(false);
                    string transcript = result.Text;
                    stopwatch.Stop();

                    if (string.IsNullOrWhiteSpace(transcript))
                    {
                        Console.WriteLine("No speech recognized.");
                    }
                    else
                    {
                        string confidence = result.Confidence.HasValue
                            ? ", confidence " + result.Confidence.Value.ToString("0.000")
                            : string.Empty;
                        string fallback = result.UsedFallback ? ", fallback" : string.Empty;
                        if (result.OmittedGrammarPhrases > 0)
                        {
                            Console.WriteLine("Vosk grammar: skipped " + result.OmittedGrammarPhrases
                                + " aliases containing words absent from this model.");
                        }
                        Console.WriteLine("Heard via " + result.Engine + fallback + confidence + " ("
                            + stopwatch.Elapsed.TotalSeconds.ToString("0.00") + "s): " + transcript);
                        if (StandaloneSpecialCommands.TryExecute(transcript, out string specialCommandMessage))
                        {
                            Console.WriteLine(specialCommandMessage);
                        }
                        else
                        {
                            runtime.SubmitTranscript(transcript);
                        }
                    }
                }
            }
            catch (InvalidOperationException exception)
            {
                host.SetError(exception.Message);
                Console.WriteLine(exception.Message);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                host.SetError(exception.Message);
                Console.Error.WriteLine("Voice command failed: " + exception.Message);
            }
            finally
            {
                runtime.ReleasePtt(tx);
            }
        }

        private static async Task Delay(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static void SubmitText(VaicomRuntime runtime, int tx, string text)
        {
            runtime.PressPtt(tx);
            try
            {
                Console.WriteLine("Submitting: " + text);
                runtime.SubmitTranscript(text);
            }
            finally
            {
                runtime.ReleasePtt(tx);
            }
        }

        private static void ShowDevices()
        {
            Console.WriteLine("-1: Windows default recording device");
            foreach (MicrophoneDevice device in MicrophoneDevices.Enumerate())
            {
                Console.WriteLine(device.DeviceNumber + ": " + device.Name);
            }
        }

        private static void ShowHelp()
        {
            Console.WriteLine("VAICOM Community noVA - local speech host for VAICOM Community");
            Console.WriteLine();
            Console.WriteLine("  --download-model            Download ggml-small.en.bin to Models");
            Console.WriteLine("  --model PATH                Use another Whisper.net GGML model");
            Console.WriteLine("  --asr vosk|hybrid|whisper   Select and save the recognition backend");
            Console.WriteLine("  --list-devices              List recording devices");
            Console.WriteLine("  --mic NUMBER                Select and save a recording device (-1 is default)");
            Console.WriteLine("  --ptt-key KEY               Legacy single PTT key (default F13)");
            Console.WriteLine("  --tx 1..6                   Legacy text TX; standalone voice PTT supports TX1/TX2");
            Console.WriteLine("  --ptt-key-tx1 KEY           Save a keyboard PTT mapped to TX1");
            Console.WriteLine("  --ptt-key-tx2 KEY           Save a keyboard PTT mapped to TX2");
            Console.WriteLine("  --open-config               Open the VAICOM configuration window at startup");
            Console.WriteLine("  --no-install-dcs-files      Do not let VAICOM modify DCS integration files");
            Console.WriteLine("  --dcs-path PATH             Set and persist a custom DCS installation path");
            Console.WriteLine("  --initialize-only           Initialize VAICOM and exit");
            Console.WriteLine("  --text \"flight rejoin\"    Submit text through the live VAICOM path");
            Console.WriteLine("  --self-test                 Run the offline VAICOM parser checks");
            Console.WriteLine();
            Console.WriteLine("Press Ctrl+Alt+C at any time to open or restore the configuration window.");
        }
    }
}
