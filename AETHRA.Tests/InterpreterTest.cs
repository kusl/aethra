// Create this as a new console project to test the interpreter in isolation
// File: AETHRA.Tests/InterpreterTest.cs (or a new console app)

using System;
using System.IO;

namespace AETHRA.Tests
{
    public class InterpreterTest
    {
        public static void Main(string[] args)
        {
            // Hard-coded test script
            string testScript = @"@Tempo(120)
@Waveform(""Sine"")
@Note(""C5"",1)
@Rest(0.5)
@Note(""D5"",1)
@Note(""E5"",1)
@Chord(""C4 E4 G4"",2)";

            string outputPath = Path.Combine(Path.GetTempPath(), "aethra_test_output.wav");
            
            Console.WriteLine("=== AETHRA Interpreter Test ===");
            Console.WriteLine();
            Console.WriteLine("Input Script:");
            Console.WriteLine("-------------");
            Console.WriteLine(testScript);
            Console.WriteLine("-------------");
            Console.WriteLine();
            Console.WriteLine($"Output Path: {outputPath}");
            Console.WriteLine();
            
            try
            {
                // Delete existing file if present
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
                
                Console.WriteLine("Running interpreter...");
                Interpreter.Run(testScript, outputPath);
                Console.WriteLine("Interpreter finished.");
                Console.WriteLine();
                
                // Check results
                if (File.Exists(outputPath))
                {
                    var fileInfo = new FileInfo(outputPath);
                    Console.WriteLine($"Output file exists: YES");
                    Console.WriteLine($"Output file size: {fileInfo.Length} bytes");
                    
                    if (fileInfo.Length > 44) // WAV header is 44 bytes
                    {
                        Console.WriteLine($"Audio data size: {fileInfo.Length - 44} bytes");
                        Console.WriteLine();
                        Console.WriteLine("SUCCESS: WAV file generated with audio data!");
                        
                        // Read and display WAV header info
                        using var fs = File.OpenRead(outputPath);
                        using var br = new BinaryReader(fs);
                        
                        string riff = new string(br.ReadChars(4));
                        int fileSize = br.ReadInt32();
                        string wave = new string(br.ReadChars(4));
                        
                        Console.WriteLine();
                        Console.WriteLine("WAV Header Info:");
                        Console.WriteLine($"  RIFF marker: {riff}");
                        Console.WriteLine($"  File size: {fileSize}");
                        Console.WriteLine($"  WAVE marker: {wave}");
                    }
                    else
                    {
                        Console.WriteLine();
                        Console.WriteLine("FAILURE: WAV file has no audio data (only header)");
                    }
                }
                else
                {
                    Console.WriteLine("Output file exists: NO");
                    Console.WriteLine();
                    Console.WriteLine("FAILURE: No output file was created");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
            
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
