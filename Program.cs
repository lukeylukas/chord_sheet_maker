/* ChordSheetMaker
 * 
 * This program generates chord sheets from MSCX musescore sheet music files.
 */
using System.Xml.Linq;
using System;
using System.Text;
using static System.Collections.Specialized.BitVector32;
using static System.Net.Mime.MediaTypeNames;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RazorLight;
using RazorLight.Compilation;
using System.Reflection;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length < 1 || args.Length > 2)
        {
            Console.WriteLine($"Usage: program <file_name> <lyric_file_name(optional)>. {args.Length} args were given");
            return;
        }
        // TODO: validate that args exist as real files with expected types
        string path = args[0];
        string lyric_file_name = "";
        if (args.Length > 1)
        {
            lyric_file_name = args[1];
        }

        ChordSheetMaker.ChordSheetMaker.Generate(path, lyric_file_name);
    }
}
namespace ChordSheetMaker
{
    public class Chord
    {
        public string bass_root { get; set; } = "";
        public string root { get; set; } = "";
        public string name { get; set; } = "";
    }
    public class Lyric
    {
        public string text { get; set; } = "";
        public bool has_continuation { get; set; } = false;
    }
    public class Beat
    {
        public Chord chord { get; set; } = new Chord();
        public bool measure_start { get; set; } = false;
        public List<Lyric>? lyrics { get; set; } // all verses' lyrics linked to each other and the related chord
    }
    public class VerseBeat // contains a single verse's chord and lyric
    {
        public Lyric lyric { get; set; } = new Lyric();
        public string chord { get; set; } = "";
        private static readonly Dictionary<int, string> chord_names = new Dictionary<int, string> {
            [6] = "F♭",
            [7] = "C♭",
            [8] = "G♭",
            [9] = "D♭",
            [10] = "A♭",
            [11] = "E♭",
            [12] = "B♭",
            [13] = "F",
            [14] = "C",
            [15] = "G",
            [16] = "D",
            [17] = "A",
            [18] = "E",
            [19] = "B",
            [20] = "F♯",
            [21] = "C♯",
            [22] = "G♯",
            [23] = "D♯",
            [24] = "A♯",
            [25] = "E♯",
            [26] = "B♯"
            // there are double flats below and double sharps above, but we don't need that
        };
        private static string EncodeChordString(Chord chord)
        {
            string result = "";
            if (int.TryParse(chord.root, out int root_number))
            {
                result += chord_names[root_number] + chord.name;
                if (int.TryParse(chord.bass_root, out int bass_number))
                {
                    result += $"/{chord_names[bass_number]}";
                }
            }
            return result;
        }
        public void InitFromBeat(Beat beat, int lyric_index)
        {
            chord = EncodeChordString(beat.chord);
            if (beat.lyrics != null
                && lyric_index >= 0
                && lyric_index < beat.lyrics.Count)
            {
                lyric = beat.lyrics[lyric_index];
            }
        }
    }
    public class MusicSection
    {
        public string name { get; set; } = "";
        public List<List<VerseBeat>> beats { get; set; } = new();
    }

    public class Line
    {
        public List<string> words { get; set; } = new();
    }
    public class LyricSection
    {
        public string name { get; set; } = "";
        public List<Line> lines { get; set; } = new();
    }
    class ChordSheetMaker
    {
        static readonly string[] NoteNames = new[]
        {
        "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"
        };
        public static void Generate(string path, string lyric_file_name)
        {
            try
            {
                XDocument doc = XDocument.Load(path);
                List<Beat> beats = ParseElements(doc);
                // PrintMscxOutput(beats);

                List<LyricSection> structured_lyrics = new List<LyricSection>();
                if (lyric_file_name != "")
                {
                    structured_lyrics = GetLyricsFile(lyric_file_name);
                }
                else
                {
                    // structured_lyrics = GenerateLyricSections(beats);
                }
                // my_log($"# lyric sections: {structured_lyrics.Count}");
                // my_log($"# beats: {beats.Count}");

                if (structured_lyrics.Count != 0)
                {
                    List<MusicSection> sections = GenerateStructuredSections(structured_lyrics, beats);
                    PrintStructuredSections(sections);
                    Task.Run(async () =>
                    {
                        string html_text = await GenerateChordSheetHtml(sections);
                        File.WriteAllText("output.html", html_text);
                    }).Wait();
                }
            }
            catch (IOException e)
            {
                my_log(e.ToString());
            }
        }
        static List<Beat> ParseElements(XDocument doc)
        {
            // more elements can be handled as needed.

            // in this function we'll also unroll the music, using repeats and codas
            // if the music is unrolled, then the Beat -> VerseBeat transformation is already done.

            List<string> elementTypes = new List<string> { "Harmony", "Chord", "Measure", "Rest"};
            var elements = doc.Descendants().Where(e => elementTypes.Contains(e.Name.ToString()));
            var beats = new List<Beat>();
            var syllableCount = 0;
            Chord last_chord = new Chord();
            bool measure_start = false;

            foreach (var e in elements)
            {
                switch (e.Name.ToString())
                {
                    case "Harmony":
                    {
                        if (!string.IsNullOrEmpty(last_chord.root)
                            && !string.IsNullOrEmpty(last_chord.root))
                        {
                            AddBeat(beats, last_chord, measure_start, null);
                            measure_start = false;
                            last_chord = new Chord();
                        }

                        if (e.Element("underline") != null)
                        {
                            if (last_chord.root != "")
                            {
                                last_chord.bass_root = last_chord.root; // assumption that Harmony elements could be in either order
                            }
                            last_chord.name = e.Element("name")?.Value; // really it's a modifier
                            last_chord.root = e.Element("root")?.Value; // value of the chord
                        }
                        else
                        {
                            last_chord.bass_root = e.Element("root")?.Value;
                        }
                        break;
                    }
                    case "Chord":
                    {
                        var lyrics = GetLyricsInChordElement(e);
                        if (lyrics.Count > 0)
                        {
                            syllableCount++;
                            AddBeat(beats, last_chord, measure_start, lyrics);
                            measure_start = false;
                            last_chord = new Chord();
                        }
                        break;
                    }
                    case "Rest":
                    {
                        if (!string.IsNullOrEmpty(last_chord.root))
                        {
                            AddBeat(beats, last_chord, measure_start, null);
                            last_chord = new Chord();
                        }
                        measure_start = false;
                        break;
                    }
                    case "Measure":
                    {
                        measure_start = true;
                        last_chord = new Chord();
                        // TODO: if count of measures without a lyric is 2 or more, it's an interlude
                        break;
                    }
                    default:
                    {
                        // throw new Exception($"no handling for element type {e.Name} in ParseElements");
                        break;
                    }
                }
            }
            return beats;
        }
        static void AddBeat(List<Beat> beats, Chord last_chord, bool measure_start, List<Lyric>? lyrics)
        {
            var beat = new Beat();
            beat.chord = last_chord;
            if (beat.chord.root == "" && beat.chord.bass_root != "")
            {
                beat.chord.root = beat.chord.bass_root;
                beat.chord.bass_root = "";
            }
            beat.measure_start = measure_start;

            if (lyrics != null)
            {
                beat.lyrics = lyrics;
            }
            beats.Add(beat);
        }
        static List<Lyric> GetLyricsInChordElement(XElement chordElement)
        {
            List<Lyric> lyrics = new List<Lyric>();
            foreach (var lyric in chordElement.Elements("Lyrics"))
            {
                string text = lyric.Element("text")?.Value ?? "";
                string syllabic = lyric.Element("syllabic")?.Value ?? "";

                bool has_continuation = syllabic.Equals("begin", StringComparison.OrdinalIgnoreCase)
                                    || syllabic.Equals("middle", StringComparison.OrdinalIgnoreCase);

                text = Regex.Replace(text, @"^\d+\.[\s\u00A0\u202F]*", "");

                lyrics.Add(new Lyric() {
                    text = text,
                    has_continuation = has_continuation
                });
            }
            return lyrics;
        }
        static List<LyricSection> GetLyricsFile(string file_name)
        {
            // gets the lyrics with verse/chorus info from a file,
            // separates into lines and sections.
            string[] lines = File.ReadAllLines(file_name);
            var result = new List<LyricSection>();
            LyricSection? current_section = null;
            bool has_started = false;

            foreach (var raw in lines)
            {
                string line = raw.Trim();

                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }
                else if (line.StartsWith("Verse") ||
                        line.StartsWith("Chorus") ||
                        line.StartsWith("Tag") ||
                        line.StartsWith("Bridge"))
                {
                    if (!has_started && line.StartsWith("Verse"))
                    {
                        has_started = true;
                    }

                    if (has_started)
                    {
                        if (current_section != null)
                        {
                            result.Add(current_section);
                        }

                        current_section = new LyricSection
                        {
                            name = line
                        };
                    }
                }
                else if (current_section != null)
                {
                    string stripped_line = StripPunctuationFromString(line);
                    List<string> words = stripped_line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                    current_section.lines.Add(new Line() { words = words });
                }
            }

            if (current_section != null)
            {
                result.Add(current_section);
            }

            // my_log("Lyrics:");
            // for (int i = 0; i < result.Count; i++)
            // {
            //     for (int j = 0; j < result[i].lines.Count; j++)
            //     {
            //         my_log(string.Join(" ", result[i].lines[j].words));
            //     }
            //     my_log("");
            // }
            return result;
        }
        static string StripPunctuationFromString(string input)
        {
            return new string(input.Where(
                    c => char.IsLetterOrDigit(c) ||
                        char.IsWhiteSpace(c) ||
                        c == '\'' ||
                        c == '’'
                ).ToArray());
        }
        static List<LyricSection> GenerateLyricSections(List<Beat> beats)
        {
            // either interpret meter or use help from another source
            // generate structured lyrics from beats, separating into lines and sections.
            return null;
        }
        static List<MusicSection> GenerateStructuredSections(List<LyricSection> structured_lyrics, List<Beat> beats)
        {
            // use the beats gathered from mscx file, and the structured lyrics,
            // to create structured musical sections containing lines of syllables and chords
            var instrumental_section = new MusicSection();
            var beat_processing_lists = new List<List<VerseBeat>>();
            bool instrumental_measure_start = false;
            var musical_sections = new List<MusicSection>();

            foreach (Beat beat in beats)
            {
                // my_log($"{beat.chord.root} {beat.chord.name} with measure start {beat.measure_start}");
                // if (beat.lyrics != null)
                // {
                //     foreach(var lyric in beat.lyrics)
                //     {
                //         my_log(lyric.text);
                //     }
                // }

                // build up the sections, and once they get long enough to match (length of the lyric sections), if the sections match a LyricSection, assign the right name to them.
                // All lines need to match. otherwise refrains would cause grief

                // if there's any chords with no syllable, hold them in a temporary instrumental_section.
                // If more lyrics come we can always append that section to each active section.

                if (beat.lyrics == null || beat.lyrics.Count == 0)
                {
                    VerseBeat chord_beat = new VerseBeat();
                    chord_beat.InitFromBeat(beat, -1);

                    if (instrumental_section.beats.Count == 0)
                    {
                        instrumental_section.beats.Add(new List<VerseBeat>());
                    }
                    if (beat.measure_start && instrumental_measure_start)
                    {
                        if (musical_sections.Count == 0)
                        {
                            instrumental_section.name = "Introduction";
                        }
                        else
                        {
                            instrumental_section.name = "Instrumental";
                        }
                    }
                    else if (beat.measure_start)
                    {
                        instrumental_measure_start = true;
                    }
                    instrumental_section.beats[0].Add(chord_beat);
                    // my_log($"chord {chord_beat.chord.root} added with last lyric {beat_processing_lists[0].Last().lyric.text}");
                }
                else if (beat_processing_lists.Count != 0 && beat_processing_lists.Count != beat.lyrics.Count)
                {
                    string syllables_string = "";
                    foreach(Lyric lyric in beat.lyrics)
                    {
                        syllables_string += $"\'{lyric.text}\', ";
                    }
                    my_log($"There are {beat.lyrics.Count} lyrics in this beat but only {beat_processing_lists.Count} sections being generated.");
                    my_log($"Lyrics: {syllables_string}");
                    return null;
                }
                else
                {
                    // ProcessInstrumental here so that, if needed, it includes any instrumental into the section after the last word of the section
                    instrumental_section = ProcessInstrumental(musical_sections, beat_processing_lists, instrumental_section);
                    instrumental_measure_start = false;
                    AddFinishedSections(musical_sections, structured_lyrics, beat_processing_lists);

                    if (beat_processing_lists.Count == 0)
                    {
                        for (int i = 0; i < beat.lyrics.Count; i++)
                        {
                            beat_processing_lists.Add(new List<VerseBeat>());
                        }
                    }

                    for (int i = 0; i < beat.lyrics.Count; i++)
                    {
                        var vb = new VerseBeat();
                        vb.InitFromBeat(beat, i);
                        beat_processing_lists[i].Add(vb);
                    }
                }
            }
            ProcessInstrumental(musical_sections, beat_processing_lists, instrumental_section);
            AddFinishedSections(musical_sections, structured_lyrics, beat_processing_lists);
            return musical_sections;
        }
        static MusicSection ProcessInstrumental(List<MusicSection> finished_sections, List<List<VerseBeat>> beat_lists, MusicSection instrumental_section)
        {
            MusicSection return_instrumental = instrumental_section;
            bool instrumental_has_chords = instrumental_section.beats.Count != 0;
            bool instrumental_is_named = instrumental_section.name != "";
            if (instrumental_has_chords)
            {
                if (instrumental_is_named)
                {
                    finished_sections.Add(instrumental_section);
                    my_log($"new inst added: {finished_sections.Count}");
                }
                else
                {
                    AddInstrumentalToBeatLists(beat_lists, instrumental_section.beats[0]);
                }
                return_instrumental = new MusicSection();
            }
            return return_instrumental;
        }
        static void AddInstrumentalToBeatLists(List<List<VerseBeat>> beat_lists, List<VerseBeat> instrumental_section)
        {
            foreach (var verse in beat_lists)
            {
                verse.AddRange(instrumental_section);
            }
        }
        static void AddFinishedSections(List<MusicSection> finished_sections, List<LyricSection> structured_lyrics, List<List<VerseBeat>> beat_lists)
        {
            List<MusicSection> temp_sections = new List<MusicSection>();
            for (int i = beat_lists.Count - 1; i >= 0; i--)
            {
                var verse = beat_lists[i];
                if (FormatMusicSectionByLyrics(out MusicSection? new_section, structured_lyrics, verse) &&
                    new_section != null)
                {
                    temp_sections.Add(new_section);
                    beat_lists.RemoveAt(i);
                    // my_log($"new verse added: {finished_sections.Count}");
                }
            }

            for (int i = temp_sections.Count - 1; i >= 0; i--)
            {
                finished_sections.Add(temp_sections[i]);
            }
        }
        static bool FormatMusicSectionByLyrics(out MusicSection? new_section, List<LyricSection> structured_lyrics, List<VerseBeat> beats)
        {
            bool new_section_formed = false;
            new_section = null;
            int beats_full_word_count = 0;
            string temp_word = "";

            for (int i = 0; i < beats.Count; i++)
            {
                VerseBeat beat = beats[i];
                if (beat.lyric.text != "")
                {
                    temp_word += StripPunctuationFromString(beat.lyric.text);
                    if (!beat.lyric.has_continuation)
                    {
                        beats_full_word_count++;
                        temp_word = "";
                    }
                }
            }
            // my_log($"word count {beats_full_word_count}. {structured_lyrics[0].name} {structured_lyrics[1].name} {structured_lyrics[2].name}");

            foreach (LyricSection lyric_set in structured_lyrics)
            {
                if (!LyricsLengthsMatch(lyric_set, beats_full_word_count))
                {
                    continue;
                }
                // my_log($"length match with {lyric_set.name}");

                new_section = new MusicSection()
                {
                    name = lyric_set.name
                };
                temp_word = "";
                int lyric_set_index = 0;
                int word_count = 0;
                int beat_start_index = 0;
                int beat_idx = 0;

                for (; beat_idx < beats.Count; beat_idx++)
                {
                    VerseBeat beat = beats[beat_idx];
                    if (beat.lyric.text != "")
                    {
                        temp_word += StripPunctuationFromString(beat.lyric.text);
                        if (!beat.lyric.has_continuation)
                        {
                            if (!WordsMatchNoApostrophe(temp_word, lyric_set.lines[lyric_set_index].words[word_count]))
                            {
                                // my_log($"word count: {word_count}, beats_word: {temp_word}, lyric: {lyric_set.lines[lyric_set_index].words[word_count]}");
                                break;
                            }
                            word_count++;
                            temp_word = "";
                        }

                        if (word_count == lyric_set.lines[lyric_set_index].words.Count)
                        {
                            int next_verse_start_beat = beat_idx + 1;
                            new_section.beats.Add(beats.GetRange(beat_start_index, next_verse_start_beat - beat_start_index)); // TODO: doesn't include chords between lines

                            beat_start_index = next_verse_start_beat;
                            word_count = 0;
                            lyric_set_index++;
                        }
                    }
                }
                if (beat_idx == beats.Count
                    && new_section.beats.Count == lyric_set.lines.Count)
                {
                    new_section_formed = true;
                    break;
                }
            }
            return new_section_formed;
        }
        static bool LyricsLengthsMatch(LyricSection lyric_set, int word_count)
        {
            int next_start_idx = 0;
            foreach (Line line in lyric_set.lines)
            {
                next_start_idx += line.words.Count;
            }
            return next_start_idx == word_count;
        }
        static bool LyricsMatch(LyricSection lyric_set, List<string> word_list)
        {
            if (!LyricsLengthsMatch(lyric_set, word_list.Count))
            {
                return false;
            }
            int next_start_idx = 0;
            // my_log($"length is good at {word_list.Count}. check match against lyric set {lyric_set.name}");
            foreach (Line line in lyric_set.lines)
            {
                if (!WordListsMatch(line.words, word_list.GetRange(next_start_idx, line.words.Count)))
                {
                    return false;
                }
                next_start_idx += line.words.Count;
            }
            my_log($"found section match for {lyric_set.name}");
            return true;
        }
        static bool WordListsMatch(List<string> words1, List<string> words2)
        {
            if (words1.Count != words2.Count)
            {
                foreach (var word in words2)
                {
                    my_log(word);
                }
                my_log($"length not equal. {words1.Count} lyrics vs {words2.Count} beat words");
                return false;
            }
            for (int i = 0; i < words1.Count; i++)
            {
                if (!WordsMatchNoApostrophe(words1[i], words2[i]))
                {
                    // my_log($"{words1[i]} != {words2[i]}, {words1.Count} words");
                    return false;
                }
            }
            return true;
        }
        static bool WordsMatchNoApostrophe(string word1, string word2)
        {
            return StringsMatch(word1, word2)
                || word1.Contains("\'")
                || word2.Contains("\'");
        }
        static bool StringsMatch(string input1, string input2)
        {
            if (input1 == null || input2 == null)
            {
                return false;
            }
            return input1.Equals(input2, StringComparison.OrdinalIgnoreCase);
        }

        static async Task<string> GenerateChordSheetHtml(List<MusicSection> sections)
        {
            if (sections == null || sections.Count == 0)
            {
                my_log("no sections to encode!");
                return null;
            }
            else if (sections[0].beats == null || sections[0].beats.Count == 0 || sections[0].beats[0].Count == 0)
            {
                my_log("no lines to encode!");
                return null;
            }
            var assembly = typeof(Program).Assembly;
            var engine = new RazorLightEngineBuilder()
                .UseEmbeddedResourcesProject(assembly, "chord_sheet_maker.Templates")
                .UseMemoryCachingProvider()
                .Build();

            string template_key = "VerseLine.cshtml";
            return await engine.CompileRenderAsync(template_key, sections[0].beats[0]);
        }
        static string FormatChord(string? root, string? name)
        {
            string rootName = RootToNoteName(root);
            if (string.IsNullOrEmpty(rootName)) return "";
            return string.IsNullOrEmpty(name) ? rootName : rootName + name;
        }
        static string RootToNoteName(string? rootValue)
        {
            if (string.IsNullOrEmpty(rootValue))
                return "";

            if (int.TryParse(rootValue, out int num))
                return NoteNames[num % 12];  // wrap safely for >11
            return rootValue; // fallback
        }

    /* ----------------------------------------------- Helpers -------------------------------------------------- */
        static void my_log(string msg)
        {
            Console.Write(msg + System.Environment.NewLine);
        }
        static void PrintMscxOutput(List<Beat> beats)
        {
            foreach (Beat beat in beats)
            {
                var name = "";
                if (beat.chord.name != null)
                {
                    name = " " + beat.chord.name;
                }

                if (beat.chord.root != null)
                {
                    Console.WriteLine($"Chord: {beat.chord.root}{name}");
                }

                if (beat.lyrics != null)
                {
                    for (int i = 0; i < beat.lyrics.Count; i++)
                    {
                        Console.WriteLine($"  Verse {i + 1}: {beat.lyrics[i].text}");
                    }
                }
            }
        }
        static void PrintStructuredSections(List<MusicSection> sections)
        {
            foreach (var section in sections)
            {
                Console.WriteLine($"=== {section.name} ===");

                foreach (var beatRow in section.beats) // List<VerseBeat>
                {
                    foreach (var beat in beatRow)
                    {
                        string lyric = beat.lyric?.text ?? "";
                        Console.Write($"[{beat.chord}: {lyric}] ");
                    }
                    Console.WriteLine();
                }
                Console.WriteLine();
            }
        }
    }
}