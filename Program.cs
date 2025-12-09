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
using System.IO.Compression;
using System.Linq;

class Program
{
    static void Main(string[] args)
    {
        string scoreFile = null;
        string lyricsFile = null;
        bool no_bass = false;
        bool generate_html = false;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--score="))
            {
                scoreFile = arg.Substring("--score=".Length);
            }
            else if (arg.StartsWith("--lyrics="))
            {
                lyricsFile = arg.Substring("--lyrics=".Length);
            }
            else if (arg.StartsWith("--no-bass"))
            {
                no_bass = true;
            }
            else if (arg.StartsWith("--html"))
            {
                generate_html = true;
            }
        }
        bool generate_chord_pro = true;

        if (scoreFile == null)
        {
            Console.WriteLine("Usage: program --score=<file> [--lyrics=<file>]");
            return;
        }
        var maker = new ChordSheetMaker.ChordSheetMaker();
        maker.Generate(scoreFile, lyricsFile, no_bass, generate_html, generate_chord_pro);
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
        public bool measure_start { get; set; } = false;
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
                result += $"{chord_names[root_number]}{chord.name}";
                if (int.TryParse(chord.bass_root, out int bass_number))
                {
                    result += $"/{chord_names[bass_number]}";
                }
            }
            return result;
        }
        public void InitFromBeat(Beat beat, int lyric_index)
        {
            measure_start = beat.measure_start;
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
    public class Song
    {
        public string name { get; set; } = "";
        public List<MusicSection> sections { get; set; } = new();
        public Song(string name, List<MusicSection> sections)
        {
            this.name = name;
            this.sections = sections;
        }
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
        string _song_name = "";
        public void Generate(string path, string lyric_file_name, bool no_bass, bool generate_html, bool generate_chord_pro)
        {
            string fileName = Path.GetFileNameWithoutExtension(path);
            try
            {
                XDocument doc = new XDocument();
                if (path.EndsWith(".mscz"))
                {
                    var msczBytes = File.ReadAllBytes(path);
                    string mscxXml = ExtractMscxFromMscz(fileName, msczBytes);
                    doc = XDocument.Parse(mscxXml);
                }
                else if (path.EndsWith(".mscx"))
                {
                    doc = XDocument.Load(path);
                }
                else
                {
                    my_log("path invalid: " + path);
                    return;
                }
                List<Beat> beats = ParseElements(doc, no_bass);
                // PrintMscxOutput(beats);

                List<LyricSection> structured_lyrics = new List<LyricSection>();
                if (!String.IsNullOrEmpty(lyric_file_name))
                {
                    structured_lyrics = GetLyricsFile(lyric_file_name);
                }
                else
                {
                    my_log("no lyric file name");
                    // structured_lyrics = GenerateLyricSections(beats);
                }
                // my_log($"# lyric sections: {structured_lyrics.Count}");
                // my_log($"# beats: {beats.Count}");

                if (structured_lyrics != null && structured_lyrics.Count != 0)
                {
                    List<MusicSection> sections = GenerateStructuredSections(structured_lyrics, beats);
                    if (no_bass)
                    {
                        RemoveRepeatedChords(sections);
                    }
                    var song = new Song(_song_name, sections);
                    // PrintStructuredSections(sections);

                    if (generate_chord_pro)
                    {
                        string chord_pro_text = WriteChordPro(song);
                        string full_name = fileName + ".chordpro";
                        File.WriteAllText(full_name, chord_pro_text);
                        my_log(full_name);
                    }

                    if (generate_html)
                    {
                        Task.Run(async () =>
                        {
                            string html_text = await GenerateChordSheetHtml(song);
                            string full_name = fileName + ".html";
                            File.WriteAllText(full_name, html_text);
                            my_log(full_name);
                        }).Wait();
                    }
                }
            }
            catch (IOException e)
            {
                my_log(e.ToString());
            }
        }
        private static string ExtractMscxFromMscz(string fileName, byte[] msczBytes)
        {
            using var memoryStream = new MemoryStream(msczBytes);
            using var zip = new ZipArchive(memoryStream, ZipArchiveMode.Read);

            // The main score file inside is usually named "score.mscx"
            var entry = zip.GetEntry(fileName + ".mscx");
            if (entry == null)
            {
                throw new FileNotFoundException(".mscx file not found in mscz");
            }

            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream, Encoding.UTF8);

            return reader.ReadToEnd();   // Return MSCX file as string
        }
        
        List<Beat> ParseElements(XDocument doc, bool no_bass)
        {
            // more elements can be handled as needed.
            List<string> elementTypes = new List<string> { "metaTag", "Harmony", "Chord", "Measure", "Rest"};
            var elements = doc.Descendants().Where(e => elementTypes.Contains(e.Name.ToString()));
            var beats = new List<Beat>();
            var syllableCount = 0;
            Chord last_chord = new Chord();
            bool measure_start = false;

            foreach (var e in elements)
            {
                switch (e.Name.ToString())
                {
                    case "metaTag":
                    {
                        string? name = (string?)e.Attribute("name");
                        if (name != null && name == "workTitle")
                        {
                            _song_name = e.Value;
                        }
                        break;
                    }
                    case "Harmony":
                    {
                        if (!string.IsNullOrEmpty(last_chord.root)
                            && !string.IsNullOrEmpty(last_chord.bass_root))
                        {
                            AddBeat(beats, last_chord, measure_start, null);
                            measure_start = false;
                            last_chord = new Chord();
                        }

                        string? root = e.Element("root")?.Value;
                        if (root == null)
                        {
                            root = "";
                        }
                        string? name = e.Element("name")?.Value;
                        if (name == null)
                        {
                            name = "";
                        }

                        if (e.Element("underline") != null)
                        {
                            if (last_chord.root != "" && !no_bass)
                            {
                                last_chord.bass_root = last_chord.root; // assumption that Harmony elements could be in either order
                            }
                            last_chord.name = name; // really it's a modifier
                            last_chord.root = root; // value of the chord
                        }
                        else
                        {
                            if (last_chord.root == "")
                            {
                                last_chord.name = name;
                            }

                            if (no_bass)
                            {
                                last_chord.root = root;
                            }
                            else
                            {
                                last_chord.bass_root = root;
                            }
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
                        if (last_chord.root != ""
                            || last_chord.bass_root != "")
                        {
                            if (last_chord.root == "")
                            {
                                last_chord.root = last_chord.bass_root;
                                last_chord.bass_root = "";
                            }
                            AddBeat(beats, last_chord, measure_start, null);
                        }
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

                text = Regex.Replace(text, @"^\d+\.[\s\u00A0\u202F]*", ""); // remove leading number, dot and non-breaking space if present
                text = text.Trim();

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

            foreach (var raw in lines)
            {
                string line = raw.Trim();

                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }
                else if (line.StartsWith("Verse") ||
                        line.StartsWith("Chorus") ||
                        line.StartsWith("Refrain") ||
                        line.StartsWith("Tag") ||
                        line.StartsWith("Bridge"))
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
        static List<MusicSection> GenerateStructuredSections(List<LyricSection> input_lyrics, List<Beat> beats)
        {
            // use the beats gathered from mscx file, and the structured lyrics,
            // to create structured musical sections containing lines of syllables and chords
            var instrumental_section = new MusicSection();
            var beat_processing_lists = new List<List<VerseBeat>>();
            bool instrumental_measure_start = false;
            var musical_sections = new List<MusicSection>();

            var verse_names = new List<string>();
            
            var structured_lyrics = new List<LyricSection> ();
            foreach (var verse in input_lyrics)
            {
                structured_lyrics.Add(verse);
                verse_names.Add(verse.name);
            }

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
                    // my_log($"chord {chord_beat.chord} added with last lyric {beat_processing_lists[0].Last().lyric.text}");
                }
                else
                {
                    // ProcessInstrumental here so that, if needed, it includes any instrumental into the section after the last word of the section
                    instrumental_section = ProcessInstrumental(musical_sections, beat_processing_lists, instrumental_section);
                    instrumental_measure_start = false;
                    AddFinishedSections(musical_sections, structured_lyrics, beat_processing_lists);

                    if (beat_processing_lists.Count != 0 && beat_processing_lists.Count != beat.lyrics.Count)
                    {
                        string syllables_string = "";
                        foreach(Lyric lyric in beat.lyrics)
                        {
                            syllables_string += $"\'{lyric.text}\', ";
                        }
                        my_log($"There are {beat.lyrics.Count} lyrics in this beat but {beat_processing_lists.Count} sections being generated.");
                        my_log($"Lyrics in beat: {syllables_string}");
                        break;
                    }

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
            if (structured_lyrics.Count != 0)
            {
                PrintRemainingLyrics(structured_lyrics);
                PrintRemainingBeatLyrics(beat_processing_lists);
                return new List<MusicSection>();
            }

            var result = new List<MusicSection>();
            foreach (var name in verse_names)
            {
                foreach(var section in musical_sections)
                if (section.name == name)
                {
                    result.Add(section);
                }
            }
            return result;
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
                    foreach(var lyric_verse in structured_lyrics)
                    {
                        if (lyric_verse.name == new_section.name)
                        {
                            structured_lyrics.Remove(lyric_verse);
                            break;
                        }
                    }
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
            string temp_word = "";

            int beats_full_word_count = 0;
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
                            int next_line_start_beat = -1;
                            int counter = 1;
                            while(next_line_start_beat < 0)
                            {
                                if (beats.Count == beat_idx + counter
                                    || beats[beat_idx + counter].measure_start
                                    || beats[beat_idx + counter].lyric.text != "")
                                {
                                    next_line_start_beat = beat_idx + counter;
                                }
                                counter++;
                            };
                            new_section.beats.Add(beats.GetRange(beat_start_index, next_line_start_beat - beat_start_index)); // TODO: doesn't include chords between lines

                            beat_start_index = next_line_start_beat;
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
        static void PrintRemainingBeatLyrics(List<List<VerseBeat>> verses)
        {
            foreach(var verse in verses)
            {
                string print_word = "";
                my_log("Verse from .mscx file:");
                for (int i = 0; i < verse.Count; i++)
                {
                    VerseBeat print_beat = verse[i];
                    if (print_beat.lyric.text != "")
                    {
                        print_word += print_beat.lyric.text;
                        if (!print_beat.lyric.has_continuation)
                        {
                            my_log(print_word);
                            print_word = "";
                        }
                    }
                }
                my_log("");
            }
        }
        static void PrintRemainingLyrics(List<LyricSection> structured_lyrics)
        {
            foreach(var verse in structured_lyrics)
            {            
                my_log("Remaining lyrics from text file:");
                foreach(var line in verse.lines)
                {
                    my_log(string.Join(" ", line.words));
                }
                my_log("");
            }
        }

        static void RemoveRepeatedChords(List<MusicSection> sections)
        {
            foreach (var verse in sections)
            {
                foreach (var line in verse.beats)
                {
                    string last_chord = "";
                    foreach (var beat in line)
                    {
                        if (beat.chord == last_chord)
                        {
                            beat.chord = "";
                        }
                        else
                        {
                            last_chord = beat.chord;
                        }
                    }
                }
            }
        }

        static string WriteChordPro(Song song)
        {
            string chord_pro = "";
            int chord_space_minimum = 4;
            chord_pro += $"{{title: {song.name}}}" + Environment.NewLine;
            // artist, key, tempo and time can go here, as well as hymnal # if possible

            foreach (var verse in song.sections)
            {
                chord_pro += $"{{comment: {verse.name}}}" + Environment.NewLine;
                foreach (var line in verse.beats)
                {
                    int space_count = 0;
                    bool is_continuation = false;
                    foreach (var beat in line)
                    {
                        if (!is_continuation)
                        {
                            chord_pro += " ";
                        }

                        if (beat.chord != "")
                        {
                            if (space_count > 1)
                            {
                                int space_idx = chord_pro.LastIndexOf(' ');
                                int bracket_idx = chord_pro.LastIndexOf(']');
                                if (space_idx > bracket_idx) // prefer lengthening spaces if possible
                                {
                                    chord_pro = chord_pro[..space_idx] + new string(' ', space_count) + chord_pro[(space_idx + 1)..];
                                }
                                else // insert hyphen section if needed
                                {
                                    string spaces = new string(' ', space_count / 2) + "-" + new string(' ', space_count / 2);
                                    chord_pro += spaces;
                                }
                            }
                            chord_pro += $"[{beat.chord}]";
                            space_count = (int)(beat.chord.Length*1.3) - beat.lyric.text.Length + chord_space_minimum;
                        }
                        else
                        {
                            space_count -= beat.lyric.text.Length;
                            if (!beat.lyric.has_continuation)
                            {
                                space_count--;
                            }
                        }
                        chord_pro += beat.lyric.text;
                        is_continuation = beat.lyric.has_continuation;
                    }
                    chord_pro += Environment.NewLine;
                }
                chord_pro += Environment.NewLine;
            }
            return chord_pro;
        }

        static async Task<string> GenerateChordSheetHtml(Song song)
        {
            if (song == null || song.sections == null || song.sections.Count == 0)
            {
                my_log("no sections to encode!");
                return null;
            }
            else if (song.sections[0].beats == null || song.sections[0].beats.Count == 0 || song.sections[0].beats[0].Count == 0)
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
            return await engine.CompileRenderAsync(template_key, song);
        }

    /* ----------------------------------------------- Helpers -------------------------------------------------- */
        public static void my_log(string msg)
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