using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;

namespace Lyricify.Lyrics.Demo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // ParsersDemo();
            // GeneratorsDemo();
            // TypeDetectorDemo();
            // SearchDemo();
            LRCLIBDemo();
        }

        static void ParsersDemo()
        {
            /* Parsers Demo */

            LyricsData? lyricsData;

            lyricsData = ParseHelper.ParseLyrics(File.ReadAllText("RawLyrics/LyricifySyllableDemo.txt"), LyricsRawTypes.LyricifySyllable);
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(lyricsData, Newtonsoft.Json.Formatting.Indented));

            lyricsData = ParseHelper.ParseLyrics(File.ReadAllText("RawLyrics/LsMixQrcDemo.txt"), LyricsRawTypes.LyricifySyllable);
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(lyricsData, Newtonsoft.Json.Formatting.Indented));

            lyricsData = ParseHelper.ParseLyrics(File.ReadAllText("RawLyrics/LyricifyLinesDemo.txt"), LyricsRawTypes.LyricifyLines);
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(lyricsData, Newtonsoft.Json.Formatting.Indented));

            lyricsData = ParseHelper.ParseLyrics(File.ReadAllText("RawLyrics/LrcDemo.txt"), LyricsRawTypes.Lrc);
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(lyricsData, Newtonsoft.Json.Formatting.Indented));

            lyricsData = ParseHelper.ParseLyrics(File.ReadAllText("RawLyrics/QrcDemo.txt"), LyricsRawTypes.Qrc);
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(lyricsData, Newtonsoft.Json.Formatting.Indented));

            lyricsData = ParseHelper.ParseLyrics(File.ReadAllText("RawLyrics/KrcDemo.txt"), LyricsRawTypes.Krc);
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(lyricsData, Newtonsoft.Json.Formatting.Indented));

            lyricsData = ParseHelper.ParseLyrics(File.ReadAllText("RawLyrics/YrcDemo.txt"), LyricsRawTypes.Yrc);
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(lyricsData, Newtonsoft.Json.Formatting.Indented));
            Helpers.Optimization.Yrc.StandardizeYrcLyrics(lyricsData!.Lines!); // 优化 YRC 歌词
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(lyricsData, Newtonsoft.Json.Formatting.Indented));

            lyricsData = ParseHelper.ParseLyrics(File.ReadAllText("RawLyrics/SpotifyDemo.txt"), LyricsRawTypes.Spotify);
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(lyricsData, Newtonsoft.Json.Formatting.Indented));

            lyricsData = ParseHelper.ParseLyrics(File.ReadAllText("RawLyrics/SpotifySyllableDemo.txt"), LyricsRawTypes.Spotify);
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(lyricsData, Newtonsoft.Json.Formatting.Indented));

            lyricsData = ParseHelper.ParseLyrics(File.ReadAllText("RawLyrics/SpotifyUnsyncedDemo.txt"), LyricsRawTypes.Spotify);
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(lyricsData, Newtonsoft.Json.Formatting.Indented));

            lyricsData = ParseHelper.ParseLyrics(File.ReadAllText("RawLyrics/MusixmatchDemo.txt"), LyricsRawTypes.Musixmatch);
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(lyricsData, Newtonsoft.Json.Formatting.Indented));
            Helpers.Optimization.Musixmatch.StandardizeMusixmatchLyrics(lyricsData!.Lines!); // 优化 Musixmatch 歌词
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(lyricsData, Newtonsoft.Json.Formatting.Indented));
        }

        static void GeneratorsDemo()
        {
            /* Generators Demo */

            // 读取歌词数据供后期生成使用
            LyricsData? lyricsData;
            lyricsData = ParseHelper.ParseLyrics(File.ReadAllText("RawLyrics/LyricifySyllableDemo.txt"), LyricsRawTypes.LyricifySyllable);
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(lyricsData, Newtonsoft.Json.Formatting.Indented));

            string? lyrics;

            lyrics = GenerateHelper.GenerateString(lyricsData!, LyricsTypes.LyricifySyllable);
            Console.WriteLine(lyrics);

            lyrics = GenerateHelper.GenerateString(lyricsData!, LyricsTypes.LyricifyLines);
            Console.WriteLine(lyrics);

            lyrics = GenerateHelper.GenerateString(lyricsData!, LyricsTypes.Lrc);
            Console.WriteLine(lyrics);

            lyrics = GenerateHelper.GenerateString(lyricsData!, LyricsTypes.Qrc);
            Console.WriteLine(lyrics);

            lyrics = GenerateHelper.GenerateString(lyricsData!, LyricsTypes.Krc);
            Console.WriteLine(lyrics);

            lyrics = GenerateHelper.GenerateString(lyricsData!, LyricsTypes.Yrc);
            Console.WriteLine(lyrics);
        }

        static void TypeDetectorDemo()
        {
            /* Type Detector Demo */

            Console.WriteLine(Helpers.Types.Lrc.IsLrc(File.ReadAllText("RawLyrics/LrcDemo.txt")));
            Console.WriteLine(Helpers.Types.Lrc.IsLrc(File.ReadAllText("RawLyrics/QrcDemo.txt")));
        }

        static void SearchDemo()
        {
            /* Search Demo */

            var search = SearchHelper.Search(new TrackMultiArtistMetadata()
            {
                Album = "RUNAWAY",
                AlbumArtists = new() { "OneRepublic" },
                Artists = new() { "OneRepublic" },
                DurationMs = 143264,
                Title = "RUNAWAY",
            }, Searchers.Searchers.Netease, Searchers.Helpers.CompareHelper.MatchType.Medium).Result;
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(search, Newtonsoft.Json.Formatting.Indented));

            //var qqSearch = new Searchers.QQMusicSearcher();
            //var result = qqSearch.SearchForResult(new TrackMultiArtistMetadata()
            //{
            //    Album = "GUTS",
            //    AlbumArtists = new() { "Olivia Rodrigo" },
            //    Artists = new() { "Olivia Rodrigo" },
            //    DurationMs = 211141,
            //    Title = "get him back!",
            //}).Result;
            //Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
            //var _result = qqSearch.SearchForResults(new TrackMultiArtistMetadata()
            //{
            //    Album = "RUNAWAY",
            //    AlbumArtists = new() { "OneRepublic" },
            //    Artists = new() { "OneRepublic" },
            //    DurationMs = 143264,
            //    Title = "RUNAWAY",
            //}).Result;
            //Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(_result, Newtonsoft.Json.Formatting.Indented));

            //var neteaseSearch = new Searchers.NeteaseSearcher();
            //result = neteaseSearch.SearchForResult(new TrackMultiArtistMetadata()
            //{
            //    Album = "GUTS",
            //    AlbumArtists = new() { "Olivia Rodrigo" },
            //    Artists = new() { "Olivia Rodrigo" },
            //    DurationMs = 211141,
            //    Title = "get him back!",
            //}).Result;
            //Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
            //_result = neteaseSearch.SearchForResults(new TrackMultiArtistMetadata()
            //{
            //    Album = "RUNAWAY",
            //    AlbumArtists = new() { "OneRepublic" },
            //    Artists = new() { "OneRepublic" },
            //    DurationMs = 143264,
            //    Title = "RUNAWAY",
            //}).Result;
            //Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(_result, Newtonsoft.Json.Formatting.Indented));
        }

        static void SyncDemo()
        {
            /* Sync Demo — 模拟悬浮窗桌面歌词的同步引擎 */

            // 解析一首 LRC 歌词
            var lyricsData = ParseHelper.ParseLyrics(File.ReadAllText("RawLyrics/LrcDemo.txt"), LyricsRawTypes.Lrc);
            if (lyricsData?.Lines == null)
            {
                Console.WriteLine("歌词解析失败");
                return;
            }

            Console.WriteLine($"共 {lyricsData.Lines.Count} 行歌词，模拟播放进度:");

            // 模拟每隔 5 秒查询一次当前歌词
            int[] positions = { 0, 835, 4850, 9480, 18920, 37720, 60000, 120000 };
            foreach (var posMs in positions)
            {
                var syncResult = SyncHelper.GetSyncResult(lyricsData, posMs);

                if (syncResult.LineIndex < 0)
                {
                    Console.WriteLine($"[{posMs,7} ms] (歌词未开始)");
                }
                else
                {
                    Console.WriteLine(
                        $"[{posMs,7} ms] 第 {syncResult.LineIndex + 1,3} 行 " +
                        $"进度 {syncResult.LineProgress:P0}  → \"{syncResult.CurrentLine?.Text}\"");
                }
            }

            // 模拟逐字同步歌词场景（使用 LyricifySyllable 格式）
            Console.WriteLine();
            Console.WriteLine("=== 逐字同步演示 ===");
            var syllableData = ParseHelper.ParseLyrics(File.ReadAllText("RawLyrics/LyricifySyllableDemo.txt"), LyricsRawTypes.LyricifySyllable);
            if (syllableData?.Lines != null)
            {
                int[] syllablePositions = { 0, 5000, 10000, 30000 };
                foreach (var posMs in syllablePositions)
                {
                    var syncResult = SyncHelper.GetSyncResult(syllableData, posMs);

                    if (syncResult.LineIndex < 0)
                    {
                        Console.WriteLine($"[{posMs,7} ms] (歌词未开始)");
                        continue;
                    }

                    if (syncResult.SyllableIndex >= 0)
                    {
                        Console.WriteLine(
                            $"[{posMs,7} ms] 第 {syncResult.LineIndex + 1,3} 行 / 第 {syncResult.SyllableIndex + 1,3} 音节 " +
                            $"音节进度 {syncResult.SyllableProgress:P0}  → \"{syncResult.CurrentSyllable?.Text}\"");
                    }
                    else
                    {
                        Console.WriteLine(
                            $"[{posMs,7} ms] 第 {syncResult.LineIndex + 1,3} 行 行进度 {syncResult.LineProgress:P0}  → \"{syncResult.CurrentLine?.Text}\"");
                    }
                }
            }
        }

        static void LRCLIBDemo()
        {
            /* LRCLIB Demo */

            // 方法1: 使用 SearchHelper 搜索
            Console.WriteLine("=== LRCLIB Search with SearchHelper ===");
            var search = SearchHelper.Search(new TrackMultiArtistMetadata()
            {
                Album = "RUNAWAY",
                AlbumArtists = new() { "OneRepublic" },
                Artists = new() { "OneRepublic" },
                DurationMs = 143264,
                Title = "RUNAWAY",
            }, Searchers.Searchers.LRCLIB, Searchers.Helpers.CompareHelper.MatchType.Medium).Result;
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(search, Newtonsoft.Json.Formatting.Indented));

            // 方法2: 使用 LRCLIB Searcher
            Console.WriteLine("\n=== LRCLIB Searcher ===");
            var lrclibSearcher = new Searchers.LRCLIBSearcher();
            var result = lrclibSearcher.SearchForResult(new TrackMultiArtistMetadata()
            {
                Album = "GUTS",
                AlbumArtists = new() { "Olivia Rodrigo" },
                Artists = new() { "Olivia Rodrigo" },
                DurationMs = 211141,
                Title = "get him back!",
            }).Result;
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));

            // 方法3: 直接使用 LRCLIB API 搜索
            Console.WriteLine("\n=== LRCLIB API Search ===");
            var searchResults = ProviderHelper.LRCLIBApi.Search("RUNAWAY", "OneRepublic").Result;
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(searchResults, Newtonsoft.Json.Formatting.Indented));

            // 方法4: 使用 LRCLIB API 获取歌词
            Console.WriteLine("\n=== LRCLIB API Get Lyrics ===");
            var lyrics = ProviderHelper.LRCLIBApi.Get("RUNAWAY", "OneRepublic", "RUNAWAY", 143).Result;
            if (lyrics != null)
            {
                Console.WriteLine($"Track: {lyrics.TrackName} - {lyrics.ArtistName}");
                Console.WriteLine($"Album: {lyrics.AlbumName}");
                Console.WriteLine($"Duration: {lyrics.Duration}s");
                Console.WriteLine($"Instrumental: {lyrics.Instrumental}");

                if (!string.IsNullOrEmpty(lyrics.SyncedLyrics))
                {
                    Console.WriteLine("\nSynced Lyrics (LRC):");
                    Console.WriteLine(lyrics.SyncedLyrics);
                }

                if (!string.IsNullOrEmpty(lyrics.PlainLyrics))
                {
                    Console.WriteLine("\nPlain Lyrics:");
                    Console.WriteLine(lyrics.PlainLyrics);
                }
            }

            // 方法5: 通过 ID 获取歌词（如果你已知 ID）
            Console.WriteLine("\n=== LRCLIB API Get by ID ===");
            var lyricsById = ProviderHelper.LRCLIBApi.GetById(2268843).Result;
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(lyricsById, Newtonsoft.Json.Formatting.Indented));
        }
    }
}