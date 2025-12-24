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
        string scoreFile = "";
        string lyricsFile = "";
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

        if (scoreFile == "")
        {
            PrintUsage();
            return;
        }
        var maker = new ChordSheetMaker.ChordSheetMaker();
        maker.Generate(scoreFile, lyricsFile, no_bass, generate_html, generate_chord_pro);
    }
    static void PrintUsage()
    {
        Console.WriteLine("Usage: program --score=<file>" + Environment.NewLine +
                          "       [" + Environment.NewLine +
                          "        --lyrics=<file>" + Environment.NewLine +
                          "        --html (for optional html printable output)" + Environment.NewLine +
                          "        --no-bass (to remove bass indicators from chords)]" + Environment.NewLine +
                          "       ]");
    }
}
namespace ChordSheetMaker
{
    public class SongMetadata
    {
        public string author { get; set; } = ""; // composer, lyricist, arranger
        public string key { get; set; } = "C"; // KeySig
        public string tempo { get; set; } = ""; // Tempo
        public string time { get; set; } = ""; // TimeSig
    }
    public class Chord
    {
        public string bass_root { get; set; } = "";
        public string root { get; set; } = "";
        public string name { get; set; } = "";
    }
    public class Lyric
    {
        public string text { get; set; } = "";
        public bool syllable_follows { get; set; } = false;
        public bool hyphenated_word_follows { get; set; } = false;
        public Lyric(string text, bool syllable_follows, bool hyphenated_word_follows)
        {
            this.text = ChordSheetMaker.StripPunctuationFromString(text);
            this.syllable_follows = syllable_follows;
            this.hyphenated_word_follows = hyphenated_word_follows;
        }
        public Lyric() : this(string.Empty, false, false) {}
        public Lyric(Lyric other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));

            text = other.text;
            syllable_follows = other.syllable_follows;
            hyphenated_word_follows = other.hyphenated_word_follows;
        }
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
        public void InitFromBeat(Beat beat, int lyric_index)
        {
            measure_start = beat.measure_start;
            chord = ChordTranslate.EncodeString(beat.chord);
            if (beat.lyrics != null
                && lyric_index >= 0
                && lyric_index < beat.lyrics.Count)
            {
                lyric = beat.lyrics[lyric_index];
            }
        }
    }
    public static class ChordTranslate
    {
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
        public static string EncodeString(Chord chord)
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
    }
    public class MusicSection
    {
        public string name { get; set; } = "";
        public List<List<VerseBeat>> beats { get; set; } = new();
    }
    public class Song
    {
        public string name { get; set; } = "";
        public SongMetadata metadata { get; set; } = new();
        public List<MusicSection> sections { get; set; } = new();
        public Song(string name, SongMetadata metadata, List<MusicSection> sections)
        {
            this.name = name;
            this.metadata = metadata;
            this.sections = sections;
        }
    }

    public class Line
    {
        public List<Lyric> words { get; set; } = new();

        public List<string> GetTextList()
        {
            return words
                .Where(l => !string.IsNullOrWhiteSpace(l.text))
                .Select(l => l.text)
                .ToList();
        }
    }
    public class LyricSection
    {
        public string name { get; set; } = "";
        public List<Line> lines { get; set; } = new();
        public List<string> GetTextList()
        {
            return lines
                .SelectMany(l => l.GetTextList())
                .ToList();
        }
    }
    class ChordSheetMaker
    {
        private static readonly Dictionary<string, string> key_names = new Dictionary<string, string> {
            ["-7"] = "C♭",
            ["-6"] = "G♭",
            ["-5"] = "D♭",
            ["-4"] = "A♭",
            ["-3"] = "E♭",
            ["-2"] = "B♭",
            ["-1"] = "F",
            ["0"] = "C",
            ["1"] = "G",
            ["2"] = "D",
            ["3"] = "A",
            ["4"] = "E",
            ["5"] = "B",
            ["6"] = "F♯",
            ["7"] = "C♯"
        };
        string _song_name = "";
        SongMetadata _metadata = new SongMetadata();

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
                    InsertHyphensBetweenWords(structured_lyrics, beats);
                    FillInWords(structured_lyrics, beats);
                    List<MusicSection> sections = GenerateStructuredSections(structured_lyrics, beats);
                    if (no_bass)
                    {
                        RemoveRepeatedChords(sections);
                    }
                    var song = new Song(_song_name, _metadata, sections);
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
            List<string> elementTypes = new List<string> {
                "metaTag",
                "KeySig",
                "TimeSig",
                "Tempo",
                "Harmony",
                "Chord",
                "Measure",
                "Rest"};
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
                        switch (name)
                        {
                            case "workTitle":
                            {
                                _song_name = e.Value;
                                break;
                            }
                            case "composer":
                            case "lyricist":
                            case "arranger":
                            {
                                if (_metadata.author != "" && e.Value.Trim() != "")
                                {
                                    _metadata.author += ", ";
                                }
                                _metadata.author += e.Value;
                                break;
                            }
                        }
                        break;
                    }
                    case "KeySig":
                    {
                        string? accidentals = e.Element("accidental")?.Value;
                        if (accidentals != null)
                        {
                            _metadata.key = key_names[accidentals];
                        }
                        break;
                    }
                    case "TimeSig":
                    {
                        string? numerator = e.Element("sigN")?.Value;
                        string? denominator = e.Element("sigD")?.Value;
                        if (numerator != null && denominator != null)
                        {
                            _metadata.time = $"{numerator}/{denominator}";
                        }
                        break;
                    }
                    case "Tempo":
                    {
                        var tempo = e.Element("text")?.Value;
                        if (tempo != null)
                        {
                            _metadata.tempo = tempo.Split('=').Last().Trim();
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
                int.TryParse(lyric.Element("no")?.Value, out int index);
                string text = lyric.Element("text")?.Value ?? "";
                string syllabic = lyric.Element("syllabic")?.Value ?? "";

                bool syllable_follows = syllabic.Equals("begin", StringComparison.OrdinalIgnoreCase)
                                    || syllabic.Equals("middle", StringComparison.OrdinalIgnoreCase);

                text = Regex.Replace(text, @"^\d+\.[\s\u00A0\u202F]*", ""); // remove leading number, dot and non-breaking space if present
                text = Regex.Replace(text, @"^DS\.[\s\u00A0\u202F]*", ""); // remove leading "DS." and non-breaking space if present
                text = text.Trim();

                while (lyrics.Count <= index)
                {
                    lyrics.Add(new Lyric());
                }
                lyrics[index] = new Lyric()
                {
                    text = text,
                    syllable_follows = syllable_follows,
                    hyphenated_word_follows = false
                };
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
                    List<string> words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                    Line new_line = new Line();
                    bool syllable_follows = false;
                    foreach (var word in words)
                    {
                        var hyphen_split_words = word.Split('-', StringSplitOptions.RemoveEmptyEntries).ToList();
                        for (int i = 0; i < hyphen_split_words.Count; i++)
                        {
                            bool hyphenated_word_follows = (i < hyphen_split_words.Count - 1);
                            new_line.words.Add(new Lyric(hyphen_split_words[i], syllable_follows, hyphenated_word_follows));
                        }
                    }
                    current_section.lines.Add(new_line);
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
            //         string print_words = "";
            //         foreach(var lyric in result[i].lines[j].words)
            //         {
            //             print_words += lyric.text + " ";
            //         }
            //         my_log(print_words);
            //     }
            //     my_log("");
            // }
            return result;
        }
        public static string StripPunctuationFromString(string input)
        {
            return new string(input.Where(
                    c => char.IsLetterOrDigit(c) ||
                        char.IsWhiteSpace(c) ||
                        c == '\'' ||
                        c == '’'
                ).ToArray());
        }
        static List<LyricSection>? GenerateLyricSections(List<Beat> beats)
        {
            // either interpret meter or use help from another source
            // generate structured lyrics from beats, separating into lines and sections.
            return null;
        }
        static void FillInWords(List<LyricSection> structured_lyrics, List<Beat> beats)
        {
            // this functionality is needed whether or not there's unrolling. Do it before the unroll.
            var temp_words = new List<string>(); // stores the temporary parsing for each verse simultaneously as it works through the beats
            var beat_words = new List<List<string>>();
            List<bool> verse_matches = new List<bool>();
            List<int> last_syllable_idx = new List<int>();
            List<List<int>> match_indices = new List<List<int>>();
            for (int beat_idx = 0; beat_idx < beats.Count; beat_idx++)
            {
                Beat beat = beats[beat_idx];
                if (beat.lyrics == null)
                {
                    continue;
                }

                while (beat_words.Count < beat.lyrics.Count)
                {
                    temp_words.Add("");
                    beat_words.Add(new List<string>());
                    verse_matches.Add(false);
                    last_syllable_idx.Add(0);
                    match_indices.Add(new List<int>());
                    if (beat_words.Count != verse_matches.Count)
                    {
                        my_log("Something went terribly wrong in FillInWords function");
                        my_log($"beat_words.Count: {beat_words.Count}");
                        my_log($"verse_matches.Count: {verse_matches.Count}");
                    }
                }

                for (int verse_idx = 0; verse_idx < beat.lyrics.Count; verse_idx++)
                {
                    temp_words[verse_idx] += StripPunctuationFromString(beat.lyrics[verse_idx].text);
                    if (!beat.lyrics[verse_idx].syllable_follows
                        && beat.lyrics[verse_idx].text != "")
                    {
                        beat_words[verse_idx].Add(temp_words[verse_idx]);
                        temp_words[verse_idx] = "";
                        if (beat.lyrics[verse_idx].text != "")
                        {
                            last_syllable_idx[verse_idx] = beat_idx;
                        }
                        
                        while (match_indices[verse_idx].Count <= structured_lyrics.Count)
                        {
                            match_indices[verse_idx].Add(-2);
                        };
                        for (int lyric_set_index = 0; lyric_set_index < structured_lyrics.Count; lyric_set_index++)
                        {
                            int match_idx = LyricsMatch(structured_lyrics[lyric_set_index], beat_words[verse_idx]);
                            if (match_idx == 0x7FFFFFFF)
                            {
                                verse_matches[verse_idx] = true;
                                // my_log($"Found match for {structured_lyrics[lyric_set_index].name} with section # {verse_idx}");
                            }
                            match_indices[verse_idx][lyric_set_index] = match_idx;
                        }
                    }
                }

                // in case there's a chorus before the verses, ensure any fully matching set of sections causes a reset for this function's data
                bool all_match = true;
                foreach (bool match in verse_matches)
                {
                    if (!match)
                    {
                        all_match = false;
                    }
                }
                if (all_match)
                {
                    temp_words = new List<string>();
                    beat_words = new List<List<string>>();
                    verse_matches = new List<bool>();
                    last_syllable_idx = new List<int>();
                    match_indices = new List<List<int>>();
                }
            }
            
            // for (int verse_idx = 0; verse_idx < beat_words.Count; verse_idx++)
            // {
            //     my_log($"Section {verse_idx}");
            //     my_log(String.Join(" ", beat_words[verse_idx]));
            // }

            if (verse_matches.Count > 1)
            {
                for (int verse_idx = 0; verse_idx < verse_matches.Count; verse_idx++)
                {
                    if (!verse_matches[verse_idx])
                    {
                        int match_idx = verse_idx - 1;
                        if (match_idx >= 0
                            && match_idx < verse_matches.Count
                            && verse_matches[match_idx]
                            && last_syllable_idx[verse_idx] < last_syllable_idx[match_idx]
                            && TryAddingWords(last_syllable_idx, verse_idx, match_idx, beats, beat_words[verse_idx], structured_lyrics))
                        {
                            verse_matches[verse_idx] = true;
                            AddWords(beats, verse_idx, match_idx, last_syllable_idx[verse_idx], last_syllable_idx[match_idx]);
                            last_syllable_idx[verse_idx] = last_syllable_idx[match_idx];
                            // my_log($"Found synthetic match for {verse_idx}" + Environment.NewLine);
                        }

                        match_idx = verse_idx + 1;
                        if (match_idx >= 0
                            && match_idx < verse_matches.Count
                            && verse_matches[match_idx]
                            && !verse_matches[verse_idx]
                            && last_syllable_idx[verse_idx] < last_syllable_idx[match_idx]
                            && TryAddingWords(last_syllable_idx, verse_idx, match_idx, beats, beat_words[verse_idx], structured_lyrics))
                        {
                            verse_matches[verse_idx] = true;
                            AddWords(beats, verse_idx, match_idx, last_syllable_idx[verse_idx], last_syllable_idx[match_idx]);
                            last_syllable_idx[verse_idx] = last_syllable_idx[match_idx];
                            // my_log($"Found synthetic match for {verse_idx}" + Environment.NewLine);
                        }

                        if (!verse_matches[verse_idx])
                        {
                            my_log($"No match found for section {verse_idx}");
                            int closest_lyric_set = 0;
                            for (int i = 0; i < match_indices.Count; i++)
                            {
                                if (match_indices[verse_idx][i] > match_indices[verse_idx][closest_lyric_set])
                                {
                                    closest_lyric_set = i;
                                }
                            }
                            int word_fail_idx = match_indices[verse_idx][closest_lyric_set];
                            List<string> lyrics_text = structured_lyrics[closest_lyric_set].GetTextList();
                            if (word_fail_idx == -2)
                            {
                                my_log($"Match index not aquired for this section.");
                            }
                            else if (word_fail_idx == -1 || word_fail_idx >= beat_words[verse_idx].Count)
                            {
                                my_log($"Something went wrong. Word Fail Index {word_fail_idx} is out of bounds for beat words of length {beat_words[verse_idx].Count}");
                            }
                            else if (word_fail_idx >= lyrics_text.Count)
                            {
                                my_log($"Closest match from lyric file, \"{structured_lyrics[closest_lyric_set].name}\", is too short for verse of length {beat_words[verse_idx].Count}");
                            }
                            else
                            {
                                my_log($"Closest match failed on lyric set word \"{lyrics_text[word_fail_idx]}\"; mscx word \"{beat_words[verse_idx][word_fail_idx]}\", index {word_fail_idx}");
                            }
                        }
                    }
                }
            }

        }
        static bool TryAddingWords(List<int> last_syllable_idx, int verse_idx, int match_idx, List<Beat> beats, List<string> verse_words, List<LyricSection> lyrics)
        { // see if a match can be found with words added from another verse
            bool found_match = false;
            string temp_word = "";
            for (int beat_idx = last_syllable_idx[verse_idx] + 1; beat_idx <= last_syllable_idx[match_idx]; beat_idx++)
            {
                if (beats.Count < beat_idx
                    || beats[beat_idx].lyrics == null
                    || beats[beat_idx].lyrics.Count <= match_idx
                    || beats[beat_idx].lyrics[match_idx].text == "")
                {
                    continue;
                }
                Beat beat = beats[beat_idx];
                temp_word += StripPunctuationFromString(beat.lyrics[match_idx].text);
                if (!beat.lyrics[match_idx].syllable_follows)
                {
                    verse_words.Add(temp_word);
                    temp_word = "";
                }
            }
            // my_log($"Synthetic section as follows:");
            // my_log(String.Join(" ", verse_words));
            // my_log("");
            foreach (LyricSection lyric_set in lyrics)
            {
                if (LyricsMatch(lyric_set, verse_words) == 0x7FFFFFFF)
                {
                    found_match = true;
                    break;
                }
            }
            return found_match;
        }
        static int LyricsMatch(LyricSection lyric_set, List<string> word_list)
        {
            if (!LyricsLengthsMatch(lyric_set, word_list.Count))
            {
                return -1;
            }
            int next_start_idx = 0;
            // my_log($"LyricsMatch: length is good at {word_list.Count}. check match against lyric set {lyric_set.name}");
            foreach (Line line in lyric_set.lines)
            {
                int list_match = WordListsMatch(line.GetTextList(), word_list.GetRange(next_start_idx, line.words.Count));
                if (list_match != line.GetTextList().Count)
                {
                    return next_start_idx + list_match;
                }
                next_start_idx += line.words.Count;
            }
            // my_log($"Found section match for {lyric_set.name}");
            return 0x7FFFFFFF;
        }
        static void AddWords(List<Beat> beats, int dest_verse_idx, int source_verse_idx, int current_last_syllable_idx, int final_syllable_idx)
        {
            for (int beat_idx = current_last_syllable_idx + 1; beat_idx <= final_syllable_idx; beat_idx++)
            {
                if (beats.Count <= beat_idx)
                {
                    my_log($"beat_idx given {beat_idx} is greater than beats count! ({beats.Count}) for section {dest_verse_idx}");
                    return;
                }
                Beat beat = beats[beat_idx];
                if (beat.lyrics != null && beat.lyrics.Count > source_verse_idx)
                {
                    while (beat.lyrics.Count <= dest_verse_idx)
                    {
                        beat.lyrics.Add(new Lyric());
                    }
                    beat.lyrics[dest_verse_idx] = new Lyric(beat.lyrics[source_verse_idx]);
                }
            }
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

                    /* This is commented out because sometimes one verse will have a syllable where the others don't. */
                    // if (beat_processing_lists.Count != 0 && beat_processing_lists.Count != beat.lyrics.Count)
                    // {
                    //     string syllables_string = "";
                    //     foreach(Lyric lyric in beat.lyrics)
                    //     {
                    //         syllables_string += $"\'{lyric.text}\', ";
                    //     }
                    //     my_log($"There are {beat.lyrics.Count} lyrics in this beat but {beat_processing_lists.Count} sections being generated.");
                    //     my_log($"Lyrics in beat: {syllables_string}");
                    //     break;
                    // }

                    while (beat_processing_lists.Count < beat.lyrics.Count)
                    {
                        beat_processing_lists.Add(new List<VerseBeat>());
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

            int beats_full_word_count = CountWords(beats);
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
                string temp_word = "";
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
                        if (!beat.lyric.syllable_follows)
                        {
                            if (!WordsMatchNoApostrophe(temp_word, lyric_set.lines[lyric_set_index].words[word_count].text))
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
        static void InsertHyphensBetweenWords(List<LyricSection> structured_lyrics, List<Beat> beats)
        {
            List<List<string>> hyphenated_word_set = new List<List<string>>();
            foreach(var verse in structured_lyrics)
            {
                hyphenated_word_set.AddRange(GetHyphenatedWordsLists(verse));
            }
            
            List<bool> processing = new List<bool>();
            List<int> hyphen_start_beat = new List<int>();
            List<string> hyphen_word = new List<string>();

            for (int i = 0; i < beats.Count; i++)
            {
                if (beats[i].lyrics == null)
                {
                    continue;
                }

                while (processing.Count < beats[i].lyrics.Count)
                {
                    processing.Add(false);
                    hyphen_start_beat.Add(0);
                    hyphen_word.Add("");
                };

                for (int verse_idx = 0; verse_idx < beats[i].lyrics.Count; verse_idx++)
                {
                    if (beats[i].lyrics[verse_idx].syllable_follows && !processing[verse_idx])
                    {
                        hyphen_start_beat[verse_idx] = i;
                        processing[verse_idx] = true;
                    }
                    
                    if (processing[verse_idx])
                    {
                        hyphen_word[verse_idx] = "";
                        for (int j = hyphen_start_beat[verse_idx]; j <= i; j++)
                        {
                            if (beats[j].lyrics != null
                                && beats[j].lyrics.Count > verse_idx
                                && beats[j].lyrics[verse_idx].text != "")
                            {
                                hyphen_word[verse_idx] += beats[j].lyrics[verse_idx].text;
                            }
                        }

                        for(int set_idx = 0; set_idx < hyphenated_word_set.Count; set_idx++)
                        {
                            if (WordsMatchNoApostrophe(StripPunctuationFromString(hyphen_word[verse_idx]), hyphenated_word_set[set_idx][0]))
                            {
                                beats[i].lyrics[verse_idx].syllable_follows = false;
                                beats[i].lyrics[verse_idx].hyphenated_word_follows = true;
                                hyphenated_word_set[set_idx].RemoveAt(0);
                                if (hyphenated_word_set[set_idx].Count == 1)
                                {
                                    hyphenated_word_set.RemoveAt(set_idx);
                                }
                                hyphen_start_beat[verse_idx] = i + 1;
                            }
                        }
                    }
                    processing[verse_idx] = beats[i].lyrics[verse_idx].syllable_follows;
                }
            }
        }
        static List<List<string>> GetHyphenatedWordsLists(LyricSection verse)
        {
            List<List<string>> word_set = new List<List<string>>();
            foreach (var line in verse.lines)
            {
                for (int i = 0; i < line.words.Count; i++)
                {
                    if (line.words[i].hyphenated_word_follows || (i != 0 && line.words[i - 1].hyphenated_word_follows))
                    {
                        if (i == 0 || !line.words[i - 1].hyphenated_word_follows)
                        {
                            word_set.Add(new List<string>());
                        }
                        word_set[^1].Add(line.words[i].text);
                    }
                }
            }
            return word_set;
        }
        static int CountWords(List<VerseBeat> beats)
        {
            string temp_word = "";
            int word_count = 0;
            for (int i = 0; i < beats.Count; i++)
            {
                VerseBeat beat = beats[i];
                if (beat.lyric.text != "")
                {
                    temp_word += StripPunctuationFromString(beat.lyric.text);
                    if (!beat.lyric.syllable_follows)
                    {
                        word_count++;
                        temp_word = "";
                    }
                }
            }
            return word_count;
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
        static int WordListsMatch(List<string> words1, List<string> words2)
        {
            if (words1.Count != words2.Count)
            {
                foreach (var word in words2)
                {
                    my_log(word);
                }
                my_log($"length not equal. {words1.Count} lyrics vs {words2.Count} beat words");
                return -1;
            }
            for (int i = 0; i < words1.Count; i++)
            {
                if (!WordsMatchNoApostrophe(words1[i], words2[i]))
                {
                    // my_log($"{words1[i]} != {words2[i]}, {words1.Count} words");
                    return i;
                }
            }
            return words1.Count;
        }
        static bool WordsMatchNoApostrophe(string word1, string word2)
        {
            return StringsMatch(word1, word2)
                || word1.Contains("\'")
                || word2.Contains("\'")
                || word1.Contains("’")
                || word2.Contains("’");
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
                string print_verse = "";
                my_log("Verse from .mscx file:");
                for (int i = 0; i < verse.Count; i++)
                {
                    VerseBeat print_beat = verse[i];
                    if (print_beat.lyric.text != "")
                    {
                        print_verse += print_beat.lyric.text;
                        if (!print_beat.lyric.syllable_follows)
                        {
                            print_verse += " ";
                        }
                    }
                }
                my_log(print_verse + Environment.NewLine);
            }
        }
        static void PrintRemainingLyrics(List<LyricSection> structured_lyrics)
        {
            if (structured_lyrics.Count == 0)
            {
                return;
            }
            my_log("Remaining lyrics from text file:");
            foreach(var verse in structured_lyrics)
            {            
                foreach(var line in verse.lines)
                {
                    string print_words = "";
                    foreach(var lyric in line.words)
                    {
                        print_words += lyric.text + " ";
                    }
                    my_log(print_words);
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
            if (song.name == "")
            {
                return "";
            }
            chord_pro += $"{{title: {song.name}}}" + Environment.NewLine;
            if (song.metadata.author != "") chord_pro += $"{{author: {song.metadata.author}}}" + Environment.NewLine;
            if (song.metadata.key != "")    chord_pro += $"{{key: {song.metadata.key}}}" + Environment.NewLine;
            if (song.metadata.tempo != "")  chord_pro += $"{{tempo: {song.metadata.tempo}}}" + Environment.NewLine;
            if (song.metadata.time != "")   chord_pro += $"{{time: {song.metadata.time}}}" + Environment.NewLine;
            chord_pro += Environment.NewLine;
            // as well as hymnal # if possible

            PlainTextSharpsAndFlats(song);

            foreach (var verse in song.sections)
            {
                chord_pro += $"{{comment: {verse.name}}}" + Environment.NewLine;
                foreach (var line in verse.beats)
                {
                    int space_count = 0;
                    bool is_continuation = false;
                    string last_chord = "";
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
                            if (!String.Equals(beat.chord, last_chord))
                            {
                                chord_pro += $"[{beat.chord}]";
                                space_count = (int)(beat.chord.Length*1.3) - beat.lyric.text.Length + chord_space_minimum;
                                last_chord = beat.chord;
                            }
                        }
                        else
                        {
                            space_count -= beat.lyric.text.Length;
                            if (!beat.lyric.syllable_follows)
                            {
                                space_count--;
                            }
                        }
                        chord_pro += beat.lyric.text;
                        is_continuation = beat.lyric.syllable_follows;
                    }
                    chord_pro += Environment.NewLine;
                }
                chord_pro += Environment.NewLine;
            }
            return chord_pro;
        }

        static void PlainTextSharpsAndFlats(Song song)
        {
            foreach (var verse in song.sections)
            {
                foreach (var line in verse.beats)
                {
                    foreach (var beat in line)
                    {
                        beat.chord = beat.chord.Replace("♭", "b")
                                               .Replace("♯", "#");
                    }
                }
            }
        }

        static async Task<string> GenerateChordSheetHtml(Song song)
        {
            if (song == null || song.sections == null || song.sections.Count == 0)
            {
                my_log("no sections to encode!");
                return "";
            }
            else if (song.sections[0].beats == null || song.sections[0].beats.Count == 0 || song.sections[0].beats[0].Count == 0)
            {
                my_log("no lines to encode!");
                return "";
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