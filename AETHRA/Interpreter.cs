using System.Globalization;
using System.Text;

namespace AETHRA
{
    public static class Interpreter
    {
        // ===== CONSTANTS =====
        private const int SampleRate = 44100;
        private static readonly Random Rng = new();

        // ===== STATE =====
        private static double _tempo = 120;
        private static double _volume = 1.0;

        private static double _attack = 0.01, _decay = 0.1, _sustain = 0.7, _release = 0.2;

        private static double _echoDelay;
        private static double _echoDecay;

        private static double _reverbRoom;
        private static double _reverbDecay;

        private static string _instrument = "Sine"; // default waveform
        private static double _lfoFreq;
        private static double _lfoDepth;

        private static int _octaveShift;
        private static double _velocityScale = 1.0;

        private static readonly List<float> Buffer = [];

        // ===== ENTRY POINT =====
        public static void Run(string script, string wavPath)
        {
            // Reset state for each run
            ResetState();
            
            Buffer.Clear();
            
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
            _tempo = 120;
            _volume = 1.0;
            _attack = 0.01;
            _decay = 0.1;
            _sustain = 0.7;
            _release = 0.2;
            _echoDelay = 0;
            _echoDecay = 0;
            _reverbRoom = 0;
            _reverbDecay = 0;
            _instrument = "Sine";
            _lfoFreq = 0;
            _lfoDepth = 0;
            _octaveShift = 0;
            _velocityScale = 1.0;
        }

        // ===== SCRIPT EXECUTION =====
        private static void Execute(string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(l) || l.StartsWith("//")) continue;

                try
                {
                    if (l.StartsWith("@Tempo")) _tempo = NumSafe(ArgSafe(l, 0));
                    else if (l.StartsWith("@Volume")) _volume = NumSafe(ArgSafe(l, 0));
                    else if (l.StartsWith("@ADSR") || l.StartsWith("@Envelope")) { _attack = NumSafe(ArgSafe(l, 0)); _decay = NumSafe(ArgSafe(l, 1)); _sustain = NumSafe(ArgSafe(l, 2)); _release = NumSafe(ArgSafe(l, 3)); }
                    else if (l.StartsWith("@Scale")) ArgSafe(l, 0).Replace("\"", "");
                    else if (l.StartsWith("@Instrument") || l.StartsWith("@Waveform")) _instrument = ArgSafe(l, 0).Replace("\"", "");
                    else if (l.StartsWith("@Echo")) { _echoDelay = NumSafe(ArgSafe(l, 0)); _echoDecay = NumSafe(ArgSafe(l, 1)); }
                    else if (l.StartsWith("@Reverb")) { _reverbRoom = NumSafe(ArgSafe(l, 0)); _reverbDecay = NumSafe(ArgSafe(l, 1)); }
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
                    else if (l.StartsWith("@OctaveShift")) _octaveShift = (int)NumSafe(ArgSafe(l, 0));
                    else if (l.StartsWith("@VelocityScale")) _velocityScale = NumSafe(ArgSafe(l, 0));
                    else if (l.StartsWith("@Glide"))
                    {
                        double f1 = NoteFreqSafe(ArgSafe(l, 0));
                        double f2 = NoteFreqSafe(ArgSafe(l, 1));
                        double beats = NumSafe(ArgSafe(l, 2));
                        Glide(f1, f2, beats);
                    }
                    else if (l.StartsWith("@LFO"))
                    {
                        ArgSafe(l, 0);
                        _lfoFreq = NumSafe(ArgSafe(l, 1));
                        _lfoDepth = NumSafe(ArgSafe(l, 2));
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
                            string note = notes[Rng.Next(notes.Length)];
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
                    // ignored
                }
            }
        }

        // ===== SOUND METHODS =====
        private static void Play(double freq, double beats, double vel)
        {
            vel *= _velocityScale;
            double sec = beats * 60 / _tempo;
            int n = (int)(sec * SampleRate);

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / n;
                double env =
                    t < _attack ? t / _attack :
                    t < _attack + _decay ? 1 - (1 - _sustain) * (t - _attack) / _decay :
                    t < 1 - _release ? _sustain :
                    _sustain * (1 - (t - (1 - _release)) / _release);

                double wave = Waveform(freq, Buffer.Count + i);
                Buffer.Add((float)(wave * env * vel * _volume));
            }
        }

        private static void PlayChord(double[] freqs, double beats, double vel)
        {
            vel *= _velocityScale;
            double sec = beats * 60 / _tempo;
            int n = (int)(sec * SampleRate);

            for (int i = 0; i < n; i++)
            {
                double t = (double)i / n;
                double env =
                    t < _attack ? t / _attack :
                    t < _attack + _decay ? 1 - (1 - _sustain) * (t - _attack) / _decay :
                    t < 1 - _release ? _sustain :
                    _sustain * (1 - (t - (1 - _release)) / _release);

                double sample = 0;
                foreach (double f in freqs) sample += Waveform(f, Buffer.Count + i);
                sample /= freqs.Length;
                Buffer.Add((float)(sample * env * vel * _volume));
            }
        }

        private static void PlayArpeggio(string[] notes, double beats, double vel, string pattern)
        {
            double secPerNote = beats / notes.Length;
            List<string> order = pattern switch
            {
                "down" => notes.Reverse().ToList(),
                "random" => notes.OrderBy(_ => Rng.Next()).ToList(),
                "updown" => notes.Concat(notes.Reverse().Skip(1)).ToList(),
                _ => notes.ToList()
            };
            foreach (string n in order)
                Play(NoteFreqSafe(n), secPerNote, vel);
        }

        private static void Glide(double f1, double f2, double beats)
        {
            double sec = beats * 60 / _tempo;
            int n = (int)(sec * SampleRate);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / n;
                double freq = f1 + (f2 - f1) * t;
                double env =
                    t < _attack ? t / _attack :
                    t < _attack + _decay ? 1 - (1 - _sustain) * (t - _attack) / _decay :
                    t < 1 - _release ? _sustain :
                    _sustain * (1 - (t - (1 - _release)) / _release);

                Buffer.Add((float)(Waveform(freq, Buffer.Count + i) * env * _volume));
            }
        }

        private static void Pulse(double freq, double beats, double vel)
        {
            double sec = beats * 60 / _tempo;
            int n = (int)(sec * SampleRate);
            for (int i = 0; i < n; i++)
            {
                double wave = Math.Sign(Math.Sin(2 * Math.PI * freq * (Buffer.Count + i) / SampleRate));
                Buffer.Add((float)(wave * vel * _volume));
            }
        }

        private static void Rest(double beats)
        {
            int n = (int)(beats * 60 / _tempo * SampleRate);
            for (int i = 0; i < n; i++) Buffer.Add(0);
        }

        private static void Noise(double sec, double vol)
        {
            int n = (int)(sec * SampleRate);
            for (int i = 0; i < n; i++)
                Buffer.Add((float)((Rng.NextDouble() * 2 - 1) * vol));
        }

        private static void Grain(double sec, double vol, double density) { /* placeholder */ }
        private static void Texture(string name) { /* placeholder */ }
        private static void Harmony(int[] intervals) { /* placeholder */ }

        // ===== FX =====
        private static void ApplyEcho()
        {
            if (_echoDelay <= 0) return;
            int d = (int)(_echoDelay * SampleRate);
            for (int i = d; i < Buffer.Count; i++)
                Buffer[i] += (float)(Buffer[i - d] * _echoDecay);
        }

        private static void ApplyReverb()
        {
            if (_reverbRoom <= 0) return;
            int d = (int)(_reverbRoom * SampleRate);
            for (int i = d; i < Buffer.Count; i++)
                Buffer[i] += (float)(Buffer[i - d] * _reverbDecay);
        }

        private static void FadeIn(double sec)
        {
            int n = (int)(sec * SampleRate);
            for (int i = 0; i < n && i < Buffer.Count; i++)
                Buffer[i] *= (float)i / n;
        }

        private static void FadeOut(double sec)
        {
            int n = (int)(sec * SampleRate);
            int s = Buffer.Count - n;
            for (int i = 0; i < n && s + i < Buffer.Count; i++)
                Buffer[s + i] *= 1f - (float)i / n;
        }

        // ===== WAVE GENERATION =====
        private static double Waveform(double freq, int sampleIndex)
        {
            double t = (double)sampleIndex / SampleRate;
            double baseWave = _instrument.ToLower() switch
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

            if (_lfoFreq > 0)
                baseWave *= 1 + _lfoDepth * Math.Sin(2 * Math.PI * _lfoFreq * t);

            return baseWave;
        }

        // ===== NOTE FREQUENCY =====
        private static double NoteFreqSafe(string n)
        {
            try
            {
                n = n.Replace("\"", "").Trim();
                string[] notes = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
                string notePart = n.Length > 1 && n[1] == '#' ? n.Substring(0, 2) : n.Substring(0, 1);
                int i = Array.IndexOf(notes, notePart);
                int oct = int.Parse(n.Substring(notePart.Length)) + _octaveShift;
                return 440 * Math.Pow(2, (i + 12 * (oct - 4) - 9) / 12.0);
            }
            catch { return 440; }
        }

        // ===== PARSER HELPERS =====
        private static string ArgSafe(string l, int i)
        {
            try { return l[(l.IndexOf('(') + 1)..l.IndexOf(')')].Split(',')[i].Trim(); }
            catch { return "0"; }
        }

        private static int ArgCountSafe(string l)
        {
            try { return l[(l.IndexOf('(') + 1)..l.IndexOf(')')].Split(',').Length; }
            catch { return 0; }
        }

        private static double NumSafe(string s)
        {
            try
            {
                return double.Parse(s.Replace("\"", ""), CultureInfo.InvariantCulture);
            }
            catch { return 0; }
        }

        // ===== WAV OUTPUT =====
        private static void WriteWav(string path)
        {
            using var bw = new BinaryWriter(File.Create(path));
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + Buffer.Count * 2);
            bw.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            bw.Write(16); bw.Write((short)1); bw.Write((short)1);
            bw.Write(SampleRate); bw.Write(SampleRate * 2);
            bw.Write((short)2); bw.Write((short)16);
            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(Buffer.Count * 2);
            foreach (var s in Buffer)
                bw.Write((short)(Math.Clamp(s, -1, 1) * 32767));
        }
    }
}
