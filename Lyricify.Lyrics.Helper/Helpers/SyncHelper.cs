using Lyricify.Lyrics.Models;

namespace Lyricify.Lyrics.Helpers
{
    /// <summary>
    /// 歌词同步帮助类，根据当前播放位置计算应显示的歌词行与音节，
    /// 可用于驱动悬浮窗桌面歌词等实时渲染场景
    /// </summary>
    public static class SyncHelper
    {
        /// <summary>
        /// 根据播放位置获取歌词同步结果
        /// </summary>
        /// <param name="lyricsData">歌词数据</param>
        /// <param name="positionMs">当前播放位置（毫秒）</param>
        /// <returns>包含当前行、音节及进度信息的 <see cref="SyncResult"/></returns>
        public static SyncResult GetSyncResult(LyricsData lyricsData, int positionMs)
        {
            if (lyricsData?.Lines == null) return new SyncResult();
            return GetSyncResult(lyricsData.Lines, positionMs);
        }

        /// <summary>
        /// 根据播放位置获取歌词同步结果
        /// </summary>
        /// <param name="lines">歌词行列表</param>
        /// <param name="positionMs">当前播放位置（毫秒）</param>
        /// <returns>包含当前行、音节及进度信息的 <see cref="SyncResult"/></returns>
        public static SyncResult GetSyncResult(List<ILineInfo> lines, int positionMs)
        {
            if (lines == null || lines.Count == 0) return new SyncResult();

            var result = new SyncResult();
            result.LineIndex = GetCurrentLineIndex(lines, positionMs);

            if (result.LineIndex < 0)
            {
                return result;
            }

            result.CurrentLine = lines[result.LineIndex];
            result.LineProgress = GetLineProgress(result.CurrentLine, positionMs);

            if (result.CurrentLine is SyllableLineInfo syllableLine)
            {
                result.SyllableIndex = GetCurrentSyllableIndex(syllableLine, positionMs);
                if (result.SyllableIndex >= 0)
                {
                    result.CurrentSyllable = syllableLine.Syllables[result.SyllableIndex];
                    result.SyllableProgress = GetSyllableProgress(result.CurrentSyllable, positionMs);
                }
            }

            return result;
        }

        /// <summary>
        /// 获取当前播放位置所对应的歌词行索引（基于 0）
        /// </summary>
        /// <param name="lines">歌词行列表（应按 StartTime 升序排列）</param>
        /// <param name="positionMs">当前播放位置（毫秒）</param>
        /// <returns>
        /// 当前行索引。如果播放位置在第一行之前，则返回 -1。
        /// 规则：返回最后一个 <c>StartTime &lt;= positionMs</c> 的行的索引。
        /// 没有 StartTime 的行（如元数据行）视为排在所有有时间戳的行之前，不会被选为当前行。
        /// </returns>
        public static int GetCurrentLineIndex(List<ILineInfo> lines, int positionMs)
        {
            if (lines == null || lines.Count == 0) return -1;

            // Binary search for the rightmost line with StartTime <= positionMs.
            // Lines without a StartTime are treated as occurring before all timed lines
            // (hi = mid - 1 branch), so they do not become the current line.
            int lo = 0, hi = lines.Count - 1, result = -1;
            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                var midStartTime = lines[mid].StartTime;

                if (midStartTime.HasValue && midStartTime.Value <= positionMs)
                {
                    result = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return result;
        }

        /// <summary>
        /// 获取逐字同步行中当前播放位置所对应的音节索引（基于 0）
        /// </summary>
        /// <param name="line">逐字同步歌词行（音节应按 StartTime 升序排列）</param>
        /// <param name="positionMs">当前播放位置（毫秒）</param>
        /// <returns>
        /// 当前音节索引。如果播放位置在第一个音节之前，则返回 -1。
        /// 规则：返回最后一个 <c>StartTime &lt;= positionMs</c> 的音节的索引。
        /// </returns>
        public static int GetCurrentSyllableIndex(SyllableLineInfo line, int positionMs)
        {
            if (line?.Syllables == null || line.Syllables.Count == 0) return -1;

            // Binary search for the rightmost syllable with StartTime <= positionMs.
            // ISyllableInfo.StartTime is non-nullable, so no null handling is needed.
            int lo = 0, hi = line.Syllables.Count - 1, result = -1;
            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (line.Syllables[mid].StartTime <= positionMs)
                {
                    result = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return result;
        }

        /// <summary>
        /// 计算播放位置在指定歌词行持续时间内的进度
        /// </summary>
        /// <param name="line">歌词行</param>
        /// <param name="positionMs">当前播放位置（毫秒）</param>
        /// <returns>
        /// 进度值，范围 [0.0, 1.0]。如果行没有结束时间，则返回 0.0。
        /// </returns>
        private static double GetLineProgress(ILineInfo line, int positionMs)
        {
            if (!line.StartTime.HasValue || !line.EndTime.HasValue) return 0.0;
            int duration = line.EndTime.Value - line.StartTime.Value;
            if (duration <= 0) return 1.0;
            double progress = (double)(positionMs - line.StartTime.Value) / duration;
            return Math.Max(0.0, Math.Min(1.0, progress));
        }

        /// <summary>
        /// 计算播放位置在指定音节持续时间内的进度
        /// </summary>
        /// <param name="syllable">音节</param>
        /// <param name="positionMs">当前播放位置（毫秒）</param>
        /// <returns>进度值，范围 [0.0, 1.0]。</returns>
        private static double GetSyllableProgress(ISyllableInfo syllable, int positionMs)
        {
            int duration = syllable.EndTime - syllable.StartTime;
            if (duration <= 0) return 1.0;
            double progress = (double)(positionMs - syllable.StartTime) / duration;
            return Math.Max(0.0, Math.Min(1.0, progress));
        }
    }
}
