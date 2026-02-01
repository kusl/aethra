using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AETHRA
{
    public class Interpreter
    {
        // ===== CONSTANTS =====
        const int SampleRate = 44100;
        static Random rng = new();

        // ===== STATE =====
        static double tempo = 120;
        static double volume = 1.0;

        static double attack = 0.01, decay = 0.1, sustain = 0.7, release = 0.2;

        static string scaleType = "";
        static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 };
        static readonly int[] Minor = { 0, 2, 3, 5, 7, 8, 10 };

        static double echoDelay = 0;
        static double echoDecay = 0;

        static double reverbRoom = 0;
        static double reverbDecay = 0;

        static string instrument = "Sine"; // default waveform
        static double lfoFreq = 0;
        static double lfoDepth = 0;

        static int octaveShift = 0;
        static double velocityScale = 1.0;

        static List<float> buffer = new();

        // ===== ENTRY POINT =====
        public static void Run(string script, string wavPath)
        {
            // Reset state for each run
            ResetState();
            
            buffer.Clear();
            
            if (string.IsNullOrWhiteSpace(script))
            {
                WriteWav(wavPath);
                return;
            }
            
            // Normalize line endings to handle all platforms (Windows \r\n, Unix \n, Classic Mac \r)
            string normalizedScript = script.ReplaceLineEndings("\n");
            Execute(normalizedScript.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            ApplyEcho();
            ApplyReverb();
            WriteWav(wavPath);
        }

        private static void ResetState()
        {
            tempo = 120;
            volume = 1.0;
            attack = 0.01;
            decay = 0.1;
            sustain = 0.7;
            release = 0.2;
            scaleType = "";
            echoDelay = 0;
            echoDecay = 0;
            reverbRoom = 0;
            reverbDecay = 0;
            instrument = "Sine";
            lfoFreq = 0;
            lfoDepth = 0;
            octaveShift = 0;
            velocityScale = 1.0;
        }

        // ===== SCRIPT EXECUTION =====
        static void Execute(string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(l) || l.StartsWith("//")) continue;

                try
                {
                    if (l.StartsWith("@Tempo")) tempo = NumSafe(ArgSafe(l, 0));
                    else if (l.StartsWith("@Volume")) volume = NumSafe(ArgSafe(l, 0));
                    else if (l.StartsWith("@ADSR")) { attack = NumSafe(ArgSafe(l, 0)); decay = NumSafe(ArgSafe(l, 1)); sustain = NumSafe(ArgSafe(l, 2)); release = NumSafe(ArgSafe(l, 3)); }
                    else if (l.StartsWith("@Envelope")) { attack = NumSafe(ArgSafe(l, 0)); decay = NumSafe(ArgSafe(l, 1)); sustain = NumSafe(ArgSafe(l, 2)); release = NumSafe(ArgSafe(l, 3)); }
                    else if (l.StartsWith("@Scale")) scaleType = ArgSafe(l, 0).Replace("\"", "");
                    else if (l.StartsWith("@Instrument")) instrument = ArgSafe(l, 0).Replace("\"", "");
                    else if (l.StartsWith("@Waveform")) instrument = ArgSafe(l, 0).Replace("\"", "");
                    else if (l.StartsWith("@Echo")) { echoDelay = NumSafe(ArgSafe(l, 0)); echoDecay = NumSafe(ArgSafe(l, 1)); }
                    else if (l.StartsWith("@Reverb")) { reverbRoom = NumSafe(ArgSafe(l, 0)); reverbDecay = NumSafe(ArgSafe(l, 1)); }
                    else if (l.StartsWith("@FadeIn")) FadeIn(NumSafe(ArgSafe(l, 0)));
                    else if (l.StartsWith("@FadeOut")) FadeOut(NumSafe(ArgSafe(l, 0)));
                    else if (l.StartsWith("@Loop") || l.StartsWith("@loop"))
                    {
                        int times = (int)NumSafe(ArgSafe(l, 0));
                        List<string> block = new();
                        i++;
                        int braceDepth = 1;
                        if (l.Contains("{"))
                        {
                            braceDepth = 1;
                        }
                        while (i < lines.Length && braceDepth > 0)
                        {
                            string blockLine = lines[i];
                            if (blockLine.Contains("{")) braceDepth++;
                            if (blockLine.Contains("}")) braceDepth--;
                            if (braceDepth > 0)
                            {
                                block.Add(blockLine);
                            }
                            i++;
                        }
                        i--;
                        for (int t = 0; t < times; t++) Execute(block.ToArray());
                    }
                    else if (l.StartsWith("@Rest")) Rest(NumSafe(ArgSafe(l, 0)));
                    else if (l.StartsWith("@Note")) Play(NoteFreqSafe(ArgSafe(l, 0)), NumSafe(ArgSafe(l, 1)), ArgCountSafe(l) > 2 ? NumSafe(ArgSafe(l, 2)) : 1);
                    else if (l.StartsWith("@Chord"))
                    {
                        string[] notes = ArgSafe(l, 0).Split();
                        double beats = NumSafe(ArgSafe(l, 1));
                        double vel = ArgCountSafe(l) > 2 ? NumSafe(ArgSafe(l, 2)) : 1;
                        double[] freqs = notes.Select(n => NoteFreqSafe(n)).ToArray();
                        PlayChord(freqs, beats, vel);
                    }
                    else if (l.StartsWith("@Arpeggio") || l.StartsWith("@Arp"))
                    {
                        string[] notes = ArgSafe(l, 0).Split();
                        double beats = NumSafe(ArgSafe(l, 1));
                        double vel = ArgCountSafe(l) > 2 ? NumSafe(ArgSafe(l, 2)) : 1;
                        string pattern = ArgCountSafe(l) > 3 ? ArgSafe(l, 3).Replace("\"", "").ToLower() : "up";
                        PlayArpeggio(notes, beats, vel, pattern);
                    }
                    else if (l.StartsWith("@OctaveShift")) octaveShift = (int)NumSafe(ArgSafe(l, 0));
                    else if (l.StartsWith("@VelocityScale")) velocityScale = NumSafe(ArgSafe(l, 0));
                    else if (l.StartsWith("@Glide"))
                    {
                        double f1 = NoteFreqSafe(ArgSafe(l, 0));
                        double f2 = NoteFreqSafe(ArgSafe(l, 1));
                        double beats = NumSafe(ArgSafe(l, 2));
                        Glide(f1, f2, beats);
                    }
                    else if (l.StartsWith("@LFO"))
                    {
                        string type = ArgSafe(l, 0);
                        lfoFreq = NumSafe(ArgSafe(l, 1));
                        lfoDepth = NumSafe(ArgSafe(l, 2));
                    }
                    else if (l.StartsWith("@Harmony"))
                    {
                        string[] intervals = ArgSafe(l, 0).Split();
                        Harmony(intervals.Select(s => int.Parse(s)).ToArray());
                    }
                    else if (l.StartsWith("@Randomize"))
                    {
                        string[] notes = ArgSafe(l, 0).Split();
                        double beats = NumSafe(ArgSafe(l, 1));
                        double vel = NumSafe(ArgSafe(l, 2));
                        int times = (int)NumSafe(ArgSafe(l, 3));
                        for (int r = 0; r < times; r++)
                        {
                            string note = notes[rng.Next(notes.Length)];
                            Play(NoteFreqSafe(note), beats, vel);
                        }
                    }
                    else if (l.StartsWith("@Pulse"))
                        Pulse(NumSafe(ArgSafe(l, 0)), NumSafe(ArgSafe(l, 1)), ArgCountSafe(l) > 2 ? NumSafe(ArgSafe(l, 2)) : 1);
                    else if (l.StartsWith("@Grain"))
                        Grain(NumSafe(ArgSafe(l, 0)), NumSafe(ArgSafe(l, 1)), NumSafe(ArgSafe(l, 2)));
                    else if (l.StartsWith("@Texture"))
                        Texture(ArgSafe(l, 0));
                    else if (l.StartsWith("@Noise"))
                        Noise(NumSafe(ArgSafe(l, 0)), NumSafe(ArgSafe(l, 1)));
                }
                catch
                {
                    continue;
                }
            }
        }

        // ===== SOUND METHODS =====
        static void Play(double freq, double beats, double vel)
        {
            vel *= velocityScale;
            double sec = beats * 60 / tempo;
            int n = (int)(sec * SampleRate);

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / n;
                double env =
                    t < attack ? t / attack :
                    t < attack + decay ? 1 - (1 - sustain) * (t - attack) / decay :
                    t < 1 - release ? sustain :
                    sustain * (1 - (t - (1 - release)) / release);

                double wave = Waveform(freq, buffer.Count + i);
                buffer.Add((float)(wave * env * vel * volume));
            }
        }

        static void PlayChord(double[] freqs, double beats, double vel)
        {
            vel *= velocityScale;
            double sec = beats * 60 / tempo;
            int n = (int)(sec * SampleRate);

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / n;
                double env =
                    t < attack ? t / attack :
                    t < attack + decay ? 1 - (1 - sustain) * (t - attack) / decay :
                    t < 1 - release ? sustain :
                    sustain * (1 - (t - (1 - release)) / release);

                double sample = 0;
                foreach (double f in freqs) sample += Waveform(f, buffer.Count + i);
                sample /= freqs.Length;
                buffer.Add((float)(sample * env * vel * volume));
            }
        }

        static void PlayArpeggio(string[] notes, double beats, double vel, string pattern)
        {
            double secPerNote = beats / notes.Length;
            List<string> order = pattern switch
            {
                "down" => notes.Reverse().ToList(),
                "random" => notes.OrderBy(_ => rng.Next()).ToList(),
                "updown" => notes.Concat(notes.Reverse().Skip(1)).ToList(),
                _ => notes.ToList()
            };
            foreach (string n in order)
                Play(NoteFreqSafe(n), secPerNote, vel);
        }

        static void Glide(double f1, double f2, double beats)
        {
            double sec = beats * 60 / tempo;
            int n = (int)(sec * SampleRate);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / n;
                double freq = f1 + (f2 - f1) * t;
                double env =
                    t < attack ? t / attack :
                    t < attack + decay ? 1 - (1 - sustain) * (t - attack) / decay :
                    t < 1 - release ? sustain :
                    sustain * (1 - (t - (1 - release)) / release);

                buffer.Add((float)(Waveform(freq, buffer.Count + i) * env * volume));
            }
        }

        static void Pulse(double freq, double beats, double vel)
        {
            double sec = beats * 60 / tempo;
            int n = (int)(sec * SampleRate);
            for (int i = 0; i < n; i++)
            {
                double wave = Math.Sign(Math.Sin(2 * Math.PI * freq * (buffer.Count + i) / SampleRate));
                buffer.Add((float)(wave * vel * volume));
            }
        }

        static void Rest(double beats)
        {
            int n = (int)(beats * 60 / tempo * SampleRate);
            for (int i = 0; i < n; i++) buffer.Add(0);
        }

        static void Noise(double sec, double vol)
        {
            int n = (int)(sec * SampleRate);
            for (int i = 0; i < n; i++)
                buffer.Add((float)((rng.NextDouble() * 2 - 1) * vol));
        }

        static void Grain(double sec, double vol, double density) { /* placeholder */ }
        static void Texture(string name) { /* placeholder */ }
        static void Harmony(int[] intervals) { /* placeholder */ }

        // ===== FX =====
        static void ApplyEcho()
        {
            if (echoDelay <= 0) return;
            int d = (int)(echoDelay * SampleRate);
            for (int i = d; i < buffer.Count; i++)
                buffer[i] += (float)(buffer[i - d] * echoDecay);
        }

        static void ApplyReverb()
        {
            if (reverbRoom <= 0) return;
            int d = (int)(reverbRoom * SampleRate);
            for (int i = d; i < buffer.Count; i++)
                buffer[i] += (float)(buffer[i - d] * reverbDecay);
        }

        static void FadeIn(double sec)
        {
            int n = (int)(sec * SampleRate);
            for (int i = 0; i < n && i < buffer.Count; i++)
                buffer[i] *= (float)i / n;
        }

        static void FadeOut(double sec)
        {
            int n = (int)(sec * SampleRate);
            int s = buffer.Count - n;
            for (int i = 0; i < n && s + i < buffer.Count; i++)
                buffer[s + i] *= 1f - (float)i / n;
        }

        // ===== WAVE GENERATION =====
        static double Waveform(double freq, int sampleIndex)
        {
            double t = (double)sampleIndex / SampleRate;
            double baseWave = instrument.ToLower() switch
            {
                "sine" => Math.Sin(2 * Math.PI * freq * sampleIndex / SampleRate),
                "square" => Math.Sign(Math.Sin(2 * Math.PI * freq * sampleIndex / SampleRate)),
                "triangle" => 2 * Math.Asin(Math.Sin(2 * Math.PI * freq * sampleIndex / SampleRate)) / Math.PI,
                "saw" => 2 * (t * freq - Math.Floor(t * freq + 0.5)),
                "strings" => (Math.Sin(2 * Math.PI * freq * sampleIndex / SampleRate) +
                             0.5 * Math.Sin(4 * Math.PI * freq * sampleIndex / SampleRate) +
                             0.25 * Math.Sin(6 * Math.PI * freq * sampleIndex / SampleRate)) / 1.75,
                _ => Math.Sin(2 * Math.PI * freq * sampleIndex / SampleRate)
            };

            if (lfoFreq > 0)
                baseWave *= 1 + lfoDepth * Math.Sin(2 * Math.PI * lfoFreq * t);

            return baseWave;
        }

        // ===== NOTE FREQUENCY =====
        static double NoteFreqSafe(string n)
        {
            try
            {
                n = n.Replace("\"", "").Trim();
                string[] notes = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
                string notePart = n.Length > 1 && n[1] == '#' ? n.Substring(0, 2) : n.Substring(0, 1);
                int i = Array.IndexOf(notes, notePart);
                int oct = int.Parse(n.Substring(notePart.Length)) + octaveShift;
                return 440 * Math.Pow(2, (i + 12 * (oct - 4) - 9) / 12.0);
            }
            catch { return 440; }
        }

        // ===== PARSER HELPERS =====
        static string ArgSafe(string l, int i)
        {
            try { return l[(l.IndexOf('(') + 1)..l.IndexOf(')')].Split(',')[i].Trim(); }
            catch { return "0"; }
        }

        static int ArgCountSafe(string l)
        {
            try { return l[(l.IndexOf('(') + 1)..l.IndexOf(')')].Split(',').Length; }
            catch { return 0; }
        }

        static double NumSafe(string s)
        {
            try
            {
                return double.Parse(s.Replace("\"", ""), CultureInfo.InvariantCulture);
            }
            catch { return 0; }
        }

        // ===== WAV OUTPUT =====
        static void WriteWav(string path)
        {
            using var bw = new BinaryWriter(File.Create(path));
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + buffer.Count * 2);
            bw.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            bw.Write(16); bw.Write((short)1); bw.Write((short)1);
            bw.Write(SampleRate); bw.Write(SampleRate * 2);
            bw.Write((short)2); bw.Write((short)16);
            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(buffer.Count * 2);
            foreach (var s in buffer)
                bw.Write((short)(Math.Clamp(s, -1, 1) * 32767));
        }
    }
}
