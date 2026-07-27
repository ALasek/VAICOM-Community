using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using VAICOM.Client;

namespace VAICOM.Standalone
{
    internal static class OfflineVaicomSelfTest
    {
        public static void Run(TextWriter output)
        {
            string dataRoot = Path.Combine(
                Path.GetTempPath(),
                "VAICOM-Standalone-SelfTest-" + Process.GetCurrentProcess().Id);
            Directory.CreateDirectory(dataRoot);

            var proxy = new StandaloneVoiceAttackProxy(dataRoot, dataRoot, Path.Combine(dataRoot, "Sounds"), false);
            proxy.LogWritten += entry => output.WriteLine("VAICOM: " + entry.Message);

            global::VAICOM.State.Proxy = proxy;
            global::VAICOM.State.SetEnvironment(proxy);
            global::VAICOM.State.startup = true;
            DcsClient.ResetProcessValues(proxy);
            global::VAICOM.FileManager.FileHandler.Root.CheckSubFolders();
            DcsClient.ResetConfig(proxy);
            global::VAICOM.State.startup = false;
            DcsClient.ResetStateValues(proxy);
            DcsClient.ResetPTTConfig(proxy);
            DcsClient.MergeRIO(proxy);
            DcsClient.CreateDatabase(proxy);

            AssertMatch(proxy, "flight rejoin", "recipient", "flight");
            AssertMatch(proxy, "flight rejoin", "command", "joinup");
            AssertMatch(proxy, "flight to rejoin", "recipient", "flight");
            AssertMatch(proxy, "flight to rejoin", "command", "joinup");
            AssertMatch(proxy, "2 rejoin", "recipient", "wingman2");
            AssertMatch(proxy, "2 rejoin", "command", "joinup");
            AssertMatch(proxy, "request startup", "command", "requestenginesstart");
            AssertMatch(proxy, "run starter", "command", "runinertialstarter");
            AssertMatch(proxy, "intent to refuel", "command", "intenttorefuel");
            AssertMatch(proxy, "a board to take off", "command", "aborttakeoff");
            AssertMatch(proxy, "abort take of", "command", "aborttakeoff");
            AssertNoMatch(proxy, "weather report for tomorrow", "command");
            AssertVoskRecovery(proxy, "flight to rejoin", "joinup");
            AssertVoskRecovery(proxy, "abort take of", "aborttakeoff");
            AssertNoVoskRecovery("weather report for tomorrow");
            AssertNoVoskRecovery("weather request startup tomorrow");
            AssertNoVoskRecovery("select channel thirty one");
            if (SpeechRecognizerRouter.TryResolve("abort take of", 0.5, out string lowConfidenceRecovery))
            {
                throw new InvalidOperationException("Low-confidence Vosk input was recovered as '" + lowConfidenceRecovery + "'.");
            }
            AssertSpecialCommand("got it", true);
            AssertSpecialCommand("GOT IT!", true);
            AssertSpecialCommand("I got it", false);

            global::VAICOM.State.currentmodule = global::VAICOM.Products.DCSmodules.LookupTable["Mi-24P"];
            AssertMatch(proxy, "petrovich weapons on", "command", "petrovichweaponson");
            AssertMatch(proxy, "gunner search forward", "command", "petrovichsearchforward");
            AssertMatch(proxy, "petrovich countermeasure interval", "command", "petrovichcminterval");
            AssertPetrovichSequence("petrovichweaponson", 30, 3008, 2, 0);
            AssertPetrovichSequence("petrovichsearchboresight", 30, 3015, 2, 0);
            AssertPetrovichSequence("petrovichsearchforward", 30, 3015, 2, 700);
            AssertPetrovichSequence("petrovichsearchpilotlos", 30, 3004, 2, 0);
            AssertPetrovichSequence("petrovichclearsearch", 30, 3005, 2, 0);
            AssertPetrovichSequence("petrovichtargetingtoggle", 30, 3002, 2, 700);
            AssertPetrovichSequence("petrovichcyclemissile", 30, 3019, 2, 0);
            AssertPetrovichSequence("petrovichfire", 30, 3015, 2, 0);
            AssertPetrovichSequence("petrovichroetoggle", 30, 3004, 2, 700);
            AssertPetrovichSequence("petrovichtargetprevious", 30, 3004, 2, 0);
            AssertPetrovichSequence("petrovichtargetnext", 30, 3005, 2, 0);
            AssertPetrovichSequence("petrovichtargetselect", 30, 3002, 2, 0);
            AssertPetrovichSequence("petrovichcminterval", 9, 3008, 1, 0);
            AssertPetrovichSequence("petrovichcmseries", 9, 3009, 1, 0);
            AssertPetrovichSequence("petrovichcmleft", 9, 3010, 1, 0);
            AssertPetrovichSequence("petrovichcmright", 9, 3011, 1, 0);
            AssertPetrovichSequence("petrovichcmset", 9, 3012, 1, 0);
            AssertPetrovichSequence("petrovichcmdispense", 9, 3014, 2, 0);

            output.WriteLine("Offline VAICOM parser checks passed.");
            output.WriteLine("Self-test data: " + dataRoot);
        }

        private static void AssertSpecialCommand(string transcript, bool expected)
        {
            if (StandaloneSpecialCommands.IsMatch(transcript) != expected
                || SpeechGrammar.IsValidRecognition(transcript) != expected)
            {
                throw new InvalidOperationException("Unexpected special-command routing for '" + transcript + "'.");
            }
        }

        private static void AssertMatch(StandaloneVoiceAttackProxy proxy, string transcript, string category, string expected)
        {
            global::VAICOM.State.MessageReset();
            proxy.SetTranscript(transcript);
            DcsClient.Message.getinputsentence();
            DcsClient.Message.scanforkeywords();

            bool matched = global::VAICOM.State.have[category];
            string actual = global::VAICOM.State.currentkey[category];
            if (!matched || !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "VAICOM did not match '" + transcript + "' as " + category + "='" + expected +
                    "'. Actual: '" + actual + "'.");
            }
        }

        private static void AssertNoMatch(StandaloneVoiceAttackProxy proxy, string transcript, string category)
        {
            global::VAICOM.State.MessageReset();
            proxy.SetTranscript(transcript);
            DcsClient.Message.getinputsentence();
            DcsClient.Message.scanforkeywords();
            if (global::VAICOM.State.have[category])
            {
                throw new InvalidOperationException(
                    "VAICOM unexpectedly matched '" + transcript + "' as " + category + "='"
                    + global::VAICOM.State.currentkey[category] + "'.");
            }
        }

        private static void AssertVoskRecovery(StandaloneVoiceAttackProxy proxy, string transcript, string expectedCommand)
        {
            if (!SpeechRecognizerRouter.TryResolve(transcript, 0.95, out string recovered))
            {
                throw new InvalidOperationException("Vosk recovery rejected '" + transcript + "'.");
            }

            AssertMatch(proxy, recovered, "command", expectedCommand);
        }

        private static void AssertNoVoskRecovery(string transcript)
        {
            if (SpeechRecognizerRouter.TryResolve(transcript, 0.95, out string recovered))
            {
                throw new InvalidOperationException("Vosk unexpectedly recovered '" + transcript + "' as '" + recovered + "'.");
            }
        }

        private static void AssertPetrovichSequence(
            string commandKey,
            int expectedDevice,
            int expectedCommand,
            int expectedActions,
            int expectedFirstDelay)
        {
            global::VAICOM.State.currentkey["command"] = commandKey;
            global::VAICOM.State.currentcommand = global::VAICOM.Database.Commands.Table[commandKey];
            global::VAICOM.State.currentmessage = new DcsClient.Message.CommsMessage();
            DcsClient.Message.ConstructPetrovich();

            var sequence = global::VAICOM.State.currentmessage.extsequence;
            if (sequence.Count != expectedActions
                || sequence[0].device != expectedDevice
                || sequence[0].command != expectedCommand
                || sequence[0].delayMs != expectedFirstDelay)
            {
                throw new InvalidOperationException("Unexpected Petrovich device sequence for " + commandKey + ".");
            }
        }
    }
}
